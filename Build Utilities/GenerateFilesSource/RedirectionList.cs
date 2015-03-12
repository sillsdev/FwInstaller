using System.Collections.Generic;
using System.Linq;

namespace GenerateFilesSource
{
	internal sealed class RedirectionList : List<RedirectionData>
	{
		internal void Add(string sourceFolder, string installerDirId)
		{
			Add(new RedirectionData(sourceFolder, installerDirId));
		}
		internal string Redirection(string source)
		{
			return (from reDir in this where reDir.SourceFolder == source select reDir.InstallerDirId).FirstOrDefault();
		}
	}
}