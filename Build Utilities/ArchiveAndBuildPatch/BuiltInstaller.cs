using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using WindowsInstaller;


namespace ArchiveAndBuildPatch
{
	class BuiltInstaller
	{
		private const string RawFileName = "SetupFW.msi";
		private const string ArchiveFolderName = "AdminInstall";
		private readonly string m_version;
		private readonly string m_productGuid;

		private readonly XmlDocument m_xmlFwWxs;
		private readonly XmlNamespaceManager m_wixNsManager;
		private string m_archivedMsiPath;

		public BuiltInstaller()
		{
			var fwWxsPath = Path.Combine(Program.ExeFolder, "FW.wxs");
			if (!File.Exists(fwWxsPath))
				throw new Exception("Could not find FW.wxs WIX source file.");

			m_xmlFwWxs = new XmlDocument();
			m_xmlFwWxs.Load(fwWxsPath);

			// Create WIX namespace:
			m_wixNsManager = new XmlNamespaceManager(m_xmlFwWxs.NameTable);
			// Add the namespace used in xmlFwWxs to the XmlNamespaceManager:
			m_wixNsManager.AddNamespace("wix", "http://schemas.microsoft.com/wix/2003/01/wi");

			var productElement = m_xmlFwWxs.SelectSingleNode("//wix:Product", m_wixNsManager) as XmlElement;

			if (productElement == null)
				throw new Exception("Product element is missing from FW.wxs.");

			m_version = productElement.GetAttribute("Version");
			m_productGuid = productElement.GetAttribute("Id");

			// Open current .msi installer:
			// Get the type of the Windows Installer object
			var installerType = Type.GetTypeFromProgID("WindowsInstaller.Installer");

			// Create the Windows Installer object
			var installer = (Installer)Activator.CreateInstance(installerType);

			// Open the MSI database in the input file
			var database = installer.OpenDatabase(RawFileName, MsiOpenDatabaseMode.msiOpenDatabaseModeReadOnly);

			// Open a view on the Property table for the version property
			var view = database.OpenView("SELECT * FROM Property WHERE Property = 'ProductVersion'");

			// Execute the view query
			view.Execute(null);

			// Get the record from the view
			var record = view.Fetch();

			// Get the version from the data
			var msiVersion = record.get_StringData(2);

			// Compare .msi version with version in WIX source, to make sure they are in sync:
			if (msiVersion != m_version)
				throw new Exception("Installer version '" + m_version + "' in WIX source does not match version '" + msiVersion +
									"' in .msi file.");
		}

		public string Version
		{
			get { return m_version; }
		}

		public string VersionSquashed
		{
			get { return Program.Squash(m_version); }
		}

		public string ProductGuid
		{
			get { return m_productGuid; }
		}

		public string CompiledFileName
		{
			get { return RawFileName; }
		}

		public string VersionedFileName
		{
			get { return RawFileName.Replace(".", VersionSquashed + "."); }
		}

		public string ArchiveFolder
		{
			get { return ArchiveFolderName; }
		}

		public List<string> GetExternalCabFileNames()
		{
			var mediaNodes = m_xmlFwWxs.SelectNodes("//wix:Media", m_wixNsManager);

			if (mediaNodes == null)
				throw new Exception("Could not read Media nodes in FW.wxs.");

			return (from XmlElement node in mediaNodes where node.GetAttribute("EmbedCab") == "no" select node.GetAttribute("Cabinet")).ToList();
		}

		public int GetCabFileCount()
		{
			var mediaNodes = m_xmlFwWxs.SelectNodes("//wix:Media", m_wixNsManager);

			if (mediaNodes == null)
				throw new Exception("Could not read Media nodes in FW.wxs.");

			return mediaNodes.Count;
		}

		public void ArchiveInstaller(string folder)
		{
			m_archivedMsiPath = Path.Combine(folder, VersionedFileName);
			File.Copy(Path.Combine(Program.ExeFolder, CompiledFileName), m_archivedMsiPath);

			// Also archive the ProcessedAutoFiles.wxs file. This will help identify folders should they subsequently disappear:
			const string processedAutoFiles = "ProcessedAutoFiles.wxs";
			File.Copy(Path.Combine(Program.ExeFolder, processedAutoFiles), Path.Combine(folder, processedAutoFiles));

			// Also archive the new FileLibrary.xml file:
			var fileLibrary = "";
			var fileLibraryNode = Program.Configuration.SelectSingleNode("//FileLibrary/Library") as XmlElement;
			if (fileLibraryNode != null)
				fileLibrary = fileLibraryNode.GetAttribute("File");

			var fileLibraryPath = Path.Combine(Program.ExeFolder, fileLibrary);
			if (File.Exists(fileLibraryPath))
				File.Copy(fileLibraryPath, Path.Combine(folder, fileLibrary));

			// Also archive the new RegLibrary.xml file (which probably doesn't exist):
			const string regLibrary = "RegLibrary.xml";
			var regLibraryPath = Path.Combine(Program.ExeFolder, regLibrary);
			if (File.Exists(regLibraryPath))
				File.Copy(regLibraryPath, Path.Combine(folder, regLibrary));

			var externalCabFileNames = GetExternalCabFileNames();
			foreach (var fileName in externalCabFileNames)
			{
				var source = Path.Combine(Program.ExeFolder, fileName);
				if (!File.Exists(source))
					continue;
				var destination = Path.Combine(folder, fileName);
				File.Copy(source, destination);
			}
		}

		public void ArchivedAdministrativeInstall(string targetRootFolder)
		{
			var targetFolder = Path.Combine(targetRootFolder, ArchiveFolder);

			if (!Directory.Exists(targetFolder))
				Directory.CreateDirectory(targetFolder);

			var procInstall = new Process
				{
					StartInfo =
					{
						FileName = "msiexec",
						Arguments = "/a \"" + m_archivedMsiPath + "\" TARGETDIR=\"" + targetFolder + "\" /qb",
						UseShellExecute = false
					}
				};
			procInstall.Start();
			procInstall.WaitForExit();
			if (procInstall.ExitCode != 0)
				throw new Exception("The administrative install of " + m_archivedMsiPath + "failed with error code " + procInstall.ExitCode + ".");
		}

		public static string GetRelPathAdminInstallMsi(string version)
		{
			var versionSquashed = Program.Squash(version);
			return Path.Combine(Path.Combine(version, ArchiveFolderName), RawFileName.Replace(".", versionSquashed + "."));
		}
	}
}
