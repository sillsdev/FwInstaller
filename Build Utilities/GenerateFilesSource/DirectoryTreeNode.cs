using System.Collections.Generic;
using System.Linq;

namespace GenerateFilesSource
{
	/// <summary>
	/// Tree structure representing folders that contain files being considered for the installer.
	/// </summary>
	internal sealed class DirectoryTreeNode
	{
		internal readonly HashSet<InstallerFile> LocalFiles;
		internal readonly List<DirectoryTreeNode> Children;
		internal string Name;
		internal string TargetPath;
		internal string DirId;
		internal bool IsDirReference;

		internal DirectoryTreeNode()
		{
			LocalFiles = new HashSet<InstallerFile>();
			Children = new List<DirectoryTreeNode>();
			DirId = "";
			IsDirReference = false;
		}

		/// <summary>
		/// Recursively examines directory tree to see if any used files are in it.
		/// A used file is one which is assigned to at least one feature.
		/// </summary>
		/// <returns>true if any used files are in the tree, otherwise false</returns>
		internal bool ContainsUsedFiles()
		{
			if (LocalFiles.Any(file => (file.Features.Count > 0 && !file.OnlyUsedInUnusedFeatures)))
				return true;

			return Children.Any(child => child.ContainsUsedFiles());
		}
	}
}