// Copyright (c) Edgardo Zoppi. All Rights Reserved.
// See License.txt in the repository root directory for license information.

namespace Model.Types
{
	// Silvergun: the accessibility of a type or member definition, as declared in metadata (ECMA-335 II.23.1.15,
	// II.23.1.10). Unknown is the default for definitions built by hand, and what a provider reports when the
	// metadata does not say. Compiler-controlled (privatescope) members are reported as Private: they are even
	// less accessible.
	public enum VisibilityKind
	{
		Unknown,
		Private,
		FamilyAndAssembly,
		Assembly,
		Family,
		FamilyOrAssembly,
		Public
	}
}
