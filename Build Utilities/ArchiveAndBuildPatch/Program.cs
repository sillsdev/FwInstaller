using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;

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
			archiveFolderManager.ArchiveFile("AutoFiles.wxs");
			archiveFolderManager.ArchiveFile("FileLibrary.xml");
			archiveFolderManager.ArchiveFile("SetupFW.msi");
			archiveFolderManager.ArchiveFile("SetupFW.wixpdb");

			// Define how many installer releases back we are going to make patches from:
			var patchFromIndexes = new [] {1, 2, 3}; // patch from previous and previous but one installers.

			Parallel.ForEach(patchFromIndexes, index =>
			{
				// If there is a previous installer in the series, build a patch from the
				// previous installer to this new one:
				if (archiveFolderManager.GetNthFromEndVersion(index) != null)
				{
					var patchBuilder =
						new PatchBuilder(archiveFolderManager.GetNthFromEndVersion(index), archiveFolderManager.LatestVersion,
										 archiveFolderManager.RootArchiveFolder);
					var patchPath = patchBuilder.BuildPatch();
					var patchWrapper = new PatchWrapper(patchPath);
					patchWrapper.Wrap();
				}
			});
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
