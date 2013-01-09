using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace InstallerBuildUtilities
{
	public class Tools
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
				throw new Exception("Exception while running this DOS command: " + cmd + Environment.NewLine + ex.Message);
			}

			if (dosError)
				throw new Exception("DOS error while running this command: " + cmd + Environment.NewLine + error);

			return output;
		}
	}

	public class FilePatternMatcher
	{
		public static bool PathMatchesPattern(string path, string pattern)
		{
			if (!pattern.Contains("*") && !pattern.Contains("?"))
				return path.Contains(pattern);

			var regex = Convert(pattern);
			return regex.IsMatch(path);
		}

		private static readonly Regex HasQuestionMarkRegEx = new Regex(@"\?", RegexOptions.Compiled);
		private static readonly Regex IlegalCharactersRegex = new Regex("[" + @"\/:<>|" + "\"]", RegexOptions.Compiled);
		private static readonly Regex CatchExtentionRegex = new Regex(@"^\s*.+\.([^\.]+)\s*$", RegexOptions.Compiled);
		private const string NonDotCharacters = @"[^.]*";

		private static Regex Convert(string pattern)
		{
			if (pattern == null)
			{
				throw new ArgumentNullException();
			}
			pattern = pattern.Trim();
			if (pattern.Length == 0)
			{
				throw new ArgumentException("File pattern is empty.");
			}
			if (IlegalCharactersRegex.IsMatch(pattern))
			{
				throw new ArgumentException("File pattern contains illegal characters.");
			}

			var hasExtension = CatchExtentionRegex.IsMatch(pattern);
			var matchExact = false;

			if (HasQuestionMarkRegEx.IsMatch(pattern))
				matchExact = true;
			else if (hasExtension)
				matchExact = CatchExtentionRegex.Match(pattern).Groups[1].Length != 3;

			var regexString = Regex.Escape(pattern);
			regexString = "^" + Regex.Replace(regexString, @"\\\*", ".*");
			regexString = Regex.Replace(regexString, @"\\\?", ".");

			if (!matchExact && hasExtension)
				regexString += NonDotCharacters;

			regexString += "$";

			var regex = new Regex(regexString, RegexOptions.Compiled | RegexOptions.IgnoreCase);
			return regex;
		}
	}
}
