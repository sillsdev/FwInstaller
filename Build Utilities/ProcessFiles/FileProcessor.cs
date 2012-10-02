using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Security.Cryptography;

namespace ProcessFiles
{
	/// <summary>
	/// Class to process the Files.wxs and AutoFiles.wxs WIX fragments, as follows:
	///
	/// 1) Convert relative source paths to absolute. This typically involves prefixing the existing
	/// source path with "C:\FW\", or wherever the root FW folder happens to be.
	///
	/// 2) THIS STEP IS NO LONGER DONE. [Add registry info. This means COM registration (for non-.NET DLLs), COM interoperability
	/// registration (for .NET DLLs), and COM server registration for a limited number of .EXE files.
	/// Registration is attempted for all DLLs, but the .EXE files to be tested are specified
	/// in this file (hard-coded).]
	///
	/// 3) Add Assembly attribute info, for .NET assemblies.
	///
	/// 4) Make sure each component containing only one file has that file set as the component key.
	///
	/// The processed fragment is written to a new file name: "Processed" + old_file_name.
	/// </summary>
	internal class FileProcessor
	{
		/// <summary>
		/// Adjust data in this function to configure how the file generator works.
		/// </summary>
		private void Configure()
		{
			// Build list of .exe files that need COM registration:
			//m_comExeFiles.Add("WorldPad.exe");

			// Build list of people to email if something goes wrong:
			m_emailList.Add("alistair_imrie@sil.org");
			m_emailList.Add("ken_zook@sil.org");
			m_emailList.Add("ann_bush@sil.org");
		}

		private readonly string m_buildType;
		private const string LogFileName = "ProcessFiles.log";
		private string m_logFilePath;
		private string m_errorReport = "";

		// Important file/folder paths:
		private string m_projRootPath;
		private string m_exeFolder;

		// List of people to email if something goes wrong:
		private readonly List<string> m_emailList = new List<string>();

		// Set up regular expressions to find GUIDs and version numbers:
		private const string GuidRegExText = "({{0,1}([0-9a-fA-F]){8}-([0-9a-fA-F]){4}-([0-9a-fA-F]){4}-([0-9a-fA-F]){4}-([0-9a-fA-F]){12}}{0,1})";
		private const string VersionRegExText = "([0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+)";
		//private const string GuidOrVersionRegExText = GuidRegExText + "|" + VersionRegExText;
		private readonly Regex m_guidRegEx = new Regex(GuidRegExText);
		private readonly Regex m_versionRegEx = new Regex(VersionRegExText);
		//private readonly Regex m_guidOrVersionRegEx = new Regex(GuidOrVersionRegExText);

		// List of .exe files that need COM registration:
		private readonly List<string> m_comExeFiles = new List<string>();

		// Registry Library details:
		private const string RegLibraryName = "RegLibrary.xml";
		private const string RegLibraryAddendaName = "RegLibraryAddenda.xml";
		private readonly XmlDocument m_xmlRegLibrary = new XmlDocument();
		private readonly XmlDocument m_xmlRegLibraryAddenda = new XmlDocument();
		private XmlElement m_xmlRegLibraryElement;
		private XmlElement m_xmlRegLibraryAddendaElement;
		private bool m_editedRegLibrary;

		private XmlDocument m_currentWixFilesSource;
		private XmlNamespaceManager m_currentWixNsManager;
		private const string WixNameSpaceUri = "http://schemas.microsoft.com/wix/2006/wi";

		class SeparateRegCluster
		{
			public readonly string Key;
			public readonly string Root;
			public string CompId;
			public readonly List<XmlElement> Cluster;

			public SeparateRegCluster(string key, string root, XmlElement firstElement)
			{
				Key = key;
				Root = root;
				Cluster = new List<XmlElement> {firstElement};
				CompId = "";
			}
		}

		class ClusterComparer : IComparer<SeparateRegCluster>
		{
			public int Compare(SeparateRegCluster a, SeparateRegCluster b)
			{
				var compIdComparison = a.CompId.CompareTo(b.CompId);
				if (compIdComparison < 0)
					return -1;
				if (compIdComparison > 0)
					return 1;
				return 0;
			}
		}

		public FileProcessor(string buildType)
		{
			m_buildType = buildType;
		}

		internal void Run()
		{
			if (m_buildType.ToLowerInvariant() != "debug" && m_buildType.ToLowerInvariant() != "release")
			{
				ReportError("ERROR - invalid build type: " + m_buildType);
				return;
			}

			Initialize();

			ProcessFiles("Files.wxs");
			ProcessFiles("AutoFiles.wxs");

			OutputModifiedRegLibrary();
			OutputErrorReport();
		}

		private void Initialize()
		{
			// Get FW root path:
			var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
			if (exePath == null)
				throw new Exception("So sorry, don't know where we are!");
			m_exeFolder = Path.GetDirectoryName(exePath);

			// Get development project root path:
			m_projRootPath = m_exeFolder.ToLowerInvariant().EndsWith("installer") ? Path.GetDirectoryName(m_exeFolder) : m_exeFolder;

			// Remove old error report if it exists:
			m_logFilePath = Path.Combine(m_exeFolder, LogFileName);
			if (File.Exists(m_logFilePath))
				File.Delete(m_logFilePath);

			Configure();
			InitRegLibrary();
		}

		private void InitRegLibrary()
		{
			ReadRegLibrary();
			ReadRegLibraryAddenda();
			TransferRegAddendaToLibrary();
		}

		/// <summary>
		/// Sets up Registry Library to read component GUIDs from previous automatic registration clusters.
		/// </summary>
		private void ReadRegLibrary()
		{
			if (File.Exists(RegLibraryName))
			{
				// There is a library file, so load it:
				m_xmlRegLibrary.Load(RegLibraryName);
			}
			else
			{
				// There is no library file:
				m_xmlRegLibrary.LoadXml("<RegLibrary></RegLibrary>");
			}

			m_xmlRegLibraryElement = m_xmlRegLibrary.SelectSingleNode("RegLibrary") as XmlElement;
			if (m_xmlRegLibraryElement == null)
				throw new Exception("There is no RegLibrary element in the Registry Library.");
		}

		/// <summary>
		/// Sets up Registry Library Addenda to read component GUIDs not yet checked in:
		/// </summary>
		private void ReadRegLibraryAddenda()
		{
			if (File.Exists(RegLibraryAddendaName))
			{
				// There is an addenda file, so load it:
				m_xmlRegLibraryAddenda.Load(RegLibraryAddendaName);
			}
			else
			{
				// There is no addenda file, so initiate an XML structure for any addenda we add later:
				m_xmlRegLibraryAddenda.LoadXml("<RegLibrary></RegLibrary>");
			}
			m_xmlRegLibraryAddendaElement = m_xmlRegLibraryAddenda.SelectSingleNode("RegLibrary") as XmlElement;
			if (m_xmlRegLibraryAddendaElement == null)
				throw new Exception("There is no RegLibrary element in the Registry Library Addenda.");
		}

		/// <summary>
		/// Transfers any addenda data into main library structure, so they won't get added again
		/// (except for registry removal nodes).
		/// </summary>
		private void TransferRegAddendaToLibrary()
		{
			var regLibraryAddendaNodes = m_xmlRegLibraryAddendaElement.SelectNodes("Component");
			if (regLibraryAddendaNodes == null)
				throw new Exception("Search for Component elements in Registry Library Addenda returned null.");

			foreach (XmlElement addendaElement in regLibraryAddendaNodes)
			{
				var compId = addendaElement.GetAttribute("Id");
				var compGuid = addendaElement.GetAttribute("ComponentGuid");

				// See if current Addenda node has a match in the main library:
				var matchElement = m_xmlRegLibraryElement.SelectSingleNode("Component[translate(@Id, \"ABCDEFGHIJKLMNOPQRSTUVWXYZ\", \"abcdefghijklmnopqrstuvwxyz\")=\"" + compId.ToLowerInvariant() + "\"]") as XmlElement;
				// If there is a matching node in the main library, we temporarily adjust library
				// record to match the addenda, unless it is a registry removal node, in
				// which case we can add it as an apparent duplicate. This is OK, as the final WIX
				// component will simply contain multiple registry deletion nodes; each will have
				// slightly different data.
				var adjustMainLibStructure = false;
				if (matchElement != null)
					if (matchElement.GetAttribute("Removal") != "true")
						adjustMainLibStructure = true;
				if (adjustMainLibStructure)
				{
					if (matchElement.GetAttribute("ComponentGuid") != compGuid)
					{
						// Error: this node has a matching ID but mismatching GUID with the main library.
						ReportError("ERROR: component " + compId + " exists in both RegLibrary and RegLibraryAddenda, but has a mismatching GUID.");
						continue; // Continue for now.
					}
					// Replace KeyHeader and Root in existing Library node with data from Addenda node:
					matchElement.SetAttribute("KeyHeader", addendaElement.GetAttribute("KeyHeader"));
					matchElement.SetAttribute("Root", addendaElement.GetAttribute("Root"));
				}
				else
				{
					XmlNode addendaClone = m_xmlRegLibrary.ImportNode(addendaElement, true);
					m_xmlRegLibraryElement.InsertBefore(addendaClone, m_xmlRegLibraryElement.FirstChild);
				}
			}
		}

		private void ProcessFiles(string wixSourceFileName)
		{
			// Set up the XML parser, including namespaces that are in WIX:
			m_currentWixFilesSource = new XmlDocument();
			m_currentWixFilesSource.Load(wixSourceFileName);

			// Create WIX namespace:
			m_currentWixNsManager = new XmlNamespaceManager(m_currentWixFilesSource.NameTable);
			// Add the namespace used in m_wixOutput to the XmlNamespaceManager:
			m_currentWixNsManager.AddNamespace("wix", "http://schemas.microsoft.com/wix/2006/wi");

			// Get all file nodes:
			var wixFileNodes = m_currentWixFilesSource.SelectNodes("//wix:File", m_currentWixNsManager);
			if (wixFileNodes == null)
				throw new Exception("Wix source " + wixSourceFileName +
									" cause a null value to be returned when searching for File nodes.");

			foreach (XmlElement fileElement in wixFileNodes)
			{
				// Replace variable ${config} with build type:
				var fileElementSourcePath = Regex.Replace(fileElement.GetAttribute("Source"),
					"\\\\\\$\\{config\\}", "\\" + m_buildType, RegexOptions.IgnoreCase);

				// Prefix the source with our Root folder, unless the source is already an absolute path:
				if (fileElementSourcePath[1] != ':' && fileElementSourcePath[0] != '\\')
				{
					// Source is relative, so add in the Root:
					fileElementSourcePath = Path.Combine(m_projRootPath, fileElementSourcePath);
				}
				fileElement.SetAttribute("Source", fileElementSourcePath);

				// Test if file was specified to omit registration info:
				if (fileElement.GetAttribute("IgnoreRegistration") == "true")
					fileElement.RemoveAttribute("IgnoreRegistration");
				else
				{
					// Run Tallow on qualified files to get any registration data there may be:
					 //AddRegInfo(fileElement); WE'RE NO LONGER COLLECTING COM INFO

					// Add assembly data, where relevant:
					AddAssemblyInfo(fileElement);
				}
				// If there is only one file node in the component, make sure its KeyPath attribute is set to "yes":
				var componentNode = fileElement.SelectSingleNode("..", m_currentWixNsManager);
				if (componentNode == null)
					throw new Exception("File element with source " + fileElementSourcePath + " in file " + wixSourceFileName + " has no parent");

				var componentFileNodes = componentNode.SelectNodes("wix:File", m_currentWixNsManager);
				if (componentFileNodes == null)
					throw new Exception("File element with source " + fileElementSourcePath + " in file " + wixSourceFileName + " has parent with no file nodes!");

				if (componentFileNodes.Count == 1)
					fileElement.SetAttribute("KeyPath", "yes");
			} // Next file

			// Save the new XML file:
			var outputFilePath = "Processed" + wixSourceFileName;
			var settings = new XmlWriterSettings { Indent = true };
			var xmlWriter = XmlWriter.Create(outputFilePath, settings);
			if (xmlWriter == null)
				throw new Exception("Could not create output file " + outputFilePath);

			m_currentWixFilesSource.Save(xmlWriter);
			xmlWriter.Close();

			m_currentWixFilesSource = null;
			m_currentWixNsManager = null;
		}

		/// <summary>
		/// Checks if the given File node is a DLL or EXE. If so, it runs the PEParser.exe utility
		/// to determine if the file is a .NET assembly. If so, three relevant attributes are added.
		/// </summary>
		/// <param name="fileElement">WIX file element describing the file.</param>
		private static void AddAssemblyInfo(XmlElement fileElement)
		{
			var fileSourcePath = fileElement.GetAttribute("Source");

			// Test if the file actually exists:
			if (!File.Exists(fileSourcePath))
				return;

			// Test if we have a DLL or EXE:
			var extension = Path.GetExtension(fileSourcePath).ToLowerInvariant();
			if (extension != ".dll" && extension != ".exe")
				return;

			// Run PEParser on the file:
			var procPeParser = new Process
			{
				StartInfo =
				{
					FileName = "PEParser",
					Arguments = "\"" + fileSourcePath + "\"",
					WindowStyle = ProcessWindowStyle.Hidden
				}
			};
			procPeParser.Start();
			procPeParser.WaitForExit();
			if (procPeParser.ExitCode != 1)
				return;

			// The file is a .NET assembly, so add the attributes:
			var fileId = fileElement.GetAttribute("Id");
			fileElement.SetAttribute("Assembly", ".net");
			fileElement.SetAttribute("AssemblyApplication", fileId);
			fileElement.SetAttribute("AssemblyManifest", fileId);
		}

		/// <summary>
		/// Checks if the given File Element is a DLL or COM EXE. If so, it calls WriteSpecificRegInfo()
		/// to produce any registration info there may be and adds it into the given ComponentNode.
		/// </summary>
		/// <param name="fileElement">WIX file element describing file.</param>
		private void AddRegInfo(XmlElement fileElement)
		{
			var fileSourcePath = fileElement.GetAttribute("Source");

			// Test if the file actually exists:
			if (!File.Exists(fileSourcePath))
				return;

			// Test if we have a DLL:
			if (Path.GetExtension(fileSourcePath).ToLowerInvariant() == ".dll")
			{
				// Get regular COM info:
				AddSpecificRegInfo(fileElement, "-s " + fileSourcePath);
				// Get COM Interop info:
				AddSpecificRegInfo(fileElement, "-c " + fileSourcePath);
			}
			else
			{
				// Test if we have a Type Library:
				if (Path.GetExtension(fileSourcePath).ToLowerInvariant() == ".tlb")
				{
					// Get TLB reg info:
					AddSpecificRegInfo(fileElement, "-t " + fileSourcePath);
				}
				else
				{
					// Test if we have a listed .exe COM server:
					var fileName = Path.GetFileName(fileSourcePath);
					if (m_comExeFiles.Contains(fileName))
						AddSpecificRegInfo(fileElement, "-e " + fileSourcePath + " #-RegRedirect");
				}
			}
		}

		/// <summary>
		/// Calls the WIX Tallow utility to produce specified registration info if it exists.
		/// This then gets written into the given ComponentNode, except where it contains a
		/// GUID or version number, in which case clusters of related registration data
		/// get added to their own new components.
		/// </summary>
		/// <param name="fileElement">WIX file element describing file.</param>
		/// <param name="tallowCmd">Tallow command line arguments.</param>
		private void AddSpecificRegInfo(XmlElement fileElement, string tallowCmd)
		{
			// Get parent node of current file node - the component node:
			var componentNode = fileElement.SelectSingleNode("..", m_currentWixNsManager) as XmlElement;
			if (componentNode == null)
				throw new Exception("File element has no parent element.");

			var xmlTallowOutput = GetTallowOutput(tallowCmd, componentNode);
			if (xmlTallowOutput == null)
				return;

			var regNodes = xmlTallowOutput.SelectNodes("//wix:Registry", m_currentWixNsManager);
			var errorNodes = xmlTallowOutput.SelectNodes("//wix:Error", m_currentWixNsManager);
			if (errorNodes == null)
				throw new Exception("Attempt to retrieve error nodes returned null.");

			// Prepare to store registry nodes that contain a GUID or version number in their Key:
			var separateNodes = new List<SeparateRegCluster>();

			// Add Registry Nodes to Component Node where possible, otherwise cache them in Separate Nodes:
			ProcessRegNodes(regNodes, componentNode, separateNodes, fileElement);

			// Add the Separate Nodes to their own Components:
			ProcessSeparateNodes(separateNodes, fileElement, componentNode);

			// Graft Error nodes into ComponentNode:
			foreach (XmlNode errorNode in errorNodes)
			{
				var clonedErrorNode = m_currentWixFilesSource.ImportNode(errorNode, true);
				componentNode.AppendChild(clonedErrorNode);
			}
		}

		/// <summary>
		/// Adds Registry Nodes to Component Node where possible, otherwise caches them in Separate Nodes.
		/// Filters for known file paths, replacing them with parameterized versions of File Keys.
		/// </summary>
		/// <param name="regNodes">WIX Registry nodes for given file.</param>
		/// <param name="componentNode">WIX Component element (parent of given file).</param>
		/// <param name="separateNodes">Cache of WIX Registry nodes that won't be added under the given file node.</param>
		/// <param name="fileElement">WIX File element of given file.</param>
		private void ProcessRegNodes(XmlNodeList regNodes, XmlElement componentNode, List<SeparateRegCluster> separateNodes, XmlElement fileElement)
		{
			// Get Component ID:
			var compId = componentNode.GetAttribute("Id");
			// Get full path of file:
			var filePath = fileElement.GetAttribute("Source");
			// Get File ID:
			var fileId = fileElement.GetAttribute("Id");

			// Get versions of file path and its folder with both backslashes and forward slashes:
			var sourceFolder = Path.GetDirectoryName(filePath);
			var filePathForward = filePath.Replace("\\", "/");
			var sourceFolderForward = sourceFolder.Replace("\\", "/");

			// Process Registry Nodes one by one:
			foreach (XmlElement currentRegNode in regNodes)
			{
				// See if there is any reason to filter out this reg node:
				if (IsRegNodeBanned(currentRegNode))
					continue;

				// Get raw text of current reg node:
				var regText = currentRegNode.OuterXml;

				// Any occurrence of the '[' character must be replaced with "[\[]"
				// and any occurrence of the ']' character must be replaced with "[\]]".
				// This must be done with an intermediate step, so as not to replace
				// a [\[] sequence already inserted:
				const string tempCloseSequence = "***CLOSE***"; // We'll take a chance that this string does not already appear.
				regText = regText.Replace("]", tempCloseSequence);
				regText = regText.Replace("[", @"[\[]");
				regText = regText.Replace(tempCloseSequence, @"[\]]");

				// Replace any occurrence of the file path with the installer-variable equivalent:
				// Try full path:
				var iFound = regText.ToLowerInvariant().IndexOf(filePath.ToLowerInvariant());
				var newXml = regText;
				if (iFound != -1)
					newXml = regText.Substring(0, iFound) + "[#" + fileId + "]" + regText.Substring(iFound + filePath.Length);
				regText = newXml;

				// Try full path using forward slashes:
				iFound = regText.ToLowerInvariant().IndexOf(filePathForward.ToLowerInvariant());
				if (iFound != -1)
					newXml = regText.Substring(0, iFound) + "[#" + fileId + "]" + regText.Substring(iFound + filePathForward.Length);
				regText = newXml;

				// Try parent folder path:
				iFound = regText.ToLowerInvariant().IndexOf(sourceFolder.ToLowerInvariant());
				if (iFound != -1)
					newXml = regText.Substring(0, iFound) + "[$" + compId + "]" + regText.Substring(iFound + sourceFolder.Length);
				regText = newXml;

				// Try parent folder path using forward slashes:
				iFound = regText.ToLowerInvariant().IndexOf(sourceFolderForward.ToLowerInvariant());
				if (iFound != -1)
					newXml = regText.Substring(0, iFound) + "[$" + compId + "]" + regText.Substring(iFound + sourceFolderForward.Length);
				regText = newXml;

				// Bomb out if the registry data refers to known system registry keys:
				if (FindSystemRegKeys(regText))
				{
					ReportError("COM registration error - system registry data detected: " + fileId + " from " + filePath + ":\n" + regText);
					throw new Exception("Terminating: system registry data detected in file registration. See log file for details.");
				}

				// Load the modified raw XML text into a new MSXML DOM container:
				var newFragment = new XmlDocument();
				newFragment.LoadXml("<?xml version=\"1.0\"?><Wix xmlns=\"http://schemas.microsoft.com/wix/2006/wi\">" + regText + "</Wix>");

				// Get the element for the modified registry data:
				var newRegNode = newFragment.SelectSingleNode("//wix:Registry", m_currentWixNsManager) as XmlElement;
				if (newRegNode == null)
					throw new Exception("Invalid XML fragment; was expecting Registry element: " + regText);

				// If this node contains a GUID or version number in its Key, we must cache it for later processing.
				// Otherwise, we can add it to the specified ComponentNode immediately:
				if (!TestRegNodeForClustering(newRegNode, separateNodes))
				{
					// We didn't cache this reg node in a separate cluster, so graft it onto specified
					// component node. First give it a controlled Id attribute:
					var regId = GenerateRegId(compId, newRegNode);
					newRegNode.SetAttribute("Id", regId);

					var newNodeClone = m_currentWixFilesSource.ImportNode(newRegNode, true) as XmlElement;
					if (newNodeClone == null)
						throw new Exception("Importing Registry node resulted in a null XmlElement.");

					newNodeClone.RemoveAttribute("xmlns");
					componentNode.AppendChild(newNodeClone);
				}
			} // Next Reg Node
		}

		/// <summary>
		/// Forms an Id attribute by hashing the given Component ID and the Root, Key and Name
		/// in the given XML registry fragment, but not including specific GUIDs or version numbers.
		/// </summary>
		/// <param name="compId">WIX Component Id attribute value</param>
		/// <param name="regFragment">WIX Registry element</param>
		/// <returns>Unique Id for the Registry element</returns>
		private string GenerateRegId(string compId, XmlElement regFragment)
		{
			var root = regFragment.GetAttribute("Root") ?? "";
			var key = regFragment.GetAttribute("Key") ?? "";
			var name = regFragment.GetAttribute("Name") ?? "";

			// Test local Key value to see if it contains any GUIDs.
			// If so, replace them with the text "GUID":
			key = m_guidRegEx.Replace(key, "GUID");

			// Test local Key value to see if it contains any version number strings.
			// If so, replace them with the text "VERSION":
			key = m_versionRegEx.Replace(key, "VERSION");

			var digest = CalcMd5(compId + root + key + name);
			return "Reg" + digest;
		}

		/// <summary>
		/// Calculates MD5 hash of given string.
		/// </summary>
		/// <param name="msg">String whose MD5 hash is required.</param>
		/// <returns>string MD5 hash</returns>
		public static string CalcMd5(string msg)
		{
			// We first have to convert the string to an array of bytes:
			byte[] inputBytes = Encoding.UTF8.GetBytes(msg);

			// Now compute the MD5 hash, also as a byte array:
			MD5 md5 = MD5.Create();
			byte[] hashBytes = md5.ComputeHash(inputBytes);

			// Convert the byte array to hexadecimal string:
			var sb = new StringBuilder();
			for (int i = 0; i < hashBytes.Length; i++)
				sb.Append(hashBytes[i].ToString("X2"));

			return sb.ToString();
		}

		/// <summary>
		/// Tests if the given regElement must be clustered into separateNodes because
		/// its key matches the given Regular Expression.
		/// </summary>
		/// <param name="regElement">WIX Registry element</param>
		/// <param name="separateNodes">Collection of registry nodes to be added to new components</param>
		/// <returns>true if regelement was added to separateNodes</returns>
		private bool TestRegNodeForClustering(XmlElement regElement, List<SeparateRegCluster> separateNodes)
		{
			var nodeWasClustered = false; // default return value.
			var key = regElement.GetAttribute("Key");
			if (key != null)
			{
				// Test for match against a GUID:
				var guidMatch = m_guidRegEx.Match(key);

				// Test for match against a version number:
				var versionMatch = m_versionRegEx.Match(key);

				// Act on whichever came later in the Key, the GUID or the version number:
				var matchIndex = Math.Max(guidMatch.Index, versionMatch.Index);

				if (matchIndex >= 0 && (guidMatch.ToString().Length > 0 || versionMatch.ToString().Length > 0))
				{
					// We found a GUID and/or a version number. Get all of the Key up to and
					// including the match, and if that isn't a whole node, include the rest
					// of that node string:
					var nodeEndIndex = key.Substring(matchIndex).IndexOf("\\");
					var matchWithLeadUp = nodeEndIndex == -1 ? key : key.Substring(0, matchIndex + nodeEndIndex);

					// Get the regElement's Root value:
					var root = regElement.GetAttribute("Root");

					// See if this match has been recognized already:
					var found = false;
					foreach (var node in
						separateNodes.Where(node => node.Key == matchWithLeadUp && node.Root == root))
					{
						// This match is not new, so just add this reg node to existing cluster:
						found = true;
						node.Cluster.Add(regElement);
						break;
					}
					if (!found)
					{
						// New discovery - make new cluster:
						var newCluster = new SeparateRegCluster(matchWithLeadUp, root, regElement);
						separateNodes.Add(newCluster);
					}
					nodeWasClustered = true;
				}
			}
			return nodeWasClustered;
		}

		/// <summary>
		/// Searches the given text for registry keys that look like they may be system keys.
		/// This helps us trap attempts to overwrite system registry keys, like FW 4.0 installer did.
		/// </summary>
		/// <param name="regText">WIX Registry element text</param>
		/// <returns>true if given text represents a system registry key</returns>
		private static bool FindSystemRegKeys(string regText)
		{
			if (regText.Contains("CLSID\\{00000"))
				return true;
			if (regText.Contains("Interface\\{00000"))
				return true;

			return false;
		}

		/// <summary>
		/// Returns true if there is some reason why given Registry Node should not be included
		/// in the installer.
		/// </summary>
		/// <param name="regNode">WIX Registry node</param>
		/// <returns>true if there is some reason why given Registry Node should not be included in the installer.</returns>
		private static bool IsRegNodeBanned(XmlElement regNode)
		{
			var root = regNode.GetAttribute("Root");
			var key = regNode.GetAttribute("Key");

			if (root == "HKCR")
			{
				// This appears to be a .NET system registry setting, so we don't want to mess with it:
				// (It should be manually included in the Registry.wxs file.)
				if (key == "Component Categories\\{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}")
					return true;
			}
			return false;
		}

		/// <summary>
		/// Adds all Separate Registry Node Clusters to their own Components, matching
		/// GUIDs with clusters identified in the Registry Library.
		/// </summary>
		/// <param name="separateNodes">Separate Registry elements for the given file.</param>
		/// <param name="fileElement">WIX File element of the file whose registry details are being processed.</param>
		/// <param name="componentNode">The parent component of fileElement.</param>
		private void ProcessSeparateNodes(List<SeparateRegCluster> separateNodes, XmlElement fileElement, XmlElement componentNode)
		{
			// Create a suitable component ID for each cluster:
			var fileNodeId = fileElement.GetAttribute("Id");
			foreach (var node in separateNodes)
				node.CompId = GenerateRegClusterComponentId(node.Cluster, fileNodeId);

			// Sort the SeparateNodes according to the ComponentId:
			// (This makes it easier to view differences in a diff viewer; it doesn't make much difference to the installer.)
			var comparer = new ClusterComparer();
			separateNodes.Sort(comparer);

			// Get overall parent node to which new components will be added:
			var componentParent = componentNode.SelectSingleNode("..", m_currentWixNsManager) as XmlElement;

			// Get all <FeatureRef> nodes which reference the componentNode in the current WIX files source:
			var compId = componentNode.GetAttribute("Id");
			var xPathCompRefs = "//wix:FeatureRef/wix:ComponentRef[translate(@Id, \"ABCDEFGHIJKLMNOPQRSTUVWXYZ\", \"abcdefghijklmnopqrstuvwxyz\")=\"" + compId.ToLowerInvariant() + "\"]/..";
			var featureRefNodes = m_currentWixFilesSource.SelectNodes(xPathCompRefs, m_currentWixNsManager);

			// Graft Separate Node clusters into their own components:
			foreach (var node in separateNodes)
				GraftRegNodesCluster(node, componentParent, componentNode, featureRefNodes);
		}

		/// <summary>
		/// Grafts a group of related WIX Registry elements into their own (new) component in
		/// the current WIX files source.
		/// </summary>
		/// <param name="regNodeGroup">Collection of releated Registry elements.</param>
		/// <param name="componentParent">The parent of the new component.</param>
		/// <param name="immediateSibling">The component before which the new component will be located.</param>
		/// <param name="featureRefNodes">List of FeatureRef elements that must reference the new component</param>
		private void GraftRegNodesCluster(SeparateRegCluster regNodeGroup, XmlElement componentParent,
			XmlNode immediateSibling, XmlNodeList featureRefNodes)
		{
			// Create the WIX component to hold the group of Registry nodes:
			var newComponent = m_currentWixFilesSource.CreateElement("Component", WixNameSpaceUri);
			var newCompId = regNodeGroup.CompId;
			newComponent.SetAttribute("Id", newCompId);

			// Identify the common data for all the registry settings in this group:
			var regKey = regNodeGroup.Key;
			var regRoot = regNodeGroup.Root;

			// Prepare for any registry deletion nodes we might make:
			var wxsRegDelNodes = new List<XmlElement>();

			// Examine RegLibrary to see whether we already have any records of deletion of
			// old data from this component:
			var libDeletionNodeId = "Old" + newCompId;
			var libDeletionNodeGuid = GetPreviousRegDeletionData(libDeletionNodeId, wxsRegDelNodes) ??
									  Guid.NewGuid().ToString().ToUpperInvariant();

			// Get installer Id of Directory in which ComponentParent is located:
			var dirId = componentParent.GetAttribute("Id") ?? "INSTALLDIR";

			// Get list of features as a comma-separated string of values:
			var featureList = GetFeatureList(featureRefNodes);

			// Get GUID for Component ID from RegLibrary, adjusting library as appropriate:
			var guid = GetRegComponentGuid(newCompId, regRoot, regKey, dirId, featureList, wxsRegDelNodes, libDeletionNodeId, libDeletionNodeGuid);
			if (guid == null)
			{
				ReportError("ERROR: no GUID for component " + newCompId);
				guid = "ERROR - Could not find GUID";
			}

			newComponent.SetAttribute("Guid", guid);

			// Add new component to parent node:
			componentParent.InsertBefore(newComponent, immediateSibling);

			// Add individual registry data to new component:
			foreach (var element in regNodeGroup.Cluster)
			{
				var regId = GenerateRegId(newCompId, element);
				element.SetAttribute("Id", regId);
				var elementClone = m_currentWixFilesSource.ImportNode(element, true) as XmlElement;
				if (elementClone == null)
					throw new Exception("Importing Registry node resulted in a null XmlElement.");

				elementClone.RemoveAttribute("xmlns");
				newComponent.AppendChild(elementClone);
			}

			// To make ICE08 pass, we have to add a CreateFolder element:
			var createFolderNode = m_currentWixFilesSource.CreateElement("CreateFolder", WixNameSpaceUri);
			newComponent.AppendChild(createFolderNode);

			// See if we need to create a new component to handle deletion of older registry data:
			var regDelComponent = CreateRegDelCompoment(wxsRegDelNodes, libDeletionNodeId, libDeletionNodeGuid);
			if (regDelComponent != null)
			{
				// Add new registry deletion component into the mix:
				componentParent.InsertBefore(regDelComponent, immediateSibling);
			}

			// Add component reference to feature references:
			foreach (XmlNode featureRefNode in featureRefNodes)
			{
				AddComponentRef(featureRefNode, newCompId);

				if (regDelComponent != null)
				{
					// Add a reference to the old registry data deletion component:
					AddComponentRef(featureRefNode, libDeletionNodeId);
				}
			} // Next feature reference
		}

		/// <summary>
		/// Adds a new ComponentRef element to the given feature reference node in the given WIX xml
		/// document, referencing the given component ID.
		/// </summary>
		/// <param name="featureRefNode">Parent FeatureRef element to hold new element</param>
		/// <param name="componentId">ID of component to create reference for</param>
		private void AddComponentRef(XmlNode featureRefNode, string componentId)
		{
			var newComponentRef = m_currentWixFilesSource.CreateElement("ComponentRef", WixNameSpaceUri);
			newComponentRef.SetAttribute("Id", componentId);
			featureRefNode.AppendChild(newComponentRef);
		}

		/// <summary>
		/// Returns a WIX component contaning all the registry deletion nodes in WxsRegDelNodes.
		/// WIX component created suitable for current WIX files source, and using given ID and GUID.
		/// If there are no entries in WxsRegDelNodes, null is returned.
		/// </summary>
		/// <param name="wxsRegDelNodes">List of registry deleteion nodes</param>
		/// <param name="compId">Component ID required for new component</param>
		/// <param name="compGuid">Component GUID required for new component</param>
		/// <returns>WIX Component element</returns>
		private XmlElement CreateRegDelCompoment(ICollection<XmlElement> wxsRegDelNodes, string compId, string compGuid)
		{
			XmlElement regDelComponent = null;
			if (wxsRegDelNodes.Count > 0)
			{
				// Prepare a new component for registry data removal nodes:
				regDelComponent = m_currentWixFilesSource.CreateElement("Component", WixNameSpaceUri);
				regDelComponent.SetAttribute("Id", compId);
				regDelComponent.SetAttribute("Guid", compGuid);

				foreach (var regDelNode in wxsRegDelNodes)
					regDelComponent.AppendChild(regDelNode);

				// To make ICE08 pass, we have to add a CreateFolder element:
				var createFolderComponent = m_currentWixFilesSource.CreateElement("CreateFolder", WixNameSpaceUri);
				regDelComponent.AppendChild(createFolderComponent);
			}
			return regDelComponent;
		}

		/// <summary>
		/// Returns a string GUID for the given component ID.
		/// If the component already exists in the registry library, then its GUID is returned.
		/// Otherwise a new GUID is created.
		/// If the component already exists in the registry library, but the recorded registry root and key
		/// do not match with the given RegRoot and RegKey, then the library addenda is updated to
		/// reflect this, and a new WIX component is added to the WxsRegDelNodes array such that
		/// the old registry data will be removed from the end user's machine. The component ID
		/// and GUID for that registry removal node is given by DelNodeId and DelNodeGuid.
		/// </summary>
		/// <param name="compId">ID attribute value of WIX Component in question.</param>
		/// <param name="regRoot">Registry root name.</param>
		/// <param name="regKey">Registry key name.</param>
		/// <param name="dirId">ID of WIX component parent directory.</param>
		/// <param name="featureList">List of WIX features referencing coponenent in question.</param>
		/// <param name="wxsRegDelNodes">List of registry deletion elements</param>
		/// <param name="delNodeId"></param>
		/// <param name="delNodeGuid"></param>
		/// <returns>GUID for the WIX component in question.</returns>
		private string GetRegComponentGuid(string compId, string regRoot, string regKey, string dirId, string featureList, List<XmlElement> wxsRegDelNodes, string delNodeId, string delNodeGuid)
		{
			string guid;

			// Test if given component already exists in RegLibrary:
			var selectString = "//Component[translate(@Id, \"ABCDEFGHIJKLMNOPQRSTUVWXYZ\", \"abcdefghijklmnopqrstuvwxyz\")=\"" + compId.ToLowerInvariant() + "\"]";
			var matchingComponent = m_xmlRegLibrary.SelectSingleNode(selectString) as XmlElement;
			if (matchingComponent != null)
			{
				// Given component already known in RegLibrary, so collect its GUID:
				guid = matchingComponent.GetAttribute("ComponentGuid");

				// Fetch library's KeyHeader and Root attributes for this component:
				var libKey = matchingComponent.GetAttribute("KeyHeader");
				var libRoot = matchingComponent.GetAttribute("Root");

				// Test if the key and root values in the RegLibrary match what we were given:
				if (libKey != regKey || libRoot != regRoot)
				{
					// The key and root in the library are different, so we must update the library addenda,
					// and prepare a registry key deletion node to remove previous versions.

					// Get a couple of extra details from the existing library component:
					var matchDirId = matchingComponent.GetAttribute("DirectoryId");
					var matchFeatureList = matchingComponent.GetAttribute("FeatureList");

					// Create a new version of the library component, adjusted to match the given data:
					var newCompElement = m_xmlRegLibraryAddenda.CreateElement("Component");
					newCompElement.SetAttribute("Id", compId); // This remains the same
					newCompElement.SetAttribute("ComponentGuid", guid); // This remains the same
					newCompElement.SetAttribute("KeyHeader", regKey); // This is new
					newCompElement.SetAttribute("Root", regRoot); // This is new
					newCompElement.SetAttribute("DirectoryId", matchDirId); // This remains the same
					newCompElement.SetAttribute("FeatureList", matchFeatureList); // This remains the same

					// Add the new library component to the top of the library addenda:
					var firstAddendaComp = m_xmlRegLibraryAddendaElement.FirstChild;
					m_xmlRegLibraryAddendaElement.InsertBefore(newCompElement, firstAddendaComp);

					// Create a new library component recording deletion of old data:
					var delCompElement = m_xmlRegLibraryAddenda.CreateElement("Component");
					delCompElement.SetAttribute("Removal", "true");
					delCompElement.SetAttribute("Id", delNodeId);
					delCompElement.SetAttribute("ComponentGuid", delNodeGuid);
					delCompElement.SetAttribute("KeyHeader", libKey);
					delCompElement.SetAttribute("Root", libRoot);
					delCompElement.SetAttribute("DirectoryId", matchDirId);
					delCompElement.SetAttribute("FeatureList", matchFeatureList);

					// Add new deletion component to library addenda:
					firstAddendaComp = m_xmlRegLibraryAddendaElement.FirstChild;
					m_xmlRegLibraryAddendaElement.InsertBefore(delCompElement, firstAddendaComp);

					m_editedRegLibrary = true;

					// Create a new WIX deletion node describing this latest data removal:
					var wxsRegDelNode = m_currentWixFilesSource.CreateElement("Registry", WixNameSpaceUri);
					wxsRegDelNode.SetAttribute("Root", libRoot);
					wxsRegDelNode.SetAttribute("Key", libKey);
					wxsRegDelNode.SetAttribute("Action", "removeKeyOnInstall");
					var newRegId = GenerateRegId(delNodeId, wxsRegDelNode);
					var newRegIdIndex = 1 + wxsRegDelNodes.Count(node => node.GetAttribute("Id").Contains(newRegId));
					wxsRegDelNode.SetAttribute("Id", "Del" + newRegIdIndex + newRegId);

					wxsRegDelNodes.Add(wxsRegDelNode);
				}
			}
			else // component not found in RegLibrary
			{
				// Create new library component:
				guid = Guid.NewGuid().ToString().ToUpperInvariant();
				var newCompElement = m_xmlRegLibraryAddenda.CreateElement("Component");
				newCompElement.SetAttribute("Id", compId);
				newCompElement.SetAttribute("ComponentGuid", guid);
				newCompElement.SetAttribute("KeyHeader", regKey);
				newCompElement.SetAttribute("Root", regRoot);
				newCompElement.SetAttribute("DirectoryId", dirId);
				newCompElement.SetAttribute("FeatureList", featureList);

				// Add new component to library addenda:
				var firstAddendaComp = m_xmlRegLibraryAddendaElement.FirstChild;
				m_xmlRegLibraryAddendaElement.InsertBefore(newCompElement, firstAddendaComp);

				m_editedRegLibrary = true;
			}
			return guid;
		}

		/// <summary>
		/// Returns a string of comma separated values, each value being the Id attribute
		/// of a FeatureRef node in the given FeatureRefNodes array.
		/// </summary>
		/// <param name="featureRefNodes">List of WIX FeatureRef elements</param>
		/// <returns>Concatenated string of all FeatureRef elements' Id values</returns>
		private static string GetFeatureList(XmlNodeList featureRefNodes)
		{
			var featureList = "";
			foreach (XmlElement node in featureRefNodes)
			{
				if (featureList.Length > 0)
					featureList += ",";
				featureList += node.GetAttribute("Id");
			}
			return featureList;
		}

		/// <summary>
		/// Examines the xmlLibrary file looking for components with ID matching delCompId.
		/// Any matches lead to a new registry removal WIX node for the current WIX files source.
		/// These WIX nodes are added to the wxsRegDelNodes list.
		/// Returns the component GUID for the given ID, or null if none found.
		/// </summary>
		/// <param name="delCompId">WIX Component Id to search for.</param>
		/// <param name="wxsRegDelNodes">List to add matching finds to.</param>
		/// <returns>Component GUID if match found</returns>
		private string GetPreviousRegDeletionData(string delCompId, ICollection<XmlElement> wxsRegDelNodes)
		{
			string libDeletionNodeGuid = null;

			var libDeletionNodes = m_xmlRegLibrary.SelectNodes("//Component[translate(@Id, \"ABCDEFGHIJKLMNOPQRSTUVWXYZ\", \"abcdefghijklmnopqrstuvwxyz\")=\"" + delCompId.ToLowerInvariant() + "\"]", m_currentWixNsManager);
			if (libDeletionNodes == null)
				throw new Exception("SelectNodes call returned null");

			if (libDeletionNodes.Count > 0)
				libDeletionNodeGuid = ((XmlElement)libDeletionNodes[0]).GetAttribute("ComponentGuid");

			// Iterate through LibDeletionNodes, adding previously-known registry data removal
			// nodes to our array:
			for (var ldn = 0; ldn < libDeletionNodes.Count; ldn++)
			{
				var delNode = libDeletionNodes[ldn] as XmlElement;
				if (delNode == null)
					throw new Exception("libDeletionNodes contains an entry that is not an XmlElement");

				var wxsRegDelNode = m_currentWixFilesSource.CreateElement("Registry", WixNameSpaceUri);
				wxsRegDelNode.SetAttribute("Root", delNode.GetAttribute("Root"));
				wxsRegDelNode.SetAttribute("Key", delNode.GetAttribute("KeyHeader"));
				wxsRegDelNode.SetAttribute("Action", "removeKeyOnInstall");
				wxsRegDelNode.SetAttribute("Id", "Del" + ldn + GenerateRegId(delCompId, wxsRegDelNode));

				wxsRegDelNodes.Add(wxsRegDelNode);
			}
			return libDeletionNodeGuid;
		}

		/// <summary>
		/// Returns a string suitable for use as an ID in a component for a cluster
		/// of similar registry settings (harvested from a file registration).
		/// </summary>
		/// <param name="elements">collection of registry settings (harvested from a file registration).</param>
		/// <param name="fileNodeId">ID of the associated file node.</param>
		/// <returns>String suitable for use as an ID in a component.</returns>
		private string GenerateRegClusterComponentId(IEnumerable<XmlElement> elements, string fileNodeId)
		{
			// Create Component Id by making a string concatenating the associated file ID with
			// all the cluster's registry Root, Key, Name & Value attributes; replacing GUIDs
			// and Version texts with generic "GUID" and "VERSION" strings; then do an MD5 hash
			// of that string:
			var idSourceString = fileNodeId;
			foreach (var element in elements)
			{
				// Get registry Root, Key, Name and Value attributes:
				var root = element.GetAttribute("Root") ?? "";
				var key = element.GetAttribute("Key") ?? "";
				var name = element.GetAttribute("Name") ?? "";
				var value = element.GetAttribute("Value") ?? "";

				// Replace GUIDs with "GUID" and file version strings with "VERSION":
				key = m_guidRegEx.Replace(key, "GUID");
				key = m_versionRegEx.Replace(key, "VERSION");
				name = m_guidRegEx.Replace(name, "GUID");
				name = m_versionRegEx.Replace(name, "VERSION");
				value = m_guidRegEx.Replace(value, "GUID");
				value = m_versionRegEx.Replace(value, "VERSION");

				idSourceString += root + key + name + value;
			}
			return "Com" + CalcMd5(idSourceString);
		}

		/// <summary>
		/// Runs the Tallow utility with the given command line arguments and appends any
		/// error elements to the given component node.
		/// </summary>
		/// <param name="tallowCmd">Command line arguments for Tallow</param>
		/// <param name="componentNode">Component to which error data should be appended.</param>
		/// <returns>XML document of Tallow output</returns>
		private XmlDocument GetTallowOutput(string tallowCmd, XmlElement componentNode)
		{
			// Set up XML parser for Tallow ouput:
			const string tempXmlFile = "temp.xml";
			var xmlTallowOutput = new XmlDocument();

			// Call Tallow to get any COM registry info into a temp file:
			var cmdArgs = "/Q /D /C  Tallow -nologo " + tallowCmd + " >\"" + tempXmlFile + "\"";

			var procTallow = new Process
			{
				StartInfo =
				{
					FileName = "cmd",
					Arguments = cmdArgs,
					WindowStyle = ProcessWindowStyle.Hidden
				}
			};
			procTallow.Start();
			procTallow.WaitForExit();
			if (procTallow.ExitCode != 0)
			{
				var newFragment = new XmlDocument();
				newFragment.LoadXml("<?xml version=\"1.0\"?><Wix xmlns=\"http://schemas.microsoft.com/wix/2006/wi\"><Error GetFilesRegInfo=\"Error encountered while running Tallow with " + tallowCmd + "\" /></Wix>");

				var newNode = newFragment.SelectSingleNode("//wix:Error", m_currentWixNsManager);
				var newNodeClone = m_currentWixFilesSource.ImportNode(newNode, true);
				componentNode.AppendChild(newNodeClone);

				return null;
			}

			xmlTallowOutput.Load(tempXmlFile);
			File.Delete(tempXmlFile);

			return xmlTallowOutput;
		}

		private void OutputModifiedRegLibrary()
		{
			if (m_editedRegLibrary)
			{
				// Save modified RegLibraryAddenda:
				const string filePath = RegLibraryAddendaName;
				var settings = new XmlWriterSettings { Indent = true };
				var xmlWriter = XmlWriter.Create(filePath, settings);
				if (xmlWriter == null)
					throw new Exception("Could not create Addenda file " + filePath);

				m_xmlRegLibraryAddenda.Save(xmlWriter);
				xmlWriter.Close();
			}
		}

		private void OutputErrorReport()
		{
			if (m_errorReport.Length <= 0)
				return;

			if (Environment.MachineName.ToLowerInvariant() == "ls-fwbuilder")
			{
				// Email the report to the key people who need to know:
				var message = new System.Net.Mail.MailMessage();
				foreach (var recipient in m_emailList)
					message.To.Add(recipient);
				message.Subject = "Automatic Report from FW Installer Build";
				message.From = new System.Net.Mail.MailAddress("alistair_imrie@sil.org");
				message.Body = m_errorReport;
				var smtp = new System.Net.Mail.SmtpClient("mail.jaars.org");
				smtp.Send(message);
			}
			else
			{
				var reportFile = new StreamWriter(m_logFilePath);
				reportFile.WriteLine("ProcessFiles Error Report");
				reportFile.WriteLine("=========================");
				reportFile.WriteLine(m_errorReport);
				reportFile.Close();
				Process.Start(m_logFilePath);
			}
		}

		private void ReportError(string error)
		{
			m_errorReport += error + Environment.NewLine;
		}
	}
}