using System;
using System.Diagnostics;
using System.IO;

namespace InstallerBuildUtilities
{
	public static class Tools
	{
		/// <summary>
		/// Undoes the obfuscation of email addresses stored in publicly-accessible files.
		/// </summary>
		/// <param name="obfuscatedAddress">Obfuscated email address</param>
		/// <returns>Un-obfuscated email address</returns>
		public static string ElucidateEmailAddress(string obfuscatedAddress)
		{
			var emailAddress = obfuscatedAddress.Replace(" underscore ", "_");
			emailAddress = emailAddress.Replace(" at ", "@");
			emailAddress = emailAddress.Replace(" dot ", ".");

			return emailAddress;
		}

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

				output = dosProc.StandardOutput.ReadToEnd();
				error = dosProc.StandardError.ReadToEnd();

				dosProc.WaitForExit();
				if (dosProc.ExitCode != 0)
					dosError = true;
			}
			catch (Exception ex)
			{
				throw new Exception("Exception while running this DOS command: " + cmd + " " + args + Environment.NewLine + ex.Message);
			}

			if (dosError)
				throw new Exception("DOS error while running this command: " + cmd + Environment.NewLine + error);

			return output;
		}

		/// <summary>
		/// Uses the master version file to retrieve the version number built into FieldWorks.
		/// </summary>
		/// <returns>A version number string e.g. "8.0.1"</returns>
		public static string GetFwBuildVersion()
		{
			string fwMajor = null;
			string fwMinor = null;
			string fwRevision = null;

			// Assume the version number is stored in fw\Src\MasterVersionInfo.txt, and that we're in fw\Installer:
			var versionInfoFile = new StreamReader(@"..\Src\MasterVersionInfo.txt");
			var currLine = versionInfoFile.ReadLine();
			while (currLine != null)
			{
				if (currLine.StartsWith("FWMAJOR="))
					fwMajor = currLine.Substring(1 + currLine.IndexOf('=')).Trim();
				else if (currLine.StartsWith("FWMINOR="))
					fwMinor = currLine.Substring(1 + currLine.IndexOf('=')).Trim();
				else if (currLine.StartsWith("FWREVISION="))
					fwRevision = currLine.Substring(1 + currLine.IndexOf('=')).Trim();

				if (fwMajor != null && fwMinor != null && fwRevision != null)
					break;

				currLine = versionInfoFile.ReadLine();
			}

			if (fwMajor == null)
				throw new InvalidDataException(@"Src\MasterVersionInfo.txt does not include definition for FWMAJOR");
			if (fwMinor == null)
				throw new InvalidDataException(@"Src\MasterVersionInfo.txt does not include definition for FWMINOR");
			if (fwRevision == null)
				throw new InvalidDataException(@"Src\MasterVersionInfo.txt does not include definition for FWREVISION");

			return fwMajor + "." + fwMinor + "." + fwRevision;
		}
	}
}
