namespace GenerateFilesSource
{
	class Program
	{

		static void Main(string[] args)
		{
			// Set default build type:
			string buildType = "Release";
			// Flag to determine if we will reuse last time's output folders for FLEx and TE:
			bool reuseOutput = false;
			// Flag to indicate if we should write a report about every decision we make:
			bool report = false;

			foreach (var arg in args)
			{
				switch (arg.ToLowerInvariant())
				{
					case "debug":
						buildType = "Debug";
						break;
					case "reuse":
						reuseOutput = true;
						break;
					case "report":
						report = true;
						break;
				}
			}
			Generator gen = new Generator(buildType, reuseOutput, report);
			gen.Run();
		}
	}
}
