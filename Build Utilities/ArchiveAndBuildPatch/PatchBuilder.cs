using System;
using System.IO;
using System.Xml;
using InstallerBuildUtilities;

namespace ArchiveAndBuildPatch
{
	class PatchBuilder
	{
		private readonly string _baseVersion;
		private readonly string _updateVersion;

		private readonly string _buildsFolder;
		private readonly string _baseVersionFolder;
		private readonly string _updateVersionFolder;

		private readonly string _wixBinPath;

		private readonly string _startAndEnd;
		private readonly string _baseWixpdbPath;
		private readonly string _updateWixpdbPath;
		private readonly string _wixSourcePath;
		private readonly string _wixObjectPath;
		private readonly string _wixmstPath;
		private readonly string _wixmspPath;
		private readonly string _mspPath;

		public PatchBuilder(string baseVersion, string updateVersion, string buildsFolder)
		{
			_baseVersion = baseVersion;
			_updateVersion = updateVersion;

			_buildsFolder = buildsFolder;
			_baseVersionFolder = Path.Combine(_buildsFolder, baseVersion);
			_updateVersionFolder = Path.Combine(_buildsFolder, updateVersion);

			_wixBinPath = Path.Combine(Environment.GetEnvironmentVariable("WIX") ?? "", "bin");

			_startAndEnd = Program.Squash(baseVersion) + "to" + Program.Squash(_updateVersion);
			var rootPatchName = "Patch" + _startAndEnd;

			_baseWixpdbPath = Path.Combine(_baseVersionFolder, "SetupFW.wixpdb");
			_updateWixpdbPath = Path.Combine(_updateVersionFolder, "SetupFW.wixpdb");
			_wixSourcePath = Path.Combine(_updateVersionFolder, rootPatchName + ".wxs");
			_wixObjectPath = Path.Combine(_updateVersionFolder, rootPatchName + ".wixobj");
			_wixmstPath = Path.Combine(_updateVersionFolder, "Diff" + _startAndEnd + ".wixmst");
			_wixmspPath = Path.Combine(_updateVersionFolder, rootPatchName + ".wixmsp");
			_mspPath = Path.Combine(_updateVersionFolder, rootPatchName + ".msp");
		}

		public string BuildPatch()
		{
			Torch();
			CreateWixSource();
			Candle();
			Light();
			Pyro();

			return _mspPath;
		}

		private static void RunAndOutputCmd(string cmd, string args)
		{
			Console.WriteLine("\"" + cmd + "\" " + args);
			var output = Tools.RunDosCmd(cmd, args);
			Console.WriteLine(output);
		}

		private void Torch()
		{
			var cmd = Path.Combine(_wixBinPath, "torch.exe");
			var args = "-p -xi \"" + _baseWixpdbPath + "\" \"" + _updateWixpdbPath + "\" -out \"" + _wixmstPath + "\"";
			RunAndOutputCmd(cmd, args);
		}

		private void CreateWixSource()
		{
			var wxsPatch = new XmlDocument();

			wxsPatch.LoadXml(
				"<?xml version='1.0'?>" +
				"<Wix xmlns='http://schemas.microsoft.com/wix/2006/wi'>" +
				"	<Patch AllowRemoval='yes' Manufacturer='SIL International' MoreInfoURL='fieldworks.sil.org'" +
				"    DisplayName='SIL FieldWorks Patch " + _baseVersion + " to " + _updateVersion + "' Description='Small Update Patch' Classification='Update'>" +
				"" +
				"		<Media Id='5000' Cabinet='FW.cab' CompressionLevel='high' EmbedCab='yes'>" +
				"			<PatchBaseline Id='FW' />" +
				"		</Media>" +
				"" +
				"		<PatchFamily Id='FwPatchFamily' Version='" + _updateVersion + "' Supersede='yes'>" +
				"		</PatchFamily>" +
				"" +
				"	</Patch>" +
				"</Wix>");

			// Save the new XML file:
			var settings = new XmlWriterSettings { Indent = true };
			var xmlWriter = XmlWriter.Create(_wixSourcePath, settings);
			if (xmlWriter == null)
				throw new Exception("Could not create output file " + _wixSourcePath);

			wxsPatch.Save(xmlWriter);
			xmlWriter.Close();
		}

		private void Candle()
		{
			var cmd = Path.Combine(_wixBinPath, "candle.exe");
			var args = "\"" + _wixSourcePath + "\" -out \"" + _wixObjectPath + "\"";
			RunAndOutputCmd(cmd, args);
		}

		private void Light()
		{
			var cmd = Path.Combine(_wixBinPath, "light.exe");
			var args = "\"" + _wixObjectPath + "\" -out \"" + _wixmspPath + "\"";
			RunAndOutputCmd(cmd, args);
		}

		private void Pyro()
		{
			var cmd = Path.Combine(_wixBinPath, "pyro.exe");
			var args = "-delta \"" + _wixmspPath + "\" -out \"" + _mspPath + "\" -t FW \"" + _wixmstPath + "\"";
			RunAndOutputCmd(cmd, args);
		}
	}
}
