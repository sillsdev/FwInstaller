using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ArchiveAndBuildPatch
{
	class ArchiveFolderManager
	{
		private const string ArchiveFolder = "Archives and Patches";
		private const string VersionFolderNamePattern = @"^([0-9\.]+)$";
		private readonly string m_archiveFolder;
		private string m_firstArchiveFolder;
		private string m_previousArchiveFolder;
		private string m_previousButOneArchiveFolder;
		private string m_currentArchiveFolder;
		private readonly List<string> m_archiveFolderVersions = new List<string>();
		private static int _numTimesCalled;

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
			m_archiveFolder = Path.Combine(Program.ExeFolder, ArchiveFolder);
			if (!Directory.Exists(m_archiveFolder))
				Directory.CreateDirectory(m_archiveFolder);

			var subFolders = Directory.GetDirectories(m_archiveFolder);
			foreach (var path in subFolders)
			{
				var folder = Path.GetFileName(path);
				if (Regex.IsMatch(folder, VersionFolderNamePattern))
					m_archiveFolderVersions.Add(folder);
			}

			if (m_archiveFolderVersions.Count > 0)
			{
				var comparer = new VersionStringComparer();
				m_archiveFolderVersions.Sort(comparer);
				m_firstArchiveFolder = m_archiveFolderVersions.First();
				m_previousArchiveFolder = m_archiveFolderVersions.Last();
			}
			if (m_archiveFolderVersions.Count > 1)
			{
				// The "previous but one" folder is at index Count - 2 in a zero-based list:
				m_previousButOneArchiveFolder = m_archiveFolderVersions[m_archiveFolderVersions.Count - 2];
			}
		}

		public string GetArchiveFolder(string version)
		{
			return Path.Combine(RootArchiveFolder, version);
		}

		public string RootArchiveFolder
		{
			get { return m_archiveFolder; }
		}

		public string EarliestVersion
		{
			get { return m_firstArchiveFolder;  }
		}

		public string PreviousVersion
		{
			get { return m_previousArchiveFolder; }
		}

		public string PreviousButOneVersion
		{
			get { return m_previousButOneArchiveFolder; }
		}

		public int NumArchives
		{
			get { return m_archiveFolderVersions.Count;  }
		}

		public string AddArchiveFolder(string version)
		{
			if (_numTimesCalled != 0)
				throw new Exception(
					"Attempting to archive another installer in the same instance of this application. That is probably not what is required.");

			_numTimesCalled = 1;

			if (!Regex.IsMatch(version, VersionFolderNamePattern))
				throw new Exception("Folder name " + version + " does not conform to expected version number pattern.");

			m_currentArchiveFolder = GetArchiveFolder(version);
			if (Directory.Exists(m_currentArchiveFolder))
				throw new Exception("Folder " + m_currentArchiveFolder +
									" already exists. Current installer may already be archived. Did you forget to increment the installer version number?");

			if (m_previousArchiveFolder != null)
			{
				var comparer = new VersionStringComparer();
				if (comparer.Compare(version, m_previousArchiveFolder) != 1)
					throw new Exception("New version " + version + " is not higher than current highest version (" + m_previousArchiveFolder +
										"). This will not make for a viable patch!");
			}

			Directory.CreateDirectory(m_currentArchiveFolder);

			m_archiveFolderVersions.Add(version);
			if (m_firstArchiveFolder == null)
				m_firstArchiveFolder = m_archiveFolderVersions.First();

			return m_currentArchiveFolder;
		}
	}
}
