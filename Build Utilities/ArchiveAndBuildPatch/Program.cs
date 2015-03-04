using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using BuildUtilities;

namespace ArchiveAndBuildPatch
{
	class Program
	{
		public static string ExeFolder;
		public static string BuildType;
		public static XmlDocument Configuration;

		static void Main(string[] args)
		{
			BuildType = "Release";

			if (args.Any(arg => arg.ToLowerInvariant() == "debug"))
				BuildType = "Debug";

			// Get our .exe folder path:
			var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
			if (exePath == null)
				throw new Exception("So sorry, don't know where we are!");
			ExeFolder = Path.GetDirectoryName(exePath);

			// Read in the InstallerConfig file:
			Configuration = new XmlDocument();
			Configuration.Load("InstallerConfig.xml");

			// Update the File and Registry Libraries:
			var libraryUpdater = new ComponentLibraryUpdater();
			libraryUpdater.UpdateLibraries();

			// Get an object to handle archiving and associated folders:
			var archiveFolderManager = new ArchiveFolderManager();

			// Archive the important files into the latest build version folder:
			archiveFolderManager.ArchiveFile(InstallerConstants.AutoFilesFileName);
			archiveFolderManager.ArchiveFile("FileLibrary.xml");
			archiveFolderManager.ArchiveFile("SetupFW.msi");
			archiveFolderManager.ArchiveFile("SetupFW.wixpdb");

			// Build patches from all previous versions to get to the latest version
			Parallel.For(1, archiveFolderManager.NumArchives, index =>
			{
				// If there is a previous installer in the series, build a patch from the
				// previous installer to this new one:
				if (archiveFolderManager.GetNthFromEndVersion(index) != null)
				{
					Parallel.Invoke
					(
						() => BuildPatch(archiveFolderManager, index, "SetupFW", "FW_SE_")
					);
				}
			});
		}

		private static void BuildPatch(ArchiveFolderManager archiveFolderManager, int index, string installerName, string patchNamePrefix)
		{
			var patchBuilder = new PatchBuilder(installerName, patchNamePrefix, archiveFolderManager.GetNthFromEndVersion(index),
												archiveFolderManager.LatestVersion, archiveFolderManager.RootArchiveFolder);
			var patchPath = patchBuilder.BuildPatch();

			if (patchPath == null)
				return;

			var patchWrapper = new PatchWrapper(patchPath);
			patchWrapper.Wrap();
		}

		/// <summary>
		/// Removes all '.' characters from a given version number string.
		/// </summary>
		/// <param name="version">A version number string, e.g. 7.0.4</param>
		/// <returns>The version number without the '.' characters, e.g. 704</returns>
		public static string Squash(string version)
		{
			return version.Replace(".", "");
		}
	}
}
