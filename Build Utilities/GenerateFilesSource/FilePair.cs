namespace GenerateFilesSource
{
	internal sealed class FilePair
	{
		public readonly string Path1;
		public readonly string Path2;

		public FilePair(string path1, string path2)
		{
			Path1 = path1;
			Path2 = path2;
		}
	}
}