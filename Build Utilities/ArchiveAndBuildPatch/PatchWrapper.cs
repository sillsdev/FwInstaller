using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using InstallerBuildUtilities;

namespace ArchiveAndBuildPatch
{
	internal class PatchWrapper
	{
		private readonly string _patchFolder;
		private readonly string _patchFileNameWithoutExtension;
		private readonly string _wrapperFileName;
		private readonly string _zipFileName;
		private readonly string _batchFilePath;
		private readonly string _configFilePath;
		private readonly string _7ZExeFilePath;
		private readonly string _7ZSfxFilePath;

		public PatchWrapper(string patchPath)
		{
			_patchFolder = Path.GetDirectoryName(patchPath);
			_patchFileNameWithoutExtension = Path.GetFileNameWithoutExtension(patchPath);
			var patchPathBase = Path.Combine(_patchFolder, _patchFileNameWithoutExtension);

			_wrapperFileName = _patchFileNameWithoutExtension + ".exe";
			_zipFileName = _patchFileNameWithoutExtension + ".temp.7z";
			_batchFilePath = patchPathBase + ".temp.bat";
			_configFilePath = patchPathBase + ".temp.cfg.txt";

			_7ZExeFilePath = Path.Combine(Program.ExeFolder, "7za.exe");
			_7ZSfxFilePath = Path.Combine(Program.ExeFolder, "7zSD_new.sfx");
		}

		internal void Wrap()
		{
			// Initiate creation of 7-zip batch file:
			var tso = new StreamWriter(_batchFilePath);

			tso.WriteLine("@echo off");
			tso.WriteLine(_patchFolder.Substring(0, 2)); // Make sure we're on the correct drive.
			tso.WriteLine("cd \"" + _patchFolder + "\"");
			tso.WriteLine("\"" + _7ZExeFilePath + "\" a \"" + _zipFileName + "\" + \"" + _patchFileNameWithoutExtension + ".msp\" -r -mx=9 -mmt=on");

			// Create configuration file to bind to self-extracting archive:
			var swConfig = new StreamWriter(_configFilePath, false, Encoding.UTF8); // Must be UTF-8
			swConfig.WriteLine(";!@Install@!UTF-8!");
			swConfig.WriteLine("Title=\"SIL FieldWorks Patch Package\"");
			swConfig.WriteLine("HelpText=\"Double-click the file '" + _patchFileNameWithoutExtension + ".exe' to extract the patch and run it. The patch will be deleted when finished. Run the file with the -nr option to simply extract the patch and leave it. File extraction will be to the same folder where this file is, in both cases.\"");
			swConfig.WriteLine("InstallPath=\"%%S\"");
			swConfig.WriteLine("Delete=\"%%S\\" + _patchFileNameWithoutExtension + ".msp\"");
			swConfig.WriteLine("ExtractTitle=\"Extracting patch file\"");
			swConfig.WriteLine("ExtractDialogText=\"Preparing the '" + _patchFileNameWithoutExtension + "' patch for installation\"");
			swConfig.WriteLine("RunProgram=\"msiexec /p \\\"%%S\\" + _patchFileNameWithoutExtension + ".msp\\\"\"");
			swConfig.WriteLine(";!@InstallEnd@!");
			swConfig.Close();

			// Add self-extracting module and configuration to launch setup.exe:
			tso.WriteLine("copy /b \"" + _7ZSfxFilePath + "\" + \"" + _configFilePath + "\" + \"" + _zipFileName + "\" \"" + _wrapperFileName + "\"");
			tso.Close();

			// Run the temporary batch file we have been building:
			Tools.RunDosCmd("cmd", "/D /C \"" + _batchFilePath + "\"");

			// Delete the batch and list files:
			File.Delete(_batchFilePath);
			File.Delete(_configFilePath);
		}
	}
}