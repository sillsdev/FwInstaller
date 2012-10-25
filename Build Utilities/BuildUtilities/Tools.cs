using System;
using System.Diagnostics;

namespace InstallerBuildUtilities
{
	public class Tools
	{
		/// <summary>
		/// Collect some details about this build to help distinguish it from
		/// other source control branches etc.
		/// </summary>
		/// <returns>Build details</returns>
		public static string GetBuildDetails(string projRootPath)
		{
			try
			{
				var branch = RunDosCmd("git", "rev-parse --symbolic-full-name --abbrev-ref HEAD", projRootPath);

				var details = "Current source control branch: " + branch + Environment.NewLine;

				return details;
			}
			catch (Exception ex)
			{
				return "Error: could not retrieve current source control branch name: " + ex.Message;
			}
		}

		/// <summary>
		/// Runs the given DOS command. Waits for it to terminate.
		/// </summary>
		/// <param name="cmd">A DOS command</param>
		/// <param name="args">Arguments to be sent to cmd</param>
		/// <param name="workingDirectory">Directory to start execution in</param>
		/// <returns>Any text sent to standard output</returns>
		public static string RunDosCmd(string cmd, string args, string workingDirectory = "")
		{
			var dosError = false;
			string output;
			string error;
			try
			{
				var startInfo = new ProcessStartInfo(cmd)
				{
					WindowStyle = ProcessWindowStyle.Hidden,
					CreateNoWindow = true,
					Arguments = args,
					WorkingDirectory = workingDirectory,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};

				var dosProc = Process.Start(startInfo);
				dosProc.WaitForExit();
				if (dosProc.ExitCode != 0)
					dosError = true;
				output = dosProc.StandardOutput.ReadToEnd();
				error = dosProc.StandardError.ReadToEnd();
			}
			catch (Exception ex)
			{
				throw new Exception("Exception while running this DOS command: " + cmd + Environment.NewLine + ex.Message);
			}

			if (dosError)
				throw new Exception("DOS error while running this command: " + cmd + Environment.NewLine + error);

			return output;
		}

	}
}
