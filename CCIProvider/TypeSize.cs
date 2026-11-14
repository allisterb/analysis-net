// Silvergun: the size of a type reference's tree, measured once over CCI's shared objects.
// See docs/agent/tasks/graph-names-hostile-types.md in the Silvergun repository.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Cci = Microsoft.Cci;

namespace CCIProvider
{
	/// <summary>
	/// The number of parts a type reference expands to as a tree, and a structural hash of it. Metadata can name a
	/// TypeSpec inside another's signature, so a few rows make a type whose tree doubles per level; CCI shares those
	/// rows as objects, so these walks, memoised by object, are linear in the metadata while the tree is exponential.
	/// A generic instance of more than <see cref="MaxParts"/> parts is not built by the type extractor, and both of
	/// Silvergun's namers render it from this measure alone, so the two agree by construction.
	/// Never throws: a reference that cannot be read measures 0 parts and hashes to "?".
	/// </summary>
	public static class TypeSize
	{
		/// <summary>The most parts a generic instance may have and still be built.</summary>
		public const long MaxParts = 4096;

		/// <summary>
		/// The parts of <paramref name="type"/>, saturating: 1 per node (a generic instance and its definition are one),
		/// plus 1 per by-ref function-pointer parameter.
		/// </summary>
		public static long Parts(Cci.ITypeReference type) =>
			Guard(() => Fold(type, parts, _ => 0L, (node, children) => children.Aggregate(1L + node.Extra, (sum, c) => Add(sum, Math.Max(1, c)))), 0L);

		/// <summary>Whether <paramref name="type"/> has more than <see cref="MaxParts"/> parts.</summary>
		public static bool IsOversized(Cci.ITypeReference type) => Parts(type) > MaxParts;

		/// <summary>The structural hash of <paramref name="type"/>: SHA-256 over each node's kind, name or index and its children's digests, as 16 hex chars.</summary>
		public static string Hash(Cci.ITypeReference type) =>
			Guard(() => Fold(type, digests, _ => unreadable, Digest) is var digest && ReferenceEquals(digest, unreadable)
				? "?"
				: string.Concat(digest.Take(8).Select(b => b.ToString("x2"))), "?");

		// A post-order fold over an explicit stack (a hostile nesting depth cannot overflow the call stack), memoised by
		// object. A child already being expanded is a cycle (hostile nesting chains) and folds as the empty value.
		private static T Fold<T>(Cci.ITypeReference root, ConditionalWeakTable<Cci.ITypeReference, Box<T>> memo,
			Func<Cci.ITypeReference, T> empty, Func<Node, T[], T> combine)
		{
			if (root == null)
				return empty(null);
			if (memo.TryGetValue(root, out var known))
				return known.Value;

			var inProgress = new HashSet<Cci.ITypeReference>(ReferenceComparer.Instance);
			var stack = new Stack<(Cci.ITypeReference Type, Node Node)>();
			stack.Push((root, null));
			while (stack.Count > 0)
			{
				var (type, node) = stack.Pop();
				if (memo.TryGetValue(type, out _))
					continue;
				if (node == null)
				{
					var read = Read(type);
					inProgress.Add(type);
					stack.Push((type, read));
					foreach (var child in read.Children.Where(c => c != null && !inProgress.Contains(c) && !memo.TryGetValue(c, out _)))
						stack.Push((child, null));
					continue;
				}
				inProgress.Remove(type);
				var values = node.Children.Select(c => c != null && memo.TryGetValue(c, out var v) ? v.Value : empty(c)).ToArray();
				memo.AddOrUpdate(type, new Box<T>(combine(node, values)));
			}
			return memo.TryGetValue(root, out var result) ? result.Value : empty(root);
		}

		// A node's kind and children, as the type extractor descends them; Extra is the parts it adds beyond its children.
		private static Node Read(Cci.ITypeReference type)
		{
			switch (type)
			{
				case Cci.IModifiedTypeReference modified:
					return new Node(type, 0, modified.UnmodifiedType);
				// One part with its definition (-1 for the definition's own), so T_k = Pair<T_k-1,T_k-1> is 2^(k+2)-1 parts
				// as SsaBounds.MaxTypeParts counts it; the definition is still descended for its containing instances. Nesting
				// links, modifiers and by-ref fnptr parameters add parts SSA does not count, so the graph never cuts later.
				case Cci.IGenericTypeInstanceReference instance:
					return new Node(type, -1, new[] { instance.GenericType }.Concat(instance.GenericArguments).ToArray());
				case Cci.IArrayTypeReference array:
					return new Node(type, 0, array.ElementType);
				case Cci.IManagedPointerTypeReference pointer:
					return new Node(type, 0, pointer.TargetType);
				case Cci.IPointerTypeReference pointer:
					return new Node(type, 0, pointer.TargetType);
				case Cci.IFunctionPointerTypeReference function:
					var parameters = function.Parameters.ToArray();
					return new Node(type, parameters.Count(p => p.IsByReference), new[] { function.Type }.Concat(parameters.Select(p => p.Type)).ToArray());
				case Cci.INestedTypeReference nested:
					return new Node(type, 0, nested.ContainingType);
				default:
					return new Node(type, 0);
			}
		}

		// A node's own identity for the hash: its kind, and its name, rank, index or by-ref flags.
		private static string Payload(Cci.ITypeReference type)
		{
			switch (type)
			{
				case null: return "?";
				case Cci.IModifiedTypeReference _: return "modified";
				case Cci.IGenericTypeInstanceReference _: return "instance";
				case Cci.IArrayTypeReference array: return "array:" + (array.IsVector ? "v" : array.Rank.ToString());
				case Cci.IManagedPointerTypeReference _: return "byref";
				case Cci.IPointerTypeReference _: return "pointer";
				case Cci.IFunctionPointerTypeReference function: return "fnptr:" + string.Concat(function.Parameters.Select(p => p.IsByReference ? "&" : "-"));
				case Cci.IGenericTypeParameterReference parameter: return "!" + parameter.Index;
				case Cci.IGenericMethodParameterReference parameter: return "!!" + parameter.Index;
				case Cci.INestedTypeReference nested: return "nested:" + nested.Name.Value + "`" + nested.GenericParameterCount;
				case Cci.INamespaceTypeReference named: return "type:" + Namespace(named.ContainingUnitNamespace) + "." + named.Name.Value + "`" + named.GenericParameterCount;
				case Cci.INamedTypeReference named: return "named:" + named.Name.Value + "`" + named.GenericParameterCount;
				default: return "other:" + type.GetType().Name;
			}
		}

		private static byte[] Digest(Node node, byte[][] children)
		{
			using (var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
			{
				digest.AppendData(Encoding.UTF8.GetBytes(Payload(node.Type)));
				foreach (var child in children)
					digest.AppendData(child);
				return digest.GetHashAndReset();
			}
		}

		private static string Namespace(Cci.IUnitNamespaceReference space)
		{
			var parts = new List<string>();
			while (space is Cci.INestedUnitNamespaceReference nested)
			{
				parts.Insert(0, nested.Name.Value);
				space = nested.ContainingUnitNamespace;
			}
			return string.Join(".", parts);
		}

		private static long Add(long a, long b) => b > long.MaxValue - a ? long.MaxValue : a + b;

		private static T Guard<T>(Func<T> read, T fallback)
		{
			try
			{
				return read();
			}
			catch (Exception)
			{
				return fallback;
			}
		}

		// The digest of a child that cannot be read, or that closes a cycle.
		private static readonly byte[] unreadable = Encoding.UTF8.GetBytes("?");

		// Weak keys: an entry lives only as long as CCI's object, so the memos root nothing.
		private static readonly ConditionalWeakTable<Cci.ITypeReference, Box<long>> parts = new ConditionalWeakTable<Cci.ITypeReference, Box<long>>();
		private static readonly ConditionalWeakTable<Cci.ITypeReference, Box<byte[]>> digests = new ConditionalWeakTable<Cci.ITypeReference, Box<byte[]>>();

		private sealed class Box<T>
		{
			public Box(T value) { Value = value; }
			public T Value { get; }
		}

		private sealed class ReferenceComparer : IEqualityComparer<Cci.ITypeReference>
		{
			public static readonly ReferenceComparer Instance = new ReferenceComparer();
			public bool Equals(Cci.ITypeReference x, Cci.ITypeReference y) => ReferenceEquals(x, y);
			public int GetHashCode(Cci.ITypeReference obj) => RuntimeHelpers.GetHashCode(obj);
		}

		private sealed class Node
		{
			public Node(Cci.ITypeReference type, int extra, params Cci.ITypeReference[] children)
			{
				Type = type;
				Extra = extra;
				Children = children;
			}

			public Cci.ITypeReference Type { get; }
			public int Extra { get; }
			public Cci.ITypeReference[] Children { get; }
		}
	}
}
