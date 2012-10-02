using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ArchiveAndBuildPatch
{
	internal class PatchWrapper
	{
		private readonly string m_patchFolder;
		private readonly string m_patchFileNameWithoutExtension;
		private readonly string m_wrapperFileName;
		private readonly string m_zipFileName;
		private readonly string m_batchFilePath;
		private readonly string m_configFilePath;
		private readonly string m_7ZExeFilePath;
		private readonly string m_7ZSfxFilePath;

		public PatchWrapper(string patchPath)
		{
			m_patchFolder = Path.GetDirectoryName(patchPath);
			m_patchFileNameWithoutExtension = Path.GetFileNameWithoutExtension(patchPath);
			var patchPathBase = Path.Combine(m_patchFolder, m_patchFileNameWithoutExtension);

			m_wrapperFileName = m_patchFileNameWithoutExtension + ".exe";
			m_zipFileName = m_patchFileNameWithoutExtension + ".temp.7z";
			m_batchFilePath = patchPathBase + ".temp.bat";
			m_configFilePath = patchPathBase + ".temp.cfg.txt";

			m_7ZExeFilePath = Path.Combine(Program.ExeFolder, "7za.exe");
			m_7ZSfxFilePath = Path.Combine(Program.ExeFolder, "7zSD_new.sfx");
		}

		internal void Wrap()
		{
			// Initiate creation of 7-zip batch file:
			var tso = new StreamWriter(m_batchFilePath);

			tso.WriteLine("@echo off");
			tso.WriteLine(m_patchFolder.Substring(0, 2)); // Make sure we're on the correct drive.
			tso.WriteLine("cd \"" + m_patchFolder + "\"");
			tso.WriteLine("\"" + m_7ZExeFilePath + "\" a \"" + m_zipFileName + "\" + \"" + m_patchFileNameWithoutExtension + ".msp\" -r -mx=9 -mmt=on");

			// Create configuration file to bind to self-extracting archive:
			var swConfig = new StreamWriter(m_configFilePath, false, Encoding.UTF8); // Must be UTF-8
			swConfig.WriteLine(";!@Install@!UTF-8!");
			swConfig.WriteLine("Title=\"SIL FieldWorks Patch Package\"");
			swConfig.WriteLine("HelpText=\"Double-click the file '" + m_patchFileNameWithoutExtension + ".exe' to extract the patch and run it. The patch will be deleted when finished. Run the file with the -nr option to simply extract the patch and leave it. File extraction will be to the same folder where this file is, in both cases.\"");
			swConfig.WriteLine("InstallPath=\"%%S\"");
			swConfig.WriteLine("Delete=\"%%S\\" + m_patchFileNameWithoutExtension + ".msp\"");
			swConfig.WriteLine("ExtractTitle=\"Extracting patch file\"");
			swConfig.WriteLine("ExtractDialogText=\"Preparing the '" + m_patchFileNameWithoutExtension + "' patch for installation\"");
			swConfig.WriteLine("RunProgram=\"msiexec /update \\\"%%S\\" + m_patchFileNameWithoutExtension + ".msp\\\" REINSTALL=ALL REINSTALLMODE=omus\"");
			swConfig.WriteLine(";!@InstallEnd@!");
			swConfig.Close();

			// Add self-extracting module and configuration to launch setup.exe:
			tso.WriteLine("copy /b \"" + m_7ZSfxFilePath + "\" + \"" + m_configFilePath + "\" + \"" + m_zipFileName + "\" \"" + m_wrapperFileName + "\"");
			tso.Close();

			// Run the temporary batch file we have been building:
			var proc7Z = new Process
			{
				StartInfo =
				{
					FileName = "cmd",
					Arguments = "/D /C \"" + m_batchFilePath + "\"",
					UseShellExecute = false
				}
			};
			proc7Z.Start();
			proc7Z.WaitForExit();
			if (proc7Z.ExitCode != 0)
				throw new Exception("Attempt to create 7-zip self-extracting archive failed with error " + proc7Z.ExitCode);

			// Delete the batch and list files:
			File.Delete(m_batchFilePath);
			File.Delete(m_configFilePath);
		}
	}
}