using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;

namespace ArchiveAndBuildPatch
{
	class PatchBuilder
	{
		private readonly BuiltInstaller m_updateBuiltInstaller;
		private readonly ArchiveFolderManager m_archiveFolderManager;

		private readonly string m_baseVersion;
		private readonly string m_updateVersion;

		private readonly string m_wixSourcePath;
		private readonly string m_wixObjectPath;
		private readonly string m_pcpPath;
		private readonly string m_msiMspLogPath;
		private readonly string m_mspPath;
		private readonly string m_mspTempFolderPath;

		public PatchBuilder(string baseVersion, BuiltInstaller updateBuiltInstaller, ArchiveFolderManager archiveFolderManager)
		{
			m_updateBuiltInstaller = updateBuiltInstaller;
			m_archiveFolderManager = archiveFolderManager;

			m_baseVersion = baseVersion;
			m_updateVersion = m_updateBuiltInstaller.Version;

			var updateVersionFolder = archiveFolderManager.GetArchiveFolder(m_updateVersion);
			var startAndEnd = Program.Squash(baseVersion) + "to" + Program.Squash(m_updateVersion);
			var rootPatchName = "Patch" + startAndEnd;

			m_wixSourcePath = Path.Combine(updateVersionFolder, rootPatchName + ".wxs");
			m_wixObjectPath = Path.Combine(updateVersionFolder, rootPatchName + ".wixobj");
			m_msiMspLogPath = Path.Combine(updateVersionFolder, "msimsp" + startAndEnd + ".log");
			m_pcpPath = Path.Combine(updateVersionFolder, rootPatchName + ".pcp");
			m_mspPath = Path.Combine(updateVersionFolder, rootPatchName + ".msp");
			m_mspTempFolderPath = Path.Combine(updateVersionFolder, rootPatchName + ".tmp");
		}

		public string BuildPatch()
		{
			if (File.Exists(m_msiMspLogPath))
				File.Delete(m_msiMspLogPath);

			CreateWixSource();

			Candle();
			Light();
			MsiMsp();

			return m_mspPath;
		}

		private void Candle()
		{
			var procCandle = new Process
				{
					StartInfo =
					{
						FileName = "Candle",
						Arguments = "\"" + m_wixSourcePath + "\" -out \"" + m_wixObjectPath + "\""
					}
				};
			procCandle.Start();
			procCandle.WaitForExit();

			if (procCandle.ExitCode != 0)
				throw new Exception("Could not compile " + m_wixSourcePath);
		}

		private void Light()
		{
			var procLight = new Process
			{
				StartInfo =
					{
						FileName = "Light",
						Arguments = "\"" + m_wixObjectPath + "\" -out \"" + m_pcpPath + "\"",
						UseShellExecute = false
					}
			};
			procLight.Start();
			procLight.WaitForExit();
			if (procLight.ExitCode != 0)
				throw new Exception("Could not link " + m_wixObjectPath);
		}

		private void MsiMsp()
		{
			if (Directory.Exists(m_mspTempFolderPath))
				Program.ForceDeleteDirectory(m_mspTempFolderPath);

			var archivesFolder = m_archiveFolderManager.GetArchiveFolder("");
			var tempFolderRelativePath = m_mspTempFolderPath.Replace(archivesFolder + "\\", "");

			var procMsiMsp = new Process
			{
				StartInfo =
				{
					FileName = "MsiMsp",
					Arguments = "-s \"" + m_pcpPath + "\" -p \"" + m_mspPath + "\" -l \"" + m_msiMspLogPath + "\" -f \"" + tempFolderRelativePath + "\"",
					WorkingDirectory = archivesFolder,
					UseShellExecute = false
				}
			};
			procMsiMsp.Start();
			procMsiMsp.WaitForExit();
			if (procMsiMsp.ExitCode != 0)
				throw new Exception("Could not create patch from " + m_pcpPath);
		}

		private void CreateWixSource()
		{
			var wxsPatch = new XmlDocument();

			string baseMsiPath = Path.Combine((m_archiveFolderManager.RootArchiveFolder),
												BuiltInstaller.GetRelPathAdminInstallMsi(m_baseVersion));
			string updateMsiPath = Path.Combine((m_archiveFolderManager.RootArchiveFolder),
												BuiltInstaller.GetRelPathAdminInstallMsi(m_updateVersion));

			var baseVersionSquashed = Program.Squash(m_baseVersion);
			var updateVersionSquashed = Program.Squash(m_updateVersion);

			wxsPatch.LoadXml(
				"<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
				"<Wix xmlns=\"http://schemas.microsoft.com/wix/2003/01/wi\">" +
				"<PatchCreation" +
				" Id=\"" + Guid.NewGuid().ToString().ToUpperInvariant() + "\"" +
				" AllowMajorVersionMismatches=\"no\"" +
				" AllowProductCodeMismatches=\"no\"" +
				" CleanWorkingFolder=\"no\"" +
				" OutputPath=\"" + m_pcpPath + "\"" +
				" WholeFilesOnly=\"no\">" +

				"<PatchInformation" +
				" Comments=\"Patch for FieldWorks\"" +
				" Compressed=\"yes\"" +
				" Description=\"Patches FieldWorks " + m_baseVersion  + " to " + m_updateVersion + "\"" +
				" Languages=\"1033\"" +
				" Manufacturer=\"SIL International\"" +
				" ShortNames=\"no\"/>" +

				"<PatchMetadata" +
				" AllowRemoval=\"yes\"" +
				" Classification=\"Update\"" +
				" Description=\"Patches FieldWorks " + m_baseVersion + " to " + m_updateVersion + "\"" +
				" DisplayName=\"Patch FieldWorks " + m_updateVersion + "\"" +
				" ManufacturerName=\"SIL International\"" +
				" MoreInfoURL=\"fieldworks.sil.org\"" +
				" TargetProductName=\"SIL FieldWorks\"/>" +

				"<Family" +
				" DiskId=\"" + (1 + m_updateBuiltInstaller.GetCabFileCount()) + "\"" +
				" MediaSrcProp=\"SILFW" + baseVersionSquashed + "_" + updateVersionSquashed + "\"" +
				" Name=\"SILFW" + baseVersionSquashed.First() + "\"" +
				" SequenceStart=\"10000\">" +

				"<UpgradeImage src=\"" + updateMsiPath + "\" Id=\"FW" + updateVersionSquashed + "\">" +
				"<TargetImage src=\"" + baseMsiPath + "\" Order=\"" + (m_archiveFolderManager.NumArchives) + "\" Id=\"FW" + baseVersionSquashed + "\" IgnoreMissingFiles=\"no\" />" +
				"</UpgradeImage>" +
				"</Family>" +

				"<PatchSequence PatchFamily=\"" + m_updateBuiltInstaller.ProductGuid.ToUpperInvariant().Replace("-", "") + "\" " +
					"Sequence=\"" + m_baseVersion + "\"/>" +

				"</PatchCreation>" +
				"</Wix>"
				);

			// Save the new XML file:
			var settings = new XmlWriterSettings { Indent = true };
			var xmlWriter = XmlWriter.Create(m_wixSourcePath, settings);
			if (xmlWriter == null)
				throw new Exception("Could not create output file " + m_wixSourcePath);

			wxsPatch.Save(xmlWriter);
			xmlWriter.Close();
		}
	}
}
