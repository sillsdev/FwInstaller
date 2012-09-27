using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;

namespace ComponentInstallerGenerator
{
	class InstallerInfo
	{
		// Define names of files we expect to exist:
		private const string DefinintionsFileName = "ComponentInstallerDefinitions.xml";
		private const string FwWxsFileName = "FW.wxs";

		// WIX namespace URI:
		public const string WixNsUri = "http://schemas.microsoft.com/wix/2006/wi";

		// Definitions of installers we can generate:
		private readonly XmlDocument m_xmlDefinitions = new XmlDocument();
		// Version number of main FW installer:
		public string FwVersion { get; private set; }

		public void Init()
		{
			// Make sure we can find all the files we will be reading:
			if (!File.Exists(DefinintionsFileName))
				throw new Exception(DefinintionsFileName + " is missing.");
			if (!File.Exists(FwWxsFileName))
				throw new Exception(FwWxsFileName + " is missing.");

			// Collect the definitions of installers we can generate:
			m_xmlDefinitions.Load(DefinintionsFileName);

			// Get the version number from the main FW WIX source:
			var fwWix = new XmlDocument();
			fwWix.Load(FwWxsFileName);
			// Set up WIX namespace stuff:
			var xmlnsManagerFw = new XmlNamespaceManager(fwWix.NameTable);
			// Add the namespace used in WIX file to the XmlNamespaceManager:
			xmlnsManagerFw.AddNamespace("wix", WixNsUri);

			FwVersion = (fwWix.SelectSingleNode("//wix:Product", xmlnsManagerFw) as XmlElement).GetAttribute("Version");
		}

		/// <summary>
		/// Generates array of user-friendly names of all the installers we can generate.
		/// </summary>
		/// <returns>Array of installer names</returns>
		public string[] GetNamesArray()
		{
			// Read all <name> node data:
			var nameNodes = m_xmlDefinitions.SelectNodes("//Name");
			return (from XmlElement nameNode in nameNodes select nameNode.InnerText).ToArray();
		}

		/// <summary>
		/// Gets the installer component definition for the specified component.
		/// </summary>
		/// <param name="index">Zero-based index from selection combo box</param>
		/// <returns>Xml element of required component</returns>
		public XmlElement GetSelectedComponent(int index)
		{
			// Add 1 to index to use as 1-based node index:
			var node = m_xmlDefinitions.SelectSingleNode("//Component[" + (index + 1) + "]");
			if (node == null)
				throw new Exception("Invalid componentselection index " + index);

			return (XmlElement) node;
		}

		/// <summary>
		/// Creates a text summary of the installer defined by the specified component.
		/// </summary>
		/// <param name="index">Zero-based index from selection combo box</param>
		/// <returns>Plain text summary of installer</returns>
		public string GetSummary(int index)
		{
			var componentNode = GetSelectedComponent(index);

			var summary = "";

			summary += "Building for FieldWorks version: " + FwVersion;
			summary += Environment.NewLine;

			summary += "Description: " + componentNode.SelectSingleNode("Description").InnerText;
			summary += Environment.NewLine;

			summary += "Features: " + componentNode.SelectNodes("Feature").Cast<XmlElement>().Aggregate("", (current, featureNode) => current + (featureNode.InnerText + " "));
			summary += Environment.NewLine;

			summary += "Product Code: " + componentNode.SelectSingleNode("ProductCode").InnerText;

			return summary;
		}
	}

	class InstallerGenerator
	{
		private const string AutoFilesFileName = "AutoFiles.wxs";
		private const string DefaultInstallerTemplateFileName = "ComponentInstallerTemplate.wxs";

		private readonly InstallerInfo m_installerInfo;
		private string m_statusReport;

		public InstallerGenerator(InstallerInfo info)
		{
			m_installerInfo = info;
		}

		public void Init()
		{
			if (!File.Exists(AutoFilesFileName))
				throw new Exception(AutoFilesFileName + " is missing.");
		}

		/// <summary>
		/// For the given component definition, generates one file for each feature included
		/// in the component, with each file being a subset of AutoFiles.wxs
		/// that includes only the files needed for that feature.
		/// </summary>
		/// <param name="definition">The Component node of the definitions XML file whose installer we are building</param>
		/// <returns>The name of the generated file</returns>
		private string GenerateComponentAutoFiles(XmlNode definition)
		{
			var featureNodes = definition.SelectNodes("Feature");
			var featureList = (from XmlElement featureNode in featureNodes select featureNode.InnerText).ToList();
			return GenerateComponentAutoFile(featureList);
		}

		/// <summary>
		/// Generates a file that is a subset of AutoFiles.wxs, where the elements
		/// are all part of the given feature.
		/// </summary>
		/// <param name="features">Main installer features to be included in file</param>
		/// <returns>The file name of the generated file.</returns>
		private string GenerateComponentAutoFile(IList<string> features)
		{
			// Create a working copy of the main AutoFiles.wxs structure:
			var xmlFiles = new XmlDocument();
			xmlFiles.Load(AutoFilesFileName);

			// Set up WIX namespace stuff:
			var xmlnsManager = new XmlNamespaceManager(xmlFiles.NameTable);
			// Add the namespace used in AutoFiles.wxs to the XmlNamespaceManager:
			xmlnsManager.AddNamespace("wix", InstallerInfo.WixNsUri);

			// Iterate over every feature in the main AutoFiles.wxs file:
			var mainFeatureNodes = xmlFiles.SelectNodes("//wix:FeatureRef", xmlnsManager);
			var mainFeatures = (from XmlElement featureNode in mainFeatureNodes select featureNode.GetAttribute("Id")).ToList();
			foreach (var mainFeature in mainFeatures)
			{
				// If the current main feature isn't the list specified by the caller, remove all trace of it:
				if (features.Contains(mainFeature)) continue;

				var condemnedFeatures = xmlFiles.SelectNodes("//wix:Feature[@Id='" + mainFeature + "']", xmlnsManager);
				RemoveNodes(condemnedFeatures);
				condemnedFeatures = xmlFiles.SelectNodes("//wix:FeatureRef[@Id='" + mainFeature + "']", xmlnsManager);
				RemoveNodes(condemnedFeatures);
			}

			// Test every component in our working copy to see if it is referred to in the
			// surviving feature. If it isn't then delete it.
			var componentNodes = xmlFiles.SelectNodes("//wix:Component", xmlnsManager);
			foreach (XmlElement componentNode in componentNodes)
			{
				// Get current component Id:
				var componentId = componentNode.GetAttribute("Id");
				// check against Features:
				var featureCompRef = xmlFiles.SelectSingleNode("//wix:ComponentRef[@Id='" + componentId + "']", xmlnsManager);
				if (featureCompRef == null)
					RemoveNode(componentNode);
			}

			// Remove every <Directory> node that contains no component nodes in any descendants:
			const string condemnedDirNodesXPath = "//wix:Directory[not(descendant::wix:Component) and not(ancestor::wix:Directory[not(descendant::wix:Component)])]";
			var condemnedDirNodes = xmlFiles.SelectNodes(condemnedDirNodesXPath, xmlnsManager);
			RemoveNodes(condemnedDirNodes);

			// Remove every <DirectoryRef> node that contains no component nodes in any descendants:
			const string condemnedDirRefNodesXPath = "//wix:DirectoryRef[not(descendant::wix:Component) and not(ancestor::wix:DirectoryRef[not(descendant::wix:Component)])]";
			var condemnedDirRefNodes = xmlFiles.SelectNodes(condemnedDirRefNodesXPath, xmlnsManager);
			RemoveNodes(condemnedDirRefNodes);

			// Set all DiskId attributes to '1':
			var fileNodes = xmlFiles.SelectNodes("//wix:File", xmlnsManager);
			foreach (XmlElement fileNode in fileNodes)
				fileNode.SetAttribute("DiskId", "1");

			// Save the new XML file:
			var fileName = "AutoFiles_" + features[0] + ".wxs";
			xmlFiles.Save(fileName);

			if (fileNodes.Count == 0)
				Report("WARNING: " + fileName + " contains no files. The files you need for this installer probably do not exist.");

			return fileName;
		}

		/// <summary>
		/// Removes the specified XmlNode.
		/// </summary>
		/// <param name="node"></param>
		private static void RemoveNode(XmlNode node)
		{
			node.SelectSingleNode("..").RemoveChild(node);
		}

		/// <summary>
		/// Removes all the nodes in the specified list.
		/// </summary>
		/// <param name="nodes"></param>
		private static void RemoveNodes(XmlNodeList nodes)
		{
			foreach (XmlElement node in nodes)
				RemoveNode(node);
		}

		/// <summary>
		/// Creates a parent WIX source file from the template.
		/// </summary>
		/// <param name="installerDefinitionElement">The XML definition of the required installer</param>
		/// <returns>The file name of the WIX source</returns>
		private string GenerateParentInstallerFile(XmlElement installerDefinitionElement)
		{
			// Create XML document for main WIX file we will be generating:
			var wixTemplateFileName = DefaultInstallerTemplateFileName;
			var templateNode = installerDefinitionElement.SelectSingleNode("Template");
			if (templateNode != null)
				wixTemplateFileName = templateNode.InnerText;

			if (!File.Exists(wixTemplateFileName))
				throw new Exception(wixTemplateFileName + " is missing.");

			var mainWix = new XmlDocument();
			mainWix.Load(wixTemplateFileName);
			// Set up WIX namespace stuff:
			var xmlnsManager = new XmlNamespaceManager(mainWix.NameTable);
			// Add the namespace used in WIX file to the XmlNamespaceManager:
			xmlnsManager.AddNamespace("wix", InstallerInfo.WixNsUri);

			// Get the main details to apply to the template:
			var name = installerDefinitionElement.SelectSingleNode("Name").InnerText;
			var description = installerDefinitionElement.SelectSingleNode("Description").InnerText;
			var productCode = installerDefinitionElement.SelectSingleNode("ProductCode").InnerText;
			var upgradeCode = installerDefinitionElement.SelectSingleNode("UpgradeCode").InnerText;

			// Apply main details to template:
			var productNode = mainWix.SelectSingleNode("//wix:Product", xmlnsManager) as XmlElement;
			productNode.SetAttribute("Name", "SIL FieldWorks " + m_installerInfo.FwVersion + " " + name);
			productNode.SetAttribute("Id", productCode);
			productNode.SetAttribute("UpgradeCode", upgradeCode);
			productNode.SetAttribute("Version", m_installerInfo.FwVersion);

			var packageNode = mainWix.SelectSingleNode("//wix:Package", xmlnsManager) as XmlElement;
			packageNode.SetAttribute("Description", description);

			// Create a Feature node for each feature:
			var definitionFeatureNodes = installerDefinitionElement.SelectNodes("Feature");
			foreach (XmlElement definitionFeatureNode in definitionFeatureNodes)
			{
				var feature = definitionFeatureNode.InnerText;
				var featureNode = mainWix.CreateElement("Feature", InstallerInfo.WixNsUri);
				featureNode.SetAttribute("Id", feature);
				featureNode.SetAttribute("Title", feature);
				featureNode.SetAttribute("Display", "expand");
				featureNode.SetAttribute("Level", "1");
				featureNode.SetAttribute("AllowAdvertise", "no");

				productNode.AppendChild(featureNode);
			}

			// Save the modified template to a new file name:
			var fileName = name + ".wxs";
			mainWix.Save(fileName);
			return fileName;
		}

		/// <summary>
		/// Runs the given DOS command. Waits for it to terminate.
		/// </summary>
		/// <param name="cmd">A DOS command</param>
		/// <param name="args">Arguments to be sent to cmd</param>
		/// <returns>Any text sent to standard output</returns>
		private string RunDosCmd(string cmd, string args)
		{
			var dosError = false;
			var output = "";
			var error = "";
			try
			{
				var startInfo = new ProcessStartInfo(cmd);
				startInfo.WindowStyle = ProcessWindowStyle.Hidden;
				startInfo.CreateNoWindow = true;
				startInfo.Arguments = args;
				startInfo.UseShellExecute = false;
				startInfo.RedirectStandardOutput = true;
				startInfo.RedirectStandardError = true;

				var dosProc = Process.Start(startInfo);
				dosProc.WaitForExit();
				if (dosProc.ExitCode != 0)
					dosError = true;
				output = dosProc.StandardOutput.ReadToEnd();
				error = dosProc.StandardError.ReadToEnd();
			}
			catch (Exception)
			{
				Report("Error while running this DOS command: " + cmd);
			}

			return dosError ? error + Environment.NewLine + output : "";
		}

		/// <summary>
		/// Uses WIX Candle tool to compile a WIX source file.
		/// </summary>
		/// <param name="fileName">Name of WIX source file</param>
		/// <param name="wixExtensions">Command-line options for WIX extensions</param>
		/// <returns>Any text sent to standard output</returns>
		private void Candle(string fileName, string wixExtensions)
		{
			var candlePath = Path.Combine(Environment.GetEnvironmentVariable("WIX"), "bin\\candle.exe");
			var args = " -nologo -sw1044 " + wixExtensions + "\"" + fileName + "\"";
			var output = RunDosCmd(candlePath, args);
			if (output.ToLowerInvariant() != fileName.ToLowerInvariant() + Environment.NewLine)
				Report(output);
		}

		/// <summary>
		/// Given an index to the user-selected installer from the list in the combo box,
		/// creates the required installer.
		/// </summary>
		/// <param name="index">User-selected installer index</param>
		/// <returns>Any text sent to standard output</returns>
		public string CreateInstaller(int index)
		{
			m_statusReport = "";

			// Get the XML definition of the requested installer:
			var installerDefinitionElement = m_installerInfo.GetSelectedComponent(index);

			// Generate the WIX files sources:
			var autoFilesWix = GenerateComponentAutoFiles(installerDefinitionElement);
			var parentWix = GenerateParentInstallerFile(installerDefinitionElement);

			// Check if any WIX extensions are required in the build:
			var extensionNodes = installerDefinitionElement.SelectNodes("Extension");
			var extensionSyntax = "";
			if (extensionNodes != null)
			{
				foreach (XmlElement extensionNode in extensionNodes)
					extensionSyntax += "-ext \"" +
									   Path.Combine(Path.Combine(Environment.GetEnvironmentVariable("WIX"), "bin"),
													extensionNode.InnerText) +
									   "\" ";
			}

			// Compile each WIX source, recording the names of the output files:
			Candle(parentWix, extensionSyntax);
			Candle(autoFilesWix, "");

			var wixobjFileList = new List<string>
			{
				Path.GetFileNameWithoutExtension(parentWix) + ".wixobj",
				Path.GetFileNameWithoutExtension(autoFilesWix) + ".wixobj"
			};

			// Get the required .msi name:
			var msiFileName = installerDefinitionElement.SelectSingleNode("Installer").InnerText + "." + m_installerInfo.FwVersion.Replace(".", "") + ".msi";

			// Link each WIX intermediate file:
			var wixobjFiles = wixobjFileList.Aggregate("", (current, wixobjFile) => current + (" \"" + wixobjFile + "\""));
			var lightPath = Path.Combine(Environment.GetEnvironmentVariable("WIX"), "bin\\light.exe");
			Report(RunDosCmd(lightPath, " -nologo -sice:ICE40 -sice:ICE48 " + extensionSyntax + wixobjFiles + " -out \"" + msiFileName + "\""));

			if (m_statusReport == "")
			{
				Report("Success!");
				Report("Installer saved as: " + msiFileName);
			}

			return m_statusReport;
		}

		private void Report(string msg)
		{
			if (msg == "")
				return;
			m_statusReport += msg + Environment.NewLine;
		}
	}
}
