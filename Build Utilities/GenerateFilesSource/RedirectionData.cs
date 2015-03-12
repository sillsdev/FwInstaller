namespace GenerateFilesSource
{
	/// <summary>
	/// Data for redirecting folders to elsewhere on user's machine.
	/// </summary>
	internal sealed class RedirectionData
	{
		internal readonly string SourceFolder;
		internal readonly string InstallerDirId;

		internal RedirectionData(string sourceFolder, string installerDirId)
		{
			SourceFolder = sourceFolder;
			InstallerDirId = installerDirId;
		}
	}
}