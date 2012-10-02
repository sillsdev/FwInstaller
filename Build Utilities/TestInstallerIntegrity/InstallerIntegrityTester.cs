﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace TestInstallerIntegrity
{
	// This utility tests the installer files for various problems, as described by the following summary
	// of error and warning messages:

	// Error Messages:
	// 1: File XXXX has been modified since the last release, but its version remains at YYYY. Patching will fail.
	// 2: File XXXX has a date/time stamp (D1) that is earlier than a previously released version (D2). Patching may fail.
	// 3: Library contains file XXXX with no FeatureList attribute.
	// 4: File XXXX has been added to the following features since the last release... Patching will fail.
	// 5: File XXXX has been removed from the following features since the last release... Patching will fail.
	// 6: File XXXX had a version of YYYY in the last release. The version has since been lowered to ZZZZ. Patching will fail.
	// 7: File XXXX has invalid version number...
	// 8: File XXXX has a version number (YYYY) that has only changed in the 4th segment since the last release (ZZZZ). The 4th version segment is ignored by the installer. Patching will fail.
	// 9: File XXXX had a version of YYYY in the last release. The version information has since been removed. Patching will fail.

	// Warning messages:
	// 1: There is no FileLibrary. [Deprecated.]
	// 2: The following files are present in DistFiles but not checked into Perforce...
	// 3: The following files are checked into Perforce but not present in DistFiles...
	// 4: File XXXX has a version number of 0.0.0.0.
	// 5: Could not determine if DistFiles folder is consistent with Perforce: XXXX

	class InstallerIntegrityTester
	{
		// Controls set via command line:
		private readonly string m_buildType;
		private readonly bool m_silent;

		private const string LogFileName = "TestInstallerIntegrity.log";
		private string m_errorLog;
		private string m_exeFolder;
		private string m_projRootPath;
		private XmlNodeList m_fileNodes;
		private XmlNodeList m_regNodes;
		private class WixSource
		{
			public readonly XmlDocument XmlDoc;
			public readonly XmlNamespaceManager XmlnsMan;
			public WixSource(string fileName)
			{
				XmlDoc = new XmlDocument();
				XmlDoc.Load(fileName);
				XmlnsMan = new XmlNamespaceManager(XmlDoc.NameTable);
				XmlnsMan.AddNamespace("wix", "http://schemas.microsoft.com/wix/2006/wi");
			}
		}
		private List<WixSource> m_wixFilesSources;

		// List of file patterns of files that may legitimately exist in DistFiles
		// without being checked into Perforce:
		private readonly List<string> m_nonP4DistFiles = new List<string>();
		// List of file patterns of files that may legitimately have a version number of 0.0.0.0:
		private readonly List<string> m_zeroVersionedFiles = new List<string>();
		// List of file name fragments whose files should be omitted:
		private readonly List<string> m_fileOmissions = new List<string>();

		// List of machines which will email people if something goes wrong:
		private readonly List<string> m_emailingMachineNames = new List<string>();
		// List of people to email if something goes wrong:
		private readonly List<string> m_emailList = new List<string>();

		public InstallerIntegrityTester(bool silent, string buildType)
		{
			m_silent = silent;
			m_buildType = buildType;
		}

		internal void Run()
		{
			Init();

			TestFileLibrary();
			TestRegLibrary();
			TestUnversionedDistFiles();

			OutputLog();
		}

		private void Init()
		{
			if (File.Exists(LogFileName))
				File.Delete(LogFileName);

			m_errorLog = "";

			// Get FW root path:
			var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
			if (exePath == null)
				throw new Exception("So sorry, don't know where we are!");
			m_exeFolder = Path.GetDirectoryName(exePath);
			if (m_exeFolder == null)
				throw new Exception("So sorry, don't know where we are!");

			// Get development project root path:
			m_projRootPath = m_exeFolder.ToLowerInvariant().EndsWith("installer") ? Path.GetDirectoryName(m_exeFolder) : m_exeFolder;

			ConfigureFromXml();

			// Load File Library:
			if (File.Exists("FileLibrary.xml"))
			{
				var xmlFileLibrary = new XmlDocument();
				xmlFileLibrary.Load("FileLibrary.xml");
				m_fileNodes = xmlFileLibrary.SelectNodes("FileLibrary/File");
			}

			// Load Registry Library:
			if (File.Exists("RegLibrary.xml"))
			{
				var xmlRegLibrary = new XmlDocument();
				xmlRegLibrary.Load("RegLibrary.xml");
				m_regNodes = xmlRegLibrary.SelectNodes("RegLibrary/Component");
			}

			// Load all .wxs files containging files definitions into a List for iterative processing:
			m_wixFilesSources = new List<WixSource> {new WixSource("Files.wxs"), new WixSource("ProcessedAutoFiles.wxs")};
			if (File.Exists("PatchCorrections.wxs"))
				m_wixFilesSources.Add(new WixSource("PatchCorrections.wxs"));
		}

		/// <summary>
		/// Reads configuration data from InstallerConfig.xml
		/// </summary>
		private void ConfigureFromXml()
		{
			var configuration = new XmlDocument();
			configuration.Load("InstallerConfig.xml");

			// Define list of (partial) paths of files that are OK in DistFiles folder without being in Perforce:
			// Format: <IgnoreNonP4DistFiles PathPattern="*partial path of any file may exist in DistFiles without being checked into Perforce*"/>
			var ignoreNonP4DistFiles = configuration.SelectNodes("//IntegrityChecks/IgnoreNonP4DistFiles");
			if (ignoreNonP4DistFiles != null)
				foreach (XmlElement file in ignoreNonP4DistFiles)
					m_nonP4DistFiles.Add(file.GetAttribute("PathPattern"));

			// Define list of (partial) paths of files that are allowed to have a version number of 0.0.0.0:
			// Format: <IgnoreZeroVersionedDistFiles PathPattern="*partial path of any file may exist in DistFiles without being checked into Perforce*"/>
			var ignoreZeroVersionedDFiles = configuration.SelectNodes("//IntegrityChecks/IgnoreZeroVersionedFiles");
			if (ignoreZeroVersionedDFiles != null)
				foreach (XmlElement file in ignoreZeroVersionedDFiles)
					m_zeroVersionedFiles.Add(file.GetAttribute("PathPattern"));

			// Define list of file patterns to be filtered out. Any file whose path contains (anywhere) one of these strings will be filtered out:
			// Format: <File PathPattern="*partial path of any file that is not needed in the FW installation*"/>
			var omittedFiles = configuration.SelectNodes("//Omissions/File");
			if (omittedFiles != null)
				foreach (XmlElement file in omittedFiles)
					m_fileOmissions.Add(file.GetAttribute("PathPattern"));

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
						m_emailList.Add(recipient.GetAttribute("Email"));
			}

		}

		private void TestFileLibrary()
		{
			if (m_fileNodes == null)
			{
				// Report("WARNING #1: There is no FileLibrary.");
				// We no longer bother to report this, as it is a normal occurrence when no prior releases exist.
				return;
			}
			foreach (XmlElement file in m_fileNodes)
			{
				TestLibraryFileStillPresent(file);
				TestLibraryFileFeatureMembership(file);
				TestLibraryFileDetails(file);
			}
		}

		/// <summary>
		/// Tests whether the given file's size, date/time, version and MD5 hash are
		/// consistent with details in the library, so that a patch will succeed.
		/// For example, if the file's date/time is more recent than that stored in
		/// the Library, yet the file version has not changed, that is not allowed.
		/// </summary>
		/// <param name="file">Element from FileLibrary.xml</param>
		private void TestLibraryFileDetails(XmlElement file)
		{
			var filePath = file.GetAttribute("Path");
			var fullFilePath = Path.Combine(m_projRootPath, filePath.Replace("\\${config}\\", "\\" + m_buildType + "\\"));

			if (!File.Exists(fullFilePath))
				return; // This should have been dealt with in Patchcorrections.wxs by now.

			var libDate = file.GetAttribute("Date");
			var libVersion = file.GetAttribute("Version");
			//var libSize = file.GetAttribute("Size");
			var libMd5 = file.GetAttribute("MD5");

			var fileVersionInfo = FileVersionInfo.GetVersionInfo(fullFilePath);
			var fileVersion = "";
			if (fileVersionInfo.FileVersion != null)
			{
				fileVersion = fileVersionInfo.FileMajorPart + "." + fileVersionInfo.FileMinorPart + "." +
							  fileVersionInfo.FileBuildPart + "." + fileVersionInfo.FilePrivatePart;
			}
			var fi = new FileInfo(fullFilePath);

			//var realSize = fi.Length;
			var realDateTime = fi.LastWriteTime.ToShortDateString() + " " + fi.LastWriteTime.ToShortTimeString();
			var realVersion = fileVersion;
			var realMd5 = CalcFileMd5(fullFilePath);

			// If the files are not identical, the versions must also be different (if present):
			if (realMd5 != libMd5 && libVersion != "" && realVersion != "")
			{
				if (realVersion == libVersion)
					Report("ERROR #1: File " + filePath + " has been modified since the last release, but its version remains at " + realVersion + ". Patching will fail.");
				else
				{
					// Although the version may have changed, the installer ignores the last segment (private part)
					// so we need to warn the user if that is the only part that changed:
					var realVersionTrunc = fileVersionInfo.FileMajorPart + "." + fileVersionInfo.FileMinorPart + "." +
										   fileVersionInfo.FileBuildPart;
					var libVersionTrunc = "";
					var libVsplit = libVersion.Split('.');
					for (int i = 0; i < Math.Min(3, libVsplit.Length); i++)
					{
						if (i > 0)
							libVersionTrunc += ".";
						libVersionTrunc += libVsplit[i];
					}

					if (realVersionTrunc == libVersionTrunc)
						Report("ERROR #8: File " + filePath + " has a version number (" + realVersion + ") that has only changed in the 4th segment since the last release (" + libVersion + "). The 4th version segment is ignored by the installer. Patching will fail.");
				}
			}

			// The date of the file must not precede that in the library (we will allow up to 24 hours' precedence):
			var libDateTime = DateTime.Parse(libDate);
			var fileDateTime = DateTime.Parse(realDateTime);
			var timeBetweenVersions = fileDateTime.Subtract(libDateTime);
			if (timeBetweenVersions.TotalHours < -24)
				Report("ERROR #2: File " + filePath + " has a date/time stamp (" + realDateTime + ") that is earlier than a previously released version (" + libDate + "). Patching may fail.");

			if (realVersion == "0.0.0.0" && !m_zeroVersionedFiles.Any(path => fullFilePath.ToLowerInvariant().Contains(path.Replace("\\${config}\\", "\\" + m_buildType + "\\").ToLowerInvariant())))
				Report("WARNING #4: File " + filePath + " has a version number of 0.0.0.0. That is very silly, and I don't like it. You'll only regret it later.");

			// The version number must not be lower in the latest verison than it was in the previous one:
			if (libVersion.Length > 0 && realVersion.Length == 0)
			{
				Report("ERROR #9: File " + filePath + " had a version of " + libVersion +
					   " in the last release. The version information has since been removed. Patching will fail.");
			}
			else
			{
				try
				{
					if (ParseVersion(realVersion) < ParseVersion(libVersion))
					{
						Report("ERROR #6: File " + filePath + " had a version of " + libVersion +
							   " in the last release. The version has since been lowered to " + realVersion + ". Patching will fail.");
					}
				}
				catch (Exception e)
				{
					Report("ERROR #7: File " + filePath + " has invalid version number (possibly in FileLibrary.xml): " + e.Message);
				}
			}
		}

		/// <summary>
		/// Returns a 64-bit version number from a string of the format "X.Y.Z.Q"
		/// </summary>
		/// <param name="version">Version number</param>
		/// <returns>Version number</returns>
		private static UInt64 ParseVersion(string version)
		{
			if (version == "")
				return 0;

			UInt64 version64 = 0;
			var versionArray = version.Split('.');

			for (int i = versionArray.Length - 1; i >= 0; i--)
			{
				var currentElement = versionArray[i];
				var currentElementNumeric = UInt64.Parse(currentElement);
				if (currentElementNumeric > 65535)
					throw new Exception("Segment index " + i + " of version " + version + " is more than 65535.");
				version64 = version64 >> 16;
				version64 |= ((currentElementNumeric) << 48);
			}
			return version64;
		}

		/// <summary>
		/// Tests if the given library file still exists. If not, composes a WIX snippet
		/// to enable an update installer/patch to remove it.
		/// </summary>
		/// <param name="file">Element from FileLibrary.xml</param>
		private void TestLibraryFileStillPresent(XmlElement file)
		{
			var guid = file.GetAttribute("ComponentGuid");

			if (!FoundComponent(guid))
			{
				// The file library contains a component GUID that is not in any of our expected source files.
				// Get path of file:
				var filePath = file.GetAttribute("Path");

				// See if there is another file of the same name that is destined for the same folder.
				// (This could happen if the file used to be taken from Output\Release but is now taken
				// from DistFiles, for example.)
				var newFileSource = FindSameFileElsewhere(file);

				// Report missing file:
				Report("<!-- File component " + guid + " [" + filePath + "] is missing from (ProcessedAuto)Files.wxs -->");
				if (newFileSource != null)
					Report("<!-- However, same file is now sourced from " + newFileSource + ". -->");

				// Get Directory ID of file:
				var dirId = file.GetAttribute("DirectoryId");
				if (dirId != "")
				{
					Report("<!-- Suggested PatchCorrections.wxs snippet: -->");
					// Create WIX snippet to remedy the problem:
					Report("<DirectoryRef Id=\"" + dirId + "\">");
					var compId = file.GetAttribute("ComponentId");
					if (compId == "")
						compId = "[unknown]";

					// By reinstating the component with the transitive attribute, its condition will
					// always be re-evaluated:
					Report("	<Component Id=\"" + compId + "\" Transitive=\"yes\" Guid=\"" + guid + "\">");
					// By setting the condition to false, we tell the installer we don't need this component:
					Report("		<Condition>FALSE</Condition>");
					Report("		<CreateFolder/>"); // Junk needed to pass ICE18
					Report("	</Component>");

					string newId = null;
					if (newFileSource == null)
					{
						// Unfortunately, we also need a new component to proactively delete the existing file
						// from the end-user's machine:
						var newGuid = Guid.NewGuid().ToString().ToUpperInvariant();
						var longName = file.GetAttribute("LongName");
						var shortName = file.GetAttribute("ShortName");
						var name = shortName;
						if (longName != shortName)
							name = longName;
						newId = MakeId("Del" + name, compId);
						Report("	<Component Id=\"" + newId + "\" Guid=\"" + newGuid + "\">");

						var nameSection = " Name=\"" + shortName + "\"";
						if (longName != shortName)
							nameSection += " LongName=\"" + longName + "\"";
						Report("		<RemoveFile Id=\"" + newId + "\"" + nameSection + " On=\"install\"/>");
						Report("		<CreateFolder/>"); // Junk needed to pass ICE18
						Report("	</Component>");
					}
					Report("</DirectoryRef>");
					var featureList = file.GetAttribute("FeatureList");
					if (featureList != "")
					{
						var features = featureList.Split(new[] {','});
						foreach (var feature in features)
						{
							Report("<FeatureRef Id=\"" + feature + "\">");
							Report("	<ComponentRef Id=\"" + compId + "\"/>");
							if (newFileSource == null)
								Report("	<ComponentRef Id=\"" + newId + "\"/>");
							Report("</FeatureRef>");
						}
					}
					else
						Report("<!-- WARNING: No features specified for above component(s) -->");
					m_errorLog += Environment.NewLine;
				}
				else
					Report("<!-- WARNING: Could not locate DirectoryId -->");
			}
		}

		/// <summary>
		/// Tests if the given file still belongs to the same feature(s) as listed in the File Library.
		/// </summary>
		/// <param name="file">A File node in the File Library</param>
		private void TestLibraryFileFeatureMembership(XmlElement file)
		{
			var filePath = file.GetAttribute("Path");

			// Get file's features as listed in the Library:
			var libFeatureList = file.GetAttribute("FeatureList");
			if (libFeatureList == "")
			{
				Report("ERROR #3: Library contains file " + filePath + " with no FeatureList attribute.");
				return;
			}
			var libFeatures = libFeatureList.Split(new[] {','});
			var libFeatureSet = new HashSet<string>();
			foreach (var feature in libFeatures)
				libFeatureSet.Add(feature);

			// Get details of the file's parent component:
			var guid = file.GetAttribute("ComponentGuid");
			if (guid == "")
				return; // This case is dealt with in TestLibraryFileStillPresent()

			XmlElement component = null;
			foreach (var wixSource in m_wixFilesSources)
			{
				component =
					wixSource.XmlDoc.SelectSingleNode("//wix:Component[@Guid='" + guid + "']", wixSource.XmlnsMan) as XmlElement;
				if (component != null)
					break;
			}
			if (component == null)
				return; // This case is dealt with in TestLibraryFileStillPresent()

			var compId = component.GetAttribute("Id");

			// Get set of features from WIX sources that currently contain the file's parent component:
			var wixFeatureSet = new HashSet<string>();
			foreach (XmlElement featureRef in
				m_wixFilesSources.Select(
					wixSource =>
					wixSource.XmlDoc.SelectNodes("//wix:FeatureRef[wix:ComponentRef[@Id=\"" + compId + "\"]]", wixSource.XmlnsMan)).
					SelectMany(featureRefs => featureRefs.Cast<XmlElement>()))
			{
				wixFeatureSet.Add(featureRef.GetAttribute("Id"));
			}
			var newFeatureMemberships = wixFeatureSet.Except(libFeatureSet);
			if (newFeatureMemberships.Count() > 0)
			{
				Report("ERROR #4: File " + filePath +
					   " has been added to the following features since the last release: " +
					   string.Join(", ", newFeatureMemberships.ToArray()) + ". Patching will fail.");
			}
			var obsoleteFeatureMemberships = libFeatureSet.Except(wixFeatureSet);
			if (obsoleteFeatureMemberships.Count() > 0)
			{
				Report("ERROR #5: File " + filePath +
					   " has been removed from the following features since the last release: " +
					   string.Join(", ", obsoleteFeatureMemberships.ToArray()) + ". Patching will fail.");
			}
		}

		private void TestRegLibrary()
		{
			if (m_regNodes == null)
			{
				//Report("<!-- There is no RegLibrary -->");
				return;
			}
			foreach (XmlElement reg in m_regNodes)
			{
				var guid = reg.GetAttribute("ComponentGuid");

				if (!FoundComponent(guid))
				{
					// The registry library contains a component GUID that is not in any of our expected source files.
					// Get registry details:
					var regRoot = reg.GetAttribute("Root");
					var regKey = reg.GetAttribute("KeyHeader");
					// Report missing file:
					Report("<!-- Registry component " + guid + " [" + regRoot + "\\" + regKey + "] is missing from (ProcessedAuto)Files.wxs -->");
					// Get Directory ID of registry data:
					var dirId = reg.GetAttribute("DirectoryId");
					if (dirId != "")
					{
						Report("<!-- Suggested PatchCorrections.wxs snippet: -->");
						// Create WIX snippet to remedy the problem:
						Report("<DirectoryRef Id=\"" + dirId + "\">");
						var compId = reg.GetAttribute("Id");
						if (compId == "")
							compId = "[unknown]";

						// By reinstating the component with the transitive attribute, its condition will
						// always be re-evaluated:
						Report("	<Component Id=\"" + compId + "\" Transitive=\"yes\" Guid=\"" + guid + "\">");
						// By setting the condition to false, we tell the installer we don"t need this component:
						Report("		<Condition>FALSE</Condition>");
						Report("		<CreateFolder/>"); // Junk needed to pass ICE18
						Report("	</Component>");

						// Unfortunately, we also need a new component to proactively delete the existing registry data
						// from the end-user's machine:
						var newGuid = Guid.NewGuid().ToString().ToUpperInvariant();
						var newId = MakeId("Del" + compId, compId);
						Report("	<Component Id=\"" + newId + "\" Guid=\"" + newGuid + "\">");
						Report("		<Registry Root=\"" + regRoot + "\" Key=\"" + regKey + "\" Action=\"removeKeyOnInstall\" Id=\"" + newId + "\"/>");
						Report("		<CreateFolder/>"); // Junk needed to pass ICE18
						Report("	</Component>");

						Report("</DirectoryRef>");
						var featureList = reg.GetAttribute("FeatureList");
						if (featureList != "")
						{
							var features = featureList.Split(new[] {','} );
							foreach (var feature in features)
							{
								Report("<FeatureRef Id=\"" + feature + "\">");
								Report("	<ComponentRef Id=\"" + compId + "\"/>");
								Report("	<ComponentRef Id=\"" + newId + "\"/>");
								Report("</FeatureRef>");
							}
						}
						else
							Report("<!-- WARNING: No features specified for above component(s) -->");
						Report("");
					}
					else
						Report("<!-- WARNING: Could not locate DirectoryId -->");
				}
			}
		}

		/// <summary>
		/// Compares files collected under DistFiles with files in Perforce under DistFiles.
		/// </summary>
		private void TestUnversionedDistFiles()
		{
			const string p4ListFilePath = "P4DistFilesList.txt";
			const string p4ListFileErrs = "P4DistFilesErrs.txt";
			const string dosListFilePath = "DosDistFiles.txt";
			const string p4LineHeader = "... clientfile ";

			// Collect from Perforce a list of files it thinks are in DistFiles (synced to the client):
			RunDosCmd("p4 fstat -T clientFile -Rh ../DistFiles/... >" + p4ListFilePath + " 2>" + p4ListFileErrs);

			var p4ListFileErrors = ConvertTextFileToLowerCaseStringList(p4ListFileErrs);
			if (p4ListFileErrors.Count > 0)
			{
				Report(p4ListFileErrors.Aggregate("Warning #5: Could not determine if DistFiles folder is consistent with Perforce:" + Environment.NewLine, (current, error) => current + error));
				return;
			}
			var p4DistFiles = ConvertTextFileToLowerCaseStringList(p4ListFilePath);
			for (var i = 0; i < p4DistFiles.Count; i++)
			{
				if (p4DistFiles[i].StartsWith(p4LineHeader))
					p4DistFiles[i] = p4DistFiles[i].Substring(p4LineHeader.Length);

				// The following substitution was found necessary on the build machine,
				// but not on Alistair's machine, and shows that Perforce output can be
				// inconsistent on different machines:
				p4DistFiles[i] = p4DistFiles[i].Replace('/', '\\');
			}

			// Collect from DOS a list of files it thinks are in DistFiles:
			RunDosCmd("dir /S /B /A-D ..\\DistFiles >" + dosListFilePath);
			var dosDistFiles = ConvertTextFileToLowerCaseStringList(dosListFilePath);

			// Create list of files in DistFiles folder that are not checked into Perforce's DistFiles:
			var distFilesNotInP4 = dosDistFiles.Except(p4DistFiles);

			// Filter out files that are allowed to exist in DistFiles without being in Perforce:
			// (specified in InstallerConfig.xml in the IntegrityChecks element)
			distFilesNotInP4 = (from file in distFilesNotInP4
								where !m_nonP4DistFiles.Any(f => file.ToLowerInvariant().Contains(f.ToLowerInvariant()))
								select file).ToList();

			// Filter out files that were specifically to be omitted from the installer:
			distFilesNotInP4 = (from file in distFilesNotInP4
							where m_fileOmissions.All(f => !file.ToLowerInvariant().Contains(f.Replace("\\${config}\\", "\\" + m_buildType + "\\").ToLowerInvariant()))
							select file).ToList();

			if (distFilesNotInP4.Count() > 0)
			{
				Report("WARNING #2: The following files are present in DistFiles but not checked into Perforce: " + Environment.NewLine + "    " +
					   string.Join(Environment.NewLine + "    ", distFilesNotInP4.ToArray()));
			}

			var filesNotInDos = p4DistFiles.Except(dosDistFiles);
			if (filesNotInDos.Count() > 0)
			{
				Report("WARNING #3: The following files are checked into Perforce but not present in DistFiles: " + Environment.NewLine + "    " +
					   string.Join(Environment.NewLine + "    ", filesNotInDos.ToArray()));
			}
		}

		/// <summary>
		/// Creates a list of strings from a text file, one string per line.
		/// Omits blank lines. Converts strings to lower case.
		/// </summary>
		/// <param name="listFilePath">Path to text file</param>
		/// <returns>List of strings</returns>
		private static List<string> ConvertTextFileToLowerCaseStringList(string listFilePath)
		{
			string line;
			var list = new List<string>();
			if (File.Exists(listFilePath))
			{
				var listFile = new StreamReader(listFilePath);

				// Put the file contents into a List structure:
				while ((line = listFile.ReadLine()) != null)
				{
					if (line.Length == 0) continue;
					list.Add(line.ToLowerInvariant());
				}
				listFile.Close();
				File.Delete(listFilePath);
			}
			return list;
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
				var nantProc = Process.Start("cmd", cmd);
				if (nantProc != null) nantProc.WaitForExit();
			}
			catch (Exception)
			{
				throw new Exception("Error while running this DOS command: " + cmd);
			}
		}

		/// <summary>
		/// Writes the error log to file, or emails it to key people.
		/// </summary>
		private void OutputLog()
		{
			if (m_errorLog.Length > 0)
			{
				// Prepend log with build-specific details:
				m_errorLog = GetBuildDetails() + m_errorLog;

				// Save the report to LogFileName:
				var reportFile = new StreamWriter(LogFileName);
				reportFile.WriteLine(m_errorLog);
				reportFile.Close();

				if (m_emailingMachineNames.Any(name => name.ToLowerInvariant() == Environment.MachineName.ToLowerInvariant()))
				{
					// Email the report to the key people who need to know:
					var message = new System.Net.Mail.MailMessage();
					foreach (var recipient in m_emailList)
						message.To.Add(recipient);
					message.Subject = "Automatic Installer Integrity Report from FW Installer Build";
					message.From = new System.Net.Mail.MailAddress("alistair_imrie@sil.org");
					message.Body = m_errorLog;
					var smtp = new System.Net.Mail.SmtpClient("mail.jaars.org");
					smtp.Send(message);
				}
				else
				{
					// Open the report for the user to see:
					if (!m_silent)
						Process.Start(LogFileName);
				}
			}
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
		/// Returns an Id suitable for the installer, based on the given name and unique data.
		/// The Id will be unique if the name and unique data combination is unique.
		/// Identifiers may contain ASCII characters A-Z, a-z, digits, underscores (_), or periods (.).
		/// Every identifier must begin with either a letter or an underscore.
		/// Invalid characters are filtered out of the name (spaces, etc.)
		/// The unique data is turned into an MD5 hash and appended to the mame.
		/// Space is limited to 72 chars, so if the name is more than 40 characters, it is truncated
		/// before appending the 32-character MD5 hash.
		/// </summary>
		/// <param name="name"></param>
		/// <param name="uniqueData"></param>
		/// <returns></returns>
		private static string MakeId(string name, string uniqueData)
		{
			const int maxLen = 72;
			const string validChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.";
			const string validStartChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_";

			var candidate = name;

			for (var iChar = 0; iChar < candidate.Length; iChar++)
				if (validChars.IndexOf(candidate[iChar]) == -1)
					candidate = candidate.Substring(0, iChar) + "_" + candidate.Substring(iChar + 1);

			if (validStartChars.IndexOf(candidate.Substring(0, 1)) == -1)
			// Can't start with a number:
				candidate = "_" + candidate;

			var hash = CalcMD5(uniqueData);
			var hashLen = hash.Length;
			var maxMainSectionLen = maxLen - hashLen - 1;

			if (candidate.Length > maxMainSectionLen)
				candidate = candidate.Substring(0, maxMainSectionLen);

			return candidate + "." + hash;
		}

		/// <summary>
		/// Calculates MD5 hash of given string.
		/// </summary>
		/// <param name="msg">String whose MD5 hash is required.</param>
		/// <returns>string MD5 hash</returns>
		public static string CalcMD5(string msg)
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
		/// Searches all Component nodes in List of WIX files to see if any match the specified GUID.
		/// </summary>
		/// <param name="guid">Component GUID to be searched for</param>
		/// <returns>true if the component was found</returns>
		private bool FoundComponent(string guid)
		{
			return m_wixFilesSources.Any(wixSource => wixSource.XmlDoc.SelectSingleNode("//wix:Component[@Guid='" + guid + "']", wixSource.XmlnsMan) != null);
		}

		/// <summary>
		/// Returns new source folder if the given file has essentially a duplciate, a file sourced from
		/// some other folder but which ends up in the same folder on the end-user's machine.
		/// (This could happen if the file used to be taken from Output\Release but is now taken from
		/// DistFiles, for example.)
		/// </summary>
		/// <param name="libraryFileNode">Element in the FileLibrary.</param>
		/// <returns>Path to duplicate file, or null if there is none.</returns>
		private string FindSameFileElsewhere(XmlElement libraryFileNode)
		{
			// We will get the node's file name, find all matches for just the name in
			// WIX file sources, and see if any are in the same directory as recorded in
			// the node's DirectoryId attribute:
			var fileName = libraryFileNode.GetAttribute("LongName");
			var directoryId = libraryFileNode.GetAttribute("DirectoryId");

			foreach (var wixSource in m_wixFilesSources)
			{
				// Collect all File nodes in this file that have the right file name:
				var matchingNodes = wixSource.XmlDoc.SelectNodes("//wix:File[@LongName='" + fileName + "' or @Name='" + fileName + "']", wixSource.XmlnsMan);
				if (matchingNodes == null) continue;

				// Iterate through all matches, looking for those in the right directory:
				foreach (XmlElement match in matchingNodes)
				{
					// The directory (or directory reference) node is the grandparent of the file node:
					var matchDirNode = match.SelectSingleNode("../..") as XmlElement;
					if (matchDirNode == null) continue;

					var matchDirId = matchDirNode.GetAttribute("Id");
					if (matchDirId == directoryId)
						return MakeRelativePath(match.GetAttribute("Source"));
				} // Next matching file node
			} // next WIX files source

			return null;
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
		/// Adds a line of text to the overall error log.
		/// </summary>
		/// <param name="msg">Text to add.</param>
		private void Report(string msg)
		{
			m_errorLog += msg + Environment.NewLine;
		}
	}
}