using System.Collections.Generic;

namespace GenerateFilesSource
{
	internal class FileOmissionList : List<FileOmission>
	{
		internal void Add(string path, bool caseSensitive)
		{
			Add(new FileOmission(path, "<Omissions PathPattern=\"" + path + "\">", caseSensitive));
		}
		internal void Add(string path, string reason)
		{
			Add(new FileOmission(path, reason));
		}
	}
}