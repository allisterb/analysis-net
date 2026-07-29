// Copyright (c) Edgardo Zoppi. All Rights Reserved.
// See License.txt in the repository root directory for license information.

using System;

namespace Model.Types
{
	// ECMA-335 (6th ed., June 2012) Partition III, section 1.5, Table III.2 "Binary Numeric
	// Operations" -- the normative result-type table for add/sub/mul/div/rem. Table III.5
	// (integer operations: and/or/xor/div.un/rem.un) and Table III.7 (overflow arithmetic) have
	// the same shape and are served by the same function. Table III.6 (shift) is NOT: there the
	// result is the type of the FIRST operand, so it is not a join and must not use this.
	//
	// Domain is the six types the CLI tracks on the evaluation stack (III.1.1):
	// int32, int64, native int, F (III.1.1.1 -- float32 and float64 both collapse to F),
	// O (III.1.1.4, object reference) and & (III.1.1.5.2, managed pointer).
	//
	//        | int32      int64  native int  F  &            O
	// int32  | int32      --     native int  --  & (add)     --
	// int64  | --         int64  --          --  --          --
	// native | native int --     native int  --  & (add)     --
	// F      | --         --     --          F   --          --
	// &      | & (add,sub)--     & (add,sub) --  native int  --
	// O      | --         --     --          --  --          --      ("--" = invalid CIL)
	//
	// The RESULT table is symmetric; only the set of legal instructions is not ("& - int32" is
	// legal, "int32 - &" is not). BinarySignedNumericOperationType -- our only caller -- receives
	// no operator, so it is pure numeric promotion and is required to be commutative. That is
	// guaranteed here structurally rather than by testing: Classify is unary, and the table below
	// is written in ONE direction only and read after the pair is sorted by StackClass ordinal.
	//
	// Signedness is deliberately absent from the domain (ECMA tracks none on the stack).
	// TypeInferenceAnalysis already passes instruction.UnsignedOperands to
	// BinaryNumericOperationType, which re-applies it via TypeHelper.UnsignedEquivalent.
	//
	// Totality: hostile and obfuscated assemblies are in scope, so every input combination must
	// yield an answer. Invalid ("--") cells are widened rather than rejected; nothing throws;
	// PlatformTypes.Unknown is never manufactured (it would permanently pin a wrong answer in
	// TypeInferenceAnalysis's fixed point -- see BinarySignedNumericOperationType's guard).
	//
	// Leniency precedence for the "--" cells, stated once instead of only per-cell below:
	// O > F > & > integer widening (Int32 < native int < Int64). Read it as "of the two classes in
	// the pair, the one further left wins". This ordering is not from ECMA -- the cells it decides
	// are invalid CIL -- it is chosen because it reproduces the answers of the hand-rolled cascade
	// this table replaces.
	public static class StackTypePromotion
	{
		// Ordinals are the sort key that makes the table one-directional. They are otherwise
		// arbitrary EXCEPT that Int32 < NativeInt < Int64 is also the integer widening order used
		// by the lenient cells below.
		private enum StackClass
		{
			Int32 = 0,
			NativeInt = 1,
			Int64 = 2,
			Float = 3,
			ManagedPointer = 4,
			Object = 5
		}

		// table[lo][hi - lo], lo <= hi: the upper triangle of Table III.2, one direction only.
		// Each row is commented with the ECMA cell it implements; "lenient" marks a "--" cell.
		private static readonly StackClass[][] table = new StackClass[][]
		{
			// int32 x ...
			new StackClass[]
			{
				StackClass.Int32,			// x int32      : Table III.2, int32 x int32 -> int32
				StackClass.NativeInt,		// x native int : Table III.2, int32 x native int -> native int
				StackClass.Int64,			// x int64      : lenient ("--"); widen to the larger integer
				StackClass.Float,			// x F          : lenient ("--"); F absorbs integers
				StackClass.ManagedPointer,	// x &          : Table III.2, & + int32 -> &
				StackClass.Object			// x O          : lenient ("--"); O absorbs everything
			},
			// native int x ...
			new StackClass[]
			{
				StackClass.NativeInt,		// x native int : Table III.2, native int x native int -> native int
				StackClass.Int64,			// x int64      : lenient ("--"); widen to the larger integer
				StackClass.Float,			// x F          : lenient ("--"); F absorbs integers
				StackClass.ManagedPointer,	// x &          : Table III.2, & +- native int -> &
				StackClass.Object			// x O          : lenient ("--"); O absorbs everything
			},
			// int64 x ...
			new StackClass[]
			{
				StackClass.Int64,			// x int64      : Table III.2, int64 x int64 -> int64
				StackClass.Float,			// x F          : lenient ("--"); F absorbs integers
				StackClass.ManagedPointer,	// x &          : lenient ("--"); treated as the & x native int cell
				StackClass.Object			// x O          : lenient ("--"); O absorbs everything
			},
			// F x ...
			new StackClass[]
			{
				StackClass.Float,			// x F          : Table III.2, F x F -> F
				StackClass.Float,			// x &          : lenient ("--"); F absorbs the pointer
				StackClass.Object			// x O          : lenient ("--"); O absorbs everything
			},
			// & x ...
			new StackClass[]
			{
				StackClass.NativeInt,		// x &          : Table III.2, & - & -> native int
				StackClass.Object			// x O          : lenient ("--"); O absorbs everything
			},
			// O x ...
			new StackClass[]
			{
				StackClass.Object			// x O          : lenient ("--"); both operands are object references
			}
		};

		/// <summary>
		/// Result type of a binary numeric operation on the two given operand types, per
		/// ECMA-335 Partition III, Table III.2. Total, commutative and never throws.
		/// Returns null when an operand is null or is the <see cref="UnknownType"/> sentinel --
		/// "not yet known", never a manufactured definite type. An unbound <see cref="BasicType"/>
		/// is not "unknown" in that sense and is classified like any other operand.
		/// </summary>
		public static IType Combine(IType left, IType right)
		{
			if (left == null || right == null) return null;

			// UnknownType is a sentinel, not a type, and it is not an IBasicType, so Classify would
			// call it O and the table would then answer PlatformTypes.Object for (Unknown, string) --
			// a confident concrete type invented from a sentinel (both operand orders agree, so the
			// commutativity sweep cannot see it). Answer null instead, and specifically NOT
			// PlatformTypes.Unknown: a non-null Unknown looks like a definite type and permanently
			// pins the wrong answer in TypeInferenceAnalysis's fixed point, whereas null means "not
			// yet known" and a later iteration can still correct it (measurements/
			// typeinference-nullguard-fixes.md). Testing both operands and returning a constant is
			// symmetric by construction; TypeInferenceAnalysis.SameTypes treats two nulls as
			// unchanged, so this still terminates.
			if (left is UnknownType || right is UnknownType) return null;

			var leftClass = Classify(left);
			var rightClass = Classify(right);

			var lo = leftClass <= rightClass ? leftClass : rightClass;
			var hi = leftClass <= rightClass ? rightClass : leftClass;

			var loIndex = (int)lo;
			var hiIndex = (int)hi - loIndex;

			// The table has one row per StackClass member; a seventh member added without its row
			// would make this read throw and break totality. Bounds-check leniently and fall back to
			// the most absorbing class rather than asserting: Debug.Assert/Debug.Fail route to
			// Environment.FailFast in this fork and would kill the process uncatchably (ADR-0021).
			// Unsigned comparisons so a negative ordinal (a future member declared with an explicit
			// negative value) is caught by the same test rather than throwing past it.
			if ((uint)loIndex >= (uint)table.Length ||
				(uint)hiIndex >= (uint)table[loIndex].Length) return PlatformTypes.Object;

			var result = table[loIndex][hiIndex];

			switch (result)
			{
				case StackClass.Int32: return PlatformTypes.Int32;
				case StackClass.NativeInt: return PlatformTypes.IntPtr;
				case StackClass.Int64: return PlatformTypes.Int64;
				case StackClass.Float: return PlatformTypes.Float64;

				// Exactly one operand can be & here: the & x & cell yields native int, so both
				// never reach this arm. Returning that operand keeps the pointer's target type,
				// which is what the code this replaces did for "& +- int" (TypeHelper.cs's former
				// IReferenceType arms) and what pointer arithmetic in the TAC expects.
				case StackClass.ManagedPointer:
					return leftClass == StackClass.ManagedPointer ? left : right;

				// At least one operand is O. Keep it when only one side is O; when both are, keep
				// the shared type if they agree and otherwise fall back to their common root
				// System.Object -- a constant, so the answer cannot depend on operand order.
				// Note what "cannot depend on operand order" means for the both-O-and-equal case:
				// this returns left for Combine(a, b) and right for Combine(b, a), which AreEqual
				// says are equal but which need not be the same instance. The guarantee in this arm
				// is therefore "equal", not "same" -- a commutativity check over it must assert
				// equality (Assert.Equal), never reference identity (Assert.Same).
				default:
					if (leftClass != StackClass.Object) return right;
					if (rightClass != StackClass.Object) return left;
					if (AreEqual(left, right)) return left;
					return PlatformTypes.Object;
			}
		}

		// Total classifier. Unary by construction -- this is where commutativity comes from.
		private static StackClass Classify(IType type)
		{
			// & (III.1.1.5.2). The model has no separate byref type: IReferenceType is carried by
			// PointerType, ArrayType and FunctionPointerType, and it is the marker the replaced
			// cascade already used for the & arms, so it stays the marker here. ECMA's split
			// between unmanaged pointers (native int, III.1.1.5.1) and managed pointers is not
			// representable in this model.
			if (type is IReferenceType) return StackClass.ManagedPointer;

			var basicType = type as IBasicType;

			// Generic parameters and anything else outside the basic-type world are not numeric:
			// O (III.1.1.4). UnknownType would land here too, which is exactly why Combine rejects
			// it before calling this -- an O answer for a sentinel is a manufactured type.
			if (basicType == null) return StackClass.Object;

			// A declared reference type is O, and answering here avoids resolving it below.
			if (basicType.TypeKind == TypeKind.ReferenceType) return StackClass.Object;

			try
			{
				// TypeHelper.StackType implements the III.1.1 collapse (bool/char/i1/i2/i4/u1/u2/u4
				// -> int32, i8/u8 -> int64, f4/f8 -> F, intptr/uintptr -> native int, enum ->
				// underlying) and returns its argument unchanged for anything else.
				var stackType = TypeHelper.StackType(basicType);

				if (stackType != null)
				{
					if (stackType.Equals(PlatformTypes.Int32)) return StackClass.Int32;
					if (stackType.Equals(PlatformTypes.Int64)) return StackClass.Int64;
					if (stackType.Equals(PlatformTypes.IntPtr)) return StackClass.NativeInt;
					if (stackType.Equals(PlatformTypes.Float64)) return StackClass.Float;
				}
			}
			catch (Exception)
			{
				// StackType reads IBasicType.ResolvedType to look through enums, and an unbound
				// BasicType reference throws InvalidOperationException from there (see the
				// BasicType constructor in Types.cs); a partially built type can also carry null
				// assembly references into Equals. Neither is a reason to take the analysis down,
				// so an operand we cannot classify is just O.
				//
				// This O is NOT the laundering that Combine's UnknownType early exit prevents. The
				// answer it produces is PlatformTypes.Object, which TypeInferenceAnalysis treats as
				// still-improvable: Visit(LoadInstruction) (and Visit(StoreInstruction)/
				// Visit(ReturnInstruction)) re-fill a slot whose Type is null OR equals
				// PlatformTypes.Object, so a later fixed-point iteration can replace it. Returning
				// PlatformTypes.Unknown instead would be permanent -- UnknownType does not override
				// Equals, so it matches neither disjunct and no iteration would ever overwrite it.
			}

			return StackClass.Object;
		}

		// Symmetric and non-throwing: IType implementations are free to have partial or
		// exception-prone Equals, and this decides a result that must not depend on which operand
		// is on the left.
		private static bool AreEqual(IType a, IType b)
		{
			if (ReferenceEquals(a, b)) return true;

			try
			{
				return a.Equals(b) && b.Equals(a);
			}
			catch (Exception)
			{
				return false;
			}
		}
	}
}
