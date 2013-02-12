using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Xml;

namespace GenerateFilesSource
{
	internal interface IBuildSystemParser
	{
		string GetOutputAssemblyPath(string folderPrefix);
		List<string> GetReferencedAssemblies(string folderPrefix);
		string GetSourceName();
		string GetSourcePath();
	}

	class VisualStudioProjectParser : IBuildSystemParser
	{
		private const string MsbuildUri = "http://schemas.microsoft.com/developer/msbuild/2003";
		private readonly string _projectPath;
		private XmlDocument _project;
		private XmlNamespaceManager _xmlnsManager;
		private string _assemblyName;
		private string _outputType;
		private readonly string _projectName;

		/// <summary>
		/// Given an MSBuild Target node and associated XmlNamespaceManager, returns a list of VS project parser
		/// objects, one for each VS project specified in the Target. (There is unlikely to be more than one in any instance.)
		/// </summary>
		/// <param name="targetNode">MSBuild Target node</param>
		/// <param name="xmlnsManager">XML Namespace Manager needed to parse targetNode</param>
		/// <param name="projRootPath"></param>
		/// <returns>List of VS project parsers if any specified in the target, or else null</returns>
		public static List<IBuildSystemParser> GetProjectParsers(XmlElement targetNode, XmlNamespaceManager xmlnsManager, string projRootPath)
		{
			var parsers = new List<IBuildSystemParser>();

			var msBuildNodes = targetNode.SelectNodes("msbuild:MSBuild", xmlnsManager);
			if (msBuildNodes == null)
				return parsers;

			foreach (XmlElement msBuildNode in msBuildNodes)
			{
				var projectPath = msBuildNode.GetAttribute("Projects").Replace("$(fwrt)", projRootPath);
				parsers.Add(GetProjectParser(projectPath));
			}

			return parsers;
		}

		public static IBuildSystemParser GetProjectParser(string projectPath)
		{
			var parser = new VisualStudioProjectParser(projectPath);

			parser.Init();

			return parser;
		}

		VisualStudioProjectParser(string projectPath)
		{
			_projectPath = projectPath;
			_projectName = Path.GetFileNameWithoutExtension(projectPath);
		}

		public void Init()
		{
			if (!File.Exists(_projectPath))
				throw new FileNotFoundException("Visual Studio project file " + _projectPath + " does not exist.");

			_project = new XmlDocument();
			_project.Load(_projectPath);

			_xmlnsManager = new XmlNamespaceManager(_project.NameTable);
			_xmlnsManager.AddNamespace("msbuild", MsbuildUri);

			var assemblyNameNode = _project.SelectSingleNode("//msbuild:Project/msbuild:PropertyGroup/msbuild:AssemblyName", _xmlnsManager);
			if (assemblyNameNode == null)
				throw new DataException("VS project " + _projectPath + " does not specify an AssemblyName.");

			_assemblyName = assemblyNameNode.InnerText;

			var outputTypeNode = _project.SelectSingleNode("//msbuild:Project/msbuild:PropertyGroup/msbuild:OutputType", _xmlnsManager);
			if (outputTypeNode == null)
				throw new DataException("VS project " + _projectPath + " does not specify an OutputType.");

			_outputType = outputTypeNode.InnerText;
		}

		string IBuildSystemParser.GetSourceName()
		{
			return _projectName;
		}

		string IBuildSystemParser.GetSourcePath()
		{
			return _projectPath;
		}

		string IBuildSystemParser.GetOutputAssemblyPath(string folderPrefix)
		{
			var builtAssemblyPathNoExtension = Path.Combine(folderPrefix, _assemblyName);
			var builtAssemblyPath = builtAssemblyPathNoExtension;
			switch (_outputType)
			{
				case "WinExe":
					builtAssemblyPath += ".exe";
					break;
				case "Library":
					builtAssemblyPath += ".dll";
					break;
				default:
					throw new DataException("VS project " + _projectPath + " specifies an unexpected OutputType: " + _outputType);
			}

			return builtAssemblyPath;
		}

		List<string> IBuildSystemParser.GetReferencedAssemblies(string folderPrefix)
		{
			var paths = new List<string>();

			var hintPathNodes = _project.SelectNodes("//msbuild:HintPath", _xmlnsManager);
			if (hintPathNodes == null)
				return paths;

			foreach (XmlNode node in hintPathNodes)
			{
				// Make sure this isn't a Linux-only reference:
				var parentRefNode = node.SelectSingleNode("..") as XmlElement;
				if (parentRefNode != null)
				{
					var condition = parentRefNode.GetAttribute("Condition");
					if (condition == "'$(OS)'=='Unix'")
						break;
				}
				var hintPath = node.InnerText;
				var refAssemblyName = Path.GetFileName(hintPath);
				if (refAssemblyName != null)
				{
					var refAssemblyPath = Path.Combine(folderPrefix, refAssemblyName);
					paths.Add(refAssemblyPath);
				}
			}
			return paths;
		}
	}

	class MakefileParser : IBuildSystemParser
	{
		private readonly string _makeFilePath;
		private readonly string _makeFile;
		private string _buildProduct;
		private string _buildExtension;

		/// <summary>
		/// Given an MSBuild Target node and associated XmlNamespaceManager, returns a list of Makefile
		/// parser objects, one for each (non-Linux) Makefile specified in the Target.
		/// </summary>
		/// <param name="targetNode">MSBuild Target node</param>
		/// <param name="xmlnsManager">XML Namespace Manager needed to parse targetNode</param>
		/// <param name="projRootPath"></param>
		/// <returns>List of Makefile parsers if any Makefiles specified in the target, or else null</returns>
		public static List<IBuildSystemParser> GetMakefileParsers(XmlElement targetNode, XmlNamespaceManager xmlnsManager, string projRootPath)
		{
			var parsers = new List<IBuildSystemParser>();

			// Look for "Make" node that isn't Unix only:
			var makeNodes = targetNode.SelectNodes("msbuild:Make[@Condition != \"'$(OS)'=='Unix'\"]", xmlnsManager);
			if (makeNodes == null)
				return parsers;

			foreach (XmlElement makeNode in makeNodes)
			{
				var makeFilePath = makeNode.GetAttribute("Makefile").Replace("$(fwrt)", projRootPath);
				parsers.Add(GetMakefileParser(makeFilePath));
			}

			return parsers;
		}

		public static IBuildSystemParser GetMakefileParser(string makeFilePath)
		{
			var parser = new MakefileParser(makeFilePath);

			parser.Init();

			return parser;
		}

		MakefileParser(string makeFilePath)
		{
			_makeFilePath = makeFilePath;
			_makeFile = Path.GetFileNameWithoutExtension(makeFilePath);
		}

		public void Init()
		{
			if (!File.Exists(_makeFilePath))
				throw new FileNotFoundException("Makefile file " + _makeFilePath + " does not exist.");

			// Read in MakeFile, looking for BUILD_PRODUCT and BUILD_EXTENSION settings:
			var makeFile = new StreamReader(_makeFilePath);
			var currLine = makeFile.ReadLine();
			while (currLine != null)
			{
				if (currLine.StartsWith("BUILD_PRODUCT"))
					_buildProduct = currLine.Substring(1 + currLine.IndexOf('=')).Trim();
				else if (currLine.StartsWith("BUILD_EXTENSION"))
					_buildExtension = currLine.Substring(1 + currLine.IndexOf('=')).Trim();

				if (_buildProduct != null && _buildExtension != null)
					break;

				currLine = makeFile.ReadLine();
			}

			if (_buildProduct == null)
				throw new DataException("Could not find BUILD_PRODUCT in MakeFile  " + _makeFilePath + ".");

			if (_buildExtension == null)
				throw new DataException("Could not find BUILD_EXTENSION in MakeFile  " + _makeFilePath + ".");
		}

		string IBuildSystemParser.GetOutputAssemblyPath(string folderPrefix)
		{
			var builtAssemblyPath = Path.Combine(folderPrefix, _buildProduct) + "." + _buildExtension;

			return builtAssemblyPath;
		}

		List<string> IBuildSystemParser.GetReferencedAssemblies(string folderPrefix)
		{
			return new List<string>();
		}

		string IBuildSystemParser.GetSourceName()
		{
			return _makeFile;
		}

		string IBuildSystemParser.GetSourcePath()
		{
			return _makeFilePath;
		}
	}
}
