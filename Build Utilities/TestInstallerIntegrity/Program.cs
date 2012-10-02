using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TestInstallerIntegrity
{
	class Program
	{
		static void Main(string[] args)
		{
			// Set default build type:
			string buildType = "Release";
			var silent = false;

			foreach (var arg in args)
			{
				switch (arg.ToLowerInvariant())
				{
					case "debug":
						buildType = "Debug";
						break;
					case "silent":
						silent = true;
						break;
				}
			}
			var tester = new InstallerIntegrityTester(silent, buildType);
			tester.Run();
		}
	}
}
