using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;

namespace GenerateFilesSource
{
	/// <summary>
	/// Class to examine Visual Studio project files and figure out the dependent project assemblies
	/// and referenced assemblies of a given project.
	/// </summary>
	internal sealed class AssemblyDependencyProcessor
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

		private readonly List<TargetsFileData> _targetsFiles = new List<TargetsFileData>();
		private readonly bool _foundFieldWorksTargetsFile;

		internal AssemblyDependencyProcessor(string projRootPath, string assemblyFolderPath, string description, FileOmissionList fileOmissions, ReportSystem report)
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

		internal void Init()
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
		internal Dictionary<string, string> GetAssemblySet(string vsProj)
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
						if (target == "Unit++")
							return;
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

			if (!foundTargetButItWasLinux)
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
			return (omissionsMatches.Any());
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

			if (!String.IsNullOrEmpty(addition))
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
}