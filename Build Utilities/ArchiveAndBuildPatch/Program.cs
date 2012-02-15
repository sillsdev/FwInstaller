using System;
using System.Collections.Generic;
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

			// Get an object representing the current installer (not an archived one):
			var installer = new BuiltInstaller();
			// Get an object to handle archiving and associated folders:
			var archiveFolderManager = new ArchiveFolderManager();

			// Create a new folder to archive the current installer:
			var archiveFolder = archiveFolderManager.AddArchiveFolder(installer.Version);
			// Archive the current installer into the new folder:
			installer.ArchiveInstaller(archiveFolder);
			// Run an administrative install on the newly-archived installer. This
			// basically extracts all the files, a necessary step for patch-building,
			// and can be achieved manually using the msiexec.exe /a option:
			installer.ArchivedAdministrativeInstall(archiveFolder);

			// Define how many installer releases back we are going to make patches from:
			var patchFromIndexes = new [] {1, 2}; // patch from previous and previous but one installers.

			Parallel.ForEach(patchFromIndexes, index =>
			{
				// If there is a previous installer in the series, build a patch from the
				// previous installer to this new one:
				if (archiveFolderManager.GetNthFromEndVersion(index) != null)
				{
					var patchBuilder =
						new PatchBuilder(archiveFolderManager.GetNthFromEndVersion(index), installer,
										 archiveFolderManager);
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
		/// <returns>The version number wihout the '.' characters, e.g. 704</returns>
		public static string Squash(string version)
		{
			return version.Replace(".", "");
		}

		public static void ForceDeleteDirectory(string path)
		{
			if (!Directory.Exists(path))
				return;

			DirectoryInfo fol;
			var fols = new Stack<DirectoryInfo>();
			var root = new DirectoryInfo(path);
			fols.Push(root);
			while (fols.Count > 0)
			{
				fol = fols.Pop();
				fol.Attributes = fol.Attributes & ~(FileAttributes.Archive | FileAttributes.ReadOnly | FileAttributes.Hidden);
				foreach (var d in fol.GetDirectories())
				{
					fols.Push(d);
				}
				foreach (var f in fol.GetFiles())
				{
					f.Attributes = f.Attributes & ~(FileAttributes.Archive | FileAttributes.ReadOnly | FileAttributes.Hidden);
					f.Delete();
				}
			}
			root.Delete(true);
		}

	}
}
