using System.Collections.Generic;

namespace GenerateFilesSource
{
	/// <summary>
	/// Data for localization resource file sets.
	/// </summary>
	internal sealed class LocalizationData
	{
		internal readonly string LanguageName;
		internal readonly string Folder;
		internal IEnumerable<InstallerFile> OtherFiles;

		internal LocalizationData(string language, string languageCode)
		{
			LanguageName = language;
			Folder = languageCode;
			OtherFiles = new HashSet<InstallerFile>();
		}
	}
}