namespace GenerateFilesSource
{
	internal sealed class FileOmission
	{
		public readonly string RelativePath;
		public readonly string Reason;
		public readonly bool CaseSensitive;

		public FileOmission(string path, string reason)
		{
			RelativePath = path;
			Reason = reason;
			CaseSensitive = false;
		}

		public FileOmission(string path, string reason, bool caseSensitive)
		{
			RelativePath = path;
			Reason = reason;
			CaseSensitive = caseSensitive;
		}
	}
}