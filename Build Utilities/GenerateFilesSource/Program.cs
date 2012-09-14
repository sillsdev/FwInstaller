using System.Diagnostics;

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
			// Flag to indicate if we should include all feature-unassigned files in FW_Core:
			var addOrphans = false;

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
					case "addorphans":
						addOrphans = true;
						break;
				}
			}
			var gen = new Generator(report, addOrphans);
			gen.Run();

			if (testIntegrity)
			{
				// Run TestInstallerIntegrity.exe:
				var procIntegrityTester = new Process
				{
					StartInfo =
					{
						FileName = "TestInstallerIntegrity.exe",
					}
				};
				procIntegrityTester.Start();
				procIntegrityTester.WaitForExit();
			}
		}
	}
}
