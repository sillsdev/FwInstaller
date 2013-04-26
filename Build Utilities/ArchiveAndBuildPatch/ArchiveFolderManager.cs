using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ArchiveAndBuildPatch
{
	class ArchiveFolderManager
	{
		private const string ArchiveFolder = "Builds";
		private const string VersionFolderNamePattern = @"^([0-9\.]+)$";
		private readonly string _archiveFolder;
		private readonly List<string> _archiveFolderVersions = new List<string>();

		class VersionStringComparer : IComparer<string>
		{
			public int Compare(string v1, string v2)
			{
				var v1Segments = v1.Split('.');
				var v2Segments = v2.Split('.');
				for (var i = 0; i < v1Segments.Count(); i++)
				{
					int v1Segment = int.Parse(v1Segments[i]);
					int v2Segment = 0;
					if (v2Segments.Count() >= i)
						v2Segment = int.Parse(v2Segments[i]);

					if (v1Segment < v2Segment)
						return -1;
					if (v1Segment > v2Segment)
						return 1;
				}
				return 0;
			}
		}

		public ArchiveFolderManager()
		{
			_archiveFolder = Path.Combine(Program.ExeFolder, ArchiveFolder);
			if (!Directory.Exists(_archiveFolder))
				throw new DirectoryNotFoundException("Directory " + _archiveFolder + " not found: have any installers been built?");

			var subFolders = Directory.GetDirectories(_archiveFolder);
			foreach (var path in subFolders)
			{
				var folder = Path.GetFileName(path);
				if (Regex.IsMatch(folder, VersionFolderNamePattern))
					_archiveFolderVersions.Add(folder);
			}

			if (_archiveFolderVersions.Count > 0)
			{
				var comparer = new VersionStringComparer();
				_archiveFolderVersions.Sort(comparer);
			}
		}

		public string GetArchiveFolder(string version)
		{
			return Path.Combine(RootArchiveFolder, version);
		}

		public string RootArchiveFolder
		{
			get { return _archiveFolder; }
		}

		public string LatestVersion
		{
			get { return (_archiveFolderVersions.Count > 0) ? _archiveFolderVersions.Last() : null; }
		}

		public string EarliestVersion
		{
			get { return (_archiveFolderVersions.Count > 0)? _archiveFolderVersions.First() : null; }
		}

		public string GetNthFromEndVersion(int n)
		{
			if (n < 0 || n >= _archiveFolderVersions.Count)
				return null;

			return _archiveFolderVersions[_archiveFolderVersions.Count - 1 - n];
		}

		public int NumArchives
		{
			get { return _archiveFolderVersions.Count;  }
		}

		public void ArchiveFile(string filePath)
		{
			var destFolder = GetArchiveFolder(LatestVersion);
			File.Copy(filePath, Path.Combine(destFolder, filePath));
		}
	}
}
