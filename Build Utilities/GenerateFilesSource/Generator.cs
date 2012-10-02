﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Security.Cryptography;

namespace GenerateFilesSource
{
	public class Generator
	{
		// Controls set via command line:
		private readonly string m_buildType;
		private readonly bool m_reuseOutput;
		private readonly bool m_fReport;
		private string m_report; // Report of significant decisions we make, and anomalies of interest to installer developers.
		private string m_seriousIssues; // Report of problems that could result in bad installer.

		// List of machines which will email people if something goes wrong:
		private readonly List<string> m_emailingMachineNames = new List<string>();
		// List of people to email if something goes wrong:
		private readonly List<string> m_emailList = new List<string>();
		// List of people to email regarding new and deleted files:
		private readonly List<string> m_fileNotificationEmailList = new List<string>();

		// List of mappings of Features to DiskId, so we can assign files to cabinets based on features:
		readonly Dictionary<string, int> m_featureCabinetMappings = new Dictionary<string, int>();

		// Variable used to control file sequencing in patches:
		private int m_newPatchGroup;

		// Important file/folder paths:
		private string m_projRootPath;
		private string m_exeFolder;

		class FileOmission
		{
			public readonly string RelativePath;
			public readonly string Reason;

			public FileOmission(string path, string reason)
			{
				RelativePath = path;
				Reason = reason;
			}
		}
		class FileOmissionList : List<FileOmission>
		{
			public void Add(string path)
			{
				Add(new FileOmission(path, "listed in the Omissions section of InstallerConfig.xml."));
			}
			public void Add(string path, string reason)
			{
				Add(new FileOmission(path, reason));
			}
		}
		// List of file name fragments whose files should be omitted:
		private readonly FileOmissionList m_fileOmissions = new FileOmissionList();

		class FilePair
		{
			public readonly string Path1;
			public readonly string Path2;

			public FilePair(string path1, string path2)
			{
				Path1 = path1;
				Path2 = path2;
			}
		}
		// List of pairs of files whose similarity warnings are to be suppressed:
		private readonly List<FilePair> m_similarFilePairs = new List<FilePair>();
		// List of file patterns of files known to be needed in the core files feature
		// but which are not needed in either TE or FLEx:
		private readonly List<string> m_customCoreFiles = new List<string>();
		// List of paths of executable files that crash and burn if you try to register them:
		private readonly List<string> m_ignoreRegistrations = new List<string>();
		// List of (partial) paths of files that must not be set to read-only:
		private readonly List<string> m_makeWritableList = new List<string>();
		// List of (partial) paths of files that have to have older versions forceably removed prior to installing:
		private readonly List<string> m_forceOverwriteList = new List<string>();
		// List of (partial) paths of files that must be set to never overwrite:
		private readonly List<string> m_neverOverwriteList = new List<string>();
		// List of partial paths of files which are installed only if respective conditions are met:
		private readonly Dictionary<string, string> m_fileSourceConditions = new Dictionary<string, string>();
		// List of regular expressions serving as heuristics for detecting if a file is meant only for TE:
		private readonly List<string> m_TeFileNameHeuristics = new List<string>();
		// List of specific files that may look like TE-only files but aren't really.
		private readonly List<string> m_TeFileNameExceptions = new List<string>();

		// List of WIX source files where installable files are manually defined:
		private readonly List<string> m_wixFileSources = new List<string>();

		// Folders where installable files are to be collected from:
		private string m_builtFilesFolderName;
		private string m_staticFilesFolderName;
		private string m_builtFilesBuildTypeFolder;

		// Paths to folders we will be using:
		private string m_builtFilesFolder;
		private string m_builtFilesFolderFull;
		private string m_builtFilesFolderFlex;
		private string m_builtFilesFolderTe;

		// Set of collected DistFiles, junk filtered out:
		private HashSet<InstallerFile> m_distFilesFiltered;
		// Set of collected built files, junk filtered out:
		private HashSet<InstallerFile> m_builtFilesFiltered;
		// Set of all collected installable files, junk filtered out:
		private HashSet<InstallerFile> m_allFilesFiltered = new HashSet<InstallerFile>();
		// Set of collected installable FLEx files, junk filtered out:
		private HashSet<InstallerFile> m_flexBuiltFilesFiltered;
		// Set of collected installable TE files, junk filtered out:
		private HashSet<InstallerFile> m_teBuiltFilesFiltered;

		// Set of files for FLEx feature:
		private IEnumerable<InstallerFile> m_flexFeatureFiles;
		// Set of files for FlexMovies feature:
		private IEnumerable<InstallerFile> m_flexMoviesFeatureFiles;
		// Set of files for TE feature:
		private IEnumerable<InstallerFile> m_teFeatureFiles;
		// Set of files for FW Core feature:
		private IEnumerable<InstallerFile> m_fwCoreFeatureFiles;
		// Set of all localization files:
		private readonly HashSet<InstallerFile> m_allLocalizationFiles = new HashSet<InstallerFile>();
		// Set of features represented in the Features.wxs file:
		private readonly HashSet<string> m_representedFeatures = new HashSet<string>();

		// File Library details:
		private string m_fileLibraryName;
		private XmlDocument m_xmlFileLibrary;

		private string m_fileLibraryAddendaName;
		private XmlDocument m_xmlPreviousFileLibraryAddenda;
		private XmlDocument m_xmlNewFileLibraryAddenda;
		private XmlNode m_newFileLibraryAddendaNode;

		// The output .wxs file details:
		private string m_autoFilesName;
		private TextWriter m_autoFiles;

		// Tree of directory nodes of installable files:
		private readonly DirectoryTreeNode m_rootDirectory = new DirectoryTreeNode();

		/// <summary>
		/// Calculates MD5 hash of given string.
		/// </summary>
		/// <param name="msg">String whose MD5 hash is required.</param>
		/// <returns>string MD5 hash</returns>
		public static string CalcMD5(string msg)
		{
			// We first have to convert the string to an array of bytes:
			byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(msg);

			// Now compute the MD5 hash, also as a byte array:
			MD5 md5 = MD5.Create();
			byte[] hashBytes = md5.ComputeHash(inputBytes);

			// Convert the byte array to hexadecimal string:
			var sb = new StringBuilder();
			for (int i = 0; i < hashBytes.Length; i++)
			{
				sb.Append(hashBytes[i].ToString("X2"));
			}
			return sb.ToString();
		}

		/// <summary>
		/// Structure to hold details about a file being considered for the installer.
		/// </summary>
		class InstallerFile
		{
			public string Id;
			public string Name;
			public string RelativeSourcePath;
			public long Size;
			public string DateTime;
			public string Version;
			public string Md5;
			public string Comment;
			public string ComponentGuid;
			public int DiskId;
			public int PatchGroup;
			public string DirId;
			public readonly List<string> Features;
			public bool OnlyUsedInUnusedFeatures;
			public bool UsedInComponent;
			public bool UsedInFeatureRef;

			public InstallerFile()
			{
				Id = "unknown";
				Name = "unknown";
				RelativeSourcePath = "unknown";
				Size = 0;
				DateTime = "unknown";
				Version = "unknown";
				Md5 = "unknown";

				Comment = "unknown";
				ComponentGuid = "unknown";
				DiskId = 0;
				PatchGroup = 0; // Initial release files will have PatchGroup 0, those in first update will have Patchgroup 1, etc.
				DirId = "";
				Features = new List<string>();
				OnlyUsedInUnusedFeatures = false;
				UsedInComponent = false;
				UsedInFeatureRef = false;
			}

			/// <summary>
			/// We define equality here as the RelativeSourcePaths being identical.
			/// </summary>
			/// <param name="obj">other file to be compared with us</param>
			/// <returns>true if the argument matches ourself</returns>
			public override bool Equals(object obj)
			{
				var that = obj as InstallerFile;
				if (that == null)
					return false;
				return RelativeSourcePath == that.RelativeSourcePath;
			}

			public override int GetHashCode()
			{
				return RelativeSourcePath.GetHashCode();
			}

			public bool FileNameMatches(InstallerFile that)
			{
				if (Name.ToLowerInvariant() == that.Name.ToLowerInvariant())
					return true;
				return false;
			}
		}

		/// <summary>
		/// Tree structure representing folders that contain files being considered for the installer.
		/// </summary>
		class DirectoryTreeNode
		{
			public readonly HashSet<InstallerFile> LocalFiles;
			public readonly List<DirectoryTreeNode> Children;
			public string Name;
			public string TargetPath;
			public string DirId;
			public bool IsDirReference;

			public DirectoryTreeNode()
			{
				LocalFiles = new HashSet<InstallerFile>();
				Children = new List<DirectoryTreeNode>();
				DirId = "";
				IsDirReference = false;
			}

			/// <summary>
			/// Recursively examines directory tree to see if any used files are in it.
			/// A used file is one which is assigned to at least one feature.
			/// </summary>
			/// <returns>true if any used files are in the tree, otherwise false</returns>
			public bool ContainsUsedFiles()
			{
				if (LocalFiles.Any(file => (file.Features.Count > 0 && !file.OnlyUsedInUnusedFeatures)))
					return true;

				return Children.Any(child => child.ContainsUsedFiles());
			}
		}

		/// <summary>
		/// Data for redirecting folders to elsewhere on user's machine.
		/// </summary>
		class RedirectionData
		{
			public readonly string SourceFolder;
			public readonly string InstallerDirId;

			public RedirectionData(string sourceFolder, string installerDirId)
			{
				SourceFolder = sourceFolder;
				InstallerDirId = installerDirId;
			}
		}
		class RedirectionList : List<RedirectionData>
		{
			public void Add(string sourceFolder, string installerDirId)
			{
				Add(new RedirectionData(sourceFolder, installerDirId));
			}
			public string Redirection(string source)
			{
				return (from reDir in this where reDir.SourceFolder == source select reDir.InstallerDirId).FirstOrDefault();
			}
		}
		private readonly RedirectionList m_folderRedirections = new RedirectionList();
		private readonly List<DirectoryTreeNode> m_redirectedFolders = new List<DirectoryTreeNode>();

		/// <summary>
		/// Data for localization resource file sets.
		/// </summary>
		class LocalizationData
		{
			public readonly string LanguageName;
			public readonly string Folder;
			public IEnumerable<InstallerFile> TeFiles;
			public IEnumerable<InstallerFile> OtherFiles;

			public LocalizationData(string language, string languageCode)
			{
				LanguageName = language;
				Folder = languageCode;
				TeFiles = new HashSet<InstallerFile>();
				OtherFiles = new HashSet<InstallerFile>();
			}
		}
		class LocalizationList : List<LocalizationData>
		{
			public void Add(string languageName, string languageCode)
			{
				Add(new LocalizationData(languageName, languageCode));
			}
		}
		private readonly LocalizationList m_languages = new LocalizationList();

		/// <summary>
		/// Class constructor
		/// </summary>
		/// <param name="buildType">Typically "Debug" or "Release"</param>
		/// <param name="reuseOutput">True if last time's preserved temporary folders should be used instead of a fresh build</param>
		/// <param name="report"></param>
		public Generator(string buildType, bool reuseOutput, bool report)
		{
			m_buildType = buildType;
			m_reuseOutput = reuseOutput;
			m_fReport = report;
		}

		/// <summary>
		/// Performs the actions needed to generate the WIX Auto Files source.
		/// </summary>
		internal void Run()
		{
			Initialize();
			CollectInstallableFiles();
			BuildFeatureFileSets();
			OutputResults();
			DoSanityChecks();

			if (m_fReport)
			{
				// Save the report to a temporary file, then open it for the user to see:
				var tempFileName = Path.GetTempFileName() + ".txt";
				var reportFile = new StreamWriter(tempFileName);
				reportFile.WriteLine("GenerateFilesSource Report");
				reportFile.WriteLine("==========================");
				reportFile.WriteLine(m_report);
				reportFile.Close();
				Process.Start(tempFileName);
				// Wait 10 seconds to give the report a good chance of being opened in NotePad:
				System.Threading.Thread.Sleep(10000);
				File.Delete(tempFileName);
			}
		}

		/// <summary>
		/// Initialize the class properly.
		/// </summary>
		private void Initialize()
		{
			// Get FW root path:
			var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
			if (exePath == null)
				throw new Exception("So sorry, don't know where we are!");
			m_exeFolder = Path.GetDirectoryName(exePath);

			// Get development project root path:
			if (m_exeFolder.ToLowerInvariant().EndsWith("installer"))
				m_projRootPath = m_exeFolder.Substring(0, m_exeFolder.LastIndexOf('\\'));
			else
				m_projRootPath = m_exeFolder;

			// Read in the XML config file:
			ConfigureFromXml();

			// Form paths to folders we will be using:
			m_builtFilesBuildTypeFolder = Path.Combine(m_builtFilesFolderName, m_buildType);
			m_builtFilesFolder = Path.Combine(m_projRootPath, m_builtFilesFolderName);
			m_builtFilesFolderFull = Path.Combine(m_projRootPath, m_builtFilesFolderName + "_FULL");
			m_builtFilesFolderFlex = Path.Combine(m_projRootPath, m_builtFilesFolderName + "_FLEx");
			m_builtFilesFolderTe = Path.Combine(m_projRootPath, m_builtFilesFolderName + "_TE");

			// Delete the temporary folders we created last time:
			if (!m_reuseOutput)
			{
				ForceDeleteDirectory(m_builtFilesFolderFlex);
				ForceDeleteDirectory(m_builtFilesFolderTe);
			}

			// Set up File Library, either from XML file or from scratch if file doesn't exist:
			InitFileLibrary();

			// Get all Files.wxs file nodes and add their source paths to FileOmissions:
			ParseWixFileSources();

			// Get a list of names of all features defined in Features.wxs:
			var xmlFeatures = new XmlDocument();
			xmlFeatures.Load("Features.wxs");

			var xmlnsManager = new XmlNamespaceManager(xmlFeatures.NameTable);
			// Add the namespace used in Features.wxs to the XmlNamespaceManager:
			xmlnsManager.AddNamespace("wix", "http://schemas.microsoft.com/wix/2006/wi");

			var featuresNodes = xmlFeatures.SelectNodes("//wix:Feature", xmlnsManager);
			foreach (XmlElement featureNode in featuresNodes)
				m_representedFeatures.Add(featureNode.GetAttribute("Id"));
		}

		/// <summary>
		/// Reads configuration data from InstallerConfig.xml
		/// </summary>
		private void ConfigureFromXml()
		{
			var configuration = new XmlDocument();
			configuration.Load("InstallerConfig.xml");

			// Define localization languages:
			// Format: <Language Name="*language name*" Id="*folder in output\release where localized DLLs get built*"/>
			var languages = configuration.SelectNodes("//Languages/Language");
			if (languages != null)
				foreach (XmlElement language in languages)
					m_languages.Add(language.GetAttribute("Name"), language.GetAttribute("Id"));

			// Define list of path patterns of files known to be needed in the core files feature but which are not needed in either TE or FLEx:
			// Format: <File PathPattern="*partial path of any file that is not needed by TE or FLEx but is needed in the FW installation*"/>
			var coreFiles = configuration.SelectNodes("//CoreFiles/File");
			if (coreFiles != null)
				foreach (XmlElement file in coreFiles)
					m_customCoreFiles.Add(file.GetAttribute("PathPattern"));

			// Define conditions to apply to specified components:
			// Format: <File Path="*partial path of a file that is conditionally installed*" Condition="*MSI Installer condition that must be true to install file*"/>
			// Beware! This XML is double-interpretted, so for example a 'less than' sign must be represented as &amp;lt;
			var fileConditions = configuration.SelectNodes("//FileConditions/File");
			if (fileConditions != null)
				foreach (XmlElement file in fileConditions)
					m_fileSourceConditions.Add(file.GetAttribute("Path"), file.GetAttribute("Condition"));

			// Define list of file patterns to be filtered out. Any file whose path contains (anywhere) one of these strings will be filtered out:
			// Format: <File PathPattern="*partial path of any file that is not needed in the FW installation*"/>
			var omittedFiles = configuration.SelectNodes("//Omissions/File");
			if (omittedFiles != null)
				foreach (XmlElement file in omittedFiles)
					m_fileOmissions.Add(file.GetAttribute("PathPattern"));

			// Define pairs of file paths known (and allowed) to be similar. This suppresses warnings about omitted files that look like other included files:
			// Format: <SuppressSimilarityWarning Path1="*path of first file of matching pair*" Path2="*path of second file of matching pair*"/>
			var similarFilePairs = configuration.SelectNodes("//Omissions/SuppressSimilarityWarning");
			if (similarFilePairs != null)
				foreach (XmlElement pair in similarFilePairs)
					m_similarFilePairs.Add(new FilePair(pair.GetAttribute("Path1"), pair.GetAttribute("Path2")));

			// Define list of folders that will be redirected to some other folder on the end-user's machine:
			// Format: <Redirect Folder="*folder whose contents will be installed to a different folder*" InstallDir="*MSI Installer folder variable where the affected files will be installed*"/>
			var folderRedirections = configuration.SelectNodes("//FolderRedirections/Redirect");
			if (folderRedirections != null)
				foreach (XmlElement redirects in folderRedirections)
					m_folderRedirections.Add(redirects.GetAttribute("Folder"), redirects.GetAttribute("InstallerDir"));

			// Define list of (partial) paths of files that must not be set to read-only:
			// Format: <File PathPattern="*partial path of any file that must not have the read-only flag set*"/>
			var writableFiles = configuration.SelectNodes("//WritableFiles/File");
			if (writableFiles != null)
				foreach (XmlElement file in writableFiles)
					m_makeWritableList.Add(file.GetAttribute("PathPattern"));

			// Define list of (partial) paths of files that have to have older versions forceably removed prior to installing:
			// Format: <File PathPattern="*partial path of any file that must be installed on top of a pre-existing version, even if that means downgrading*"/>
			var forceOverwriteFiles = configuration.SelectNodes("//ForceOverwrite/File");
			if (forceOverwriteFiles != null)
				foreach (XmlElement file in forceOverwriteFiles)
					m_forceOverwriteList.Add(file.GetAttribute("PathPattern"));

			// Define list of paths of executable files that crash and burn if you try to register them:
			// Format: <File Path="*relative path from FW folder of a file that registration should not be attempted on*"/>
			var ignoreRegistrations = configuration.SelectNodes("//IgnoreRegistration/File");
			if (ignoreRegistrations != null)
				foreach (XmlElement file in ignoreRegistrations)
					m_ignoreRegistrations.Add(file.GetAttribute("Path"));

			// Define list of regular expressions serving as heuristics for detecting if a file is meant only for TE:
			// Format: <Heuristic RegExp="*regular expression matching paths of files that belong exclusively to TE*"/>
			var teHeuristics = configuration.SelectNodes("//TeFileNameHeuristics/Heuristic");
			if (teHeuristics != null)
				foreach (XmlElement heuristic in teHeuristics)
					m_TeFileNameHeuristics.Add(heuristic.GetAttribute("RegExp"));

			// Define list of specific files that may look like TE-only files but aren't really:
			// Format: <File Path="*relative path from FW folder of a file looks like it is TE-exclusive but isn't*"/>
			var teFileNameExceptions = configuration.SelectNodes("//NotTeOnly/File");
			if (teFileNameExceptions != null)
				foreach (XmlElement file in teFileNameExceptions)
					m_TeFileNameExceptions.Add(file.GetAttribute("Path"));

			// Define list of machines to email people if something goes wrong:
			// Format: <EmailingMachine Name="*name of machine (within current domain) which is required to email people if there is a problem*"/>
			var failureNotification = configuration.SelectSingleNode("//FailureNotification");
			if (failureNotification != null)
			{
				// Define list of machines to email people if something goes wrong:
				// Format: <EmailingMachine Name="*name of machine (within current domain) which is required to email people if there is a problem*"/>
				var emailingMachines = failureNotification.SelectNodes("EmailingMachine");
				if (emailingMachines != null)
					foreach (XmlElement emailingMachine in emailingMachines)
						m_emailingMachineNames.Add(emailingMachine.GetAttribute("Name"));

				// Define list of people to email if something goes wrong:
				// Format: <Recipient Email="*email address of someone to notify if there is a problem*"/>
				var failureReportRecipients = failureNotification.SelectNodes("Recipient");
				if (failureReportRecipients != null)
					foreach (XmlElement recipient in failureReportRecipients)
					{
						var address = recipient.GetAttribute("Email");
						m_emailList.Add(address);
						// Test if this recipient is to be emailed regarding new and deleted files:
						if (recipient.GetAttribute("NotifyFileChanges") == "true")
							m_fileNotificationEmailList.Add(address);
					}
			}

			var sourceFolders = configuration.SelectSingleNode("//FilesSources");
			if (sourceFolders != null)
			{
				// Define folders from which files are to be collected for the installer:
				// (Only two entries are possible: BuiltFiles and StaticFiles)
				var builtFiles = sourceFolders.SelectSingleNode("BuiltFiles") as XmlElement;
				if (builtFiles != null)
					m_builtFilesFolderName = builtFiles.GetAttribute("Path");
				var staticFiles = sourceFolders.SelectSingleNode("StaticFiles") as XmlElement;
				if (staticFiles != null)
					m_staticFilesFolderName = staticFiles.GetAttribute("Path");

				// Define list of WIX files that list files explicitly:
				// Format: <WixSource File="*name of WIX source file in Installer folder that contains <File> definitions inside <Component> definitions*"/>
				var wixSources = sourceFolders.SelectNodes("WixSource");
				foreach (XmlElement wixSource in wixSources)
					m_wixFileSources.Add(wixSource.GetAttribute("File"));

				// Define list of files that list merge modules to be included. (The files in any merge modules will be taken into account.)
				// Format: <MergeModules File="*name of WIX source file in Installer folder that contains <Merge> definitions*"/>
				// Merge modules defined in such files must have WIX source file names formed by substituting the .msm extension with .mm.wxs
				var mergeModuleSources = sourceFolders.SelectNodes("MergeModules");
				foreach (XmlElement mergeModuleSource in mergeModuleSources)
				{
					var mergeModuleSourceFile = mergeModuleSource.GetAttribute("File");
					var mergeModuleWxs = new XmlDocument();
					mergeModuleWxs.Load(mergeModuleSourceFile);
					var xmlnsManager = new XmlNamespaceManager(mergeModuleWxs.NameTable);
					// Add the namespace used in Files.wxs to the XmlNamespaceManager:
					xmlnsManager.AddNamespace("wix", "http://schemas.microsoft.com/wix/2006/wi");

					var mergeModules = mergeModuleWxs.SelectNodes("//wix:Merge", xmlnsManager);
					foreach (XmlElement mergeModule in mergeModules)
					{
						var msmFilePath = mergeModule.GetAttribute("SourceFile");
						var mergeModuleFile = Path.Combine(m_projRootPath, msmFilePath);
						var mergeModuleWixFile =
							Path.Combine(Path.GetDirectoryName(mergeModuleFile), Path.GetFileNameWithoutExtension(mergeModuleFile)) +
							".mm.wxs";
						if (File.Exists(mergeModuleWixFile))
							m_wixFileSources.Add(mergeModuleWixFile);
					}
				}
			}

			// Define File Library details:
			// (Only two entries are possible: Library and Addenda)
			var fileLibrary = configuration.SelectSingleNode("//FileLibrary");
			if (fileLibrary != null)
			{
				var fileLibraryName = fileLibrary.SelectSingleNode("Library") as XmlElement;
				if (fileLibraryName != null)
					m_fileLibraryName = fileLibraryName.GetAttribute("File");
				var fileLibraryAddendaName = fileLibrary.SelectSingleNode("Addenda") as XmlElement;
				if (fileLibraryAddendaName != null)
					m_fileLibraryAddendaName = fileLibraryAddendaName.GetAttribute("File");
			}

			// Define output file:
			var outputFile = configuration.SelectSingleNode("//Output") as XmlElement;
			if (outputFile != null)
				m_autoFilesName = outputFile.GetAttribute("File");

			// Define file cabinet index allocations:
			var cabinetAssignments = configuration.SelectSingleNode("//CabinetAssignments");
			if (cabinetAssignments != null)
			{
				var defaultAssignment = cabinetAssignments.SelectSingleNode("Default") as XmlElement;
				if (defaultAssignment != null)
					m_featureCabinetMappings.Add("Default", int.Parse(defaultAssignment.GetAttribute("CabinetIndex")));

				var assignments = cabinetAssignments.SelectNodes("Cabinet");
				foreach (XmlElement assignment in assignments)
					m_featureCabinetMappings.Add(assignment.GetAttribute("Feature"), int.Parse(assignment.GetAttribute("CabinetIndex")));
			}
		}

		public static void ForceDeleteDirectory(string path)
		{
			if (!Directory.Exists(path))
				return;

			DirectoryInfo fol;
			var fols = new Stack<DirectoryInfo>();
			var root = new DirectoryInfo(path);
			fols.Push(root);
			while (fols.Count > 0)
			{
				fol = fols.Pop();
				fol.Attributes = fol.Attributes & ~(FileAttributes.Archive | FileAttributes.ReadOnly | FileAttributes.Hidden);
				foreach (var d in fol.GetDirectories())
				{
					fols.Push(d);
				}
				foreach (var f in fol.GetFiles())
				{
					f.Attributes = f.Attributes & ~(FileAttributes.Archive | FileAttributes.ReadOnly | FileAttributes.Hidden);
					f.Delete();
				}
			}
			root.Delete(true);
		}

		/// <summary>
		/// Sets up File Library, either from an XML file or from scratch if the file doesn't exist.
		/// Also sets up Previous File Library Addenda in the same way, and New File Library Addenda from scratch.
		/// </summary>
		private void InitFileLibrary()
		{
			m_xmlFileLibrary = new XmlDocument();
			var libraryPath = LocalFileFullPath(m_fileLibraryName);
			if (File.Exists(libraryPath))
				m_xmlFileLibrary.Load(libraryPath);
			else
				m_xmlFileLibrary.LoadXml("<FileLibrary>\r\n</FileLibrary>");

			// Find the highest PatchGroup value so far so that we can know what PatchGroup
			// to assign to any new files. (This has to be iterated with XPath 1.0):
			var libraryFileNodes = m_xmlFileLibrary.SelectNodes("//File");
			if (libraryFileNodes != null)
			{
				AddReportLine("File Library contains " + libraryFileNodes.Count + " items.");
				int maxPatchGroup = -1;
				foreach (XmlElement libraryFileNode in libraryFileNodes)
				{
					var group = libraryFileNode.GetAttribute("PatchGroup");
					if (group.Length > 0)
					{
						int currentPatchGroup = Int32.Parse(group);
						if (currentPatchGroup > maxPatchGroup)
							maxPatchGroup = currentPatchGroup;
					}
				}
				AddReportLine("Maximum PatchGroup in File Library = " + maxPatchGroup);
				if (maxPatchGroup >= 0)
					m_newPatchGroup = 1 + maxPatchGroup;
			}
			else
				AddReportLine("No Library file nodes contained a PatchGroup attribute.");
			AddReportLine("New PatchGroup value = " + m_newPatchGroup);

			// Set up File Library Addenda:
			m_xmlPreviousFileLibraryAddenda = new XmlDocument();
			var addendaPath = LocalFileFullPath(m_fileLibraryAddendaName);
			if (File.Exists(addendaPath))
			{
				m_xmlPreviousFileLibraryAddenda.Load(addendaPath);
				AddReportLine(" Previous file library addenda contains " + m_xmlPreviousFileLibraryAddenda.FirstChild.ChildNodes.Count + " items.");

				// Copy items from addenda into internal main library object:
				/*
				var fileLibraryNode = m_xmlFileLibrary.SelectSingleNode("//FileLibrary");

				foreach (XmlNode xn in m_xmlPreviousFileLibraryAddenda.FirstChild.ChildNodes)
				{
					var clone = m_xmlFileLibrary.ImportNode(xn, true);
					((XmlElement)clone).SetAttribute("PatchGroup", m_newPatchGroup.ToString());
					fileLibraryNode.AppendChild(clone);
				}*/
			}
			else
			{
				AddReportLine("No previous file library addenda.");
			}

			m_xmlNewFileLibraryAddenda = new XmlDocument();
			m_xmlNewFileLibraryAddenda.LoadXml("<FileLibrary>\r\n</FileLibrary>");
			m_newFileLibraryAddendaNode = m_xmlNewFileLibraryAddenda.SelectSingleNode("FileLibrary");

		}

		/// <summary>
		/// Reads all WIX source files that already contain file definitions so that we won't
		/// repeat any when we collect files automatically.
		/// </summary>
		private void ParseWixFileSources()
		{
			foreach (string wxs in m_wixFileSources)
			{
				var xmlFilesPath = LocalFileFullPath(wxs);
				var xmlFiles = new XmlDocument();
				xmlFiles.Load(xmlFilesPath);
				xmlFiles.PreserveWhitespace = true;

				var xmlnsManager = new XmlNamespaceManager(xmlFiles.NameTable);
				// Add the namespace used in Files.wxs to the XmlNamespaceManager:
				xmlnsManager.AddNamespace("wix", "http://schemas.microsoft.com/wix/2006/wi");

				// Get all Files.wxs and merge module file nodes and add their source paths to FileOmissions:
				var customFileNodes = xmlFiles.SelectNodes("//wix:File", xmlnsManager);
				foreach (XmlElement fileNode in customFileNodes)
				{
					string sourcePath = fileNode.GetAttribute("Source");
					m_fileOmissions.Add(sourcePath, "already included in WIX source " + xmlFilesPath);
				}
			}
		}

		/// <summary>
		/// Builds up sets of files that are either already built, or which are built in this method.
		/// </summary>
		private void CollectInstallableFiles()
		{
			// Collect main file sets. First is Output\Release:
			HashSet<InstallerFile> fileSet = CollectFiles(Path.Combine(m_projRootPath, m_builtFilesBuildTypeFolder), m_rootDirectory, "", true, false);
			AddReportLine("Collected " + fileSet.Count + " files from " + m_builtFilesBuildTypeFolder);

			// Filter out known junk:
			m_builtFilesFiltered = FilterOutSpecifiedOmissions(fileSet);
			AddReportLine("Removed " + (fileSet.Count - m_builtFilesFiltered.Count) + " junk files:");
			foreach (var file in fileSet.Except(m_builtFilesFiltered))
				AddReportLine("    " + file.RelativeSourcePath);

			// Next is DistFiles:
			fileSet = CollectFiles(Path.Combine(m_projRootPath, m_staticFilesFolderName), m_rootDirectory, "", true, false);
			AddReportLine("Collected " + fileSet.Count + " files from " + m_staticFilesFolderName);

			// Filter out known junk:
			m_distFilesFiltered = FilterOutSpecifiedOmissions(fileSet);
			AddReportLine("Removed " + (fileSet.Count - m_distFilesFiltered.Count) + " junk files:");
			foreach (var file in fileSet.Except(m_distFilesFiltered))
				AddReportLine("    " + file.RelativeSourcePath);

			// Merge all collected files together:
			var rejectedFiles = new HashSet<InstallerFile>();
			m_allFilesFiltered = MergeFileSets(m_builtFilesFiltered, m_distFilesFiltered, rejectedFiles);

			AddReportLine("Rejected " + rejectedFiles.Count + " files as duplicates:");
			foreach (var file in rejectedFiles)
				AddReportLine("    " + file.RelativeSourcePath);

			// Collect file sets from special build targets, typically remakele for FLEx and remakete for TE:

			// Rename pre-existing output folder temporarily:
			Directory.Move(m_builtFilesFolder, m_builtFilesFolderFull);

			// Now build FLEx:
			if (!m_reuseOutput)
				Nant("remakele");
			else
				Directory.Move(m_builtFilesFolderFlex, m_builtFilesFolder);

			string fullOutputBuildFolder = Path.Combine(m_projRootPath, m_builtFilesBuildTypeFolder);

			// Collect files built for FLEx:
			HashSet<InstallerFile> flexBuiltFiles = CollectFiles(fullOutputBuildFolder, m_rootDirectory, "From FLEx target", true, false);
			AddReportLine("Collected " + flexBuiltFiles.Count + " files from FLEx target.");

			// Filter out known junk:
			HashSet<InstallerFile> flexBuiltFilesDejunked = FilterOutSpecifiedOmissions(flexBuiltFiles);
			AddReportLine("Removed " + (flexBuiltFiles.Count - flexBuiltFilesDejunked.Count) + " junk files:");
			foreach (var file in flexBuiltFiles.Except(flexBuiltFilesDejunked))
				AddReportLine("    " + file.RelativeSourcePath);

			// Remove any files that have already been rejected:
			m_flexBuiltFilesFiltered = flexBuiltFilesDejunked;
			foreach (var file in rejectedFiles.Where(m_flexBuiltFilesFiltered.Contains))
				m_flexBuiltFilesFiltered.Remove(file);
			AddReportLine("Removed " + (flexBuiltFilesDejunked.Count - m_flexBuiltFilesFiltered.Count) + " already rejected files:");
			foreach (var file in flexBuiltFilesDejunked.Except(m_flexBuiltFilesFiltered))
				AddReportLine("    " + file.RelativeSourcePath);

			// Make sure m_flexBuiltFilesFiltered references equivalent files from m_allFilesFiltered:
			ReplaceEquivalentFiles(m_flexBuiltFilesFiltered, m_allFilesFiltered);

			// Rename Flex output folder:
			Directory.Move(m_builtFilesFolder, m_builtFilesFolderFlex);

			// Now build TE:
			if (!m_reuseOutput)
				Nant("remakete");
			else
				Directory.Move(m_builtFilesFolderTe, m_builtFilesFolder);

			// Collect files built for TE:
			HashSet<InstallerFile> teBuiltFiles = CollectFiles(fullOutputBuildFolder, m_rootDirectory, "From TE target", true, false);
			AddReportLine("Collected " + teBuiltFiles.Count + " files from TE target.");

			// Filter out known junk:
			HashSet<InstallerFile> teBuiltFilesDejunked = FilterOutSpecifiedOmissions(teBuiltFiles);
			AddReportLine("Removed " + (teBuiltFiles.Count - teBuiltFilesDejunked.Count) + " junk files:");
			foreach (var file in teBuiltFiles.Except(teBuiltFilesDejunked))
				AddReportLine("    " + file.RelativeSourcePath);

			// Remove any files that have already been rejected:
			m_teBuiltFilesFiltered = teBuiltFilesDejunked;
			foreach (var file in rejectedFiles.Where(m_teBuiltFilesFiltered.Contains))
				m_teBuiltFilesFiltered.Remove(file);
			AddReportLine("Removed " + (teBuiltFilesDejunked.Count - m_teBuiltFilesFiltered.Count) + " already rejected files:");
			foreach (var file in teBuiltFilesDejunked.Except(m_teBuiltFilesFiltered))
				AddReportLine("    " + file.RelativeSourcePath);

			// Make sure m_teBuiltFilesFiltered references equivalent files from m_allFilesFiltered:
			ReplaceEquivalentFiles(m_teBuiltFilesFiltered, m_allFilesFiltered);

			// Rename TE output folder:
			Directory.Move(m_builtFilesFolder, m_builtFilesFolderTe);

			// Restore original output folder:
			Directory.Move(m_builtFilesFolderFull, m_builtFilesFolder);

			// Collect localization files:
			foreach (var currentLanguage in m_languages)
			{
				var language = currentLanguage;
				currentLanguage.TeFiles = (m_allFilesFiltered.Where(file => FileIsForTeLocalization(file, language)));
				currentLanguage.OtherFiles = (m_allFilesFiltered.Where(file => FileIsForNonTeLocalization(file, language)));

				m_allLocalizationFiles.UnionWith(currentLanguage.TeFiles);
				m_allLocalizationFiles.UnionWith(currentLanguage.OtherFiles);
			}
		}

		/// <summary>
		/// Collects files from given path, building directory tree along the way.
		/// </summary>
		/// <param name="folderPath">Full path to begin collecting files from</param>
		/// <param name="dirNode">Root of directory tree to fill in along the way</param>
		/// <param name="comment">Comment to tag each file with</param>
		/// <param name="isInstallerRoot">True if given directory represents the installation folder on the end-user's machine</param>
		/// <param name="alreadyRedirectedParent">True if we have added an ancestor of dirNode to m_redirectedFolders, so we don't need to consider this dirNode for that purpose.</param>
		/// <returns>Set (flattened) of collected files.</returns>
		private HashSet<InstallerFile> CollectFiles(string folderPath, DirectoryTreeNode dirNode, string comment, bool isInstallerRoot, bool alreadyRedirectedParent)
		{
			// Fill in data about current directory node:
			dirNode.TargetPath = MakeRelativeTargetPath(folderPath);
			// If the folderPath has been earmarked for redirection to elsewhere on the
			// end user's machine, make the necessary arrangements:
			string redirection = m_folderRedirections.Redirection(MakeRelativePath(folderPath));
			if (redirection != null && !alreadyRedirectedParent)
			{
				dirNode.Name = redirection;
				dirNode.IsDirReference = true;
				m_redirectedFolders.Add(dirNode);
				alreadyRedirectedParent = true;
			}
			else if (isInstallerRoot)
			{
				dirNode.Name = "INSTALLDIR";
				dirNode.IsDirReference = true;
			}
			else
			{
				dirNode.Name = Path.GetFileName(folderPath);
				dirNode.DirId = MakeId(dirNode.Name, dirNode.TargetPath);
			}

			// Instantiate return object:
			var fileSet = new HashSet<InstallerFile>();

			// Check if current Folder is a Subversion metadata folder:
			if (folderPath.EndsWith(".svn"))
				return fileSet;

			// Check if folder exists:
			if (!Directory.Exists(folderPath))
				return fileSet;

			// Add files in current folder:
			foreach (string file in Directory.GetFiles(folderPath))
			{
				var instFile = new InstallerFile();
				// Put in all the file data the WIX install build will need to know:
				instFile.Name = Path.GetFileName(file);
				instFile.Comment = comment;
				instFile.RelativeSourcePath = MakeRelativePath(file);
				instFile.Id = MakeId(instFile.Name, instFile.RelativeSourcePath);
				instFile.DirId = dirNode.DirId;

				var fileVersionInfo = FileVersionInfo.GetVersionInfo(file);
				var fileVersion = "";
				if (fileVersionInfo.FileVersion != null)
				{
					fileVersion = fileVersionInfo.FileMajorPart + "." + fileVersionInfo.FileMinorPart + "." +
								  fileVersionInfo.FileBuildPart + "." + fileVersionInfo.FilePrivatePart;
				}
				var fi = new FileInfo(file);

				instFile.Size = fi.Length;
				instFile.DateTime = fi.LastWriteTime.ToShortDateString() + " " + fi.LastWriteTime.ToShortTimeString();
				instFile.Version = fileVersion;
				instFile.Md5 = CalcFileMd5(file);

				// Record the current file in the return object and the local directory node's data.
				fileSet.Add(instFile);
				dirNode.LocalFiles.Add(instFile);
			}

			// Now recurse all subfolders:
			foreach (var subFolder in Directory.GetDirectories(folderPath))
			{
				// Re-use relevant DirectoryTreeNode if target path has been used before:
				string targetPath = MakeRelativeTargetPath(subFolder);
				var dtn = dirNode.Children.FirstOrDefault(c => c.TargetPath.ToLowerInvariant() == targetPath.ToLowerInvariant());
				if (dtn == null)
				{
					dtn = new DirectoryTreeNode();
					dirNode.Children.Add(dtn);
				}
				HashSet<InstallerFile> subFolderFiles = CollectFiles(subFolder, dtn, comment, false, alreadyRedirectedParent);
				fileSet.UnionWith(subFolderFiles);
			}
			return fileSet;
		}

		private static string CalcFileMd5(string filePath)
		{
			// We first have to read the file into an array of bytes:
			var inputBytes = File.ReadAllBytes(filePath);

			// Now compute the MD5 hash, also as a byte array:
			var md5 = MD5.Create();
			var hashBytes = md5.ComputeHash(inputBytes);

			// Convert the byte array to hexadecimal string:
			var sb = new StringBuilder();
			for (var i = 0; i < hashBytes.Length; i++)
			{
				sb.Append(hashBytes[i].ToString("X2"));
			}
			return sb.ToString();
		}

		/// <summary>
		/// Removes junk entries (defined in m_fileOmissions) from a set of files.
		/// </summary>
		/// <param name="fileSet">Set of files</param>
		/// <returns>New set containing cleaned up version of fileSet</returns>
		private HashSet<InstallerFile> FilterOutSpecifiedOmissions(IEnumerable<InstallerFile> fileSet)
		{
			var returnSet = new HashSet<InstallerFile>();
			foreach (InstallerFile f in fileSet)
			{
				var relPathLower = f.RelativeSourcePath.ToLowerInvariant();
				var fOk = m_fileOmissions.All(s => !relPathLower.Contains(s.RelativePath.Replace("\\${config}\\", "\\" + m_buildType + "\\").ToLowerInvariant()));
				if (fOk)
					returnSet.Add(f);
			}
			return returnSet;
		}

		/// <summary>
		/// Merges 2 sets of installable files, filtering out any from the second set that appear to be duplicates.
		/// The method ArbitrateFileMatches below provides the heuristics for determining if a file is a duplicate.
		/// </summary>
		/// <param name="preferredSet">This set of files will always be in the result</param>
		/// <param name="otherSet">Files from this set will be in the result if there are no duplicates with the first set</param>
		/// <param name="rejectedFiles">A set that gets populated with files that get rejected because they are effectively duplicates</param>
		/// <returns>A new set containing the merged combination of the arguments</returns>
		private HashSet<InstallerFile> MergeFileSets(IEnumerable<InstallerFile> preferredSet, IEnumerable<InstallerFile> otherSet,
			HashSet<InstallerFile> rejectedFiles)
		{
			// Find sets of files with matching names:
			foreach (InstallerFile currentFile in preferredSet)
			{
				InstallerFile file = currentFile;
				IEnumerable<InstallerFile> matchingFiles = from other in otherSet
														   where other.FileNameMatches(file)
														   select other;

				ArbitrateFileMatches(currentFile, matchingFiles, rejectedFiles);
			}

			var returnSet = new HashSet<InstallerFile>();

			returnSet.UnionWith(preferredSet.Except(rejectedFiles));
			returnSet.UnionWith(otherSet.Except(rejectedFiles));

			return returnSet;
		}

		/// <summary>
		/// Decides whether to accept or reject a given file when compared with the given set of "matching" files.
		/// </summary>
		/// <param name="candidate">File to be considered</param>
		/// <param name="matchingFiles">Set of files to consider against</param>
		/// <param name="rejectedFiles">set to receive rejected files</param>
		private void ArbitrateFileMatches(InstallerFile candidate, IEnumerable<InstallerFile> matchingFiles, HashSet<InstallerFile> rejectedFiles)
		{
			string candidateTargetPath = MakeRelativeTargetPath(candidate.RelativeSourcePath);
			foreach (InstallerFile file in matchingFiles)
			{
				// If the candidate is functionally equivalent to the current file, then we'll skip this match,
				// avoiding the possibility of rejecting one of them, which would be interpreted as rejecting them all:
				if (candidate.Equals(file))
					continue;

				// If the candidate has a different file hash from the current file's hash, then we'll skip
				// this match, as they are genuinely different files:
				var candidateFullPath = Path.Combine(m_projRootPath, candidate.RelativeSourcePath);
				var fileFullPath = Path.Combine(m_projRootPath, file.RelativeSourcePath);
				var candidateBytes = File.ReadAllBytes(candidateFullPath);
				var fileBytes = File.ReadAllBytes(fileFullPath);
				var md5 = MD5.Create();
				var candidateHash = md5.ComputeHash(candidateBytes);
				var fileHash = md5.ComputeHash(fileBytes);
				var hashValuesEqual = true;
				for (int i = 0; i < candidateHash.Length; i++)
				{
					if (candidateHash[i] != fileHash[i])
					{
						hashValuesEqual = false;
						break;
					}
				}
				if (!hashValuesEqual)
				{
					// The files are different. If they were destined for the same target path,
					// then we have a problem that needs to be reported:
					if (MakeRelativeTargetPath(fileFullPath) == MakeRelativeTargetPath(candidateFullPath))
						m_seriousIssues += "WARNING: " + fileFullPath + " is supposed to be the same as " + candidateFullPath +
										   ", but it isn't. Only one will get into the installer. You should check and see which one is correct, and do something about the other." +
										   Environment.NewLine;
					else
						continue;
				}

				string fileTargetPath = MakeRelativeTargetPath(file.RelativeSourcePath);
				// If the relative target paths are the same, we'll prefer the candidate. Where they differ, we'll take the
				// one with the deeper target path:
				if (candidateTargetPath == fileTargetPath || candidateTargetPath.Length >= fileTargetPath.Length)
				{
					rejectedFiles.Add(file);
					candidate.Comment += "(preferred over " + file.RelativeSourcePath + ")";
				}
				else
				{
					rejectedFiles.Add(candidate);
					file.Comment += "(preferred over " + candidate.RelativeSourcePath + ")";
					return;
				}
			}
		}

		/// <summary>
		/// Every file in the first set that has an equivalent file in the second set is replaced with the
		/// one in the second set. This means the first set still has the same functional data, but uses
		/// references to a master set.
		/// </summary>
		/// <param name="subSet">the set whose members will get replaced by others</param>
		/// <param name="masterSet">the source of items to replace in the other set</param>
		private static void ReplaceEquivalentFiles(HashSet<InstallerFile> subSet, IEnumerable<InstallerFile> masterSet)
		{
			foreach (var file in subSet.ToArray())
			{
				var masterEquivalenceSet = masterSet.Where(masterFile => masterFile.Equals(file));
				if (masterEquivalenceSet.Count() == 0)
					continue;
				subSet.Remove(file);
				subSet.Add(masterEquivalenceSet.Single());
			}
		}

		/// <summary>
		/// Manipulates sets of built files to produce sets of files for each installer feature.
		/// This is the cleverest function in the whole project.
		/// </summary>
		private void BuildFeatureFileSets()
		{
			// UsedDistFiles consists of all DistFiles except those that were duplicated in Output\Release:
			var usedDistFiles = m_allFilesFiltered.Intersect(m_distFilesFiltered);

			var flexOnlyDistFiles = from file in usedDistFiles
									where FileIsForFlexOnly(file)
									select file;

			m_flexMoviesFeatureFiles = from file in usedDistFiles
									   where FileIsForFlexMoviesOnly(file)
									   select file;
			var localizationFiles = from file in usedDistFiles
									where FileIsForLocalization(file)
									select file;

			var teOnlyDistFiles = from file in usedDistFiles
								  where FileIsForTeOnly(file)
								  select file;
			var coreDistFiles = usedDistFiles.Except(flexOnlyDistFiles).Except(teOnlyDistFiles).Except(m_flexMoviesFeatureFiles).Except(localizationFiles);

			var coreBuiltFiles = m_flexBuiltFilesFiltered.Intersect(m_teBuiltFilesFiltered);
			var flexOnlyBuiltFiles = m_flexBuiltFilesFiltered.Except(coreBuiltFiles);
			var teOnlyBuiltFiles = m_teBuiltFilesFiltered.Except(coreBuiltFiles);

			m_flexFeatureFiles = flexOnlyBuiltFiles.Union(flexOnlyDistFiles);
			m_teFeatureFiles = teOnlyBuiltFiles.Union(teOnlyDistFiles);
			m_fwCoreFeatureFiles = coreBuiltFiles.Union(coreDistFiles);

			// Add to m_fwCoreFeatureFiles any files specified in the m_customCoreFiles set:
			// (By definition, such files are not needed by FLEx or TE, so won't appear in FLEx or TE sets.)
			var fwCoreFeatureFiles = new HashSet<InstallerFile>();
			foreach (var file in from file in m_allFilesFiltered
								 from coreFile in m_customCoreFiles
								 where file.RelativeSourcePath.ToLowerInvariant().Contains(coreFile.Replace("\\${config}\\", "\\" + m_buildType + "\\").ToLowerInvariant())
								 select file)
			{
				fwCoreFeatureFiles.Add(file);
			}
			m_fwCoreFeatureFiles = m_fwCoreFeatureFiles.Union(fwCoreFeatureFiles);

			// Run tests to see if any files that look like they are for TE appear in the core or in FLEx:
			TestNoTeFiles(m_fwCoreFeatureFiles, "Core");
			TestNoTeFiles(m_flexFeatureFiles, "FLEx");

			// Assign feature names in m_allFilesFiltered:
			foreach (var file in m_allFilesFiltered)
			{
				if (m_flexFeatureFiles.Contains(file))
					file.Features.Add("FLEx");
				if (m_flexMoviesFeatureFiles.Contains(file))
					file.Features.Add("FlexMovies");
				if (m_teFeatureFiles.Contains(file))
					file.Features.Add("TE");
				if (m_fwCoreFeatureFiles.Contains(file))
					file.Features.Add("FW_Core");

				foreach (var language in m_languages)
				{
					if (language.OtherFiles.Contains(file))
						file.Features.Add(language.LanguageName);
					if (language.TeFiles.Contains(file))
						file.Features.Add(language.LanguageName + "_TE");
				}

				// Test if any features of the current file are represented in Features.wxs:
				file.OnlyUsedInUnusedFeatures = !m_representedFeatures.Any(feature => file.Features.Contains(feature));

				if (file.Features.Count > 0)
				{
					// Assign a DiskId for the file's cabinet:
					var firstFeature = file.Features.First();
					if (m_featureCabinetMappings.ContainsKey(firstFeature))
						file.DiskId = m_featureCabinetMappings[firstFeature];
					else
						file.DiskId = m_featureCabinetMappings["Default"];
				}
			}
		}

		/// <summary>
		/// Uses a set of heuristics to judge whether the given set of files contains any
		/// that belong uniquely to TE. If so, the m_seriousIssues report is extended to
		/// include the misplaced files.
		/// Uses heuristics from the TeFileNameHeuristics node of InstallerConfig.xml,
		/// and checks only against file name, not folder path.
		/// </summary>
		/// <param name="files">List of files</param>
		/// <param name="section">Label of installer section to report to user if there is a problem</param>
		private void TestNoTeFiles(IEnumerable<InstallerFile> files, string section)
		{
			foreach (var file in from file in files
								 from heuristic in m_TeFileNameHeuristics
								 let re = new Regex(heuristic)
								 where re.IsMatch(file.Name)
								 select file)
			{
				// We've found a match via the heuristics. Just check the file isn't specifically exempted:
				var parameterizedPath = file.RelativeSourcePath.Replace("\\" + m_buildType + "\\", "\\${config}\\");
				if (!m_TeFileNameExceptions.Contains(parameterizedPath))
				{
					m_seriousIssues += "WARNING: " + file.RelativeSourcePath +
									   " looks like a file specific to TE, but it appears in the " + section +
									   " section of the installer. This may adversely affect FLEx-only users in sensitive locations." +
									   Environment.NewLine;
				}
			}
		}

		/// <summary>
		/// Determines heuristically whether a given installer file is for use in FLEx only.
		/// Currently uses hard-coded heuristics, and examines file name and relative path.
		/// </summary>
		/// <param name="file">the candidate installer file</param>
		/// <returns>true if the file is for FLEx feature only</returns>
		private bool FileIsForFlexOnly(InstallerFile file)
		{
			if (FileIsForLocalization(file))
				return false;
			if (FileIsForFlexMoviesOnly(file))
				return false;
			if (file.RelativeSourcePath.Contains("Language Explorer"))
				return true;
			if (file.RelativeSourcePath.Contains("Language_Explorer"))
				return true;
			if (file.RelativeSourcePath.Contains("Data Notebook"))
				return true;
			if (file.RelativeSourcePath.Contains("Data_Notebook"))
				return true;

			return false;
		}

		/// <summary>
		/// Determines heuristically whether a given installer file is for use in FLEx Movies only.
		/// Currently uses hard-coded heuristics, and examines file name and relative path.
		/// </summary>
		/// <param name="file">the candidate installer file</param>
		/// <returns>true if the file is for FLEx Movies only</returns>
		private static bool FileIsForFlexMoviesOnly(InstallerFile file)
		{
			if (file.RelativeSourcePath.Contains("Language Explorer\\Movies\\notfound.html"))
				return false;

			if (file.RelativeSourcePath.Contains("Language Explorer\\Movies"))
				return true;

			return false;
		}

		/// <summary>
		/// Determines heuristically whether a given installer file is for use in TE only.
		/// Currently uses hard-coded heuristics, and examines file name and relative path.
		/// </summary>
		/// <param name="file">the candidate installer file</param>
		/// <returns>true if the file is for TE only</returns>
		private bool FileIsForTeOnly(InstallerFile file)
		{
			// If a file is a localization file it will be put in a localization pack
			// and not considered a TE file as such:
			if (FileIsForLocalization(file) || FileIsForTeLocalization(file))
				return false;
			if (file.RelativeSourcePath.Contains("Editorial Checks"))
				return true;
			if (file.RelativeSourcePath.Contains("Translation_Editor"))
				return true;
			if (file.RelativeSourcePath.Contains("Translation Editor"))
				return true;
			if (file.RelativeSourcePath.Contains("Scripture"))
				return true;
			if (file.RelativeSourcePath.Contains("Bibl"))
				return true;

			return false;
		}

		/// <summary>
		/// Returns true if the given file is a localization resource file for the
		/// given language.
		/// </summary>
		/// <param name="file">File to be analyzed</param>
		/// <param name="language">Language to be considered</param>
		/// <returns>True if file is a localization file for that language</returns>
		private static bool FileIsForLocalization(InstallerFile file, LocalizationData language)
		{
			if (language.Folder.Length == 0)
				throw new Exception("Language defined with no localization folder");

			if (file.RelativeSourcePath.Contains("\\" + language.Folder + "\\"))
				return true;

			if (file.RelativeSourcePath.EndsWith("\\strings-" + language.Folder + ".xml"))
				return true;

			if (file.RelativeSourcePath.EndsWith("\\LocalizedLists-" + language.Folder + ".xml"))
				return true;

			if (file.RelativeSourcePath.EndsWith("\\BiblicalTerms-" + language.Folder + ".xml"))
				return true;

			return false;
		}

		/// <summary>
		/// Returns true if the given file is a localization resource file for any language.
		/// </summary>
		/// <param name="file">File to be analyzed</param>
		/// <returns>True if file is a localization file for any language</returns>
		private bool FileIsForLocalization(InstallerFile file)
		{
			return m_languages.Any(currentLanguage => FileIsForLocalization(file, currentLanguage));
		}

		/// <summary>
		/// Returns true if the given file is a TE localization resource file.
		/// </summary>
		/// <param name="file">File to be analyzed</param>
		/// <param name="language">Language to be considered</param>
		/// <returns>True if file is a TE localization file for that language</returns>
		private static bool FileIsForTeLocalization(InstallerFile file, LocalizationData language)
		{
			if (language.Folder.Length == 0)
				throw new Exception("Language defined with no localization folder");

			if (file.RelativeSourcePath.Contains("\\" + language.Folder + "\\Te"))
				return true;
			if (file.RelativeSourcePath.Contains("\\" + language.Folder + "\\Scr"))
				return true;
			if (file.RelativeSourcePath.Contains("\\" + language.Folder + "\\DiffView"))
				return true;

			if (file.RelativeSourcePath.EndsWith("\\BiblicalTerms-" + language.Folder + ".xml"))
				return true;

			return false;
		}

		/// <summary>
		/// Returns true if the given file is a TE localization resource file for any language.
		/// </summary>
		/// <param name="file">File to be analyzed</param>
		/// <returns>True if file is a TE localization file for any language</returns>
		private bool FileIsForTeLocalization(InstallerFile file)
		{
			return m_languages.Any(currentLanguage => FileIsForTeLocalization(file, currentLanguage));
		}

		/// <summary>
		/// Returns true if the given file is a TE or FLEx localization resource file.
		/// </summary>
		/// <param name="file">File to be analyzed</param>
		/// <param name="language">Language to be considered</param>
		/// <returns>True if file is a TE or FLEx localization file</returns>
		private static bool FileIsForNonTeLocalization(InstallerFile file, LocalizationData language)
		{
			if (FileIsForLocalization(file, language) && !FileIsForTeLocalization(file, language))
				return true;

			return false;
		}

		/// <summary>
		/// Runs NAnt on a given target.
		/// </summary>
		/// <param name="target">The NAnt target to run</param>
		private void Nant(string target)
		{
			var batchFilePath = Path.Combine(m_projRootPath, "__nant.bat");
			var batchFile = new StreamWriter(batchFilePath);
			batchFile.WriteLine("set fwroot=" + m_projRootPath);
			batchFile.WriteLine("set path=%fwroot%\\DistFiles;%path%");
			batchFile.WriteLine("cd " + Path.Combine(m_projRootPath, "bld"));
			// We need to specify .NET 3.5 to avoid errors on Windows 7 64-bit machines which
			// come with part of .NET 4.0 installed. When we move beyond .NET 3.5, we will need
			// to change this line:
			batchFile.WriteLine("..\\bin\\nant\\bin\\nant -t:net-3.5 release extraInstallerBuild build " + target);
			batchFile.Close();

			var nantProc = new Process();
			nantProc.StartInfo.UseShellExecute = false;
			nantProc.StartInfo.RedirectStandardError = true;
			nantProc.StartInfo.RedirectStandardOutput = true;
			nantProc.StartInfo.FileName = batchFilePath;
			nantProc.Start();

			string output = nantProc.StandardOutput.ReadToEnd();
			string error = nantProc.StandardError.ReadToEnd();

			nantProc.WaitForExit();

			Console.WriteLine("Output: " + output);
			Console.WriteLine("Errors: " + error);
			Console.WriteLine("exit code = " + nantProc.ExitCode);

			File.Delete(batchFilePath);
		}

		/// <summary>
		/// Make a full path for a file in the same folder as this program's .exe
		/// </summary>
		/// <param name="fileName">Name of local file</param>
		/// <returns>string full path of given file</returns>
		private string LocalFileFullPath(string fileName)
		{
			return Path.Combine(m_exeFolder, fileName);
		}

		/// Returns an Id suitable for the installer, based on the given name and unique data.
		/// The Id will be unique if the name and unique data combination is unique.
		/// Identifiers may contain ASCII characters A-Z, a-z, digits, underscores (_), or periods (.).
		/// Every identifier must begin with either a letter or an underscore.
		/// Invalid characters are filtered out of the name (spaces, etc.)
		/// The unique data is turned into an MD5 hash and appended to the mame.
		/// Space is limited to 72 chars, so if the name is more than 40 characters, it is truncated
		/// before appending the 32-character MD5 hash.
		private static string MakeId(string name, string uniqueData)
		{
			const int maxLen = 72;
			const string validChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.";
			const string validStartChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_";

			var candidate = "";
			foreach (char c in name)
			{
				if (validChars.Contains(c))
					candidate += c;
				else
					candidate += '_';
			}

			// Test first character in Candidate:
			if (!validStartChars.Contains(candidate.First()))
			{
				// Can't start with a number:
				candidate = "_" + candidate;
			}

			var hash = CalcMD5(uniqueData);
			var hashLen = hash.Length;
			var maxMainSectionLen = maxLen - hashLen - 1;

			if (candidate.Length > maxMainSectionLen)
				candidate = candidate.Substring(0, maxMainSectionLen);

			return candidate + "." + hash;
		}

		/// <summary>
		/// Returns the full path of the given relative path.
		/// </summary>
		/// <param name="relPath">A relative path (from the root FW folder)</param>
		/// <returns>The full path of the specified file or folder</returns>
		private string MakeFullPath(string relPath)
		{
			// Replace ${config} variable with absolute equivalent:
			return Path.Combine(m_projRootPath, relPath.Replace("\\${config}\\", "\\" + m_buildType + "\\"));
		}

		/// <summary>
		/// Returns a given full path minus the front bit that defines where FieldWorks is.
		/// </summary>
		/// <param name="path">Full path</param>
		/// <returns>Full path minus FW bit.</returns>
		private string MakeRelativePath(string path)
		{
			if (path.StartsWith(m_projRootPath))
			{
				string p = path.Remove(0, m_projRootPath.Length);
				if (p.EndsWith("\\"))
					p = p.Remove(m_projRootPath.Length - 1, 1);
				if (p.StartsWith("\\"))
					p = p.Remove(0, 1);
				return p;
			}
			return path;
		}

		/// <summary>
		/// Forms a relative path from the given full path by removing any trace of FW, DistFiles, Output\Release
		/// etc. so that what is left is the relative path from the root installation folder on the end-user's
		/// machine.
		/// </summary>
		/// <param name="path"></param>
		/// <returns></returns>
		private string MakeRelativeTargetPath(string path)
		{
			foreach (string source in new[] { m_builtFilesBuildTypeFolder, m_staticFilesFolderName })
			{
				string root = Path.Combine(m_projRootPath, source);
				string bitToHackOff = "";
				if (path.StartsWith(root))
					bitToHackOff = root;
				if (path.StartsWith(source))
					bitToHackOff = source;
				if (bitToHackOff.Length > 0)
				{
					string p = path.Remove(0, bitToHackOff.Length);
					if (p.EndsWith("\\"))
						p = p.Remove(bitToHackOff.Length - 1, 1);
					if (p.StartsWith("\\"))
						p = p.Remove(0, 1);
					return p;
				}
			}
			return path;
		}

		/// <summary>
		/// Writes out the tree of folders and files to the m_AutoFiles WIX source file.
		/// </summary>
		private void OutputResults()
		{
			InitOutputFile();

			OutputFileTreeNode(m_rootDirectory, 2);
			// Output the redirected folders, removing them from the m_redirectedFolders list
			// as we go, so that they don't get rejected by the OutputFileTreeNode method:
			foreach (var redirectedFolder in m_redirectedFolders.ToArray())
			{
				m_redirectedFolders.Remove(redirectedFolder);
				OutputFileTreeNode(redirectedFolder, 2);
			}

			OutputFeatureRefs();
			TerminateOutputFile();

			// Remove Previous File Library Addenda file:
			if (File.Exists(m_fileLibraryAddendaName))
				File.Delete(m_fileLibraryAddendaName);

			if (m_newFileLibraryAddendaNode.ChildNodes.Count > 0)
			{
				// Save NewFileLibraryAddenda:
				m_xmlNewFileLibraryAddenda.Save(m_fileLibraryAddendaName);
			}

			ReportFileChanges();
		}

		/// <summary>
		/// Opens the output WIX source file and writes headers.
		/// </summary>
		private void InitOutputFile()
		{
			// Open the output file:
			string autoFiles = LocalFileFullPath(m_autoFilesName);
			m_autoFiles = new StreamWriter(autoFiles);
			m_autoFiles.WriteLine("<?xml version=\"1.0\"?>");
			m_autoFiles.WriteLine("<Wix xmlns=\"http://schemas.microsoft.com/wix/2006/wi\">");
			m_autoFiles.WriteLine("	<Fragment Id=\"AutoFilesFragment\">");
			m_autoFiles.WriteLine("		<Property  Id=\"AutoFilesFragment\" Value=\"1\"/>");
		}

		/// <summary>
		/// Outputs given directory tree (including files) to the WIX source file.
		/// </summary>
		/// <param name="dtn">Current root node of directory tree</param>
		/// <param name="indentLevel">Number of indentation tabs needed to line up tree children</param>
		private void OutputFileTreeNode(DirectoryTreeNode dtn, int indentLevel)
		{
			if (!dtn.ContainsUsedFiles())
				return;

			// Don't output the folder here if it is still in the list of redirected ones:
			if (m_redirectedFolders.Contains(dtn))
				return;

			var indentation = Indent(indentLevel);

			if (dtn.IsDirReference)
				m_autoFiles.WriteLine(indentation + "<DirectoryRef Id=\"" + dtn.Name + "\">");
			else
			{
				var nameClause = "Name=\"" + dtn.Name + "\"";
				m_autoFiles.WriteLine(indentation + "<Directory Id=\"" + dtn.DirId + "\" " + nameClause + ">");
			}

			// Iterate over all local files:
			foreach (var file in dtn.LocalFiles.Where(file => file.Features.Count != 0 && !file.OnlyUsedInUnusedFeatures))
			{
				// See if the file looks like a file that is specifically omitted:
				foreach (var omission in m_fileOmissions)
				{
					if (!omission.RelativePath.EndsWith(file.Name))
						continue;

					// Don't fuss over files that might match if they are already specified as a permitted matching pair in
					// a SuppressSimilarityWarning node of InstallerConfig.xml:
					var suppressWarning = false;
					foreach (var pair in m_similarFilePairs)
					{
						var path1 = pair.Path1.Replace("\\${config}\\", "\\" + m_buildType + "\\").ToLowerInvariant();
						var path2 = pair.Path2.Replace("\\${config}\\", "\\" + m_buildType + "\\").ToLowerInvariant();
						var filePath = file.RelativeSourcePath.Replace("\\${config}\\", "\\" + m_buildType + "\\").ToLowerInvariant();
						var omissionPath = omission.RelativePath.Replace("\\${config}\\", "\\" + m_buildType + "\\").ToLowerInvariant();

						if (path1 == filePath && path2 == omissionPath)
						{
							suppressWarning = true;
							break;
						}
						if (path1 == omissionPath && path2 == filePath)
						{
							suppressWarning = true;
							break;
						}
					}
					if (suppressWarning)
						continue;

					var fileFullPath = MakeFullPath(file.RelativeSourcePath);
					if (!File.Exists(fileFullPath))
					{
						m_seriousIssues += file.RelativeSourcePath + " does not exist at expected place (" + fileFullPath + ").";
						break;
					}

					var omissionFullPath = MakeFullPath(omission.RelativePath);
					if (!File.Exists(omissionFullPath))
						continue;

					var omissionMd5 = CalcFileMd5(omissionFullPath);
					var fileCurrentMd5 = CalcFileMd5(fileFullPath);
					if (omissionMd5 == fileCurrentMd5)
					{
						m_seriousIssues += file.RelativeSourcePath +
										   " is included in the installer, but is identical to a file that was omitted [" +
										   omission.RelativePath + "] because it was " + omission.Reason + Environment.NewLine;
						break;
					}

					var omissionFileSize = (new FileInfo(omissionFullPath)).Length;
					var fileSize = (new FileInfo(fileFullPath)).Length;
					if (omissionFileSize == fileSize)
					{
						m_seriousIssues += file.RelativeSourcePath +
										   " is included in the installer, but is similar to a file that was omitted [" +
										   omission.RelativePath + "] because it was " + omission.Reason + Environment.NewLine;
					}
					break;
				}

				// Replace build type with variable equivalent:
				var relativeSource = file.RelativeSourcePath.Replace("\\" + m_buildType + "\\", "\\${config}\\");

				SynchWithFileLibrary(file);

				m_autoFiles.Write(indentation + "	<Component Id=\"" + file.Id + "\" Guid=\"" + file.ComponentGuid + "\"");

				// Configure component to never overwrite an existing instance, if specified in m_neverOverwriteList:
				m_autoFiles.Write(m_neverOverwriteList.Where(relativeSource.Contains).Count() > 0
									? " NeverOverwrite=\"yes\""
									: "");

				m_autoFiles.Write(">");
				m_autoFiles.WriteLine((file.Comment.Length > 0) ? " <!-- " + file.Comment + " -->" : "");

				// Add condition, if one applies:
				var matchingKeys = m_fileSourceConditions.Keys.Where(key => relativeSource.ToLowerInvariant().Contains(key.ToLowerInvariant()));
				foreach (var matchingKey in matchingKeys)
				{
					string condition;
					if (m_fileSourceConditions.TryGetValue(matchingKey, out condition))
						m_autoFiles.WriteLine(indentation + "		<Condition>" + condition + "</Condition>");
				}

				m_autoFiles.Write(indentation + "		<File Id=\"" + file.Id + "\"");

				// Mark files that do not work with Tallow for harvesting registration data:
				if (m_ignoreRegistrations.Contains(relativeSource))
					m_autoFiles.Write(" IgnoreRegistration=\"true\"");

				// Fill in file details:
				m_autoFiles.Write(" Name=\"" + file.Name + "\" ");

				// Add in a ReadOnly attribute, configured according to what's in the m_makeWritableList:
				m_autoFiles.Write(m_makeWritableList.Where(relativeSource.Contains).Count() > 0
									? "ReadOnly=\"no\""
									: "ReadOnly=\"yes\"");

				m_autoFiles.Write(" Checksum=\"yes\" KeyPath=\"yes\"");
				m_autoFiles.Write(" DiskId=\"" + file.DiskId + "\"");
				m_autoFiles.Write(" Source=\"" + relativeSource + "\"");
				if (file.PatchGroup > 0)
					m_autoFiles.Write(" PatchGroup=\"" + file.PatchGroup + "\"");
				m_autoFiles.WriteLine(" />");

				// If file has to be foreceably overwritten, then add a RemoveFile element:
				if (m_forceOverwriteList.Where(relativeSource.Contains).Count() > 0)
				{
					m_autoFiles.Write(indentation + "		<RemoveFile Id=\"_" + file.Id + "\"");
					m_autoFiles.WriteLine(" Name=\"" + file.Name + "\" On=\"install\"/>");
				}

				m_autoFiles.WriteLine(indentation + "	</Component>");
				file.UsedInComponent = true;
			}

			// Recurse over all child folders:
			foreach (var child in dtn.Children)
				OutputFileTreeNode(child, indentLevel + 1);

			if (dtn.IsDirReference)
				m_autoFiles.WriteLine(indentation + "</DirectoryRef>");
			else
				m_autoFiles.WriteLine(indentation + "</Directory>");
		}

		/// <summary>
		/// Returns string containing given number of tab charaters.
		/// </summary>
		/// <param name="indentLevel">Number of tabs required</param>
		/// <returns>String of tabs</returns>
		private static string Indent(int indentLevel)
		{
			string s = "";
			for (int i = 0; i < indentLevel; i++)
				s += "	";
			return s;
		}

		/// <summary>
		/// Writes out to the WIX source all the component refs needed for all the feature refs.
		/// </summary>
		private void OutputFeatureRefs()
		{
			// Iterate over all features that are defined in Features.wxs (files that claim to belong to
			// other features will get omitted):
			foreach (string feature in m_representedFeatures)
			{
				m_autoFiles.WriteLine("		<FeatureRef Id=\"" + feature + "\">");
				foreach (InstallerFile file in m_allFilesFiltered)
				{
					if (file.Features.Contains(feature))
					{
						m_autoFiles.WriteLine("			<ComponentRef Id=\"" + file.Id + "\"/> <!-- " + file.RelativeSourcePath + " " + file.Comment + " -->");
						file.UsedInFeatureRef = true;
					}
				}
				m_autoFiles.WriteLine("		</FeatureRef>");
			}
		}

		/// <summary>
		/// Finishes off WIX source file and closes it.
		/// </summary>
		private void TerminateOutputFile()
		{
			m_autoFiles.WriteLine("	</Fragment>");
			m_autoFiles.WriteLine("</Wix>");
			m_autoFiles.Close();
		}

		/// <summary>
		/// Looks up the given file in the File Library. If found, the file is updated with some extra
		/// details from the library. Otherwise, the library is updated with the file data, and new
		/// data for the library entry is also added to the file.
		/// </summary>
		/// <param name="file">file to be searched for in the library</param>
		private void SynchWithFileLibrary(InstallerFile file)
		{
			// Get file's relative source path with build output folder parameterized:
			string libRelSourcePath = file.RelativeSourcePath.Replace(m_builtFilesBuildTypeFolder, m_builtFilesFolderName + "\\${config}");

			// Test if file already exists in FileLibrary.
			// If it does, then use the existing GUID.
			// Else create a new GUID etc. and add it to FileLibrary.
			string selectString = "//File[translate(@Path, \"ABCDEFGHIJKLMNOPQRSTUVWXYZ\", \"abcdefghijklmnopqrstuvwxyz\")=\"" +
							   libRelSourcePath.ToLowerInvariant() + "\"]"; // case-insensitive look-up
			var libSearch = m_xmlFileLibrary.SelectSingleNode(selectString) as XmlElement;
			if (libSearch != null)
			{
				// File already exists in Library:
				file.ComponentGuid = libSearch.GetAttribute("ComponentGuid");
				var patchGroup = libSearch.GetAttribute("PatchGroup");
				if (patchGroup.Length > 0)
					file.PatchGroup = int.Parse(patchGroup);
			}
			else // No XML node found
			{
				// This is an unknown file:
				file.ComponentGuid = Guid.NewGuid().ToString().ToUpperInvariant();
				file.PatchGroup = m_newPatchGroup;

				// Add file to File Library Addenda:
				var newFileElement = m_xmlNewFileLibraryAddenda.CreateElement("File");
				newFileElement.SetAttribute("Path", libRelSourcePath);
				newFileElement.SetAttribute("ComponentGuid", file.ComponentGuid);
				newFileElement.SetAttribute("ComponentId", file.Id);
				newFileElement.SetAttribute("Name", file.Name);
				newFileElement.SetAttribute("DirectoryId", file.DirId.Length > 0 ? file.DirId : "INSTALLDIR");
				newFileElement.SetAttribute("FeatureList", string.Join(",", file.Features.ToArray()));
				newFileElement.SetAttribute("Date", file.DateTime);
				newFileElement.SetAttribute("Version", file.Version);
				newFileElement.SetAttribute("Size", file.Size.ToString());
				newFileElement.SetAttribute("MD5", file.Md5);

				var firstAddendaFile = m_newFileLibraryAddendaNode.SelectSingleNode("File[1]");
				m_newFileLibraryAddendaNode.InsertBefore(newFileElement, firstAddendaFile);
				m_newFileLibraryAddendaNode.InsertBefore(m_xmlNewFileLibraryAddenda.CreateTextNode("\n"), firstAddendaFile);
				m_newFileLibraryAddendaNode.InsertBefore(m_xmlNewFileLibraryAddenda.CreateTextNode("\t"), firstAddendaFile);
			}
		}

		/// <summary>
		/// Compares New File Library Addenda with Previous File Library Addenda and reports
		/// new and deleted files.
		/// </summary>
		private void ReportFileChanges()
		{
			var previousFileLibraryAddendaNode = m_xmlPreviousFileLibraryAddenda.SelectSingleNode("FileLibrary");
			if (previousFileLibraryAddendaNode == null)
				return;

			var report = "";

			var previousAddendaFiles = previousFileLibraryAddendaNode.SelectNodes("//File");
			var newAddendaFiles = m_newFileLibraryAddendaNode.SelectNodes("//File");

			// Iterate over files in the new file library addenda:
			foreach (XmlElement newFileNode in newAddendaFiles)
			{
				// See if current new file can be found in previous file library addenda
				if (!previousAddendaFiles.Cast<XmlElement>().Any(f => f.GetAttribute("Path") == newFileNode.GetAttribute("Path")))
					report += "New file: " + newFileNode.GetAttribute("Path") + Environment.NewLine;
			}

			// Iterate over files in the previous file library addenda:
			foreach (XmlElement previousFileNode in previousAddendaFiles)
			{
				// See if current previous file can be found in new file library addenda
				if (!newAddendaFiles.Cast<XmlElement>().Any(f => f.GetAttribute("Path") == previousFileNode.GetAttribute("Path")))
					report += "Deleted file: " + previousFileNode.GetAttribute("Path") + Environment.NewLine;
			}

			if (report.Length > 0)
			{
				report = GetBuildDetails() + Environment.NewLine + report;

				if (m_emailingMachineNames.Any(name => name.ToLowerInvariant() == Environment.MachineName.ToLowerInvariant()))
				{
					// Email the report to the people who need to know:
					if (m_fileNotificationEmailList.Count > 0)
					{
						var message = new System.Net.Mail.MailMessage();
						foreach (var recipient in m_fileNotificationEmailList)
							message.To.Add(recipient);
						message.Subject = "Automatic Report from FW Installer Build";
						message.From = new System.Net.Mail.MailAddress("alistair_imrie@sil.org");
						message.Body = report;
						var smtp = new System.Net.Mail.SmtpClient("mail.jaars.org");
						smtp.Send(message);
					}
				}
				else
				{
					// Save the report to a temporary file, then open it in for the user to see:
					var tempFileName = Path.GetTempFileName() + ".txt";
					var reportFile = new StreamWriter(tempFileName);
					reportFile.WriteLine("File Changes Report");
					reportFile.WriteLine("");
					reportFile.WriteLine(report);
					reportFile.Close();
					Process.Start(tempFileName);
					// Wait 10 seconds to give the report a good chance of being opened in NotePad:
					System.Threading.Thread.Sleep(10000);
					File.Delete(tempFileName);
				}
			}
		}

		/// <summary>
		/// Does some tests to make sure all files are accounted for.
		/// Throws exceptions if tests fail.
		/// </summary>
		private void DoSanityChecks()
		{
			string failureReport = m_seriousIssues ?? "";

			// List all files that were only used in unused features:
			IEnumerable<InstallerFile> unusedFileSet = from file in m_allFilesFiltered
													   where file.OnlyUsedInUnusedFeatures
													   select file;

			if (unusedFileSet.Count() > 0)
			{
				failureReport += Environment.NewLine;
				failureReport += "The following files were left out because the features they belong to are not defined in Features.wxs: ";
				failureReport = unusedFileSet.Aggregate(failureReport, (current, unusedFile) => current + (Environment.NewLine + "    " + unusedFile.RelativeSourcePath + " [" + string.Join(", ", unusedFile.Features.ToArray()) + "]"));
				failureReport += Environment.NewLine;
			}

			// Test that all files in m_flexFeatureFiles were used:
			IEnumerable<string> unusedFiles = from file in m_flexFeatureFiles
											  where !file.UsedInComponent && !file.OnlyUsedInUnusedFeatures
											  select file.RelativeSourcePath;

			if (unusedFiles.Count() > 0)
			{
				failureReport += Environment.NewLine;
				failureReport += "The following files were earmarked for FLEx only but got left out: ";
				failureReport += Environment.NewLine + string.Join(Environment.NewLine + "    ", unusedFiles.ToArray());
				failureReport += Environment.NewLine;
			}

			// Test that all files in m_teFeatureFiles were used:
			unusedFiles = from file in m_teFeatureFiles
						  where !file.UsedInComponent && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath;

			if (unusedFiles.Count() > 0)
			{
				failureReport += Environment.NewLine;
				failureReport += "The following files were earmarked for TE only but got left out: ";
				failureReport += Environment.NewLine + string.Join(Environment.NewLine + "    ", unusedFiles.ToArray());
				failureReport += Environment.NewLine;
			}

			// Test that all files in m_fwCoreFeatureFiles were used:
			unusedFiles = from file in m_fwCoreFeatureFiles
						  where !file.UsedInComponent && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath;

			if (unusedFiles.Count() > 0)
			{
				failureReport += Environment.NewLine;
				failureReport += "The following files were earmarked for FW_Core but got left out: ";
				failureReport += Environment.NewLine + string.Join(Environment.NewLine + "    ", unusedFiles.ToArray());
				failureReport += Environment.NewLine;
			}

			// Test that all files in m_flexFeatureFiles were referenced:
			unusedFiles = from file in m_flexFeatureFiles
						  where !file.UsedInFeatureRef && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath;

			if (unusedFiles.Count() > 0)
			{
				failureReport += Environment.NewLine;
				failureReport += "The following files were earmarked for FLEx only but were not referenced in any features: ";
				failureReport += Environment.NewLine + string.Join(Environment.NewLine + "    ", unusedFiles.ToArray());
				failureReport += Environment.NewLine;
			}

			// Test that all files in m_teFeatureFiles were referenced:
			unusedFiles = from file in m_teFeatureFiles
						  where !file.UsedInFeatureRef && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath;

			if (unusedFiles.Count() > 0)
			{
				failureReport += Environment.NewLine;
				failureReport += "The following files were earmarked for TE only but were not referenced in any features: ";
				failureReport += Environment.NewLine + string.Join(Environment.NewLine + "    ", unusedFiles.ToArray());
				failureReport += Environment.NewLine;
			}

			// Test that all files in m_fwCoreFeatureFiles were referenced:
			unusedFiles = from file in m_fwCoreFeatureFiles
						  where !file.UsedInFeatureRef && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath;

			if (unusedFiles.Count() > 0)
			{
				failureReport += Environment.NewLine;
				failureReport += "The following files were earmarked for FW_Core but were not referenced in any features: ";
				failureReport += Environment.NewLine + string.Join(Environment.NewLine + "    ", unusedFiles.ToArray());
				failureReport += Environment.NewLine;
			}

			// Test that all files in m_allFilesFiltered were used:
			unusedFiles = from file in m_allFilesFiltered
						  where !file.UsedInComponent && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath;

			if (unusedFiles.Count() > 0)
			{
				failureReport += Environment.NewLine;
				failureReport += "The following files were unused: they probably appear in the full build but are not used by either TE (built by remakete) or FLEx (built by remakele): ";
				failureReport += Environment.NewLine + string.Join(Environment.NewLine + "    ", unusedFiles.ToArray());
				failureReport += Environment.NewLine;
			}

			// Test that all files in m_allFilesFiltered were referenced exactly once:
			unusedFiles = from file in m_allFilesFiltered
						  where !file.UsedInFeatureRef && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath;

			if (unusedFiles.Count() > 0)
			{
				failureReport += Environment.NewLine;
				failureReport += "The following files were not referenced in any features (tested via UsedInFeatureRef flag): ";
				failureReport += Environment.NewLine + string.Join(Environment.NewLine + "    ", unusedFiles.ToArray());
				failureReport += Environment.NewLine;
			}

			IEnumerable<string> overusedFiles = from file in m_allFilesFiltered
												where file.Features.Count > 1
												select file.RelativeSourcePath;

			if (overusedFiles.Count() > 0)
			{
				failureReport += Environment.NewLine;
				failureReport += "The following files were referenced by more than one feature: ";
				failureReport += Environment.NewLine + string.Join(Environment.NewLine + "    ", overusedFiles.ToArray());
				failureReport += Environment.NewLine;
			}

			// Test Features.Count to verify that all files in m_allFilesFiltered were referenced exactly once:
			unusedFiles = from file in m_allFilesFiltered
						  where file.Features.Count == 0
						  select file.RelativeSourcePath;

			if (unusedFiles.Count() > 0)
			{
				failureReport += Environment.NewLine;
				failureReport += "The following files were not referenced in any features (tested Features.Count==0): ";
				failureReport += Environment.NewLine + string.Join(Environment.NewLine + "    ", unusedFiles.ToArray());
				failureReport += Environment.NewLine;
			}

			if (failureReport.Length > 0)
			{
				// Prepend log with build-specific details:
				failureReport = GetBuildDetails() + Environment.NewLine + failureReport;

				if (m_fReport)
				{
					AddReportLine("");
					AddReportLine("Sanity checks report:");
					AddReportLine("=====================");
					AddReportLine(failureReport);
				}
				else if (m_emailingMachineNames.Any(name => name.ToLowerInvariant() == Environment.MachineName.ToLowerInvariant()))
				{
					// Email the report to the key people who need to know:
					var message = new System.Net.Mail.MailMessage();
					foreach (var recipient in m_emailList)
						message.To.Add(recipient);
					message.Subject = "Automatic Report from FW Installer Build";
					message.From = new System.Net.Mail.MailAddress("alistair_imrie@sil.org");
					message.Body = failureReport;
					var smtp = new System.Net.Mail.SmtpClient("mail.jaars.org");
					smtp.Send(message);
				}
				else
				{
					// Save the report to a temporary file, then open it in for the user to see:
					var tempFileName = Path.GetTempFileName() + ".txt";
					var reportFile = new StreamWriter(tempFileName);
					reportFile.WriteLine("GenerateFilesSource Report");
					reportFile.WriteLine("==========================");
					reportFile.WriteLine(failureReport);
					reportFile.Close();
					Process.Start(tempFileName);
					// Wait 10 seconds to give the report a good chance of being opened in NotePad:
					System.Threading.Thread.Sleep(10000);
					File.Delete(tempFileName);
				}
			}
		}

		/// <summary>
		/// Adds a line of text to the overall report
		/// </summary>
		/// <param name="line">Text to add to report</param>
		private void AddReportLine(string line)
		{
			m_report += line + Environment.NewLine;
		}

		/// <summary>
		/// Collect some details about this build to help distinguish it from
		/// other Perforce branches etc.
		/// </summary>
		/// <returns>Build details</returns>
		private static string GetBuildDetails()
		{
			var details = "";

			// Collect P4 registry variables:
			const string p4VarListPath = "__P4Set__.txt";

			RunDosCmd("p4 set >" + p4VarListPath);
			var p4VarList = new StreamReader(p4VarListPath);
			string line;
			while ((line = p4VarList.ReadLine()) != null)
			{
				if (line.Length == 0) continue;

				// We're interested in the client workspace name:
				if (line.StartsWith("P4CLIENT"))
					details += "Perforce registry variable " + line + Environment.NewLine;
			}
			p4VarList.Close();
			File.Delete(p4VarListPath);

			return details;
		}

		/// <summary>
		/// Runs the given DOS command. Waits for it to terminate.
		/// </summary>
		/// <param name="cmd">A DOS command</param>
		private static void RunDosCmd(string cmd)
		{
			const string dosCmdIntro = "/Q /D /C ";
			cmd = dosCmdIntro + cmd;
			try
			{
				var dosProc = Process.Start("cmd", cmd);
				if (dosProc != null) dosProc.WaitForExit();
			}
			catch (Exception)
			{
				throw new Exception("Error while running this DOS command: " + cmd);
			}
		}
	}
}