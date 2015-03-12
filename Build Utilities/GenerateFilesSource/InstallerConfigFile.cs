using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using BuildUtilities;
using InstallerBuildUtilities;

namespace GenerateFilesSource
{
	internal class InstallerConfigFile
	{
		internal readonly List<LocalizationData> Languages = new List<LocalizationData>();
		internal readonly FileHeuristics FlexMovieFileHeuristics = new FileHeuristics();
		internal readonly Dictionary<string, FileHeuristics> LocalizationHeuristics = new Dictionary<string, FileHeuristics>();
		// List of partial paths of files which are installed only if respective conditions are met:
		internal readonly Dictionary<string, string> FileSourceConditions = new Dictionary<string, string>();
		// List of URLs of files which have to be fetched from the internet:
		internal readonly Dictionary<string, string> ExtraFiles = new Dictionary<string, string>();
		// List of file name fragments whose files should be omitted:
		internal readonly FileOmissionList FileOmissions = new FileOmissionList();
		// List of pairs of files whose similarity warnings are to be suppressed:
		internal readonly List<FilePair> SimilarFilePairs = new List<FilePair>();
		internal readonly RedirectionList FolderRedirections = new RedirectionList();
		// List of (partial) paths of files that must not be set to read-only:
		internal readonly List<string> MakeWritableList = new List<string>();
		// List of (partial) paths of files that have to have older versions forcibly removed prior to installing:
		internal readonly List<string> ForceOverwriteList = new List<string>();
		// List of machines which will email people if something goes wrong:
		internal readonly List<string> EmailingMachineNames = new List<string>();
		// List of people to email if something goes wrong:
		internal readonly List<string> EmailList = new List<string>();
		// List of people to email regarding new and deleted files:
		internal readonly List<string> FileNotificationEmailList = new List<string>();
		// List of WIX source files where installable files are manually defined:
		internal readonly List<string> WixFileSources = new List<string>();
		internal string InstallerFolderAbsolutePath;
		// List of mappings of Features to DiskId, so we can assign files to cabinets based on features:
		internal readonly Dictionary<string, CabinetMappingData> FeatureCabinetMappings = new Dictionary<string, CabinetMappingData>();
		// List of file patterns of files that may legitimately exist in DistFiles
		// without being checked into source control:
		internal readonly List<string> NonVersionedDistFiles = new List<string>();
		// List of file patterns of files that may legitimately have a version number of 0.0.0.0:
		internal readonly List<string> VersionZeroFiles = new List<string>();

		internal static InstallerConfigFile LoadInstallerConfigFile(string installerFolderAbsolutePath)
		{
			var retVal = new InstallerConfigFile
			{
				InstallerFolderAbsolutePath = installerFolderAbsolutePath
			};
			retVal.LoadConfigurationFile();
			return retVal;
		}

		private void LoadConfigurationFile()
		{
			var configurationDocument = XDocument.Load(InstallerConstants.InstallerConfigFileName);
			var root = configurationDocument.Root;

			// Load localization languages:
			// Format: <Language Name="*language name*" Id="*folder in output\release where localized DLLs get built*"/>
			LoadLocalizationLanguages(root); // NB: This must be done before the call to LoadFeatureAllocationData.

			LoadIntegrityChecks(root);

			LoadFeatureAllocationData(root);

			// Define conditions to apply to specified components:
			// Format: <File Path="*partial path of a file that is conditionally installed*" Condition="*MSI Installer condition that must be true to install file*"/>
			// Beware! This XML is double-interpreted, so for example a 'less than' sign must be represented as &amp;lt;
			LoadFileConditions(root);

			// Define files needed in the installer that are not built locally or part of source control:
			// Format: <File URL="*URL of an extra file*" Destination="*Local relative path to download file to*"/>
			LoadExtraFiles(root);

			// Define list of file patterns to be filtered out. Any file whose path contains (anywhere) one of these strings will be filtered out:
			// Format: <File PathPattern="*partial path of any file that is not needed in the FW installation*"/>
			LoadOmmisions(root);

			// Define list of folders that will be redirected to some other folder on the end-user's machine:
			// Format: <Redirect Folder="*folder whose contents will be installed to a different folder*" InstallDir="*MSI Installer folder variable where the affected files will be installed*"/>
			LoadFolderRedirections(root);

			// Define list of (partial) paths of files that must not be set to read-only:
			// Format: <File PathPattern="*partial path of any file that must not have the read-only flag set*"/>
			LoadWriteableFiles(root);

			// Define list of (partial) paths of files that have to have older versions forcibly removed prior to installing:
			// Format: <File PathPattern="*partial path of any file that must be installed on top of a pre-existing version, even if that means downgrading*"/>
			LoadForceOverwrites(root);

			// Define list of machines to email people if something goes wrong:
			// Format: <EmailingMachine Name="*name of machine (within current domain) which is required to email people if there is a problem*"/>
			LoadFailureNotifications(root);

			// Load "FilesSources" element.
			LoadFilesSources(root);

			// Define file cabinet index allocations:
			LoadCabinetAssignments(root);
		}

		private void LoadLocalizationLanguages(XElement root)
		{
			var languagesElement = root.Element("Languages");
			if (languagesElement == null || !languagesElement.HasElements)
			{
				Languages.Clear();
				return;
			}
			Languages.AddRange(languagesElement.Elements("Language").Select(language => new LocalizationData(language.Attribute("Name").Value, language.Attribute("Id").Value)));
		}

		private void LoadIntegrityChecks(XElement root)
		{
			var integrityChecks = root.Element("IntegrityChecks");
			if (integrityChecks == null || !integrityChecks.HasElements)
			{
				NonVersionedDistFiles.Clear();
				VersionZeroFiles.Clear();
				return;
			}

			// Define list of (partial) paths of files that are OK in DistFiles folder without being in source control:
			// Format: <IgnoreNonVersionedDistFiles PathPattern="*partial path of any file may exist in DistFiles without being checked into version control*"/>
			foreach (var file in integrityChecks.Elements("IgnoreNonVersionedDistFiles"))
			{
				NonVersionedDistFiles.Add(file.Attribute("PathPattern").Value);
			}

			// Define list of (partial) paths of files that are allowed to have a version number of 0.0.0.0:
			// Format: <IgnoreVersionZeroFiles PathPattern="*partial path of any file allowed to have a version number of 0.0.0.0*"/>
			foreach (var file in integrityChecks.Elements("IgnoreVersionZeroFiles"))
			{
				VersionZeroFiles.Add(file.Attribute("PathPattern").Value);
			}
		}

		private void LoadFeatureAllocationData(XElement root)
		{
			var featureAllocationElement = root.Element("FeatureAllocation");
			if (featureAllocationElement == null || !featureAllocationElement.HasElements)
			{
				return;
			}

			ConfigureHeuristics(featureAllocationElement.Element("FlexMoviesOnly"), FlexMovieFileHeuristics);

			// Do the same for localization files.
			// Prerequisite: _languages already configured with all languages:
			ConfigureLocalizationHeuristics(featureAllocationElement.Element("Localization"));
		}

		private void LoadFileConditions(XElement root)
		{
			var fileConditionsElement = root.Element("FileConditions");
			if (fileConditionsElement == null || !fileConditionsElement.HasElements)
			{
				FileSourceConditions.Clear();
				return;
			}
			foreach (var condition in fileConditionsElement.Elements("File"))
			{
				FileSourceConditions.Add(condition.Attribute("Path").Value, condition.Attribute("Condition").Value);
			}
		}

		private void LoadExtraFiles(XElement root)
		{
			var extraFilesElement = root.Element("ExtraFiles");
			if (extraFilesElement == null || !extraFilesElement.HasElements)
			{
				ExtraFiles.Clear();
				return;
			}
			foreach (var extraFile in extraFilesElement.Elements("File"))
			{
				ExtraFiles.Add(extraFile.Attribute("URL").Value, extraFile.Attribute("Destination").Value);
			}
		}

		private void LoadOmmisions(XElement root)
		{
			var ommisionsElement = root.Element("Omissions");
			if (ommisionsElement == null || !ommisionsElement.HasElements)
			{
				FileOmissions.Clear();
				SimilarFilePairs.Clear();
				return;
			}

			// Define list of file patterns to be filtered out. Any file whose path contains (anywhere) one of these strings will be filtered out:
			// Format: <File PathPattern="*partial path of any file that is not needed in the FW installation*"/>
			foreach (var file in ommisionsElement.Elements("File"))
			{
				var caseSensitiveAttr = file.Attribute("CaseSensitive");
				FileOmissions.Add(file.Attribute("PathPattern").Value, caseSensitiveAttr != null && bool.Parse(caseSensitiveAttr.Value));
			}

			// Define pairs of file paths known (and allowed) to be similar. This suppresses warnings about omitted files that look like other included files:
			// Format: <SuppressSimilarityWarning Path1="*path of first file of matching pair*" Path2="*path of second file of matching pair*"/>
			foreach (var warning in ommisionsElement.Elements("SuppressSimilarityWarning"))
			{
				SimilarFilePairs.Add(new FilePair(warning.Attribute("Path1").Value, warning.Attribute("Path2").Value));
			}
		}

		private void LoadFolderRedirections(XElement root)
		{
			var folderRedirectionsElement = root.Element("FolderRedirections");
			if (folderRedirectionsElement == null || !folderRedirectionsElement.HasElements)
			{
				FolderRedirections.Clear();
				return;
			}
			foreach (var redirectedFile in folderRedirectionsElement.Elements("Redirect"))
			{
				FolderRedirections.Add(redirectedFile.Attribute("Folder").Value, redirectedFile.Attribute("InstallerDir").Value);
			}
		}

		private void LoadWriteableFiles(XElement root)
		{
			var writeableFilesElement = root.Element("WritableFiles");
			if (writeableFilesElement == null || !writeableFilesElement.HasElements)
			{
				MakeWritableList.Clear();
				return;
			}
			foreach (var writeableFile in writeableFilesElement.Elements("File"))
			{
				MakeWritableList.Add(writeableFile.Attribute("PathPattern").Value);
			}
		}

		private void LoadForceOverwrites(XElement root)
		{
			var forceOverwriteElement = root.Element("ForceOverwrite");
			if (forceOverwriteElement == null || !forceOverwriteElement.HasElements)
			{
				ForceOverwriteList.Clear();
				return;
			}
			foreach (var overwriteableFile in forceOverwriteElement.Elements("File"))
			{
				ForceOverwriteList.Add(overwriteableFile.Attribute("PathPattern").Value);
			}
		}

		private void LoadFailureNotifications(XElement root)
		{
			var failureNotificationsElement = root.Element("FailureNotification");
			if (failureNotificationsElement == null || !failureNotificationsElement.HasElements)
			{
				EmailingMachineNames.Clear();
				EmailList.Clear();
				FileNotificationEmailList.Clear();
				return;
			}
			foreach (var emailMachineName in failureNotificationsElement.Elements("EmailingMachine"))
			{
				EmailingMachineNames.Add(emailMachineName.Attribute("Name").Value);
			}
			foreach (var emailRecipient in failureNotificationsElement.Elements("Recipient"))
			{
				var address = Tools.ElucidateEmailAddress(emailRecipient.Attribute("Email").Value);
				EmailList.Add(address);
				var notifyAttr = emailRecipient.Attribute("NotifyFileChanges");
				if (notifyAttr != null && bool.Parse(notifyAttr.Value))
					FileNotificationEmailList.Add(address);
			}
		}

		private void LoadFilesSources(XElement root)
		{
			var fileSourcesElement = root.Element("FilesSources");
			if (fileSourcesElement == null || !fileSourcesElement.HasElements)
			{
				WixFileSources.Clear();
				return;
			}
			// Define list of WIX files that list files explicitly:
			// Format: <WixSource File="*name of WIX source file in Installer folder that contains <File> definitions inside <Component> definitions*"/>
			foreach (var wixSource in fileSourcesElement.Elements("WixSource"))
			{
				WixFileSources.Add(wixSource.Attribute("File").Value);
			}

			// Define list of files that list merge modules to be included. (The files in any merge modules will be taken into account.)
			// Format: <MergeModules File="*name of WIX source file in Installer folder that contains <Merge> definitions*"/>
			// Merge modules defined in such files must have WIX source file names formed by substituting the .msm extension with .mm.wxs
			foreach (var mergeModuleSource in fileSourcesElement.Elements("MergeModules"))
			{
				var mmDoc = XDocument.Load(mergeModuleSource.Attribute("File").Value);
				var nsm = new XmlNamespaceManager(new NameTable());
				nsm.AddNamespace("wix", "http://schemas.microsoft.com/wix/2006/wi");
				foreach (var mergeElement in mmDoc.Descendants(XName.Get("Merge", "http://schemas.microsoft.com/wix/2006/wi")))
				{
					var msmFilePath = mergeElement.Attribute("SourceFile").Value;
					var mergeModuleFile = Path.Combine(InstallerFolderAbsolutePath, msmFilePath);
					var mergeModuleWixFile = Path.Combine(Path.GetDirectoryName(mergeModuleFile), Path.GetFileNameWithoutExtension(mergeModuleFile)) + ".mm.wxs";
					if (File.Exists(mergeModuleWixFile))
					{
						WixFileSources.Add(mergeModuleWixFile);
					}
					else
					{
						// If the merge module source file is not in the Installer folder, it is probably in a subfolder
						// of the same name:
						var mergeModuleFolderPath = Path.Combine(InstallerFolderAbsolutePath, Path.GetFileNameWithoutExtension(msmFilePath));
						mergeModuleFile = Path.Combine(mergeModuleFolderPath, msmFilePath);
						mergeModuleWixFile =
							Path.Combine(Path.GetDirectoryName(mergeModuleFile), Path.GetFileNameWithoutExtension(mergeModuleFile)) +
							".mm.wxs";
						if (File.Exists(mergeModuleWixFile))
						{
							WixFileSources.Add(mergeModuleWixFile);
						}
					}
				}
			}
		}

		private void LoadCabinetAssignments(XElement root)
		{
			var cabinetAssignmentsElement = root.Element("CabinetAssignments");
			if (cabinetAssignmentsElement == null || !cabinetAssignmentsElement.HasElements)
			{
				FeatureCabinetMappings.Clear();
				return;
			}
			string cabinetIndex;
			string cabinetIndexes;
			string cabinetDivisions;
			var defaultAssignment = cabinetAssignmentsElement.Element("Default");
			if (defaultAssignment != null)
			{
				GetCabinetAttributeValues(defaultAssignment, out cabinetIndex, out cabinetIndexes, out cabinetDivisions);
				FeatureCabinetMappings.Add("Default",
					new CabinetMappingData(cabinetIndex, cabinetIndexes, cabinetDivisions));
			}
			foreach (var assignment in cabinetAssignmentsElement.Elements("Cabinet"))
			{
				GetCabinetAttributeValues(assignment, out cabinetIndex, out cabinetIndexes, out cabinetDivisions);
				FeatureCabinetMappings.Add(assignment.Attribute("Feature").Value,
					new CabinetMappingData(cabinetIndex, cabinetIndexes, cabinetDivisions));
			}
		}

		private static void GetCabinetAttributeValues(XElement cabinetElement, out string cabinetIndex,
			out string cabinetIndexes, out string cabinetDivisions)
		{
			cabinetIndex = cabinetElement.Attribute("CabinetIndex") == null ? "" : cabinetElement.Attribute("CabinetIndex").Value;
			cabinetIndexes = cabinetElement.Attribute("CabinetIndexes") == null ? "" : cabinetElement.Attribute("CabinetIndexes").Value;
			cabinetDivisions = cabinetElement.Attribute("CabinetDivisions") == null ? "" : cabinetElement.Attribute("CabinetDivisions").Value;
		}

		/// <summary>
		/// Configures a "dictionary" of heuristics sets. Each entry in the dictionary is referenced by a
		/// language code, and is a set of heuristics for distinguishing files that belong to that language's
		/// localization pack. The heuristics are defined in the InstallerConfig.xml document, but in a
		/// language-independent way, such that the language code is represented by "{0}", so that the
		/// string.Format method can be used to substitute in the correct language code.
		/// </summary>
		private void ConfigureLocalizationHeuristics(XElement parent)
		{
			// Make one heuristics set per language:
			foreach (var language in Languages)
			{
				var heuristics = new FileHeuristics();
				// Use the standard configuration method to read in the heuristics:
				ConfigureHeuristics(parent, heuristics);

				// Swap out the "{0}" occurrences and replace with the language code:
				FormatLocalizationHeuristics(heuristics.Inclusions.PathContains, language.Folder);
				FormatLocalizationHeuristics(heuristics.Inclusions.PathEnds, language.Folder);
				FormatLocalizationHeuristics(heuristics.Exclusions.PathContains, language.Folder);
				FormatLocalizationHeuristics(heuristics.Exclusions.PathEnds, language.Folder);

				// Add the new heuristics set to the dictionary:
				LocalizationHeuristics.Add(language.Folder, heuristics);
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
		/// <param name="parent">The InstallerConfig.xml file</param>
		/// <param name="heuristics">The initialized heuristics set</param>
		private void ConfigureHeuristics(XElement parent, FileHeuristics heuristics)
		{
			if (parent == null || !parent.HasElements)
				return;

			// Configure the Inclusions subset from the "File" sub-element:
			ConfigureHeuristicSet(parent.Elements("File").ToList(), heuristics.Inclusions);
			// Configure the Exclusions subset from the "Except" sub-element:
			ConfigureHeuristicSet(parent.Elements("Except").ToList(), heuristics.Exclusions);
		}

		/// <summary>
		/// Configures a heuristics subset (Either the Inclusions or the Exclusions) from the specified
		/// section of the InstallerConfig.xml file.
		/// </summary>
		/// <param name="selectChildren">child nodes to work with. There may not be any</param>
		/// <param name="heuristicSet">The heuristics subset</param>
		private void ConfigureHeuristicSet(IList<XElement> selectChildren, HeuristicSet heuristicSet)
		{
			if (!selectChildren.Any())
				return;

			// Process each heuristic definition:
			foreach (var heuristic in selectChildren)
			{
				// The definition should contain either a "PathContains" attribute or
				// a "PathEnds" attribute. Anything else is ignored:
				var attr = heuristic.Attribute("PathContains");
				if (attr != null && attr.Value.Length > 0)
				{
					heuristicSet.PathContains.Add(attr.Value);
				}
				attr = heuristic.Attribute("PathEnds");
				if (attr != null && attr.Value.Length > 0)
				{
					heuristicSet.PathEnds.Add(attr.Value);
				}
			}
		}
	}
}