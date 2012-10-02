namespace ProcessFiles
{
	class Program
	{
		static void Main(string[] args)
		{
			// Set default build type:
			string buildType = "Release";

			foreach (var arg in args)
			{
				switch (arg.ToLowerInvariant())
				{
					case "debug":
						buildType = "Debug";
						break;
				}
			}
			var fileProcessor = new FileProcessor(buildType);
			fileProcessor.Run();
		}
	}
}
