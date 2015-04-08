using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Security.Cryptography;
using InstallerBuildUtilities;

namespace GenerateFilesSource
{
	public class Generator
	{
		private const string BuildsFolderFragment = "Builds"; // Folder to copy files for installer
		private const string BuildConfig = "Release"; // No point in allowing a debug-based installer

		// Controls set via command line:
		private readonly bool _needReport;
		private readonly bool _addOrphans;
		private readonly bool _testIntegrity;
		private ReportSystem _overallReport;

		// List of machines which will email people if something goes wrong:
		private readonly List<string> _emailingMachineNames = new List<string>();
		// List of people to email if something goes wrong:
		private readonly List<string> _emailList = new List<string>();
		// List of people to email regarding new and deleted files:
		private readonly List<string> _fileNotificationEmailList = new List<string>();

		// List of mappings of Features to DiskId, so we can assign files to cabinets based on features:
		internal class CabinetMappingData
		{
			private readonly int _index;
			private readonly int[] _indexes;
			private readonly string[] _divisions;

			public CabinetMappingData(string index, string indexes, string divisions)
			{
				if (index.Length == 0)
				{
					if (indexes.Length == 0 || divisions.Length == 0)
						throw new InvalidDataException(
							"Invalid CabinetAssignment node: when CabinetIndex is not assigned, both CabinetIndexes and CabinetDivisions must be assigned.");
					_index = 0;

					var indexStrings = indexes.Split(new[] { ',', ';' });

					_indexes = new int[indexStrings.Length];
					for (var i = 0; i < indexStrings.Length; i++)
						_indexes[i] = int.Parse(indexStrings[i]);

					_divisions = divisions.ToLowerInvariant().Split(new[] { ',', ';' });

					if (_indexes.Length != _divisions.Length + 1)
						throw new InvalidDataException("Invalid CabinetAssignment node: \"CabinetIndexes=\"" + indexes +
													   " \"CabinetDivisions=\"" + divisions +
													   "\" is wrong because there must be one more index than division.");
					return;
				}

				if (indexes.Length != 0 || divisions.Length != 0)
					throw new InvalidDataException(
						"Invalid CabinetAssignment node: when CabinetIndex is assigned, neither CabinetIndexes nor CabinetDivisions may be assigned.");

				_index = int.Parse(index);
			}

			internal int GetCabinet(InstallerFile file)
			{
				if (_index != 0)
					return _index;

				for (var index = 0; index < _divisions.Length; index++)
				{
					if (file.Name.ToLowerInvariant().CompareTo(_divisions[index]) < 0)
						return _indexes[index];
				}
				return _indexes.Last();
			}
		}
		readonly Dictionary<string, CabinetMappingData> _featureCabinetMappings = new Dictionary<string, CabinetMappingData>();

		// Important file/folder paths:
		private string _projRootPath;
		private string _exeFolder;

		internal class FileOmission
		{
			public readonly string RelativePath;
			public readonly string Reason;
			public readonly bool CaseSensitive;

			public FileOmission(string path, string reason)
			{
				RelativePath = path;
				Reason = reason;
				CaseSensitive = false;
			}

			public FileOmission(string path, string reason, bool caseSensitive)
			{
				RelativePath = path;
				Reason = reason;
				CaseSensitive = caseSensitive;
			}
		}
		internal class FileOmissionList : List<FileOmission>
		{
			public void Add(string path, bool caseSensitive)
			{
				Add(new FileOmission(path, "<Omissions PathPattern=\"" + path + "\">", caseSensitive));
			}
			public void Add(string path, string reason)
			{
				Add(new FileOmission(path, reason));
			}
		}
		// List of file name fragments whose files should be omitted:
		private readonly FileOmissionList _fileOmissions = new FileOmissionList();

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
		private readonly List<FilePair> _similarFilePairs = new List<FilePair>();
		// List of file patterns of files known to be needed in the core files feature
		// but which are not needed in either TE or FLEx:
		private readonly List<string> _coreFileOrphans = new List<string>();
		// List of file patterns of built files known to be needed by both TE and FLEx
		// but where one or the other does not reference them properly, so they end up
		// solely in the other:
		private readonly List<string> _forceBuiltFilesIntoCore = new List<string>();
		// List of file patterns of built files known to be needed by FLEx but which are not
		// referenced in FLEx's dependencies:
		private readonly List<string> _forceBuiltFilesIntoFlex = new List<string>();
		// List of file patterns of built files known to be needed by TE but which are not
		// referenced in TE's dependencies:
		private readonly List<string> _forceBuiltFilesIntoTe = new List<string>();

		// List of (partial) paths of files that must not be set to read-only:
		private readonly List<string> _makeWritableList = new List<string>();
		// List of (partial) paths of files that have to have older versions forcibly removed prior to installing:
		private readonly List<string> _forceOverwriteList = new List<string>();
		// List of (partial) paths of files that must be set to never overwrite:
		private readonly List<string> _neverOverwriteList = new List<string>();
		// List of partial paths of files which are installed only if respective conditions are met:
		private readonly Dictionary<string, string> _fileSourceConditions = new Dictionary<string, string>();
		// List of URLs of files which have to be fetched from the internet:
		private readonly Dictionary<string, string> _extraFiles = new Dictionary<string, string>();

		class FileHeuristics
		{
			public class HeuristicSet
			{
				public readonly List<string> PathContains = new List<string>();
				public readonly List<string> PathEnds = new List<string>();

				public bool MatchFound(string path)
				{
					if (PathContains.Any(path.Contains))
						return true;
					if (PathEnds.Any(path.EndsWith))
						return true;
					return false;
				}
				public void Merge(HeuristicSet that)
				{
					PathContains.AddRange(that.PathContains);
					PathEnds.AddRange(that.PathEnds);
				}
			}
			public readonly HeuristicSet Inclusions = new HeuristicSet();
			public readonly HeuristicSet Exclusions = new HeuristicSet();

			public bool IsFileIncluded(string path)
			{
				if (Exclusions.MatchFound(path))
					return false;
				if (Inclusions.MatchFound(path))
					return true;
				return false;
			}
			public void Merge(FileHeuristics that)
			{
				Inclusions.Merge(that.Inclusions);
				Exclusions.Merge(that.Exclusions);
			}
		}
		private readonly FileHeuristics _flexFileHeuristics = new FileHeuristics();
		private readonly FileHeuristics _flexMovieFileHeuristics = new FileHeuristics();
		private readonly FileHeuristics _sampleDataFileHeuristics = new FileHeuristics();
		private readonly FileHeuristics _teFileHeuristics = new FileHeuristics();
		private readonly Dictionary<string, FileHeuristics> _localizationHeuristics = new Dictionary<string, FileHeuristics>();
		private readonly Dictionary<string, FileHeuristics> _teLocalizationHeuristics = new Dictionary<string, FileHeuristics>();

		// List of regular expressions serving as coarse heuristics for guessing if a file might be meant only for TE:
		private readonly List<string> _teFileNameTestHeuristics = new List<string>();
		// List of specific files that may look like TE-only files but aren't really.
		private readonly List<string> _teFileNameExceptions = new List<string>();

		// List of WIX source files where installable files are manually defined:
		private readonly List<string> _wixFileSources = new List<string>();

		// Folders where installable files are to be collected from:
		private string _outputFolderName;
		private string _distFilesFolderName;
		private string _distFilesFolderAbsolutePath;
		private string _outputReleaseFolderRelativePath;
		private string _outputReleaseFolderAbsolutePath;
		private string _installerFolderAbsolutePath;
		private string _installerFilesVersionedFolder;

		// Set of collected DistFiles, junk filtered out:
		private HashSet<InstallerFile> _distFilesFiltered;
		// Set of collected built files, junk filtered out:
		private HashSet<InstallerFile> _builtFilesFiltered;
		// Set of all collected installable files, junk filtered out:
		private HashSet<InstallerFile> _allFilesFiltered = new HashSet<InstallerFile>();
		// Set of collected installable FLEx files:
		private HashSet<InstallerFile> _flexFiles;
		// Set of collected installable TE files:
		private HashSet<InstallerFile> _teFiles;
		// Set of collected installable files from build target "allCsharpNoTests":
		private HashSet<InstallerFile> _allCsharpNoTestsFiles;
		// Set of collected installable files from build target "allCppNoTest":
		private HashSet<InstallerFile> _allCppNoTestFiles;
		// Set of files rejected as duplicates:
		private readonly HashSet<InstallerFile> _duplicateFiles = new HashSet<InstallerFile>();

		// Set of files for FLEx feature:
		private IEnumerable<InstallerFile> _flexFeatureFiles;
		// Set of files for FlexMovies feature:
		private IEnumerable<InstallerFile> _flexMoviesFeatureFiles;
		// Set of files for SampleData feature:
		private IEnumerable<InstallerFile> _sampleDataFeatureFiles;
		// Set of files for TE feature:
		private IEnumerable<InstallerFile> _teFeatureFiles;
		// Set of files for FW Core feature:
		private IEnumerable<InstallerFile> _fwCoreFeatureFiles;
		// Set of files in Output\Release that don't seem to belong to any features:
		private IEnumerable<InstallerFile> _orphanFiles;
		// Set of features represented in the Features.wxs file:
		private readonly HashSet<string> _representedFeatures = new HashSet<string>();

		// File Library details:
		private string _fileLibraryName;
		private XmlDocument _xmlFileLibrary;

		private string _fileLibraryAddendaName;
		private XmlDocument _xmlPreviousFileLibraryAddenda;
		private XmlDocument _xmlNewFileLibraryAddenda;
		private XmlNode _newFileLibraryAddendaNode;

		// The output .wxs file details:
		private string _autoFilesName;
		private TextWriter _autoFiles;

		// Tree of directory nodes of installable files:
		private readonly DirectoryTreeNode _rootDirectory = new DirectoryTreeNode();

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
			{
				sb.Append(hashBytes[i].ToString("X2"));
			}
			return sb.ToString();
		}

		/// <summary>
		/// Structure to hold details about a file being considered for the installer.
		/// </summary>
		internal class InstallerFile
		{
			public string Id;
			public string Name;
			public string FullPath;
			public string RelativeSourcePath;
			public long Size;
			public string DateTime;
			public string Version;
			public string Md5;
			public string Comment;
			public string ReasonForRemoval;
			public string ComponentGuid;
			public int DiskId;
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
				ReasonForRemoval = "";
				ComponentGuid = "unknown";
				DiskId = 0;
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
			/// <returns>true if the argument matches our self</returns>
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
		private readonly RedirectionList _folderRedirections = new RedirectionList();
		private readonly List<DirectoryTreeNode> _redirectedFolders = new List<DirectoryTreeNode>();

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
		private readonly LocalizationList _languages = new LocalizationList();

		/// <summary>
		/// Class constructor
		/// </summary>
		/// <param name="report"></param>
		/// <param name="addOrphans"></param>
		/// /// <param name="testIntegrity"></param>
		public Generator(bool report, bool addOrphans, bool testIntegrity)
		{
			_needReport = report;
			_addOrphans = addOrphans;
			_testIntegrity = testIntegrity;
		}

		/// <summary>
		/// Performs the actions needed to generate the WIX Auto Files source.
		/// </summary>
		internal void Run()
		{
			_overallReport = new ReportSystem();

			Initialize();
			CopyExtraFiles();

			Parallel.Invoke
			(
				delegate
					{
						CollectInstallableFiles();
						BuildFeatureFileSets();
						OutputResults();
						DoSanityChecks();
					},
				delegate
					{
						DeleteExistingVersionedFolder();
						CopyFilesToVersionedFolder();
					}
			);

			if (_testIntegrity)
			{
				var tester = new InstallerIntegrityTester(BuildConfig, _overallReport);
				tester.Run();
			}

			if (!_overallReport.IsReportEmpty(_needReport))
			{
				if (_emailingMachineNames.Any(name => name.ToLowerInvariant() == Environment.MachineName.ToLowerInvariant()))
				{
					// Email the report to the key people who need to know:
					var message = new System.Net.Mail.MailMessage();
					foreach (var recipient in _emailList)
						message.To.Add(recipient);
					message.Subject = "Automatic Report from FW Installer Build";
					message.From = new System.Net.Mail.MailAddress("ken_zook@sil.org");
					message.Body = _overallReport.CombineReports(Tools.GetBuildDetails(_projRootPath), _needReport);
					var smtp = new System.Net.Mail.SmtpClient("mail.jaars.org");
					smtp.Send(message);
				}
				else
				{
					// Save the report to a temporary file, then open it in for the user to see:
					_overallReport.DisplayReport(Tools.GetBuildDetails(_projRootPath), _needReport);
				}
			}
		}

		/// <summary>
		/// Removes the copy of Output\Release files and DistFiles associated with the current
		/// build version of FW.
		/// </summary>
		private void DeleteExistingVersionedFolder()
		{
			if (Directory.Exists(_installerFilesVersionedFolder))
				Directory.Delete(_installerFilesVersionedFolder, true);
		}

		/// <summary>
		/// Copies all DistFiles and Output\Release files to a subfolder named after the current
		/// FW build version. This is so we can keep the files for building a patch later.
		/// </summary>
		private void CopyFilesToVersionedFolder()
		{
			DirectoryCopy(_outputReleaseFolderAbsolutePath, Path.Combine(Path.Combine(_installerFilesVersionedFolder, _outputFolderName), BuildConfig), true);
			DirectoryCopy(_distFilesFolderAbsolutePath, Path.Combine(_installerFilesVersionedFolder, _distFilesFolderName), true);
		}

		private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
		{
			if (sourceDirName.Contains(".git"))
				return;

			var dir = new DirectoryInfo(sourceDirName);
			var dirs = dir.GetDirectories();

			if (!dir.Exists)
			{
				throw new DirectoryNotFoundException(
					"Source directory does not exist or could not be found: "
					+ sourceDirName);
			}

			if (!Directory.Exists(destDirName))
				Directory.CreateDirectory(destDirName);

			var files = dir.GetFiles();
			foreach (var file in files)
			{
				var temppath = Path.Combine(destDirName, file.Name);
				file.CopyTo(temppath, false);
			}

			if (copySubDirs)
			{
				foreach (var subdir in dirs)
				{
					var temppath = Path.Combine(destDirName, subdir.Name);
					DirectoryCopy(subdir.FullName, temppath, copySubDirs);
				}
			}
		}


		/// <summary>
		/// Initialize the class properly.
		/// </summary>
		private void Initialize()
		{
			// Get FW root path:
			var exePath = Assembly.GetExecutingAssembly().Location;
			if (exePath == null)
				throw new Exception("Cannot get path of own .exe");
			_exeFolder = Path.GetDirectoryName(exePath);
			if (_exeFolder == null)
				throw new Exception("Cannot get directory of " + exePath);

			// Get development project root path:
			if (_exeFolder.ToLowerInvariant().EndsWith("installer"))
				_projRootPath = Path.GetDirectoryName(_exeFolder);
			else
				_projRootPath = _exeFolder;

			// This MUST happen before ConfigureFromXml():
			_installerFolderAbsolutePath = Path.Combine(_projRootPath, "Installer");

			// Read in the XML config file:
			ConfigureFromXml();

			// Define paths to folders we will be using:
			_distFilesFolderAbsolutePath = Path.Combine(_projRootPath, _distFilesFolderName);
			_outputReleaseFolderRelativePath = Path.Combine(_outputFolderName, BuildConfig);
			_outputReleaseFolderAbsolutePath = Path.Combine(_projRootPath, _outputReleaseFolderRelativePath);
			var fwVersion = Tools.GetFwBuildVersion();
			_installerFilesVersionedFolder = Path.Combine(Path.Combine(_installerFolderAbsolutePath, BuildsFolderFragment), fwVersion);

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
			if (featuresNodes != null)
			{
				foreach (XmlElement featureNode in featuresNodes)
					_representedFeatures.Add(featureNode.GetAttribute("Id"));
			}
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
					_languages.Add(language.GetAttribute("Name"), language.GetAttribute("Id"));

			// Define list of path patterns of files known to be needed in the core files feature but which are not needed in either TE or FLEx:
			// Format: <File PathPattern="*partial path of any file that is not needed by TE or FLEx but is needed in the FW installation*"/>
			var coreFileOrphans = configuration.SelectNodes("//FeatureAllocation/CoreFileOrphans/File");
			if (coreFileOrphans != null)
				foreach (XmlElement file in coreFileOrphans)
					_coreFileOrphans.Add(file.GetAttribute("PathPattern"));

			// Define list of file patterns of built files known to be needed by both TE and FLEx but where one or the other does not reference them properly, so they end up solely in the other:
			// Format: <File PathPattern="*partial path of any file that is needed by FLEx*"/>
			var forceBuiltFilesIntoCore = configuration.SelectNodes("//FeatureAllocation/ForceBuiltFilesIntoCore/File");
			if (forceBuiltFilesIntoCore != null)
				foreach (XmlElement file in forceBuiltFilesIntoCore)
					_forceBuiltFilesIntoCore.Add(file.GetAttribute("PathPattern"));

			// Define list of path patterns of built files known to be needed by FLEx but which are not referenced in FLEx's dependencies:
			// Format: <File PathPattern="*partial path of any file that is needed by FLEx*"/>
			var forceBuiltFilesIntoFlex = configuration.SelectNodes("//FeatureAllocation/ForceBuiltFilesIntoFlex/File");
			if (forceBuiltFilesIntoFlex != null)
				foreach (XmlElement file in forceBuiltFilesIntoFlex)
					_forceBuiltFilesIntoFlex.Add(file.GetAttribute("PathPattern"));

			// Define list of path patterns of built files known to be needed by TE but which are not not referenced in TE's dependencies:
			// Format: <File PathPattern="*partial path of any file that is needed by TE*"/>
			var forceBuiltFilesIntoTe = configuration.SelectNodes("//FeatureAllocation/ForceBuiltFilesIntoTE/File");
			if (forceBuiltFilesIntoTe != null)
				foreach (XmlElement file in forceBuiltFilesIntoTe)
					_forceBuiltFilesIntoTe.Add(file.GetAttribute("PathPattern"));

			// Define conditions to apply to specified components:
			// Format: <File Path="*partial path of a file that is conditionally installed*" Condition="*MSI Installer condition that must be true to install file*"/>
			// Beware! This XML is double-interpreted, so for example a 'less than' sign must be represented as &amp;lt;
			var fileConditions = configuration.SelectNodes("//FileConditions/File");
			if (fileConditions != null)
				foreach (XmlElement file in fileConditions)
					_fileSourceConditions.Add(file.GetAttribute("Path"), file.GetAttribute("Condition"));

			// Define files needed in the installer that are not built locally or part of source control:
			// Format: <File URL="*URL of an extra file*" Destination="*Local relative path to download file to*"/>
			var extraFiles = configuration.SelectNodes("//ExtraFiles/File");
			if (extraFiles != null)
				foreach (XmlElement file in extraFiles)
					_extraFiles.Add(file.GetAttribute("URL"), file.GetAttribute("Destination"));

			// Define list of file patterns to be filtered out. Any file whose path contains (anywhere) one of these strings will be filtered out:
			// Format: <File PathPattern="*partial path of any file that is not needed in the FW installation*"/>
			var omittedFiles = configuration.SelectNodes("//Omissions/File");
			if (omittedFiles != null)
				foreach (XmlElement file in omittedFiles)
					_fileOmissions.Add(file.GetAttribute("PathPattern"), file.GetAttribute("CaseSensitive") == "true");

			// Define pairs of file paths known (and allowed) to be similar. This suppresses warnings about omitted files that look like other included files:
			// Format: <SuppressSimilarityWarning Path1="*path of first file of matching pair*" Path2="*path of second file of matching pair*"/>
			var similarFilePairs = configuration.SelectNodes("//Omissions/SuppressSimilarityWarning");
			if (similarFilePairs != null)
				foreach (XmlElement pair in similarFilePairs)
					_similarFilePairs.Add(new FilePair(pair.GetAttribute("Path1"), pair.GetAttribute("Path2")));

			// Define list of folders that will be redirected to some other folder on the end-user's machine:
			// Format: <Redirect Folder="*folder whose contents will be installed to a different folder*" InstallDir="*MSI Installer folder variable where the affected files will be installed*"/>
			var folderRedirections = configuration.SelectNodes("//FolderRedirections/Redirect");
			if (folderRedirections != null)
				foreach (XmlElement redirects in folderRedirections)
					_folderRedirections.Add(redirects.GetAttribute("Folder"), redirects.GetAttribute("InstallerDir"));

			// Define list of (partial) paths of files that must not be set to read-only:
			// Format: <File PathPattern="*partial path of any file that must not have the read-only flag set*"/>
			var writableFiles = configuration.SelectNodes("//WritableFiles/File");
			if (writableFiles != null)
				foreach (XmlElement file in writableFiles)
					_makeWritableList.Add(file.GetAttribute("PathPattern"));

			// Define list of (partial) paths of files that have to have older versions forcibly removed prior to installing:
			// Format: <File PathPattern="*partial path of any file that must be installed on top of a pre-existing version, even if that means downgrading*"/>
			var forceOverwriteFiles = configuration.SelectNodes("//ForceOverwrite/File");
			if (forceOverwriteFiles != null)
				foreach (XmlElement file in forceOverwriteFiles)
					_forceOverwriteList.Add(file.GetAttribute("PathPattern"));

			// Define lists of heuristics for assigning DistFiles files into their correct installer features:
			ConfigureHeuristics(configuration, _flexFileHeuristics, "//FeatureAllocation/FlexOnly");
			ConfigureHeuristics(configuration, _flexMovieFileHeuristics, "//FeatureAllocation/FlexMoviesOnly");
			ConfigureHeuristics(configuration, _teFileHeuristics, "//FeatureAllocation/TeOnly");
			ConfigureHeuristics(configuration, _sampleDataFileHeuristics, "//FeatureAllocation/SampleDataOnly");

			// Do the same for localization files.
			// Prerequisite: _languages already configured with all languages:
			ConfigureLocalizationHeuristics(configuration, _localizationHeuristics, "//FeatureAllocation/Localization");
			ConfigureLocalizationHeuristics(configuration, _teLocalizationHeuristics, "//FeatureAllocation/TeLocalization");
			// Merge _teLocalizationHeuristics into _localizationHeuristics so that the latter is a complete set of all localization heuristics:
			foreach (var language in _languages)
				_localizationHeuristics[language.Folder].Merge(_teLocalizationHeuristics[language.Folder]);

			// Define list of regular expressions serving as coarse heuristics for guessing if a file that
			// has been allocated to the Core or FLEx features may actually be meant only for TE (used as
			// a secondary measure only in generating warnings):
			// Format: <Heuristic RegExp="*regular expression matching paths of files that belong exclusively to TE*"/>
			var teTestHeuristics = configuration.SelectNodes("//FeatureAllocation/TeFileNameTestHeuristics/Heuristic");
			if (teTestHeuristics != null)
				foreach (XmlElement heuristic in teTestHeuristics)
					_teFileNameTestHeuristics.Add(heuristic.GetAttribute("RegExp"));

			// Define list of specific files that may look like TE-only files but aren't really (used in
			// conjunction with _teFileNameTestHeuristics to suppress warnings):
			// Format: <File Path="*relative path from FW folder of a file looks like it is TE-exclusive but isn't*"/>
			var teFileNameExceptions = configuration.SelectNodes("//FeatureAllocation/TeFileNameTestHeuristics/Except");
			if (teFileNameExceptions != null)
				foreach (XmlElement file in teFileNameExceptions)
					_teFileNameExceptions.Add(file.GetAttribute("Path"));

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
						_emailingMachineNames.Add(emailingMachine.GetAttribute("Name"));

				// Define list of people to email if something goes wrong:
				// Format: <Recipient Email="*email address of someone to notify if there is a problem*"/>
				var failureReportRecipients = failureNotification.SelectNodes("Recipient");
				if (failureReportRecipients != null)
					foreach (XmlElement recipient in failureReportRecipients)
					{
						var address = Tools.ElucidateEmailAddress(recipient.GetAttribute("Email"));
						_emailList.Add(address);
						// Test if this recipient is to be emailed regarding new and deleted files:
						if (recipient.GetAttribute("NotifyFileChanges") == "true")
							_fileNotificationEmailList.Add(address);
					}
			}

			var sourceFolders = configuration.SelectSingleNode("//FilesSources");
			if (sourceFolders != null)
			{
				// Define folders from which files are to be collected for the installer:
				// (Only two entries are possible: BuiltFiles and StaticFiles)
				var builtFiles = sourceFolders.SelectSingleNode("BuiltFiles") as XmlElement;
				if (builtFiles != null)
					_outputFolderName = builtFiles.GetAttribute("Path");
				var staticFiles = sourceFolders.SelectSingleNode("StaticFiles") as XmlElement;
				if (staticFiles != null)
					_distFilesFolderName = staticFiles.GetAttribute("Path");

				// Define list of WIX files that list files explicitly:
				// Format: <WixSource File="*name of WIX source file in Installer folder that contains <File> definitions inside <Component> definitions*"/>
				var wixSources = sourceFolders.SelectNodes("WixSource");
				foreach (XmlElement wixSource in wixSources)
					_wixFileSources.Add(wixSource.GetAttribute("File"));

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
						var mergeModuleFile = Path.Combine(_installerFolderAbsolutePath, msmFilePath);
						var mergeModuleWixFile =
							Path.Combine(Path.GetDirectoryName(mergeModuleFile), Path.GetFileNameWithoutExtension(mergeModuleFile)) +
							".mm.wxs";
						if (File.Exists(mergeModuleWixFile))
							_wixFileSources.Add(mergeModuleWixFile);
						else
						{
							// If the merge module source file is not in the Installer folder, it is probably in a subfolder
							// of the same name:
							var mergeModuleFolderPath = Path.Combine(_installerFolderAbsolutePath, Path.GetFileNameWithoutExtension(msmFilePath));
							mergeModuleFile = Path.Combine(mergeModuleFolderPath, msmFilePath);
							mergeModuleWixFile =
								Path.Combine(Path.GetDirectoryName(mergeModuleFile), Path.GetFileNameWithoutExtension(mergeModuleFile)) +
								".mm.wxs";
							if (File.Exists(mergeModuleWixFile))
								_wixFileSources.Add(mergeModuleWixFile);
						}
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
					_fileLibraryName = fileLibraryName.GetAttribute("File");
				var fileLibraryAddendaName = fileLibrary.SelectSingleNode("Addenda") as XmlElement;
				if (fileLibraryAddendaName != null)
					_fileLibraryAddendaName = fileLibraryAddendaName.GetAttribute("File");
			}

			// Define output file:
			var outputFile = configuration.SelectSingleNode("//Output") as XmlElement;
			if (outputFile != null)
				_autoFilesName = outputFile.GetAttribute("File");

			// Define file cabinet index allocations:
			var cabinetAssignments = configuration.SelectSingleNode("//CabinetAssignments");
			if (cabinetAssignments != null)
			{
				var defaultAssignment = cabinetAssignments.SelectSingleNode("Default") as XmlElement;
				if (defaultAssignment != null)
					_featureCabinetMappings.Add("Default", new CabinetMappingData(defaultAssignment.GetAttribute("CabinetIndex"), defaultAssignment.GetAttribute("CabinetIndexes"), defaultAssignment.GetAttribute("CabinetDivisions")));

				var assignments = cabinetAssignments.SelectNodes("Cabinet");
				foreach (XmlElement assignment in assignments)
				{
					var feature = assignment.GetAttribute("Feature");
					var index = assignment.GetAttribute("CabinetIndex");
					var indexes = assignment.GetAttribute("CabinetIndexes");
					var divisions = assignment.GetAttribute("CabinetDivisions");
					_featureCabinetMappings.Add(feature, new CabinetMappingData(index, indexes, divisions));
					if (_languages.Any(language => language.LanguageName == feature))
						_featureCabinetMappings.Add(feature + "_TE", new CabinetMappingData(index, indexes, divisions));
				}
			}
		}

		/// <summary>
		/// Configures a "dictionary" of heuristics sets. Each entry in the dictionary is referenced by a
		/// language code, and is a set of heuristics for distinguishing files that belong to that language's
		/// localization pack. The heuristics are defined in the InstallerConfig.xml document, but in a
		/// language-independent way, such that the language code is represented by "{0}", so that the
		/// string.Format method can be used to substitute in the correct language code.
		/// </summary>
		/// <param name="configuration">The InstallerConfig.xml document</param>
		/// <param name="localizationHeuristics">The dictionary of heuristics sets</param>
		/// <param name="xPath">The xPath to the relevant heuristics section in the configuration file</param>
		private void ConfigureLocalizationHeuristics(XmlDocument configuration, IDictionary<string, FileHeuristics> localizationHeuristics, string xPath)
		{
			// Make one heuristics set per language:
			foreach (var language in _languages)
			{
				var heuristics = new FileHeuristics();
				// Use the standard configuration method to read in the heuristics:
				ConfigureHeuristics(configuration, heuristics, xPath);

				// Swap out the "{0}" occurrences and replace with the language code:
				FormatLocalizationHeuristics(heuristics.Inclusions.PathContains, language.Folder);
				FormatLocalizationHeuristics(heuristics.Inclusions.PathEnds, language.Folder);
				FormatLocalizationHeuristics(heuristics.Exclusions.PathContains, language.Folder);
				FormatLocalizationHeuristics(heuristics.Exclusions.PathEnds, language.Folder);

				// Add the new heuristics set to the dictionary:
				localizationHeuristics.Add(language.Folder, heuristics);
			}
		}

		/// <summary>
		/// Replaces each occurrence of "{0}" with the given language string.
		/// </summary>
		/// <param name="heuristicList">List of heuristic strings</param>
		/// <param name="language">language code</param>
		private static void FormatLocalizationHeuristics(IList<string> heuristicList, string language)
		{
			for (int i = 0; i < heuristicList.Count; i++)
				heuristicList[i] = string.Format(heuristicList[i], language);
		}

		/// <summary>
		/// Configures a complete set of heuristics (Inclusions, Exclusions, Files and Exceptions) from
		/// the specified section of the InstallerConfig.xml file.
		/// </summary>
		/// <param name="configuration">The InstallerConfig.xml file</param>
		/// <param name="heuristics">The initialized heuristics set</param>
		/// <param name="xPath">The root section of the XML configuration file</param>
		private static void ConfigureHeuristics(XmlDocument configuration, FileHeuristics heuristics, string xPath)
		{
			// Configure the Inclusions subset from the "File" sub-element:
			ConfigureHeuristicSet(configuration, heuristics.Inclusions, xPath + "/File");
			// Configure the Exclusions subset from the "Except" sub-element:
			ConfigureHeuristicSet(configuration, heuristics.Exclusions, xPath + "/Except");
		}

		/// <summary>
		/// Configures a heuristics subset (Either the Inclusions or the Exclusions) from the specified
		/// section of the InstallerConfig.xml file.
		/// </summary>
		/// <param name="configuration">The InstallerConfig.xml file</param>
		/// <param name="heuristicSet">The heuristics subset</param>
		/// <param name="xPath">The relevant section of the XML configuration file</param>
		private static void ConfigureHeuristicSet(XmlDocument configuration, FileHeuristics.HeuristicSet heuristicSet, string xPath)
		{
			// Create a set of heuristic definitions:
			var heuristicNodes = configuration.SelectNodes(xPath);
			if (heuristicNodes == null)
				return;

			// Process each heuristic definition:
			foreach (XmlElement heuristic in heuristicNodes)
			{
				// The definition should contain either a "PathContains" attribute or
				// a "PathEnds" attribute. Anything else is ignored:
				var pathContains = heuristic.GetAttribute("PathContains");
				if (pathContains.Length > 0)
					heuristicSet.PathContains.Add(pathContains);
				else
				{
					var pathEnds = heuristic.GetAttribute("PathEnds");
					if (pathEnds.Length > 0)
						heuristicSet.PathEnds.Add(pathEnds);
				}
			}
		}

		/// <summary>
		/// Sets up File Library, either from an XML file or from scratch if the file doesn't exist.
		/// Also sets up Previous File Library Addenda in the same way, and New File Library Addenda from scratch.
		/// </summary>
		private void InitFileLibrary()
		{
			_xmlFileLibrary = new XmlDocument();
			var libraryPath = LocalFileFullPath(_fileLibraryName);
			if (File.Exists(libraryPath))
				_xmlFileLibrary.Load(libraryPath);
			else
				_xmlFileLibrary.LoadXml("<FileLibrary>\r\n</FileLibrary>");

			var libraryFileNodes = _xmlFileLibrary.SelectNodes("//File");
			if (libraryFileNodes != null && libraryFileNodes.Count > 0)
				_overallReport.AddReportLine("File Library contains " + libraryFileNodes.Count + " items.");

			// Set up File Library Addenda:
			_xmlPreviousFileLibraryAddenda = new XmlDocument();
			var addendaPath = LocalFileFullPath(_fileLibraryAddendaName);
			if (File.Exists(addendaPath))
			{
				_xmlPreviousFileLibraryAddenda.Load(addendaPath);
				_overallReport.AddReportLine("Previous file library addenda contains " + _xmlPreviousFileLibraryAddenda.FirstChild.ChildNodes.Count + " items.");
			}
			else
			{
				_overallReport.AddReportLine("No previous file library addenda.");
			}

			_xmlNewFileLibraryAddenda = new XmlDocument();
			_xmlNewFileLibraryAddenda.LoadXml("<FileLibrary>\r\n</FileLibrary>");
			_newFileLibraryAddendaNode = _xmlNewFileLibraryAddenda.SelectSingleNode("FileLibrary");

		}

		/// <summary>
		/// Reads all WIX source files that already contain file definitions so that we won't
		/// repeat any when we collect files automatically.
		/// </summary>
		private void ParseWixFileSources()
		{
			var stringsToChop = new [] { @"..\", Path.Combine(BuildsFolderFragment, "$(var.Version)") + @"\" };

			foreach (var wxs in _wixFileSources)
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
				if (customFileNodes != null)
				{
					foreach (XmlElement fileNode in customFileNodes)
					{
						var sourcePath = fileNode.GetAttribute("Source");
						foreach (var chop in stringsToChop)
						{
							while (sourcePath.StartsWith(chop))
								sourcePath = sourcePath.Substring(chop.Length);
						}
						_fileOmissions.Add(sourcePath, "already included in WIX source " + xmlFilesPath);
					}
				}
			}
		}

		/// <summary>
		/// Pull in any additional files specified in the InstallerConfig.xml file
		/// </summary>
		private void CopyExtraFiles()
		{
			var webClient = new WebClient();

			foreach (var file in _extraFiles)
			{
				var fullPath = MakeFullPath(file.Value);
				if (File.Exists(fullPath))
				{
					try
					{
						File.SetAttributes(fullPath, FileAttributes.Normal);
						File.Delete(fullPath);
					}
					catch (IOException e)
					{
						_overallReport.AddSeriousIssue("Error: could not download file to '" + file.Value +
							" because the file already exists and is in use. [" + e.Message + "]");
						continue;
					}
					catch (UnauthorizedAccessException e)
					{
						_overallReport.AddSeriousIssue("Error: could not download file to '" + file.Value +
							" because the file already exists and cannot be deleted. [" + e.Message + "]");
						continue;
					}
				}

				var destDirectory = Path.GetDirectoryName(fullPath);
				if (!Directory.Exists(destDirectory))
				{
					try
					{
						Directory.CreateDirectory(destDirectory);
					}
					catch (Exception e)
					{
						_overallReport.AddSeriousIssue("Error: could not download file to '" + file.Value +
							" because the folder it is destined for cannot be created. [" + e.Message + "]");
						continue;
					}
				}

				try
				{
					webClient.DownloadFile(file.Key, fullPath);
				}
				catch (WebException e)
				{
					_overallReport.AddSeriousIssue(
						"Error: could not download file '" + file.Key + " (destined for " + file.Value + "): " + e.Message);
				}
			}
		}

		/// <summary>
		/// Builds up sets of files that are either already built, or which are built in this method.
		/// </summary>
		private void CollectInstallableFiles()
		{
			// We do these tasks in parallel:
			// 1) Collect existing files from Output\Release and DistFiles;
			// 2) Collect files from the LexTextExe target and all its dependencies;
			// 3) Collect files from the TeExe target and all its dependencies;
			// 4) Collect files from the allCsharpNoTests target and all its dependencies;
			// 5) Collect files from the allCppNoTest target and all its dependencies;
			 Parallel.Invoke(
				GetAllFilesFiltered, // fills up _allFilesFiltered and _duplicateFiles sets
				GetFlexFiles, // fills up _flexFiles set
				GetTeFiles, // fills up _teFiles set
				GetAllCsharpNoTestsFiles, // fills up _allCsharpNoTestsFiles set
				GetAllCppNoTestFiles // fills up _allCppNoTestFiles set
			);

			Parallel.Invoke(
				CleanUpFlexFiles,
				CleanUpTeFiles,
				CleanUpAllCsharpNoTestsFiles,
				CleanUpAllCppNoTestFiles,
				CollectLocalizationFiles
			);
		}

		/// <summary>
		/// Examines _allFilesFiltered to assign localization files to their respective _languages.
		/// </summary>
		private void CollectLocalizationFiles()
		{
			foreach (var currentLanguage in _languages)
			{
				var language = currentLanguage;
				currentLanguage.TeFiles = (_allFilesFiltered.Where(file => FileIsForTeLocalization(file, language)));
				currentLanguage.OtherFiles = (_allFilesFiltered.Where(file => FileIsForNonTeLocalization(file, language)));
			}
		}

		/// <summary>
		/// Removes any _teFiles that have already been rejected elsewhere.
		/// </summary>
		private void CleanUpTeFiles()
		{
			CleanUpFileSet(_teFiles, "TE");
		}

		/// <summary>
		/// Removes any _flexFiles that have already been rejected elsewhere.
		/// </summary>
		private void CleanUpFlexFiles()
		{
			CleanUpFileSet(_flexFiles, "FLEx");
		}

		/// <summary>
		/// Removes any _allCsharpNoTestsFiles that have already been rejected elsewhere.
		/// </summary>
		private void CleanUpAllCsharpNoTestsFiles()
		{
			CleanUpFileSet(_allCsharpNoTestsFiles, "allCsharpNoTests");
		}

		/// <summary>
		/// Removes any _allCppNoTestFiles that have already been rejected elsewhere.
		/// </summary>
		private void CleanUpAllCppNoTestFiles()
		{
			CleanUpFileSet(_allCppNoTestFiles, "allCppNoTest");
		}

		/// <summary>
		/// Removes any files that have already been rejected elsewhere.
		/// </summary>
		/// <param name="fileSet">Set of files to be cleaned up</param>
		/// <param name="fileSetName">Name of file set to be used in text reports</param>
		private void CleanUpFileSet(HashSet<InstallerFile> fileSet, string fileSetName)
		{
			var initialFiles = fileSet;
			foreach (var file in _duplicateFiles.Where(fileSet.Contains))
			{
				file.ReasonForRemoval += " Duplicate file";
				fileSet.Remove(file);
			}

			lock (_overallReport)
			{
				var removedFiles = initialFiles.Count - fileSet.Count;
				if (removedFiles > 0)
				{
					_overallReport.AddReportLine("Removed " + (removedFiles) +
								  " already rejected files from " + fileSetName + " file set:");
					foreach (var file in initialFiles.Except(fileSet))
						_overallReport.AddReportLine("    " + file.RelativeSourcePath);
				}
			}
			// Make sure fileSet references equivalent files from _allFilesFiltered:
			ReplaceEquivalentFiles(fileSet, _allFilesFiltered);
		}

		/// <summary>
		/// Assigns to _allCsharpNoTestsFiles the list of files built by target allCsharpNoTests.
		/// </summary>
		private void GetAllCsharpNoTestsFiles()
		{
			_allCsharpNoTestsFiles = GetSpecificTargetFiles("allCsharpNoTests", "from allCsharpNoTests target");
		}

		/// <summary>
		/// Assigns to _allCppNoTestFiles the list of files built by target allCppNoTest.
		/// </summary>
		private void GetAllCppNoTestFiles()
		{
			_allCppNoTestFiles = GetSpecificTargetFiles("allCppNoTest", "from allCppNoTest target");
		}

		/// <summary>
		/// Assigns to _flexFiles the list of files determined to be dependencies of FLEx.exe.
		/// </summary>
		private void GetFlexFiles()
		{
			_flexFiles = GetSpecificTargetFiles("LexTextExe", "from FLEx target");
		}

		/// <summary>
		/// Assigns to _teFiles the list of files determined to be dependencies of TE.exe.
		/// </summary>
		private void GetTeFiles()
		{
			_teFiles = GetSpecificTargetFiles("TeExe", "from TE target");
		}

		/// <summary>
		/// Works out the set of files that are dependencies of the given MSBuild target.
		/// </summary>
		/// <param name="target">MSBuild target</param>
		/// <param name="description">Used in error messages, describes target</param>
		/// <returns>Set of dependencies of given target</returns>
		private HashSet<InstallerFile> GetSpecificTargetFiles(string target, string description)
		{
			var assemblyProcessor = new AssemblyDependencyProcessor(_projRootPath, _outputReleaseFolderAbsolutePath, description, _fileOmissions, _overallReport);
			assemblyProcessor.Init();

			var flexBuiltFilePaths = assemblyProcessor.GetAssemblySet(target);

			var fileSet = new HashSet<InstallerFile>();
			foreach (var pathDefinition in flexBuiltFilePaths)
			{
				var comment = AssemblyDependencyProcessor.CombineStrings(description, pathDefinition.Value);
				var file = GetInstallerFile(pathDefinition.Key, null, comment);
				fileSet.Add(file);
			}
			return fileSet;
		}

		/// <summary>
		/// Collects the main set of files (typically from Output\Release and DistFiles), removes junk,
		/// then returns the combined file set.
		/// </summary>
		/// <returns>A set of files rejected as duplicates (typically in both Output\Release and DistFiles)</returns>
		private void GetAllFilesFiltered()
		{
			Parallel.Invoke(
				CollectOutputReleaseFiles, // fills up _builtFilesFiltered
				CollectDistFiles // fills up _distFilesFiltered
				);

			// Merge all collected files together:
			_allFilesFiltered = MergeFileSets(_builtFilesFiltered, _distFilesFiltered);

			// Report on removed duplicate files:
			lock (_overallReport)
			{
				_overallReport.AddReportLine("Found " + _duplicateFiles.Count + @" duplicate files between Output\Release and DistFiles:");
				foreach (var file in _duplicateFiles)
					_overallReport.AddReportLine("    " + file.RelativeSourcePath);
			}
		}

		/// <summary>
		/// Collects all files from DistFiles. Filters out recognizable junk.
		/// Fills up _distFilesFiltered set.
		/// </summary>
		private void CollectDistFiles()
		{
			_distFilesFiltered = CollectAndFilterFiles(_distFilesFolderAbsolutePath);
		}

		/// <summary>
		/// Collects all files from Output\Release. Filters out recognizable junk.
		/// Fills up _builtFilesFiltered set.
		/// </summary>
		private void CollectOutputReleaseFiles()
		{
			_builtFilesFiltered = CollectAndFilterFiles(_outputReleaseFolderAbsolutePath);
		}

		/// <summary>
		/// Collects all files from given path. Filters out recognizable junk.
		/// </summary>
		/// <param name="path">Folder path at root of search</param>
		/// <returns>Filtered set of files</returns>
		private HashSet<InstallerFile> CollectAndFilterFiles(string path)
		{
			var fileSet = CollectFiles(path, _rootDirectory, "", true, false);
			_overallReport.AddReportLine("Collected " + fileSet.Count + " files in total from " + path + ".");

			// Filter out known junk:
			var fileSetFiltered = FilterOutSpecifiedOmissions(fileSet);
			lock (_overallReport)
			{
				_overallReport.AddReportLine("Removed " + (fileSet.Count - fileSetFiltered.Count) + " files from " + path + " file set:");
				foreach (var file in fileSet.Except(fileSetFiltered))
					_overallReport.AddReportLine("    " + file.RelativeSourcePath + ": " + file.ReasonForRemoval);
			}
			return fileSetFiltered;
		}

		/// <summary>
		/// Class to examine Visual Studio project files and figure out the dependent project assemblies
		/// and referenced assemblies of a given project.
		/// </summary>
		internal class AssemblyDependencyProcessor
		{
			private bool _initialized;
			private readonly string _projRootPath;
			private readonly string _assemblyFolderPath;
			private readonly string _description;
			private const string MsbuildUri = "http://schemas.microsoft.com/developer/msbuild/2003";
			private readonly HashSet<string> _completedTargets = new HashSet<string>();
			private const string CommentDelimiter = "; ";
			private readonly FileOmissionList _fileOmissions;
			private readonly ReportSystem _report;

			class TargetsFileData
			{
				public string FilePath;
				public readonly XmlDocument XmlDoc = new XmlDocument();
				public XmlNamespaceManager XmlnsManager;
			}
			private readonly List<TargetsFileData> _targetsFiles = new List<TargetsFileData>();
			private readonly bool _foundFieldWorksTargetsFile;

			public AssemblyDependencyProcessor(string projRootPath, string assemblyFolderPath, string description, FileOmissionList fileOmissions, ReportSystem report)
			{
				_fileOmissions = fileOmissions;
				_report = report;
				_projRootPath = projRootPath;
				_assemblyFolderPath = assemblyFolderPath;
				_description = description;
				_foundFieldWorksTargetsFile = false;

				var targetsFiles = Directory.GetFiles(Path.Combine(_projRootPath, "Build"), "*.targets", SearchOption.TopDirectoryOnly);
				foreach (var file in targetsFiles)
				{
					_targetsFiles.Add(new TargetsFileData {FilePath = file});
					if (file.EndsWith(@"\FieldWorks.targets"))
						_foundFieldWorksTargetsFile = true;
				}
			}

			public void Init()
			{
				if (!_foundFieldWorksTargetsFile)
					throw new FileNotFoundException("Could not find FieldWorks Targets file");

				foreach (var targetsFile in _targetsFiles)
				{
					targetsFile.XmlDoc.Load(targetsFile.FilePath);
					targetsFile.XmlnsManager = new XmlNamespaceManager(targetsFile.XmlDoc.NameTable);
					targetsFile.XmlnsManager.AddNamespace("msbuild", MsbuildUri);
				}

				_initialized = true;
			}

			/// <summary>
			/// Main entry point.
			/// </summary>
			/// <param name="vsProj">Visual Studio project name to be mined for dependencies</param>
			/// <returns>Dictionary where Keys are paths of assemblies, Values are descriptions of dependency links</returns>
			public Dictionary<string, string> GetAssemblySet(string vsProj)
			{
				if (!_initialized)
					throw new Exception("Internal (programmer) error: method Init() not called on AssemblyDependencyProcessor object");

				lock (_completedTargets)
				{
					if (_completedTargets.Contains(vsProj))
						return new Dictionary<string, string>();
					_completedTargets.Add(vsProj);
				}

				return InternalGetAssemblySet(vsProj, null);
			}

			/// <summary>
			/// Entry point of recursion.
			/// </summary>
			/// <param name="vsProj">Visual Studio project name to be mined for dependencies</param>
			/// <param name="parentProj">Parent project of vsProj</param>
			/// <returns>Dictionary where Keys are paths of assemblies, Values are descriptions of dependency links</returns>
			private Dictionary<string, string> InternalGetAssemblySet(string vsProj, string parentProj)
			{
				var assembliesToReturn = new Dictionary<string, string>();

				var foundTargetButItWasLinux = false;

				foreach (var targetsFile in _targetsFiles)
				{
					var targetNode =
						targetsFile.XmlDoc.SelectSingleNode("/msbuild:Project/msbuild:Target[@Name='" + vsProj + "']", targetsFile.XmlnsManager) as XmlElement;
					if (targetNode == null)
						continue; // Not in current targetsFile

					// If target is for Linux, then ignore:
					var condition = targetNode.GetAttribute("Condition");
					if (condition == "'$(OS)'=='Unix'")
					{
						foundTargetButItWasLinux = true;
						continue;
					}
					// This target is c++ and does not have an assembly, so don't flag it as a serious error.
					if (vsProj == "Unit++")
						continue;
					// The target will have one or more MSBuild nodes or Make nodes that we will use to
					// see what assemblies get built by it.
					try
					{
						var buildSystemParsers = VisualStudioProjectParser.GetProjectParsers(targetNode, targetsFile.XmlnsManager, _projRootPath, _report);
						buildSystemParsers.AddRange(MakefileParser.GetMakefileParsers(targetNode, targetsFile.XmlnsManager, _projRootPath));

						foreach (var buildSystemParser in buildSystemParsers)
						{
							// Important: we are assuming that the built assembly will ultimately appear
							// in Output\Release (_assemblyFolderPath):
							var builtAssemblyPath = buildSystemParser.GetOutputAssemblyPath(_assemblyFolderPath);
							if (builtAssemblyPath == null)
								continue;

							var newDescription = parentProj == null ? "" : "ProjDep:" + parentProj;
							AddAssemblyAndRelativesToDictionary(builtAssemblyPath, assembliesToReturn, newDescription);

							// Add all referenced assemblies where given, as these are probably part of the FW system,
							// but may not necessarily be built by our build system:
							var referencedAssemblies = buildSystemParser.GetReferencedAssemblies(_assemblyFolderPath);
							foreach (var assembly in referencedAssemblies)
								AddAssemblyAndRelativesToDictionary(assembly, assembliesToReturn, "AssRef:" + buildSystemParser.GetSourceName());
						}
					}
					catch (FileNotFoundException fnfe)
					{
						_report.AddSeriousIssue("Error " + _description + " in Target " + vsProj + ": " + fnfe.Message);
					}
					catch (DataException de)
					{
						_report.AddSeriousIssue("Error " + _description + " in Target " + vsProj + ": " + de.Message);
					}

					// Recurse through dependencies:
					var dependsOnTargetsTxt = targetNode.GetAttribute("DependsOnTargets");
					string[] dependsOnTargets = null;
					if (dependsOnTargetsTxt.Length > 0)
						dependsOnTargets = dependsOnTargetsTxt.Split(new[] { ';' });

					if (dependsOnTargets != null)
					{
						Parallel.ForEach(dependsOnTargets, target =>
						{
							var doneTargetAlready = false;
							lock (_completedTargets)
							{
								if (_completedTargets.Contains(target))
									doneTargetAlready = true;
								else
									_completedTargets.Add(target);
							}
							if (!doneTargetAlready)
							{
								var dependencies = InternalGetAssemblySet(target, vsProj);
								lock (assembliesToReturn)
								{
									foreach (var dependency in dependencies)
										AddOrAugmentDictionaryValue(assembliesToReturn, dependency.Key, dependency.Value);
								}
							}
						});
					}
					return assembliesToReturn;
				}

				if (!foundTargetButItWasLinux && vsProj != "Unit++")
					_report.AddSeriousIssue("Error " + _description + ": could not find MSBuild Target " + vsProj + " (referenced by " + (parentProj ?? "nothing") + ") in any .targets file.");

				return assembliesToReturn;
			}

			/// <summary>
			/// Adds the given assembly path to the given dictionary, along with the .pdb, config and manifest files for
			/// that assembly.
			/// </summary>
			/// <param name="fullPath">Full path to assembly</param>
			/// <param name="dictionary">Dictionary to record data</param>
			/// <param name="description">Description of dictionary entry</param>
			private void AddAssemblyAndRelativesToDictionary(string fullPath, Dictionary<string, string> dictionary, string description)
			{
				var fullPathNoExtension = Path.Combine(Path.GetDirectoryName(fullPath), Path.GetFileNameWithoutExtension(fullPath));
				AddOrAugmentDictionaryValue(dictionary, fullPath, description);

				AddReleatedFileIfExists(dictionary, fullPathNoExtension, ".pdb", description);
				AddReleatedFileIfExists(dictionary, fullPath, ".config", description);
				AddReleatedFileIfExists(dictionary, fullPath, ".manifest", description);
			}

			/// <summary>
			/// Adds key/value pair to dictionary if key not already there. If it is, then given value is appended to existing value.
			/// </summary>
			/// <param name="dictionary">dictionary to modify</param>
			/// <param name="key">key to look for or add</param>
			/// <param name="value">data to be added to existing value, or used for new value</param>
			private void AddOrAugmentDictionaryValue(IDictionary<string, string> dictionary, string key, string value)
			{
				if (FileShouldBeOmitted(key))
					return;

				string existingValue;
				if (dictionary.TryGetValue(key, out existingValue))
					dictionary[key] = CombineStrings(existingValue, value);
				else
					dictionary.Add(key, value);
			}

			/// <summary>
			/// Decides if the given path is referenced, even partially, by an element in the file omissions list.
			/// </summary>
			/// <param name="path">Path to be considered</param>
			/// <returns>true if file matches an element in the omissions list</returns>
			private bool FileShouldBeOmitted(string path)
			{
				var omissionsMatches = _fileOmissions.Where(om => om.CaseSensitive ? path.Contains(om.RelativePath) : path.ToLowerInvariant().Contains(om.RelativePath.ToLowerInvariant()));
				return (omissionsMatches.Count() > 0);
			}

			/// <summary>
			/// Concatenates two strings, using constant delimiter string. Handles either being empty or null
			/// </summary>
			/// <param name="original">string on left of concatenation</param>
			/// <param name="addition">string on right of concatenation</param>
			/// <returns>concatenated strings</returns>
			public static string CombineStrings(string original, string addition)
			{
				var combinedStrings = original ?? "";

				if (!string.IsNullOrEmpty(addition))
				{
					if (combinedStrings.Length > 0)
						combinedStrings += CommentDelimiter;
					combinedStrings += addition;
				}
				return combinedStrings;
			}

			/// <summary>
			/// Adds a file path to given dictionary, if the file exists, where the path is made of a path plus file name
			/// stored in one argument, and a file extension stored in another argument.
			/// </summary>
			/// <param name="dictionary">the dictionary to modify</param>
			/// <param name="fullPathNoExtension">full file path minus extension</param>
			/// <param name="extension">file extension</param>
			/// <param name="description">text to associate with file path in the dictionary</param>
			private void AddReleatedFileIfExists(IDictionary<string, string> dictionary, string fullPathNoExtension, string extension, string description)
			{
				var fullPath = fullPathNoExtension + extension;

				if (FileShouldBeOmitted(fullPath))
					return;

				if (File.Exists(fullPath))
					AddOrAugmentDictionaryValue(dictionary, fullPath, description);
			}
		}

		/// <summary>
		/// Collects files from given path, building directory tree along the way.
		/// </summary>
		/// <param name="folderPath">Full path to begin collecting files from</param>
		/// <param name="dirNode">Root of directory tree to fill in along the way</param>
		/// <param name="comment">Comment to tag each file with</param>
		/// <param name="isInstallerRoot">True if given directory represents the installation folder on the end-user's machine</param>
		/// <param name="alreadyRedirectedParent">True if we have added an ancestor of dirNode to _redirectedFolders, so we don't need to consider this dirNode for that purpose.</param>
		/// <returns>Set (flattened) of collected files.</returns>
		private HashSet<InstallerFile> CollectFiles(string folderPath, DirectoryTreeNode dirNode, string comment, bool isInstallerRoot, bool alreadyRedirectedParent)
		{
			lock (dirNode)
			{
				// Fill in data about current directory node:
				dirNode.TargetPath = MakeRelativeTargetPath(folderPath);
				// If the folderPath has been earmarked for redirection to elsewhere on the
				// end user's machine, make the necessary arrangements:
				string redirection = _folderRedirections.Redirection(MakeRelativePath(folderPath));
				if (redirection != null && !alreadyRedirectedParent)
				{
					dirNode.Name = redirection;
					dirNode.IsDirReference = true;
					_redirectedFolders.Add(dirNode);
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
				var instFile = GetInstallerFile(file, dirNode.DirId, comment);

				// Record the current file in the return object and the local directory node's data.
				fileSet.Add(instFile);
				lock (dirNode)
				{
					dirNode.LocalFiles.Add(instFile);
				}
			}

			// Now recurse all subfolders:
			foreach (var subFolder in Directory.GetDirectories(folderPath))
			{
				// Re-use relevant DirectoryTreeNode if target path has been used before:
				var targetPath = MakeRelativeTargetPath(subFolder);
				DirectoryTreeNode dtn;
				lock (dirNode)
				{
					dtn = dirNode.Children.FirstOrDefault(c => c.TargetPath.ToLowerInvariant() == targetPath.ToLowerInvariant());
					if (dtn == null)
					{
						dtn = new DirectoryTreeNode();
						dirNode.Children.Add(dtn);
					}
				}
				var subFolderFiles = CollectFiles(subFolder, dtn, comment, false, alreadyRedirectedParent);
				fileSet.UnionWith(subFolderFiles);
			}
			return fileSet;
		}

		/// <summary>
		/// Makes an InstallerFile object from a given file path.
		/// </summary>
		/// <param name="file">Full path to file</param>
		/// <param name="dirId">Installer directory Id</param>
		/// <param name="comment">Comment to tag file with</param>
		/// <returns>InstallerFile object containing lots of file details</returns>
		private InstallerFile GetInstallerFile(string file, string dirId, string comment)
		{
			var instFile = new InstallerFile();
			// Put in all the file data the WIX install build will need to know:
			instFile.Name = Path.GetFileName(file);
			instFile.Comment = comment;
			instFile.FullPath = file;
			instFile.RelativeSourcePath = MakeRelativePath(file);
			instFile.Id = MakeId(instFile.Name, instFile.RelativeSourcePath);
			instFile.DirId = dirId;

			if (!File.Exists(file))
			{
				_overallReport.AddSeriousIssue("ERROR: file '" + file + "' [" + comment + "] could not be found");
				return instFile;
			}
			var fileVersionInfo = FileVersionInfo.GetVersionInfo(file);
			var fileVersion = "";
			if (fileVersionInfo.FileVersion != null)
			{
				fileVersion = fileVersionInfo.FileMajorPart + "." + fileVersionInfo.FileMinorPart + "." +
							  fileVersionInfo.FileBuildPart + "." + fileVersionInfo.FilePrivatePart;
			}
			var fi = new FileInfo(file);

			instFile.Size = fi.Length;
			instFile.DateTime = fi.LastWriteTime.ToString(CultureInfo.InvariantCulture);
			instFile.Version = fileVersion;
			instFile.Md5 = CalcFileMd5(file);
			return instFile;
		}

		/// <summary>
		/// Computes the MD5 hash of the given file.
		/// </summary>
		/// <param name="filePath">Path of file to compute MD5 hash for</param>
		/// <returns>MD5 hash of file</returns>
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
		/// Removes junk entries (defined in _fileOmissions) from a set of files.
		/// </summary>
		/// <param name="fileSet">Set of files</param>
		/// <returns>New set containing cleaned up version of fileSet</returns>
		private HashSet<InstallerFile> FilterOutSpecifiedOmissions(IEnumerable<InstallerFile> fileSet)
		{
			var returnSet = new HashSet<InstallerFile>();
			foreach (var f in fileSet)
			{
				var relPath = f.RelativeSourcePath;
				var relPathLower = relPath.ToLowerInvariant();
				var omissionsMatches = _fileOmissions.Where(om => om.CaseSensitive ? relPath.Contains(om.RelativePath) : relPathLower.Contains(om.RelativePath.ToLowerInvariant()));
				if (omissionsMatches.Count() == 0)
					returnSet.Add(f);
				else
					f.ReasonForRemoval += " " + omissionsMatches.First().Reason;
			}
			return returnSet;
		}

		/// <summary>
		/// Merges 2 sets of installable files, filtering out any from the second set that appear to be duplicates.
		/// The method ArbitrateFileMatches below provides the heuristics for determining if a file is a duplicate.
		/// </summary>
		/// <param name="preferredSet">This set of files will always be in the result</param>
		/// <param name="otherSet">Files from this set will be in the result if there are no duplicates with the first set</param>
		/// <returns>A new set containing the merged combination of the arguments</returns>
		private HashSet<InstallerFile> MergeFileSets(IEnumerable<InstallerFile> preferredSet, IEnumerable<InstallerFile> otherSet)
		{
			// Find sets of files with matching names:
			foreach (var currentFile in preferredSet.ToArray())
			{
				var matchingFiles = from other in otherSet
									where other.FileNameMatches(currentFile)
									select other;

				ArbitrateFileMatches(currentFile, matchingFiles);
			}

			var returnSet = new HashSet<InstallerFile>();

			returnSet.UnionWith(preferredSet.Except(_duplicateFiles));
			returnSet.UnionWith(otherSet.Except(_duplicateFiles));

			return returnSet;
		}

		/// <summary>
		/// Decides whether to accept or reject a given file when compared with the given set of "matching" files.
		/// </summary>
		/// <param name="candidate">File to be considered</param>
		/// <param name="matchingFiles">Set of files to consider against</param>
		private void ArbitrateFileMatches(InstallerFile candidate, IEnumerable<InstallerFile> matchingFiles)
		{
			var candidateTargetPath = MakeRelativeTargetPath(candidate.RelativeSourcePath);
			foreach (var file in matchingFiles)
			{
				// If the candidate is functionally equivalent to the current file, then we'll skip this match,
				// avoiding the possibility of rejecting one of them, which would be interpreted as rejecting them all:
				if (candidate.Equals(file))
					continue;

				// If the candidate has a different file hash from the current file's hash, then we'll skip
				// this match, as they are genuinely different files:
				var fileFullPath = Path.Combine(_projRootPath, file.RelativeSourcePath);
				var fileHash = CalcFileMd5(fileFullPath);

				var candidateFullPath = Path.Combine(_projRootPath, candidate.RelativeSourcePath);
				var candidateHash = CalcFileMd5(candidateFullPath);

				if (fileHash != candidateHash)
				{
					// The files are different. If they were destined for the same target path,
					// then we have a problem that needs to be reported:
					if (MakeRelativeTargetPath(fileFullPath) == MakeRelativeTargetPath(candidateFullPath))
					{
						_overallReport.AddSeriousIssue("WARNING: " + fileFullPath + " is supposed to be the same as " + candidateFullPath +
							", but it isn't. Only one will get into the installer. You should check and see which one is correct, and do something about the other.");
					}
					else
						continue;
				}

				var fileTargetPath = MakeRelativeTargetPath(file.RelativeSourcePath);
				// If the relative target paths are the same, we'll prefer the candidate. Where they differ, we'll take the
				// one with the deeper target path:
				if (candidateTargetPath == fileTargetPath || candidateTargetPath.Length >= fileTargetPath.Length)
				{
					_duplicateFiles.Add(file);
					candidate.Comment += "(preferred over " + file.RelativeSourcePath + ")";
				}
				else
				{
					_duplicateFiles.Add(candidate);
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
		private static void ReplaceEquivalentFiles(ISet<InstallerFile> subSet, IEnumerable<InstallerFile> masterSet)
		{
			foreach (var file in subSet.ToArray())
			{
				var masterEquivalenceSet = masterSet.Where(masterFile => masterFile.Equals(file));
				if (masterEquivalenceSet.Count() == 0)
					continue;

				var replacement = masterEquivalenceSet.Single();
				replacement.Comment = AssemblyDependencyProcessor.CombineStrings(replacement.Comment, file.Comment);

				subSet.Remove(file);
				subSet.Add(replacement);
			}
		}

		/// <summary>
		/// Manipulates sets of built files to produce sets of files for each installer feature.
		/// This is the cleverest function in the whole project.
		/// </summary>
		private void BuildFeatureFileSets()
		{
			// UsedDistFiles consists of all DistFiles except those that were duplicated in Output\Release:
			var usedDistFiles = _allFilesFiltered.Intersect(_distFilesFiltered);

			var flexOnlyDistFiles = from file in usedDistFiles
									where FileIsForFlexOnly(file)
									select file;

			_flexMoviesFeatureFiles = from file in usedDistFiles
									   where FileIsForFlexMoviesOnly(file)
									   select file;
			_sampleDataFeatureFiles = from file in usedDistFiles
									  where FileIsForSampleDataOnly(file)
									  select file;

			var localizationFiles = from file in usedDistFiles
									where FileIsForLocalization(file)
									select file;

			var teOnlyDistFiles = from file in usedDistFiles
								  where FileIsForTeOnly(file)
								  select file;
			var coreDistFiles = usedDistFiles.Except(flexOnlyDistFiles).Except(teOnlyDistFiles).Except(_flexMoviesFeatureFiles).Except(_sampleDataFeatureFiles).Except(localizationFiles);

			var allCppAndCsharpFiles = _allCsharpNoTestsFiles.Union(_allCppNoTestFiles);

			var flexButNotTeFiles = _flexFiles.Except(_teFiles);
			var teButNotFlexFiles = _teFiles.Except(_flexFiles);

			var coreBuiltFiles = allCppAndCsharpFiles.Except(flexButNotTeFiles).Except(teButNotFlexFiles);
			var flexOnlyBuiltFiles = flexButNotTeFiles.Except(coreBuiltFiles);
			var teOnlyBuiltFiles = teButNotFlexFiles.Except(coreBuiltFiles);

			_flexFeatureFiles = flexOnlyBuiltFiles.Union(flexOnlyDistFiles);
			_teFeatureFiles = teOnlyBuiltFiles.Union(teOnlyDistFiles);
			_fwCoreFeatureFiles = coreBuiltFiles.Union(coreDistFiles);

			// Add to _fwCoreFeatureFiles any files specified in the _coreFileOrphans set:
			// (Such files are not needed by FLEx or TE, so won't appear in FLEx or TE sets.)
			var fwCoreFeatureFiles = new HashSet<InstallerFile>();
			foreach (var file in from file in _allFilesFiltered
								 from coreFile in _coreFileOrphans
								 where file.RelativeSourcePath.ToLowerInvariant().Contains(coreFile.ToLowerInvariant())
								 select file)
			{
				file.Comment += " Listed in <CoreFileOrphans> ";
				fwCoreFeatureFiles.Add(file);
			}
			_fwCoreFeatureFiles = _fwCoreFeatureFiles.Union(fwCoreFeatureFiles);

			// Add to _fwCoreFeatureFiles any files specified in the _forceBuiltFilesIntoCore set:
			// (Such files are needed by FLEx and TE but aren't referenced in the dependencies of one of them, so they would otherwise end up in the other.)
			var forceBuiltFilesIntoCore = new HashSet<InstallerFile>();
			foreach (var file in from file in _allFilesFiltered
								 from coreFile in _forceBuiltFilesIntoCore
								 where file.RelativeSourcePath.ToLowerInvariant().Contains(coreFile.ToLowerInvariant())
								 select file)
			{
				file.Comment += " Specifically added to FW_Core via //FeatureAllocation/ForceBuiltFilesIntoCore in XML configuration. ";
				forceBuiltFilesIntoCore.Add(file);
			}
			_fwCoreFeatureFiles = _fwCoreFeatureFiles.Union(forceBuiltFilesIntoCore);
			// Remove from the TE and FLex features the files we've just forced into FW_Core:
			_flexFeatureFiles = _flexFeatureFiles.Except(forceBuiltFilesIntoCore);
			_teFeatureFiles = _teFeatureFiles.Except(forceBuiltFilesIntoCore);

			// Add to _flexFeatureFiles any files specified in the _forceBuiltFilesIntoFlex set:
			// (Such files are needed by FLEx but aren't referenced in FLEx's dependencies.)
			var forceBuiltFilesIntoFlex = new HashSet<InstallerFile>();
			foreach (var file in from file in _allFilesFiltered
								 from flexFile in _forceBuiltFilesIntoFlex
								 where file.RelativeSourcePath.ToLowerInvariant().Contains(flexFile.ToLowerInvariant())
								 select file)
			{
				file.Comment += " Specifically added to FLEx via //FeatureAllocation/ForceBuiltFilesIntoFlex in XML configuration. ";
				forceBuiltFilesIntoFlex.Add(file);
			}
			_flexFeatureFiles = _flexFeatureFiles.Union(forceBuiltFilesIntoFlex);
			// Remove from the Core features the files we've just forced into FLEx:
			_fwCoreFeatureFiles = _fwCoreFeatureFiles.Except(forceBuiltFilesIntoFlex);

			// Add to _teFeatureFiles any files specified in the _forceBuiltFilesIntoTe set:
			// (Such files are needed by TE but aren't referenced in TE's dependencies.)
			var forceBuiltFilesIntoTe = new HashSet<InstallerFile>();
			foreach (var file in from file in _allFilesFiltered
								 from teFile in _forceBuiltFilesIntoTe
								 where file.RelativeSourcePath.ToLowerInvariant().Contains(teFile.ToLowerInvariant())
								 select file)
			{
				file.Comment += " Specifically added to TE via //FeatureAllocation/ForceBuiltFilesIntoTE in XML configuration. ";
				forceBuiltFilesIntoTe.Add(file);
			}
			_teFeatureFiles = _teFeatureFiles.Union(forceBuiltFilesIntoTe);
			// Remove from the Core features the files we've just forced into TE:
			_fwCoreFeatureFiles = _fwCoreFeatureFiles.Except(forceBuiltFilesIntoTe);

			// Run tests to see if any files that look like they are for TE appear in the core or in FLEx:
			TestForPossibleTeFiles(_fwCoreFeatureFiles, "Core");
			TestForPossibleTeFiles(_flexFeatureFiles, "FLEx");

			// Assign feature names:
			foreach (var file in _flexFeatureFiles)
				file.Features.Add("FLEx");
			foreach (var file in _flexMoviesFeatureFiles)
				file.Features.Add("FlexMovies");
			foreach (var file in _sampleDataFeatureFiles)
				file.Features.Add("SampleData");
			foreach (var file in _teFeatureFiles)
				file.Features.Add("TE");
			foreach (var file in _fwCoreFeatureFiles)
				file.Features.Add("FW_Core");

			foreach (var language in _languages)
			{
				foreach (var file in language.OtherFiles)
					file.Features.Add(language.LanguageName);
				foreach (var file in language.TeFiles)
					file.Features.Add(language.LanguageName + "_TE");
			}

			_orphanFiles = _allFilesFiltered.Where(file => file.Features.Count == 0).ToArray();
			if (_addOrphans)
			{
				foreach (var file in _orphanFiles)
					file.Features.Add("FW_Core");
			}

			// Iterate over files that are assigned to at least one feature:)
			foreach (var file in _allFilesFiltered.Where(file => file.Features.Count > 0))
			{
				// Test if any features of the current file are represented in Features.wxs:
				var file1 = file;
				file.OnlyUsedInUnusedFeatures =
					!_representedFeatures.Any(feature => file1.Features.Contains(feature));

				// Assign a DiskId for the file's cabinet:
				var firstFeature = file.Features.First();
				if (_featureCabinetMappings.ContainsKey(firstFeature))
					file.DiskId = _featureCabinetMappings[firstFeature].GetCabinet(file);
				else
					file.DiskId = _featureCabinetMappings["Default"].GetCabinet(file);
			}
		}

		/// <summary>
		/// Uses a set of coarse heuristics to guess whether any files in the given set
		/// might possibly belong uniquely to TE. If so, the _seriousIssues report is extended to
		/// report the suspect files.
		/// Uses heuristics from the TeFileNameTestHeuristics node of InstallerConfig.xml,
		/// and checks only against file name, not folder path.
		/// </summary>
		/// <param name="files">List of files</param>
		/// <param name="section">Label of installer section to report to user if there is a problem</param>
		private void TestForPossibleTeFiles(IEnumerable<InstallerFile> files, string section)
		{
			foreach (var file in from file in files
								 from heuristic in _teFileNameTestHeuristics
								 let re = new Regex(heuristic)
								 where re.IsMatch(file.Name)
								 select file)
			{
				// We've found a match via the heuristics. Just check the file isn't specifically exempted:
				var parameterizedPath = file.RelativeSourcePath;
				if (!_teFileNameExceptions.Contains(parameterizedPath))
				{
					_overallReport.AddSeriousIssue("WARNING: " + file.RelativeSourcePath +
						" looks like a file specific to TE, but it appears in the " + section +
						" section of the installer. This may adversely affect FLEx-only users in sensitive locations.");
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

			return _flexFileHeuristics.IsFileIncluded(file.RelativeSourcePath);
		}

		/// <summary>
		/// Determines heuristically whether a given installer file is for use in FLEx Movies only.
		/// Examines file name and relative path.
		/// </summary>
		/// <param name="file">the candidate installer file</param>
		/// <returns>true if the file is for FLEx Movies only</returns>
		private bool FileIsForFlexMoviesOnly(InstallerFile file)
		{
			return _flexMovieFileHeuristics.IsFileIncluded(file.RelativeSourcePath);
		}

		/// <summary>
		/// Determines heuristically whether a given installer file is for use in Sample Data only.
		/// Examines file name and relative path.
		/// </summary>
		/// <param name="file">the candidate installer file</param>
		/// <returns>true if the file is for FLEx Movies only</returns>
		private bool FileIsForSampleDataOnly(InstallerFile file)
		{
			return _sampleDataFileHeuristics.IsFileIncluded(file.RelativeSourcePath);
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
			if (FileIsForLocalization(file))
				return false;

			return _teFileHeuristics.IsFileIncluded(file.RelativeSourcePath);
		}

		/// <summary>
		/// Returns true if the given file is a localization resource file for the
		/// given language.
		/// </summary>
		/// <param name="file">File to be analyzed</param>
		/// <param name="language">Language to be considered</param>
		/// <returns>True if file is a localization file for that language</returns>
		private bool FileIsForLocalization(InstallerFile file, LocalizationData language)
		{
			if (language.Folder.Length == 0)
				throw new Exception("Language defined with no localization folder");

			return _localizationHeuristics[language.Folder].IsFileIncluded(file.RelativeSourcePath);
		}

		/// <summary>
		/// Returns true if the given file is a localization resource file for any language.
		/// </summary>
		/// <param name="file">File to be analyzed</param>
		/// <returns>True if file is a localization file for any language</returns>
		private bool FileIsForLocalization(InstallerFile file)
		{
			return _languages.Any(currentLanguage => FileIsForLocalization(file, currentLanguage));
		}

		/// <summary>
		/// Returns true if the given file is a TE localization resource file.
		/// </summary>
		/// <param name="file">File to be analyzed</param>
		/// <param name="language">Language to be considered</param>
		/// <returns>True if file is a TE localization file for that language</returns>
		private bool FileIsForTeLocalization(InstallerFile file, LocalizationData language)
		{
			if (language.Folder.Length == 0)
				throw new Exception("Language defined with no localization folder");

			return _teLocalizationHeuristics[language.Folder].IsFileIncluded(file.RelativeSourcePath);
		}

		/// <summary>
		/// Returns true if the given file is a TE localization resource file for any language.
		/// </summary>
		/// <param name="file">File to be analyzed</param>
		/// <returns>True if file is a TE localization file for any language</returns>
		private bool FileIsForTeLocalization(InstallerFile file)
		{
			return _languages.Any(currentLanguage => FileIsForTeLocalization(file, currentLanguage));
		}

		/// <summary>
		/// Returns true if the given file is a TE or FLEx localization resource file.
		/// </summary>
		/// <param name="file">File to be analyzed</param>
		/// <param name="language">Language to be considered</param>
		/// <returns>True if file is a TE or FLEx localization file</returns>
		private bool FileIsForNonTeLocalization(InstallerFile file, LocalizationData language)
		{
			if (FileIsForLocalization(file, language) && !FileIsForTeLocalization(file, language))
				return true;

			return false;
		}

		/// <summary>
		/// Make a full path for a file in the same folder as this program's .exe
		/// </summary>
		/// <param name="fileName">Name of local file</param>
		/// <returns>string full path of given file</returns>
		private string LocalFileFullPath(string fileName)
		{
			return Path.Combine(_exeFolder, fileName);
		}

		/// Returns an Id suitable for the installer, based on the given name and unique data.
		/// The Id will be unique if the name and unique data combination is unique.
		/// Identifiers may contain ASCII characters A-Z, a-z, digits, underscores (_), or periods (.).
		/// Every identifier must begin with either a letter or an underscore.
		/// Invalid characters are filtered out of the name (spaces, etc.)
		/// The unique data is turned into an MD5 hash and appended to the name.
		/// While the maximum permitted Id length of a regular installer is 72 chars, The Pyro tool
		/// for creating patches complains if an Id it generates from that (by prefixing with"Patch.")
		/// is more than just 62 characters. So here, if the name is more than 24 characters, it is
		/// truncated before appending the 32-character MD5 hash.
		private static string MakeId(string name, string uniqueData)
		{
			const int maxLen = 56; // When prefixed with "Patch.", must not exceed 62 characters.
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

			var hash = CalcMd5(uniqueData);
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
			return Path.Combine(_projRootPath, relPath);
		}

		/// <summary>
		/// Returns a given full path minus the front bit that defines where FieldWorks is.
		/// </summary>
		/// <param name="path">Full path</param>
		/// <returns>Full path minus FW bit.</returns>
		private string MakeRelativePath(string path)
		{
			if (path.StartsWith(_projRootPath))
			{
				string p = path.Remove(0, _projRootPath.Length);
				if (p.EndsWith("\\"))
					p = p.Remove(_projRootPath.Length - 1, 1);
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
			foreach (var source in new[] { _outputReleaseFolderRelativePath, _distFilesFolderName })
			{
				var root = Path.Combine(_projRootPath, source);
				var bitToHackOff = "";
				if (path.StartsWith(root))
					bitToHackOff = root;
				if (path.StartsWith(source))
					bitToHackOff = source;
				if (bitToHackOff.Length > 0)
				{
					var p = path.Remove(0, bitToHackOff.Length);
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
		/// Writes out the tree of folders and files to the _autoFiles WIX source file.
		/// </summary>
		private void OutputResults()
		{
			InitOutputFile();

			OutputDirectoryTreeWix(_rootDirectory, 2);

			// Output the redirected folders, removing them from the _redirectedFolders list
			// as we go, so that they don't get rejected by the OutputDirectoryTreeWix method:
			foreach (var redirectedFolder in _redirectedFolders.ToArray())
			{
				_redirectedFolders.Remove(redirectedFolder);
				OutputDirectoryTreeWix(redirectedFolder, 2);
			}

			OutputFeatureRefs();
			TerminateOutputFile();

			// Remove Previous File Library Addenda file:
			if (File.Exists(_fileLibraryAddendaName))
				File.Delete(_fileLibraryAddendaName);

			if (_newFileLibraryAddendaNode.ChildNodes.Count > 0)
			{
				// Save NewFileLibraryAddenda:
				_xmlNewFileLibraryAddenda.Save(_fileLibraryAddendaName);
			}

			ReportFileChanges();
		}

		/// <summary>
		/// Opens the output WIX source file and writes headers.
		/// </summary>
		private void InitOutputFile()
		{
			// Open the output file:
			string autoFiles = LocalFileFullPath(_autoFilesName);
			_autoFiles = new StreamWriter(autoFiles);
			_autoFiles.WriteLine("<?xml version=\"1.0\"?>");
			_autoFiles.WriteLine("<?define Version = $(Fw.Version) ?>");
			_autoFiles.WriteLine("<Wix xmlns=\"http://schemas.microsoft.com/wix/2006/wi\">");
			_autoFiles.WriteLine("	<Fragment Id=\"AutoFilesFragment\">");
			_autoFiles.WriteLine("		<Property  Id=\"AutoFilesFragment\" Value=\"1\"/>");
		}

		/// <summary>
		/// Writes WIX source (to _autoFiles) for the given directory tree (including files).
		/// </summary>
		/// <param name="dtn">Current root node of directory tree</param>
		/// <param name="indentLevel">Number of indentation tabs needed to line up tree children</param>
		private void OutputDirectoryTreeWix(DirectoryTreeNode dtn, int indentLevel)
		{
			if (!dtn.ContainsUsedFiles())
				return;

			// Don't output the folder here if it is still in the list of redirected ones:
			if (_redirectedFolders.Contains(dtn))
				return;

			var indentation = Indent(indentLevel);

			if (dtn.IsDirReference)
				_autoFiles.WriteLine(indentation + "<DirectoryRef Id=\"" + dtn.Name + "\">");
			else
			{
				var nameClause = "Name=\"" + dtn.Name + "\"";
				_autoFiles.WriteLine(indentation + "<Directory Id=\"" + dtn.DirId + "\" " + nameClause + ">");
			}

			// Iterate over all local files:
			var localFiles = dtn.LocalFiles.Where(file => file.Features.Count != 0 && !file.OnlyUsedInUnusedFeatures).ToArray();
			foreach (var file in localFiles)
				OutputFileWix(file, indentation);

			// Recurse over all child folders:
			foreach (var child in dtn.Children)
				OutputDirectoryTreeWix(child, indentLevel + 1);

			if (dtn.IsDirReference)
				_autoFiles.WriteLine(indentation + "</DirectoryRef>");
			else
				_autoFiles.WriteLine(indentation + "</Directory>");
		}

		/// <summary>
		/// Writes WIX source (to _autoFiles) for the given file.
		/// </summary>
		/// <param name="file">File to write WIX source for</param>
		/// <param name="indentation">Number of indentation tabs needed to line up with rest of WIX source</param>
		private void OutputFileWix(InstallerFile file, string indentation)
		{
			CheckFileAgainstOmissionList(file);

			// Replace build type with variable equivalent:
			var relativeSource = file.RelativeSourcePath;

			SynchWithFileLibrary(file);

			_autoFiles.Write(indentation + "	<Component Id=\"" + file.Id + "\" Guid=\"" + file.ComponentGuid + "\"");

			// Configure component to never overwrite an existing instance, if specified in _neverOverwriteList:
			_autoFiles.Write(_neverOverwriteList.Where(relativeSource.Contains).Count() > 0
							? " NeverOverwrite=\"yes\""
							: "");

			_autoFiles.Write(">");
			_autoFiles.WriteLine((file.Comment.Length > 0) ? " <!-- " + file.Comment + " -->" : "");

			// Add condition, if one applies:
			var matchingKeys = _fileSourceConditions.Keys.Where(key => relativeSource.ToLowerInvariant().Contains(key.ToLowerInvariant()));
			foreach (var matchingKey in matchingKeys)
			{
				string condition;
				if (_fileSourceConditions.TryGetValue(matchingKey, out condition))
					_autoFiles.WriteLine(indentation + "		<Condition>" + condition + "</Condition>");
			}

			_autoFiles.Write(indentation + "		<File Id=\"" + file.Id + "\"");

			// Fill in file details:
			_autoFiles.Write(" Name=\"" + file.Name + "\"");
			_autoFiles.Write(" Source=\"" + file.FullPath.Replace(_projRootPath, Path.Combine(BuildsFolderFragment, "$(var.Version)")) + "\"");

			// Add in a ReadOnly attribute, configured according to what's in the _makeWritableList:
			_autoFiles.Write(_makeWritableList.Where(relativeSource.Contains).Count() > 0
							? " ReadOnly=\"no\""
							: " ReadOnly=\"yes\"");

			_autoFiles.Write(" Checksum=\"yes\" KeyPath=\"yes\"");
			_autoFiles.Write(" DiskId=\"" + file.DiskId + "\"");

			if (IsDotNetAssembly(file.FullPath))
				_autoFiles.Write(" Assembly=\".net\" AssemblyApplication=\"" + file.Id + "\" AssemblyManifest=\"" + file.Id + "\"");

			_autoFiles.WriteLine(" />");

			// If file has to be forcibly overwritten, then add a RemoveFile element:
			if (_forceOverwriteList.Where(relativeSource.Contains).Count() > 0)
			{
				_autoFiles.Write(indentation + "		<RemoveFile Id=\"_" + file.Id + "\"");
				_autoFiles.WriteLine(" Name=\"" + file.Name + "\" On=\"install\"/>");
			}

			_autoFiles.WriteLine(indentation + "	</Component>");
			file.UsedInComponent = true;
		}

		/// <summary>
		/// Examines given file for possible matches in the _fileOmissions list. Reports
		/// anything suspicious in the _seriousIssues string.
		/// </summary>
		/// <param name="file">File to be examined</param>
		private void CheckFileAgainstOmissionList(InstallerFile file)
		{
			var fileNameLower = file.Name.ToLowerInvariant();
			var filePathLower = file.RelativeSourcePath.ToLowerInvariant();

			foreach (var omission in _fileOmissions)
			{
				var omissionRelPathLower = omission.RelativePath.ToLowerInvariant();

				if (!omissionRelPathLower.EndsWith(fileNameLower))
					continue;

				// Don't fuss over files that might match if they are already specified as a permitted matching pair in
				// a SuppressSimilarityWarning node of InstallerConfig.xml:
				var suppressWarning = false;
				foreach (var pair in _similarFilePairs)
				{
					var path1 = pair.Path1.ToLowerInvariant();
					var path2 = pair.Path2.ToLowerInvariant();
					var filePath = filePathLower;

					if (path1 == filePath && path2 == omissionRelPathLower)
					{
						suppressWarning = true;
						break;
					}
					if (path1 == omissionRelPathLower && path2 == filePath)
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
					_overallReport.AddSeriousIssue(file.RelativeSourcePath + " does not exist at expected place (" + fileFullPath + ").");
					break;
				}

				var omissionFullPath = MakeFullPath(omission.RelativePath);
				if (!File.Exists(omissionFullPath))
					continue;

				var omissionMd5 = CalcFileMd5(omissionFullPath);
				var fileCurrentMd5 = CalcFileMd5(fileFullPath);
				if (omissionMd5 == fileCurrentMd5)
				{
					_overallReport.AddSeriousIssue(file.RelativeSourcePath +
						" is included in the installer, but is identical to a file that was omitted [" +
						omission.RelativePath + "] because it was " + omission.Reason);
					break;
				}

				var omissionFileSize = (new FileInfo(omissionFullPath)).Length;
				var fileSize = (new FileInfo(fileFullPath)).Length;
				if (omissionFileSize == fileSize)
				{
					_overallReport.AddSeriousIssue(file.RelativeSourcePath +
						" is included in the installer, but is similar to a file that was omitted [" +
						omission.RelativePath + "] because it was " + omission.Reason);
				}
				break;
			}
		}

		/// <summary>
		/// Returns string containing given number of tab characters.
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
			foreach (var feature in _representedFeatures)
			{
				_autoFiles.WriteLine("		<FeatureRef Id=\"" + feature + "\">");
				foreach (var file in _allFilesFiltered)
				{
					if (file.Features.Contains(feature))
					{
						var output = "			<ComponentRef Id=\"" + file.Id + "\"/> <!-- " + file.RelativeSourcePath + " DiskId:" +
									 file.DiskId + " " + file.Comment + " -->";
						_autoFiles.WriteLine(output);
						file.UsedInFeatureRef = true;
					}
				}
				_autoFiles.WriteLine("		</FeatureRef>");
			}
		}

		/// <summary>
		/// Finishes off WIX source file and closes it.
		/// </summary>
		private void TerminateOutputFile()
		{
			_autoFiles.WriteLine("	</Fragment>");
			_autoFiles.WriteLine("</Wix>");
			_autoFiles.Close();
		}

		/// <summary>
		/// Looks up the given file in the File Library. If found, the file is updated with some extra
		/// details from the library. Otherwise, the library is updated with the file data, and new
		/// data for the library entry is also added to the file.
		/// </summary>
		/// <param name="file">file to be searched for in the library</param>
		private void SynchWithFileLibrary(InstallerFile file)
		{
			// Get file's relative source path:
			var libRelSourcePath = file.RelativeSourcePath;

			// Test if file already exists in FileLibrary.
			// If it does, then use the existing GUID.
			// Else create a new GUID etc. and add it to FileLibrary.
			var selectString = "//File[translate(@Path, \"ABCDEFGHIJKLMNOPQRSTUVWXYZ\", \"abcdefghijklmnopqrstuvwxyz\")=\"" +
							   libRelSourcePath.ToLowerInvariant() + "\"]"; // case-insensitive look-up
			var libSearch = _xmlFileLibrary.SelectSingleNode(selectString) as XmlElement;
			if (libSearch != null)
			{
				// File already exists in Library:
				file.ComponentGuid = libSearch.GetAttribute("ComponentGuid");
			}
			else // No XML node found
			{
				// This is an unknown file:
				file.ComponentGuid = Guid.NewGuid().ToString().ToUpperInvariant();

				// Add file to File Library Addenda:
				var newFileElement = _xmlNewFileLibraryAddenda.CreateElement("File");
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

				var firstAddendaFile = _newFileLibraryAddendaNode.SelectSingleNode("File[1]");
				_newFileLibraryAddendaNode.InsertBefore(newFileElement, firstAddendaFile);
				_newFileLibraryAddendaNode.InsertBefore(_xmlNewFileLibraryAddenda.CreateTextNode("\n"), firstAddendaFile);
				_newFileLibraryAddendaNode.InsertBefore(_xmlNewFileLibraryAddenda.CreateTextNode("\t"), firstAddendaFile);
			}
		}

		/// <summary>
		/// Compares New File Library Addenda with Previous File Library Addenda and reports
		/// new and deleted files.
		/// </summary>
		private void ReportFileChanges()
		{
			var newAddendaFiles = _newFileLibraryAddendaNode.SelectNodes("//File");
			if (newAddendaFiles == null)
				return;

			var previousFileLibraryAddendaNode = _xmlPreviousFileLibraryAddenda.SelectSingleNode("FileLibrary");
			if (previousFileLibraryAddendaNode == null)
			{
				foreach (XmlElement newFileNode in newAddendaFiles)
					_overallReport.AddNewFile(newFileNode.GetAttribute("Path"));
			}
			else
			{
				var previousAddendaFiles = previousFileLibraryAddendaNode.SelectNodes("//File");
				if (previousAddendaFiles != null)
				{
					// Iterate over files in the new file library addenda:
					foreach (XmlElement newFileNode in newAddendaFiles)
					{
						// See if current new file can be found in previous file library addenda
						var node = newFileNode;
						if (!previousAddendaFiles.Cast<XmlElement>().Any(f => f.GetAttribute("Path") == node.GetAttribute("Path")))
							_overallReport.AddNewFile(newFileNode.GetAttribute("Path"));
					}

					// Iterate over files in the previous file library addenda:
					foreach (XmlElement previousFileNode in previousAddendaFiles)
					{
						// See if current previous file can be found in new file library addenda
						var node = previousFileNode;
						if (!newAddendaFiles.Cast<XmlElement>().Any(f => f.GetAttribute("Path") == node.GetAttribute("Path")))
							_overallReport.AddDeletedFile(previousFileNode.GetAttribute("Path"));
					}
				}
			}
		}

		/// <summary>
		/// Tests if the given file is a .NET assembly. The code is adapted from the MSDN article
		/// "How to: Determine If a File Is an Assembly (C# and Visual Basic)"
		/// http://msdn.microsoft.com/en-us/library/ms173100(v=vs.100).aspx
		/// </summary>
		/// <param name="filePath">Full path to the file.</param>
		/// <returns>True if the given file is a .NET assembly.</returns>
		private static bool IsDotNetAssembly(string filePath)
		{
			try
			{
				var assembly = AssemblyName.GetAssemblyName(filePath);

				return true;
			}
			catch (FileNotFoundException)
			{
				// The file cannot be found:
				return false;
			}

			catch (BadImageFormatException)
			{
				// The file is not an assembly:
				return false;
			}

			catch (FileLoadException)
			{
				// The assembly has already been loaded:
				return true;
			}
		}

		/// <summary>
		/// Does some tests to make sure all files are accounted for.
		/// Throws exceptions if tests fail.
		/// </summary>
		private void DoSanityChecks()
		{
			const string indent = "    ";

			// List all files that were only used in unused features:
			var unusedFiles = from file in _allFilesFiltered
							  where file.OnlyUsedInUnusedFeatures && file.Features.Count > 0
							  select file.RelativeSourcePath + " [" + string.Join(", ", file.Features.ToArray()) + "]";

			if (unusedFiles.Count() > 0)
			{
				_overallReport.AddSeriousIssue(
					"The following files were left out because the features they belong to are not defined in Features.wxs: " +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, unusedFiles.ToArray()));
			}

			// Test that all files in _flexFeatureFiles were used:
			unusedFiles = from file in _flexFeatureFiles
						  where !file.UsedInComponent && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath + " [" + file.Comment + "]" + ": " + file.ReasonForRemoval;

			if (unusedFiles.Count() > 0)
			{
				_overallReport.AddSeriousIssue(
					"The following files were earmarked for FLEx only but got left out (possibly listed in the <Omissions> section of InstallerConfig.xml but referenced in a VS project): " +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, unusedFiles.ToArray()));
			}

			// Test that all files in _teFeatureFiles were used:
			unusedFiles = from file in _teFeatureFiles
						  where !file.UsedInComponent && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath + " [" + file.Comment + "]" + ": " + file.ReasonForRemoval;

			if (unusedFiles.Count() > 0)
			{
				_overallReport.AddSeriousIssue(
					"The following files were earmarked for TE only but got left out (possibly listed in the <Omissions> section of InstallerConfig.xml but referenced in a VS project): " +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, unusedFiles.ToArray()));
			}

			// Test that all files in _fwCoreFeatureFiles were used:
			unusedFiles = from file in _fwCoreFeatureFiles
						  where !file.UsedInComponent && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath + " [" + file.Comment + "]" + ": " + file.ReasonForRemoval;

			if (unusedFiles.Count() > 0)
			{
				_overallReport.AddSeriousIssue(
					"The following files were earmarked for FW_Core but got left out (possibly listed in the <Omissions> section of InstallerConfig.xml but referenced in a VS project): " +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, unusedFiles.ToArray()));
			}

			// Test that all files in _flexFeatureFiles were referenced:
			unusedFiles = from file in _flexFeatureFiles
						  where !file.UsedInFeatureRef && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath + " [" + file.Comment + "]" + ": " + file.ReasonForRemoval;

			if (unusedFiles.Count() > 0)
			{
				_overallReport.AddSeriousIssue(
					"The following files were earmarked for FLEx only but were not referenced in any features (possibly listed in the <Omissions> section of InstallerConfig.xml but referenced in a VS project): " +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, unusedFiles.ToArray()));
			}

			// Test that all files in _teFeatureFiles were referenced:
			unusedFiles = from file in _teFeatureFiles
						  where !file.UsedInFeatureRef && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath + " [" + file.Comment + "]" + ": " + file.ReasonForRemoval;

			if (unusedFiles.Count() > 0)
			{
				_overallReport.AddSeriousIssue(
					"The following files were earmarked for TE only but were not referenced in any features (possibly listed in the <Omissions> section of InstallerConfig.xml but referenced in a VS project): " +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, unusedFiles.ToArray()));
			}

			// Test that all files in _fwCoreFeatureFiles were referenced:
			unusedFiles = from file in _fwCoreFeatureFiles
						  where !file.UsedInFeatureRef && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath + " [" + file.Comment + "]" + ": " + file.ReasonForRemoval;

			if (unusedFiles.Count() > 0)
			{
				_overallReport.AddSeriousIssue(
					"The following files were earmarked for FW_Core but were not referenced in any features (possibly listed in the <Omissions> section of InstallerConfig.xml but referenced in a VS project): " +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, unusedFiles.ToArray()));
			}

			// Test that all files in _allFilesFiltered were used:
			unusedFiles = from file in _allFilesFiltered
						  where !file.UsedInComponent && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath + " [" + file.Comment + "]" + ": " + file.ReasonForRemoval;

			if (unusedFiles.Count() > 0)
			{
				_overallReport.AddSeriousIssue(
					"The following files were unused: they probably appear in the full build but are not used by either TE (dependency of TeExe target) or FLEx (dependency of LexTextExe target): " +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, unusedFiles.ToArray()));
			}

			// Test that all files in _allFilesFiltered were referenced exactly once:
			unusedFiles = from file in _allFilesFiltered
						  where !file.UsedInFeatureRef && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath + " [" + file.Comment + "]" + ": " + file.ReasonForRemoval;

			if (unusedFiles.Count() > 0)
			{
				_overallReport.AddSeriousIssue(
					"The following files were not referenced in any features (tested via UsedInFeatureRef flag): " +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, unusedFiles.ToArray()));
			}

			var overusedFiles = from file in _allFilesFiltered
								where file.Features.Count > 1
								select file.RelativeSourcePath + " [" + file.Comment + "]" + ": " + file.ReasonForRemoval;

			if (overusedFiles.Count() > 0)
			{
				_overallReport.AddSeriousIssue(
					"The following files were referenced by more than one feature: " +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, overusedFiles.ToArray()));
			}

			// Test Features.Count to verify that all files in _allFilesFiltered were referenced exactly once:
			unusedFiles = from file in _allFilesFiltered
						  where file.Features.Count == 0
						  select file.RelativeSourcePath + " [" + file.Comment + "]" + ": " + file.ReasonForRemoval;

			if (unusedFiles.Count() > 0)
			{
				_overallReport.AddSeriousIssue(
					"The following files were omitted because they were not referenced in any features (tested Features.Count==0):" +
					Environment.NewLine +
					"(This is typical of files in " + _outputReleaseFolderRelativePath + " that are not part of the FLEx build or TE build and not referenced in the CoreFileOrphans section of InstallerConfig.xml)" +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, unusedFiles.ToArray()));
			}

			// Warn about orphaned files that were included:
			if (_addOrphans && _orphanFiles.Count() > 0)
			{
				_overallReport.AddSeriousIssue(
					"The following " + _orphanFiles.Count() + " \"orphan\" files were added to FW_Core because the AddOrphans command line option was used and the files were found in Output\\Release, even though they were not referenced by any VS project or specified in InstallerConfig.xml:" +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, from file in _orphanFiles select file.RelativeSourcePath));
			}
		}
	}
}
