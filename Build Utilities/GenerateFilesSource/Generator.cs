using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Security.Cryptography;
using BuildUtilities;
using InstallerBuildUtilities;

namespace GenerateFilesSource
{
	internal sealed class Generator
	{
		private const string BuildsFolderFragment = "Builds"; // Folder to copy files for installer
		private const string BuildConfig = "Release"; // No point in allowing a debug-based installer
		// Controls set via command line:
		private readonly bool _needReport;
		private readonly bool _testIntegrity;
		private ReportSystem _overallReport;
		private InstallerConfigFile _installerConfigFile;
		// Important file/folder paths:
		private string _projRootPath;
		private string _exeFolder;
		// Folders where installable files are to be collected from:
		private string _distFilesFolderAbsolutePath;
		private string _outputReleaseFolderRelativePath;
		private string _outputReleaseFolderAbsolutePath;
		private string _installerFilesVersionedFolder;
		// Set of collected DistFiles, junk (Omissions) filtered out:
		private HashSet<InstallerFile> _distFilesFiltered;
		// Set of collected built files, junk (Omissions) filtered out:
		private HashSet<InstallerFile> _builtFilesFiltered;
		// Set of all collected installable files, junk (Omissions) filtered out:
		// Combines _distFilesFiltered with _builtFilesFiltered,
		// with duplicates removed. The duplicate may be from either set,
		// depending on how MergeFileSets handles the duplicates.
		private HashSet<InstallerFile> _allFilesFiltered = new HashSet<InstallerFile>();
		// Set of collected installable files from build target "allCsharpNoTests":
		private HashSet<InstallerFile> _allCsharpNoTestsFiles;
		// Set of collected installable files from build target "allCppNoTest":
		private HashSet<InstallerFile> _allCppNoTestFiles;
		// Set of files rejected as duplicates (in both Distfiles & build output):
		// This contains one or the other of the duplicates.
		private readonly HashSet<InstallerFile> _duplicateFiles = new HashSet<InstallerFile>();
		// Set of files for FlexMovies feature:
		private List<InstallerFile> _flexMoviesFeatureFiles;
		// Set of files for FW Core feature:
		private List<InstallerFile> _fwCoreFeatureFiles;
		// Set of features represented in the Features.wxs file:
		private readonly HashSet<string> _representedFeatures = new HashSet<string>();
		// File Library details:
		private XmlDocument _xmlFileLibrary;
		private XmlDocument _xmlPreviousFileLibraryAddenda;
		private XmlDocument _xmlNewFileLibraryAddenda;
		private XmlNode _newFileLibraryAddendaNode;
		// The output .wxs file details:
		private TextWriter _autoFilesWriter;

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
			foreach (var t in hashBytes)
			{
				sb.Append(t.ToString("X2"));
			}
			return sb.ToString();
		}

		private readonly List<DirectoryTreeNode> _redirectedFolders = new List<DirectoryTreeNode>();

		/// <summary>
		/// Class constructor
		/// </summary>
		/// <param name="report"></param>
		/// /// <param name="testIntegrity"></param>
		internal Generator(bool report, bool testIntegrity)
		{
			_needReport = report;
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
				var tester = new InstallerIntegrityTester(_installerConfigFile, BuildConfig, _overallReport);
				tester.Run();
			}

			if (!_overallReport.IsReportEmpty(_needReport))
			{
				if (_installerConfigFile.EmailingMachineNames.Any(name => name.ToLowerInvariant() == Environment.MachineName.ToLowerInvariant()))
				{
					// Email the report to the key people who need to know:
					var message = new System.Net.Mail.MailMessage();
					foreach (var recipient in _installerConfigFile.EmailList)
						message.To.Add(recipient);
					message.Subject = "Automatic Report from FW Installer Build";
					message.From = new System.Net.Mail.MailAddress("ken_zook@sil.org");
					message.Body = _overallReport.CombineReports(Tools.GetBuildDetails(_projRootPath), _needReport);
					var smtp = new System.Net.Mail.SmtpClient("mail.jaars.org");
					// smtp.Send(message);  GTIS disabled this capability so we need another solution.
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
			if (!Directory.Exists(_installerFilesVersionedFolder))
				return;

			var done = false;
			while (!done)
			{
				try
				{
					Directory.Delete(_installerFilesVersionedFolder, true);
				}
				catch (Exception e)
				{
					Console.WriteLine("crashed.");
				}
				done = true;
			}
		}

		/// <summary>
		/// Copies all DistFiles and Output\Release files to a subfolder named after the current
		/// FW build version. This is so we can keep the files for building a patch later.
		/// </summary>
		private void CopyFilesToVersionedFolder()
		{
			DirectoryCopy(_outputReleaseFolderAbsolutePath, Path.Combine(Path.Combine(_installerFilesVersionedFolder, InstallerConstants.Output), BuildConfig), true);
			DirectoryCopy(_distFilesFolderAbsolutePath, Path.Combine(_installerFilesVersionedFolder, InstallerConstants.DistFiles), true);
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

			if (!copySubDirs)
				return;

			foreach (var subdir in dirs)
			{
				var temppath = Path.Combine(destDirName, subdir.Name);
				DirectoryCopy(subdir.FullName, temppath, true);
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
			{
				_projRootPath = Path.GetDirectoryName(_exeFolder);
			}
			else
			{
				_projRootPath = _exeFolder;
			}

			// Read in the XML config file:
			_installerConfigFile = InstallerConfigFile.LoadInstallerConfigFile(Path.Combine(_projRootPath, "Installer"));

			// Define paths to folders we will be using:
			_distFilesFolderAbsolutePath = Path.Combine(_projRootPath, InstallerConstants.DistFiles);
			_outputReleaseFolderRelativePath = Path.Combine(InstallerConstants.Output, BuildConfig);
			_outputReleaseFolderAbsolutePath = Path.Combine(_projRootPath, _outputReleaseFolderRelativePath);
			var fwVersion = Tools.GetFwBuildVersion();
			_installerFilesVersionedFolder = Path.Combine(Path.Combine(_installerConfigFile.InstallerFolderAbsolutePath, BuildsFolderFragment), fwVersion);

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
		/// Sets up File Library, either from an XML file or from scratch if the file doesn't exist.
		/// Also sets up Previous File Library Addenda in the same way, and New File Library Addenda from scratch.
		/// </summary>
		private void InitFileLibrary()
		{
			_xmlFileLibrary = new XmlDocument();
			var libraryPath = LocalFileFullPath(InstallerConstants.FileLibraryFilename);
			if (File.Exists(libraryPath))
				_xmlFileLibrary.Load(libraryPath);
			else
				_xmlFileLibrary.LoadXml("<FileLibrary>\r\n</FileLibrary>");

			var libraryFileNodes = _xmlFileLibrary.SelectNodes("//File");
			if (libraryFileNodes != null && libraryFileNodes.Count > 0)
				_overallReport.AddReportLine("File Library contains " + libraryFileNodes.Count + " items.");

			// Set up File Library Addenda:
			_xmlPreviousFileLibraryAddenda = new XmlDocument();
			var addendaPath = LocalFileFullPath(InstallerConstants.AddendaFilename);
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

			foreach (var wxs in _installerConfigFile.WixFileSources)
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
						_installerConfigFile.FileOmissions.Add(sourcePath, "already included in WIX source " + xmlFilesPath);
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

			foreach (var file in _installerConfigFile.ExtraFiles)
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
			// 3) Collect files from the allCsharpNoTests target and all its dependencies;
			// 4) Collect files from the allCppNoTest target and all its dependencies;
			 Parallel.Invoke(
				GetAllFilesFiltered, // fills up _allFilesFiltered and _duplicateFiles sets
				GetAllCsharpNoTestsFiles, // fills up _allCsharpNoTestsFiles set
				GetAllCppNoTestFiles // fills up _allCppNoTestFiles set
			);

			Parallel.Invoke(
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
			foreach (var currentLanguage in _installerConfigFile.Languages)
			{
				var language = currentLanguage;
				currentLanguage.OtherFiles = (_allFilesFiltered.Where(file => FileIsForNonTeLocalization(file, language)));
			}
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
		/// Works out the set of files that are dependencies of the given MSBuild target.
		/// </summary>
		/// <param name="target">MSBuild target</param>
		/// <param name="description">Used in error messages, describes target</param>
		/// <returns>Set of dependencies of given target</returns>
		private HashSet<InstallerFile> GetSpecificTargetFiles(string target, string description)
		{
			var assemblyProcessor = new AssemblyDependencyProcessor(_projRootPath, _outputReleaseFolderAbsolutePath, description, _installerConfigFile.FileOmissions, _overallReport);
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
				string redirection = _installerConfigFile.FolderRedirections.Redirection(MakeRelativePath(folderPath));
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
			var instFile = new InstallerFile
			{
				Name = Path.GetFileName(file),
				Comment = comment,
				FullPath = file,
				RelativeSourcePath = MakeRelativePath(file),
				DirId = dirId
			};
			// Put in all the file data the WIX install build will need to know:
			instFile.Id = MakeId(instFile.Name, instFile.RelativeSourcePath);

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
			foreach (var t in hashBytes)
			{
				sb.Append(t.ToString("X2"));
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
				var omissionsMatches = _installerConfigFile.FileOmissions.Where(om => om.CaseSensitive ? relPath.Contains(om.RelativePath) : relPathLower.Contains(om.RelativePath.ToLowerInvariant())).ToList();
				if (!omissionsMatches.Any())
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
		private HashSet<InstallerFile> MergeFileSets(HashSet<InstallerFile> preferredSet, HashSet<InstallerFile> otherSet)
		{
			// Find sets of files with matching names:
			foreach (var currentFile in preferredSet.ToArray())
			{
				var cf = currentFile;
				var matchingFiles = from other in otherSet
									where other.FileNameMatches(cf)
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
		private static void ReplaceEquivalentFiles(ISet<InstallerFile> subSet, HashSet<InstallerFile> masterSet)
		{
			foreach (var file in subSet.ToArray())
			{
				var masterEquivalenceSet = masterSet.Where(masterFile => masterFile.Equals(file)).ToList();
				if (!masterEquivalenceSet.Any())
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
			// "usedDistFiles" consists of all DistFiles except those that were duplicated in Output\Release:
			var usedDistFiles = _allFilesFiltered.Intersect(_distFilesFiltered).ToList();

			_flexMoviesFeatureFiles = (from file in usedDistFiles
									   where FileIsForFlexMoviesOnly(file)
									   select file).ToList();

			var localizationFiles = from file in usedDistFiles
									where FileIsForLocalization(file)
									select file;

			var coreDistFiles = usedDistFiles.Except(_flexMoviesFeatureFiles).Except(localizationFiles).ToList();

			var allCppAndCsharpFiles = _allCsharpNoTestsFiles.Union(_allCppNoTestFiles);

			var coreBuiltFiles = allCppAndCsharpFiles.ToList();

			_fwCoreFeatureFiles = coreBuiltFiles.Union(coreDistFiles).ToList();

			// Assign feature names:
			foreach (var file in _flexMoviesFeatureFiles)
			{
				file.Features.Add("FlexMovies");
			}
			foreach (var file in _fwCoreFeatureFiles)
			{
				file.Features.Add("FW_Core");
			}
			foreach (var language in _installerConfigFile.Languages)
			{
				foreach (var file in language.OtherFiles)
					file.Features.Add(language.LanguageName);
			}

			foreach (var file in _allFilesFiltered.Where(file => file.Features.Count == 0))
			{
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
				file.DiskId = _installerConfigFile.FeatureCabinetMappings.ContainsKey(firstFeature)
					? _installerConfigFile.FeatureCabinetMappings[firstFeature].GetCabinet(file)
					: _installerConfigFile.FeatureCabinetMappings["Default"].GetCabinet(file);
			}
		}

		/// <summary>
		/// Determines heuristically whether a given installer file is for use in FLEx Movies only.
		/// Examines file name and relative path.
		/// </summary>
		/// <param name="file">the candidate installer file</param>
		/// <returns>true if the file is for FLEx Movies only</returns>
		private bool FileIsForFlexMoviesOnly(InstallerFile file)
		{
			return _installerConfigFile.FlexMovieFileHeuristics.IsFileIncluded(file.RelativeSourcePath);
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

			return _installerConfigFile.LocalizationHeuristics[language.Folder].IsFileIncluded(file.RelativeSourcePath);
		}

		/// <summary>
		/// Returns true if the given file is a localization resource file for any language.
		/// </summary>
		/// <param name="file">File to be analyzed</param>
		/// <returns>True if file is a localization file for any language</returns>
		private bool FileIsForLocalization(InstallerFile file)
		{
			return _installerConfigFile.Languages.Any(currentLanguage => FileIsForLocalization(file, currentLanguage));
		}

		/// <summary>
		/// Returns true if the given file is a FLEx localization resource file.
		/// </summary>
		/// <param name="file">File to be analyzed</param>
		/// <param name="language">Language to be considered</param>
		/// <returns>True if file is a FLEx localization file</returns>
		private bool FileIsForNonTeLocalization(InstallerFile file, LocalizationData language)
		{
			if (FileIsForLocalization(file, language))
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
			foreach (var source in new[] { _outputReleaseFolderRelativePath, InstallerConstants.DistFiles })
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
		/// Writes out the tree of folders and files to the _autoFilesWriter WIX source file.
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
			if (File.Exists(InstallerConstants.AddendaFilename))
				File.Delete(InstallerConstants.AddendaFilename);

			if (_newFileLibraryAddendaNode.ChildNodes.Count > 0)
			{
				// Save NewFileLibraryAddenda:
				_xmlNewFileLibraryAddenda.Save(InstallerConstants.AddendaFilename);
			}

			ReportFileChanges();
		}

		/// <summary>
		/// Opens the output WIX source file and writes headers.
		/// </summary>
		private void InitOutputFile()
		{
			// Open the output file:
			string autoFiles = LocalFileFullPath(InstallerConstants.AutoFilesFileName);
			_autoFilesWriter = new StreamWriter(autoFiles);
			_autoFilesWriter.WriteLine("<?xml version=\"1.0\"?>");
			_autoFilesWriter.WriteLine("<?define Version = $(Fw.Version) ?>");
			_autoFilesWriter.WriteLine("<Wix xmlns=\"http://schemas.microsoft.com/wix/2006/wi\">");
			_autoFilesWriter.WriteLine("	<Fragment Id=\"AutoFilesFragment\">");
			_autoFilesWriter.WriteLine("		<Property  Id=\"AutoFilesFragment\" Value=\"1\"/>");
		}

		/// <summary>
		/// Writes WIX source (to _autoFilesWriter) for the given directory tree (including files).
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
			{
				_autoFilesWriter.WriteLine(indentation + "<DirectoryRef Id=\"" + dtn.Name + "\">");
			}
			else
			{
				var nameClause = "Name=\"" + dtn.Name + "\"";
				_autoFilesWriter.WriteLine(indentation + "<Directory Id=\"" + dtn.DirId + "\" " + nameClause + ">");
			}

			// Iterate over all local files:
			InstallerFile[] localFiles = dtn.LocalFiles.Where(file => file.Features.Count != 0 && !file.OnlyUsedInUnusedFeatures).ToArray();
			var sortedLocalFiles = new SortedDictionary<string, InstallerFile>();
			foreach (var file in localFiles)
			{
				sortedLocalFiles.Add(file.Name, file);
			}
			foreach (var file in sortedLocalFiles.Values)
			{
				OutputFileWix(file, indentation);
			}

			// Recurse over all child folders:
			foreach (var child in dtn.Children)
			{
				OutputDirectoryTreeWix(child, indentLevel + 1);
			}

			if (dtn.IsDirReference)
			{
				_autoFilesWriter.WriteLine(indentation + "</DirectoryRef>");
			}
			else
			{
				_autoFilesWriter.WriteLine(indentation + "</Directory>");
			}
		}

		/// <summary>
		/// Writes WIX source (to _autoFilesWriter) for the given file.
		/// </summary>
		/// <param name="file">File to write WIX source for</param>
		/// <param name="indentation">Number of indentation tabs needed to line up with rest of WIX source</param>
		private void OutputFileWix(InstallerFile file, string indentation)
		{
			CheckFileAgainstOmissionList(file);

			// Replace build type with variable equivalent:
			var relativeSource = file.RelativeSourcePath;

			SynchWithFileLibrary(file);

			_autoFilesWriter.Write(indentation + "	<Component Id=\"" + file.Id + "\" Guid=\"" + file.ComponentGuid + "\"");

			_autoFilesWriter.Write(">");
			_autoFilesWriter.WriteLine((file.Comment.Length > 0) ? " <!-- " + file.Comment + " -->" : "");

			// Add condition, if one applies:
			var matchingKeys = _installerConfigFile.FileSourceConditions.Keys.Where(key => relativeSource.ToLowerInvariant().Contains(key.ToLowerInvariant()));
			foreach (var matchingKey in matchingKeys)
			{
				string condition;
				if (_installerConfigFile.FileSourceConditions.TryGetValue(matchingKey, out condition))
					_autoFilesWriter.WriteLine(indentation + "		<Condition>" + condition + "</Condition>");
			}

			_autoFilesWriter.Write(indentation + "		<File Id=\"" + file.Id + "\"");

			// Fill in file details:
			_autoFilesWriter.Write(" Name=\"" + file.Name + "\"");
			_autoFilesWriter.Write(" Source=\"" + file.FullPath.Replace(_projRootPath, Path.Combine(BuildsFolderFragment, "$(var.Version)")) + "\"");

			// Add in a ReadOnly attribute, configured according to what's in the _makeWritableList:
			_autoFilesWriter.Write(_installerConfigFile.MakeWritableList.Where(relativeSource.Contains).Any()
							? " ReadOnly=\"no\""
							: " ReadOnly=\"yes\"");

			_autoFilesWriter.Write(" Checksum=\"yes\" KeyPath=\"yes\"");
			_autoFilesWriter.Write(" DiskId=\"" + file.DiskId + "\"");

			if (IsDotNetAssembly(file.FullPath))
				_autoFilesWriter.Write(" Assembly=\".net\" AssemblyApplication=\"" + file.Id + "\" AssemblyManifest=\"" + file.Id + "\"");

			_autoFilesWriter.WriteLine(" />");

			// If file has to be forcibly overwritten, then add a RemoveFile element:
			if (_installerConfigFile.ForceOverwriteList.Where(relativeSource.Contains).Any())
			{
				_autoFilesWriter.Write(indentation + "		<RemoveFile Id=\"_" + file.Id + "\"");
				_autoFilesWriter.WriteLine(" Name=\"" + file.Name + "\" On=\"install\"/>");
			}

			_autoFilesWriter.WriteLine(indentation + "	</Component>");
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

			foreach (var omission in _installerConfigFile.FileOmissions)
			{
				var omissionRelPathLower = omission.RelativePath.ToLowerInvariant();

				if (!omissionRelPathLower.EndsWith(fileNameLower))
					continue;

				// Don't fuss over files that might match if they are already specified as a permitted matching pair in
				// a SuppressSimilarityWarning node of InstallerConfig.xml:
				var suppressWarning = false;
				foreach (var pair in _installerConfigFile.SimilarFilePairs)
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
				_autoFilesWriter.WriteLine("		<FeatureRef Id=\"" + feature + "\">");
				foreach (var file in _allFilesFiltered)
				{
					if (file.Features.Contains(feature))
					{
						var output = "			<ComponentRef Id=\"" + file.Id + "\"/> <!-- " + file.RelativeSourcePath + " DiskId:" +
									 file.DiskId + " " + file.Comment + " -->";
						_autoFilesWriter.WriteLine(output);
						file.UsedInFeatureRef = true;
					}
				}
				_autoFilesWriter.WriteLine("		</FeatureRef>");
			}
		}

		/// <summary>
		/// Finishes off WIX source file and closes it.
		/// </summary>
		private void TerminateOutputFile()
		{
			_autoFilesWriter.WriteLine("	</Fragment>");
			_autoFilesWriter.WriteLine("</Wix>");
			_autoFilesWriter.Close();
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
				if (previousAddendaFiles == null)
					return;

				// Iterate over files in the new file library addenda:
				foreach (XmlElement newFileNode in newAddendaFiles)
				{
					// See if current new file can be found in previous file library addenda
					var node = newFileNode;
					if (previousAddendaFiles.Cast<XmlElement>().All(f => f.GetAttribute("Path") != node.GetAttribute("Path")))
						_overallReport.AddNewFile(newFileNode.GetAttribute("Path"));
				}

				// Iterate over files in the previous file library addenda:
				foreach (XmlElement previousFileNode in previousAddendaFiles)
				{
					// See if current previous file can be found in new file library addenda
					var node = previousFileNode;
					if (newAddendaFiles.Cast<XmlElement>().All(f => f.GetAttribute("Path") != node.GetAttribute("Path")))
						_overallReport.AddDeletedFile(previousFileNode.GetAttribute("Path"));
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
				AssemblyName.GetAssemblyName(filePath);

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
			var unusedFiles = (from file in _allFilesFiltered
							  where file.OnlyUsedInUnusedFeatures && file.Features.Count > 0
							  select file.RelativeSourcePath + " [" + string.Join(", ", file.Features.ToArray()) + "]").ToList();

			if (unusedFiles.Any())
			{
				_overallReport.AddSeriousIssue(
					"The following files were left out because the features they belong to are not defined in Features.wxs: " +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, unusedFiles.ToArray()));
			}

			// Test that all files in _fwCoreFeatureFiles were used:
			unusedFiles = (from file in _fwCoreFeatureFiles
						  where !file.UsedInComponent && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath + " [" + file.Comment + "]" + ": " + file.ReasonForRemoval).ToList();

			if (unusedFiles.Any())
			{
				_overallReport.AddSeriousIssue(
					"The following files were earmarked for FW_Core but got left out (possibly listed in the <Omissions> section of InstallerConfig.xml but referenced in a VS project): " +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, unusedFiles.ToArray()));
			}

			// Test that all files in _fwCoreFeatureFiles were referenced:
			unusedFiles = (from file in _fwCoreFeatureFiles
						  where !file.UsedInFeatureRef && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath + " [" + file.Comment + "]" + ": " + file.ReasonForRemoval).ToList();

			if (unusedFiles.Any())
			{
				_overallReport.AddSeriousIssue(
					"The following files were earmarked for FW_Core but were not referenced in any features (possibly listed in the <Omissions> section of InstallerConfig.xml but referenced in a VS project): " +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, unusedFiles.ToArray()));
			}

			// Test that all files in _allFilesFiltered were used:
			unusedFiles = (from file in _allFilesFiltered
						  where !file.UsedInComponent && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath + " [" + file.Comment + "]" + ": " + file.ReasonForRemoval).ToList();

			if (unusedFiles.Any())
			{
				_overallReport.AddSeriousIssue(
					"The following files were unused: they probably appear in the full build but are not used by either TE (dependency of TeExe target) or FLEx (dependency of LexTextExe target): " +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, unusedFiles.ToArray()));
			}

			// Test that all files in _allFilesFiltered were referenced exactly once:
			unusedFiles = (from file in _allFilesFiltered
						  where !file.UsedInFeatureRef && !file.OnlyUsedInUnusedFeatures
						  select file.RelativeSourcePath + " [" + file.Comment + "]" + ": " + file.ReasonForRemoval).ToList();

			if (unusedFiles.Any())
			{
				_overallReport.AddSeriousIssue(
					"The following files were not referenced in any features (tested via UsedInFeatureRef flag): " +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, unusedFiles.ToArray()));
			}

			var overusedFiles = (from file in _allFilesFiltered
								where file.Features.Count > 1
								select file.RelativeSourcePath + " [" + file.Comment + "]" + ": " + file.ReasonForRemoval).ToList();

			if (overusedFiles.Any())
			{
				_overallReport.AddSeriousIssue(
					"The following files were referenced by more than one feature: " +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, overusedFiles.ToArray()));
			}

			// Test Features.Count to verify that all files in _allFilesFiltered were referenced exactly once:
			unusedFiles = (from file in _allFilesFiltered
						  where file.Features.Count == 0
						  select file.RelativeSourcePath + " [" + file.Comment + "]" + ": " + file.ReasonForRemoval).ToList();

			if (unusedFiles.Any())
			{
				_overallReport.AddSeriousIssue(
					"The following files were omitted because they were not referenced in any features (tested Features.Count==0):" +
					Environment.NewLine +
					"(This is typical of files in " + _outputReleaseFolderRelativePath + " that are not part of the FLEx build and not referenced in the CoreFileOrphans section of InstallerConfig.xml)" +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, unusedFiles.ToArray()));
			}

/*
			// Warn about orphaned files that were included:
			if (_addOrphans && _orphanFiles.Any())
			{
				_overallReport.AddSeriousIssue(
					"The following " + _orphanFiles.Count() + " \"orphan\" files were added to FW_Core because the AddOrphans command line option was used and the files were found in Output\\Release, even though they were not referenced by any VS project or specified in InstallerConfig.xml:" +
					Environment.NewLine + indent + string.Join(Environment.NewLine + indent, from file in _orphanFiles select file.RelativeSourcePath));
			}
*/
		}
	}
}
