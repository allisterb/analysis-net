// Copyright (c) Edgardo Zoppi. All Rights Reserved.
// See License.txt in the repository root directory for license information.

using System;
using System.Collections.Generic;

namespace Model.Types
{
	// Known core-library assembly names, each observed directly on a real assembly (see
	// docs/agent/measurements/platformtypes-comparison-audit.md and
	// docs/agent/measurements/tac-platformtypes-spike.md): "mscorlib" (.NET Framework/Mono/net45
	// -- the name PlatformTypes, below, is pinned to), "System.Runtime" (net5.0+
	// reference-assembly-style typerefs, the overwhelming majority of modern IL),
	// "System.Private.CoreLib" (when the analyzed module is the runtime's own corlib), and
	// "netstandard" (netstandard2.0-targeted assemblies, common across NuGet). Compared
	// case-insensitively: assembly-name binding itself is case-insensitive in .NET, and metadata
	// is free to author the name in any case.
	//
	// This is the single shared definition -- do not fork a second copy. Used by:
	//  - BasicType.Equals (Model/Types/Types.cs), which unifies two IBasicTypes whose name, namespace,
	//    generic arity and generic arguments all match but whose ContainingAssembly differs only
	//    because they're two different corlibs (e.g. [mscorlib]System.Boolean and
	//    [System.Runtime]System.Boolean -- via type forwarding these really are the same type, so
	//    treating them as unequal is itself the bug: see docs/agent/tasks/platformtypes-comparison-fix.md).
	//  - Backend.Transformations.Disassembler.IsVoidReturn, whose own former local allowlist has
	//    been replaced by this one.
	//
	// Known residual, not closed by this list: IAssemblyReference (Model/Assembly.cs) has
	// name-only identity -- no public key token, no version -- so this allowlist is spoofable by
	// any assembly that simply names itself "mscorlib" (etc.) while defining a hostile type of
	// the same name+namespace. Closing that needs strong assembly identity in the Model layer;
	// out of scope here, carried forward from the prior IsVoidReturn fix.
	public static class CoreLibraryNames
	{
		private static readonly HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"mscorlib",
			"System.Runtime",
			"System.Private.CoreLib",
			"netstandard",
		};

		public static bool IsCoreLibrary(IAssemblyReference assembly)
		{
			return assembly != null && assembly.Name != null && known.Contains(assembly.Name);
		}

		public static bool AreBothCoreLibraries(IAssemblyReference a, IAssemblyReference b)
		{
			return IsCoreLibrary(a) && IsCoreLibrary(b);
		}
	}
}
