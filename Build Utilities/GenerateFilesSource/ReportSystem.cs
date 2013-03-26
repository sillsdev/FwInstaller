using System;
using System.Diagnostics;
using System.IO;

namespace GenerateFilesSource
{
	class ReportSystem
	{
		public string GeneralReport { get; private set; }
		public string SeriousIssues { get; private set; }
		public string NewFiles { get; private set; }
		public string DeletedFiles { get; private set; }

		public ReportSystem()
		{
			GeneralReport = "";
			SeriousIssues = "";
			NewFiles = "";
			DeletedFiles = "";
		}

		/// <summary>
		/// Adds a line of text to the overall report
		/// </summary>
		/// <param name="line">Text to add to report</param>
		public void AddReportLine(string line)
		{
			lock (GeneralReport)
			{
				GeneralReport += line + Environment.NewLine;
			}
		}

		/// <summary>
		/// Adds a line of text to the SeriousIssues report
		/// </summary>
		/// <param name="line">Text to add to report</param>
		public void AddSeriousIssue(string line)
		{
			lock (SeriousIssues)
			{
				SeriousIssues += line + Environment.NewLine;
			}
		}

		/// <summary>
		/// Used for logging new files that have just been added to the installer.
		/// </summary>
		/// <param name="path">Path of new file</param>
		public void AddNewFile(string path)
		{
			lock (NewFiles)
			{
				NewFiles += "    " + path + Environment.NewLine;
			}
		}

		/// <summary>
		/// Used for logging files that have just been removed from the installer,
		/// typically because they have been deleted.
		/// </summary>
		/// <param name="path">Path of removed file</param>
		public void AddDeletedFile(string path)
		{
			lock (DeletedFiles)
			{
				DeletedFiles += "    " + path + Environment.NewLine;
			}
		}

		/// <summary>
		/// Determines whether there is any installer file activity worth reporting to the user.
		/// </summary>
		/// <param name="includeGeneralReport">true if a general status report is required</param>
		/// <returns>true if there is nothing to report</returns>
		public bool IsReportEmpty(bool includeGeneralReport)
		{
			if (NewFiles.Length > 0)
				return false;

			if (DeletedFiles.Length > 0)
				return false;

			if (SeriousIssues.Length > 0)
				return false;

			if (includeGeneralReport && GeneralReport.Length > 0)
				return false;

			return true;
		}

		/// <summary>
		/// Forms a single string (formatted with newlines) combining all the elements
		/// of the report.
		/// </summary>
		/// <param name="preamble">Optional text to go at the start of the report</param>
		/// <param name="includeGeneralReport">true if a general status report is required</param>
		/// <returns>the text of the full report</returns>
		public string CombineReports(string preamble, bool includeGeneralReport)
		{
			string combinedReport = "";

			if (preamble != null)
				combinedReport += preamble + Environment.NewLine;

			if (NewFiles.Length == 0)
				combinedReport += "No new files." + Environment.NewLine + Environment.NewLine;
			else
			{
				combinedReport += "Added files" + Environment.NewLine;
				combinedReport += "===========" + Environment.NewLine;
				combinedReport += NewFiles + Environment.NewLine;
			}

			if (DeletedFiles.Length == 0)
				combinedReport += "No deleted files." + Environment.NewLine + Environment.NewLine;
			else
			{
				combinedReport += "Deleted files" + Environment.NewLine;
				combinedReport += "=============" + Environment.NewLine;
				combinedReport += DeletedFiles + Environment.NewLine;
			}

			if (SeriousIssues.Length == 0)
				combinedReport += "No serious issues." + Environment.NewLine + Environment.NewLine;
			else
			{
				combinedReport += "Serious Issues" + Environment.NewLine;
				combinedReport += "==============" + Environment.NewLine;
				combinedReport += SeriousIssues + Environment.NewLine;
			}

			if (includeGeneralReport)
			{
				combinedReport += "General Report" + Environment.NewLine;
				combinedReport += "==============" + Environment.NewLine;
				combinedReport += GeneralReport + Environment.NewLine;
			}

			return combinedReport;
		}

		/// <summary>
		/// Displays the report to the user, via a temporary text file
		/// typically opened in NotePad.
		/// </summary>
		/// <param name="preamble">Optional text to go at the start of the report</param>
		/// <param name="includeGeneralReport">true if a general status report is required</param>
		public void DisplayReport(string preamble, bool includeGeneralReport)
		{
			// Save the report to a temporary file, then open it for the user to see:
			var tempFileName = Path.GetTempFileName() + ".txt";

			var reportFile = new StreamWriter(tempFileName);
			reportFile.WriteLine(CombineReports(preamble, includeGeneralReport));
			reportFile.Close();

			var process = Process.Start(tempFileName);

			// Wait for NotePad window to appear before deleting temporary file:
			if (process != null)
				process.WaitForInputIdle(5000);

			File.Delete(tempFileName);
		}
	}
}
