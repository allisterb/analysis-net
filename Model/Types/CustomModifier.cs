// Silvergun: added to the fork; not part of upstream Analysis.Net.

namespace Model.Types
{
	// A custom modifier of a signature (modreq or modopt), which the CLR compares when it binds a reference.
	public class CustomModifier
	{
		public IType Modifier { get; private set; }
		public bool IsOptional { get; private set; }

		public CustomModifier(IType modifier, bool isOptional)
		{
			this.Modifier = modifier;
			this.IsOptional = isOptional;
		}

		public override string ToString()
		{
			return string.Format("{0}({1})", this.IsOptional ? "modopt" : "modreq", this.Modifier);
		}
	}
}
