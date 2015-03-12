namespace GenerateFilesSource
{
	class Program
	{

		static void Main(string[] args)
		{
			// Flag to indicate if we should write a report about every decision we make:
			var report = false;
			// Flag to indicate if we should launch the installer integrity tester at the end:
			var testIntegrity = false;

			foreach (var arg in args)
			{
				switch (arg.ToLowerInvariant())
				{
					case "report":
						report = true;
						break;
					case "check":
						testIntegrity = true;
						break;
				}
			}
			var gen = new Generator(report, testIntegrity);
			gen.Run();
		}
	}
}
