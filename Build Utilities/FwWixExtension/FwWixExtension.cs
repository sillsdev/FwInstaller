using InstallerBuildUtilities;
using Microsoft.Tools.WindowsInstallerXml;

namespace FwWixExtension
{
	/// <summary>
	/// A class to provide extended functionality to WIX. In this case, to enable us
	/// to pass data from the FieldWorks build into the installer build.
	/// </summary>
	public class FwWixExtension : WixExtension
	{
		private FwPreprocessorExtension _preprocessorExtension;

		/// <summary>
		/// A property allowing us to extend WIX preprocessing functionality.
		/// </summary>
		public override PreprocessorExtension PreprocessorExtension
		{
			get { return _preprocessorExtension ?? (_preprocessorExtension = new FwPreprocessorExtension()); }
		}
	}

	/// <summary>
	/// A class allowing us to define extra preprocessing variables related to the FW build.
	/// </summary>
	public class FwPreprocessorExtension : PreprocessorExtension
	{
		// Specify which variable prefixes we deal with:
		private static readonly string[] prefixes = { "Fw" };
		public override string[] Prefixes { get { return prefixes; } }

		public override string GetVariableValue(string prefix, string name)
		{
			// Based on the namespace and name, define the resulting string.
			if (prefix == prefixes[0])
			{
				switch (name)
				{
					case "Version":
						return Tools.GetFwBuildVersion();
					case "MajorVersion":
						return Tools.MajorVersion;
				}
			}
			return null;
		}
	}
}
