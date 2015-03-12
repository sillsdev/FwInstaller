using System.Collections.Generic;

namespace GenerateFilesSource
{
	/// <summary>
	/// Class to hold details about a file being considered for the installer.
	/// </summary>
	internal sealed class InstallerFile
	{
		internal string Id;
		internal string Name;
		internal string FullPath;
		internal string RelativeSourcePath;
		internal long Size;
		internal string DateTime;
		internal string Version;
		internal string Md5;
		internal string Comment;
		internal string ReasonForRemoval;
		internal string ComponentGuid;
		internal int DiskId;
		internal string DirId;
		internal readonly List<string> Features;
		internal bool OnlyUsedInUnusedFeatures;
		internal bool UsedInComponent;
		internal bool UsedInFeatureRef;

		internal InstallerFile()
		{
			Id = "unknown";
			Name = "unknown";
			RelativeSourcePath = "unknown";
			Size = 0;
			DateTime = "unknown";
			Version = "unknown";
			Md5 = "unknown";

			Comment = "unknown";
			ReasonForRemoval = "";
			ComponentGuid = "unknown";
			DiskId = 0;
			DirId = "";
			Features = new List<string>();
			OnlyUsedInUnusedFeatures = false;
			UsedInComponent = false;
			UsedInFeatureRef = false;
		}

		/// <summary>
		/// We define equality here as the RelativeSourcePaths being identical.
		/// </summary>
		/// <param name="obj">other file to be compared with us</param>
		/// <returns>true if the argument matches our self</returns>
		public override bool Equals(object obj)
		{
			var that = obj as InstallerFile;
			if (that == null)
				return false;
			return RelativeSourcePath == that.RelativeSourcePath;
		}

		public override int GetHashCode()
		{
			return RelativeSourcePath.GetHashCode();
		}

		internal bool FileNameMatches(InstallerFile that)
		{
			if (Name.ToLowerInvariant() == that.Name.ToLowerInvariant())
				return true;
			return false;
		}
	}
}