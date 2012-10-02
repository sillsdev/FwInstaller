using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Microsoft.Tools.WindowsInstallerXml.Tools;
using System.IO;

namespace ExtractLibAddenda
{
	internal class LibraryExtractor
	{
		private readonly string m_installerFilePath; // (Relative) path to FieldWorks installer
		private XmlDocument m_installerWxs; // The WIX source of the reverse-engineered FW installer
		private XmlNamespaceManager m_xmlnsManager; // Needed to cope with the WIX namespace in XML

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="installerFilePath"></param>
		public LibraryExtractor(string installerFilePath)
		{
			// Record the path of the FW installer file:
			m_installerFilePath = installerFilePath;
		}

		/// <summary>
		/// The main control method.
		/// </summary>
		public void ExtractFileLibrary()
		{
			// Collect list of <File> nodes from reverse-engineered installer:
			var libraryFiles = GetLibraryFiles();
			// Analyze <File> nodes and produce Library Addenda file:
			var fileLibrary = CreateFileLibrary(libraryFiles);

			// Output the Library Addenda file nicely:
			const string filePath = "__FileLibraryAddenda.xml";
			var settings = new XmlWriterSettings { Indent = true };
			var xmlWriter = XmlWriter.Create(filePath, settings);
			if (xmlWriter == null)
				throw new Exception("Could not create Library file " + filePath);

			fileLibrary.Save(xmlWriter);
			xmlWriter.Close();
		}

		/// <summary>
		/// Reverse-engineers the required FieldWorks installer into WIX source, and returns
		/// a list of all the File elements that aren't in merge modules nor the existing File
		/// Library, nor in existing manually-crafted WIX sources.
		/// </summary>
		/// <returns>Collection of WIX File elements</returns>
		private IEnumerable<XmlElement> GetLibraryFiles()
		{
			// Create list of other WIX sources that contain components we don't
			// want in the library, typically because they come from merge modules:
			var nonLibraryComponentDocuments = new List<XmlDocument>();
			var allWxsFiles = Directory.GetFiles(Program.ExeFolder, "*.wxs", SearchOption.TopDirectoryOnly);
			// We'll consider any WIX source except those that were created automatically:
			foreach (var file in (from wxsFile in allWxsFiles where !wxsFile.Contains("AutoFiles.wxs") select wxsFile))
			{
				// Try and load each WIX source into an XML object:
				var wxs = new XmlDocument();
				bool fileOk;
				try
				{
					wxs.Load(file);
					fileOk = true;
				}
				catch (Exception)
				{
					fileOk = false;
				}
				// If the file wasn't proper XML, just ignore it:
				if (fileOk)
					nonLibraryComponentDocuments.Add(wxs);
			}

			// Add in all the external merge modules, reverse engineered into wix docs:
			var allMergeModules = Directory.GetFiles(Path.Combine(Program.ExeFolder, "ExternalMergeModules"), "*.msm",
													 SearchOption.AllDirectories);
			foreach (var mergeModule in allMergeModules)
			{
				// Reverse engineer current merge module into WIX source:
				var wxs = Dark(mergeModule);
				nonLibraryComponentDocuments.Add(wxs);
			}

			// Get reverse-engineered WIX version of FieldWorks installer:
			m_installerWxs = Dark(m_installerFilePath);
			m_xmlnsManager = new XmlNamespaceManager(m_installerWxs.NameTable);
			m_xmlnsManager.AddNamespace("wix", "http://schemas.microsoft.com/wix/2003/01/wi");

			// Create list of GUIDs of components we don't want in our output addenda file:
			var excludedGuids = new List<string>();

			// Add in component GUIDs from the nonLibraryComponentDocuments:
			foreach (var document in nonLibraryComponentDocuments)
			{
				var xmlnsManager = new XmlNamespaceManager(document.NameTable);
				// Add the namespace used in Files.wxs to the XmlNamespaceManager:
				xmlnsManager.AddNamespace("wix", "http://schemas.microsoft.com/wix/2003/01/wi");

				var componentList = document.SelectNodes("//wix:Component", xmlnsManager);
				foreach (XmlElement component in componentList)
					excludedGuids.Add(component.GetAttribute("Guid").ToUpperInvariant());
			}

			// Add in list of GUIDs from existing FileLibrary.xml:
			if (File.Exists("FileLibrary.xml"))
			{
				var libraryXml = new XmlDocument();
				libraryXml.Load("FileLibrary.xml");
				var fileNodes = libraryXml.SelectNodes("//File");
				foreach (XmlElement fileNode in fileNodes)
					excludedGuids.Add(fileNode.GetAttribute("ComponentGuid"));
			}

			// Build list of files in the FieldWorks installer whose components do not
			// match any of the excluded components:
			var libraryFileList = new List<XmlElement>();
			var libraryFiles = m_installerWxs.SelectNodes("//wix:File", m_xmlnsManager);
			foreach (XmlElement file in libraryFiles)
			{
				var parentComponent = file.ParentNode as XmlElement;
				var guid = parentComponent.GetAttribute("Guid").ToUpperInvariant();
				if (!excludedGuids.Contains(guid))
					libraryFileList.Add(file);
			}

			return libraryFileList;
		}

		/// <summary>
		/// Runs the WIX Dark utility, which reverse-engineers a .msi installer or .msm
		/// merge module into WIX source XML.
		/// </summary>
		/// <param name="installerFilePath">(Relative) path to installer or merge module</param>
		/// <returns>WIX source XML object</returns>
		private static XmlDocument Dark(string installerFilePath)
		{
			var args = new string[2];
			args[0] = installerFilePath;
			// Output WIX source to temporary file:
			args[1] = "___Temp.wxs";

			var dark = new Dark();
			var retVal = dark.Run(args);

			if (retVal != 0)
				throw new Exception("Running Dark with file '" + installerFilePath + "' exited with error " + retVal);

			// Load temporary file into XML object:
			var xmlDoc = new XmlDocument();
			xmlDoc.Load(args[1]);

			// Delete temporary file:
			File.Delete(args[1]);

			return xmlDoc;
		}

		/// <summary>
		/// Analyzes the given collection of WIX File elements and writes a FileLibraryAddenda file
		/// containing the files' details.
		/// </summary>
		/// <param name="libraryFiles">Collection of WIX File elements</param>
		/// <returns>XML object representing file library</returns>
		private XmlDocument CreateFileLibrary(IEnumerable<XmlElement> libraryFiles)
		{
			// Create object to be returned:
			var fileLibrary = new XmlDocument();
			// Give it skeleton content:
			fileLibrary.LoadXml("<FileLibrary></FileLibrary>");
			// Get pointer to outermost node:
			var fileLibraryNode = fileLibrary.SelectSingleNode("FileLibrary") as XmlElement;

			// Analyze each <File> node in turn:
			foreach (var libraryFile in libraryFiles)
			{
				// Get the <File>'s parent <Component>
				var libraryComponent = libraryFile.ParentNode as XmlElement;
				// Get the <Component>'s parent <Direcory>:
				var libraryDirectory = libraryComponent.ParentNode as XmlElement;

				// Create new file library node:
				var newElement = fileLibrary.CreateElement("File");
				// File new node with details:
				newElement.SetAttribute("Path", GetPath(libraryFile));
				newElement.SetAttribute("ComponentGuid", libraryComponent.GetAttribute("Guid").ToUpperInvariant());
				newElement.SetAttribute("ComponentId", libraryComponent.GetAttribute("Id"));
				var shortName = libraryFile.GetAttribute("Name");
				var longName = libraryFile.GetAttribute("LongName");
				if (longName == "")
					longName = shortName;
				newElement.SetAttribute("LongName", longName);
				newElement.SetAttribute("ShortName", shortName);
				newElement.SetAttribute("DirectoryId", libraryDirectory.GetAttribute("Id"));
				newElement.SetAttribute("FeatureList", GetFeatureList(libraryComponent));

				// Add new node into library object:
				fileLibraryNode.InsertAfter(newElement, null);
			}

			return fileLibrary;
		}

		/// <summary>
		/// Helper function to work out list of features in which the given WIX component is
		/// included.
		/// </summary>
		/// <param name="libraryComponent">WIX Component node</param>
		/// <returns>Comma-separated list of installer features.</returns>
		private string GetFeatureList(XmlElement libraryComponent)
		{
			// Assume no features, initially:
			var featureList = "";
			// Get our component's Id:
			var id = libraryComponent.GetAttribute("Id");
			// Get all the features in the FieldWorks installer:
			var featureNodes = m_installerWxs.SelectNodes("//wix:Feature", m_xmlnsManager);
			foreach (XmlElement feature in featureNodes)
			{
				// See if current feature has a reference to our component:
				var compRef = feature.SelectSingleNode("wix:ComponentRef[@Id=\"" + id + "\"]", m_xmlnsManager);
				if (compRef != null)
				{
					// Add current feature Id to output list:
					if (featureList.Length > 0)
						featureList += ",";
					featureList += feature.GetAttribute("Id");
				}
			}
			return featureList;
		}

		/// <summary>
		/// Attempts to work out the source path of the given file.
		/// Unfortunately, this relies on the file still being present in the place
		/// it was when the candidate FW installer was built. Of course, there is
		/// no guarantee about this.
		/// </summary>
		/// <param name="libraryFile">WIX File element</param>
		/// <returns>String representing source path of given file relative to FW main folder</returns>
		private static string GetPath(XmlElement libraryFile)
		{
			// Deduce proper file name:
			var shortName = libraryFile.GetAttribute("Name");
			var name = libraryFile.GetAttribute("LongName");
			if (name == "")
				name = shortName;

			var path = name;

			// Iterate up the <Directory> elements to build the path:
			var reachedTop = false;
			// Start with immediate directory ancestor of our file:
			var directory = libraryFile.ParentNode.ParentNode as XmlElement;
			while (!reachedTop)
			{
				// Various situations can terminate the iteration:
				if (directory.Name == "Product" || directory.GetAttribute("Id") == "PROJECTSDIR" ||
					directory.GetAttribute("Id") == "INSTALLDIR" || directory.GetAttribute("Id") == "TARGETDIR")
				{
					reachedTop = true;

					// This is the folder the user configures for their data. Files in
					// this folder were redirected from DistFiles\ReleaseData:
					if (directory.GetAttribute("Id") == "PROJECTSDIR")
						path = "ReleaseData\\" + path;
				}
				else
				{
					if (directory.Name != "Directory")
						throw new Exception("Expected Directory node, got " + directory.Name + " node instead.");

					// Determine proper Directory name:
					var shortDirName = directory.GetAttribute("Name");
					var dirName = directory.GetAttribute("LongName");
					if (dirName == "")
						dirName = shortDirName;
					if (dirName != "SourceDir")
						path = Path.Combine(dirName, path);

					// Jump to next Directory up the chain:
					directory = directory.ParentNode as XmlElement;
				}
			}

			// We now have a relative path for the file. See if it exists firstly in Output\Release,
			// then failing that, DistFiles, first obtaining main FW folder path (developer machine):
			var projRootPath = Program.ExeFolder.ToLowerInvariant().EndsWith("installer") ?
				Path.GetDirectoryName(Program.ExeFolder) : Program.ExeFolder;

			if (File.Exists(Path.Combine(Path.Combine(projRootPath, "Output\\Release"), path)))
				path = "Output\\${config}\\" + path;
			else
				path = "DistFiles\\" + path;

			return path;
		}

	}
}
