using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace ArchiveAndBuildPatch
{
	class ComponentLibraryUpdater
	{
		// Registry Library stuff is suspended indefinitely:
		private const string RegLibraryName = "RegLibrary.xml";
		private const string RegLibraryAddendaName = "RegLibraryAddenda.xml";

		private string m_finalReport = "";

		private readonly string m_fileLibraryName;
		private readonly string m_fileLibraryAddendaName;
		private readonly string m_fileLibPath;
		private readonly string m_fileLibAddPath;
		private readonly string m_regLibPath;
		private readonly string m_regLibAddPath;
		private readonly string m_projRootPath;

		private XmlDocument m_xmlRegLibrary;
		private XmlDocument m_xmlRegLibraryAddenda;

		public ComponentLibraryUpdater()
		{
			m_fileLibraryName = "";
			var fileLibraryNode = Program.Configuration.SelectSingleNode("//FileLibrary/Library") as XmlElement;
			if (fileLibraryNode != null)
				m_fileLibraryName = fileLibraryNode.GetAttribute("File");

			m_fileLibraryAddendaName = "";
			var fileLibraryAddNode = Program.Configuration.SelectSingleNode("//FileLibrary/Addenda") as XmlElement;
			if (fileLibraryAddNode != null)
				m_fileLibraryAddendaName = fileLibraryAddNode.GetAttribute("File");

			m_fileLibPath = Path.Combine(Program.ExeFolder, m_fileLibraryName);
			m_fileLibAddPath = Path.Combine(Program.ExeFolder, m_fileLibraryAddendaName);

			m_regLibPath = Path.Combine(Program.ExeFolder, RegLibraryName);
			m_regLibAddPath = Path.Combine(Program.ExeFolder, RegLibraryAddendaName);

			// Get development project root path:
			if (Program.ExeFolder.ToLowerInvariant().EndsWith("installer"))
				m_projRootPath = Program.ExeFolder.Substring(0, Program.ExeFolder.LastIndexOf('\\'));
			else
				m_projRootPath = Program.ExeFolder;
		}

		public void UpdateLibraries()
		{
			UpdateFileLibrary();
			UpdateRegLibrary();

			Console.WriteLine(m_finalReport);
		}

		/// <summary>
		/// Combines the FileLibraryAddenda.xml file into the FileLibrary.xml file.
		/// </summary>
		private void UpdateFileLibrary()
		{
			// Test if we can write to the FileLibrary file:
			if (File.Exists(m_fileLibPath))
				if ((File.GetAttributes(m_fileLibPath) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
					throw new Exception("The file " + m_fileLibPath + " is read-only. Did you forget to check it out of Perforce?");

			// Open the File Library, or create one if it doesn't yet exist:
			var xmlFileLibrary = new XmlDocument();
			if (File.Exists(m_fileLibPath))
			{
				// There is a library file, so load it:
				xmlFileLibrary.Load(m_fileLibPath);
			}
			else
			{
				// There is no library file, so initiate an XML structure for an empty library:
				xmlFileLibrary.LoadXml("<FileLibrary>\r\n</FileLibrary>");
			}

			MergeAddendaIntoLibrary(xmlFileLibrary);
			UpdateLibraryFileDetails(xmlFileLibrary);

			// Save modified File Library:
			var settings = new XmlWriterSettings { Indent = true };
			var xmlWriter = XmlWriter.Create(m_fileLibPath, settings);
			if (xmlWriter == null)
				throw new Exception("Could not create output file " + m_fileLibPath);

			xmlFileLibrary.Save(xmlWriter);
			xmlWriter.Close();

			// Remove File Library Addenda file:
			if (File.Exists(m_fileLibAddPath))
				File.Delete(m_fileLibAddPath);
		}

		/// <summary>
		/// Updates File Library data regarding file date, size, version and MD5 hash
		/// </summary>
		/// <param name="xmlFileLibrary">The main File Library</param>
		private void UpdateLibraryFileDetails(XmlDocument xmlFileLibrary)
		{
			var fileLibraryNode = xmlFileLibrary.SelectSingleNode("FileLibrary");
			var fileLibraryNodes = fileLibraryNode.SelectNodes("File");

			foreach (XmlElement file in fileLibraryNodes)
			{
				var filePath = MakeFullPath(file.GetAttribute("Path"));

				if (!File.Exists(filePath))
					continue;

				var fileVersionInfo = FileVersionInfo.GetVersionInfo(filePath);
				var fileVersion = "";
				if (fileVersionInfo.FileVersion != null)
				{
					fileVersion = fileVersionInfo.FileMajorPart + "." + fileVersionInfo.FileMinorPart + "." +
								  fileVersionInfo.FileBuildPart + "." + fileVersionInfo.FilePrivatePart;
				}
				var fi = new FileInfo(filePath);

				file.SetAttribute("Size", fi.Length.ToString());
				file.SetAttribute("Date", fi.LastWriteTime.ToShortDateString() + " " + fi.LastWriteTime.ToShortTimeString());
				file.SetAttribute("Version", fileVersion);
				file.SetAttribute("MD5", CalcFileMd5(filePath));
			}
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
		/// Returns the full path of the given relative path.
		/// </summary>
		/// <param name="relPath">A relative path (from the root FW folder)</param>
		/// <returns>The full path of the specified file or folder</returns>
		private string MakeFullPath(string relPath)
		{
			// Replace ${config} variable with absolute equivalent:
			return Path.Combine(m_projRootPath, relPath.Replace("\\${config}\\", "\\" + Program.BuildType + "\\"));
		}

		/// <summary>
		/// Adds the XML elements in the File Library Addenda file to the main File Library structure.
		/// </summary>
		/// <param name="xmlFileLibrary">The main File Library</param>
		private void MergeAddendaIntoLibrary(XmlDocument xmlFileLibrary)
		{
			// See if there is a FileLibraryAddenda.xml file:
			if (!File.Exists(m_fileLibAddPath))
			{
				// There is no addenda file:
				m_finalReport += m_fileLibraryAddendaName + " file not found: " + m_fileLibraryName + " not changed.\n";
				return;
			}

			// Get the first file in the main library, if it exists:
			var fileLibraryNode = xmlFileLibrary.SelectSingleNode("FileLibrary");
			var firstFile = fileLibraryNode.SelectSingleNode("File[1]") as XmlElement;

			// Get maximum PatchGroup attribute so that we can increment it for the addenda files.
			var fileLibraryNodes = fileLibraryNode.SelectNodes("File");
			if (fileLibraryNodes == null)
				throw new Exception("Attempt to select File nodes in " + m_fileLibPath + " returned null.");
			var maxPatchGroup = 0;
			foreach (XmlElement node in fileLibraryNodes)
			{
				var patchGroup = node.GetAttribute("PatchGroup");
				if (patchGroup.Length > 0)
				{
					int currentPatchGroup = int.Parse(patchGroup);
					if (currentPatchGroup > maxPatchGroup)
						maxPatchGroup = currentPatchGroup;
				}
			}
			string newPatchGroup = (1 + maxPatchGroup).ToString();

			// Load addenda file:
			var xmlFileLibraryAddenda = new XmlDocument();
			xmlFileLibraryAddenda.Load(m_fileLibAddPath);

			// Transfer any addenda into main library structure:
			var fileLibraryAddendaNode = xmlFileLibraryAddenda.SelectSingleNode("FileLibrary");
			var fileLibraryAddendaNodes = fileLibraryAddendaNode.SelectNodes("File");

			if (fileLibraryAddendaNodes == null)
				throw new Exception("Attempt to select File nodes in " + m_fileLibAddPath + " returned null.");

			foreach (XmlElement fileElement in fileLibraryAddendaNodes)
			{
				if (newPatchGroup != "0")
					fileElement.SetAttribute("PatchGroup", newPatchGroup);
				var elementClone = xmlFileLibrary.ImportNode(fileElement, true);
				fileLibraryNode.InsertBefore(elementClone, firstFile);
			}

			if (fileLibraryAddendaNodes.Count == 0)
				m_finalReport += m_fileLibraryAddendaName + " contains no data!" + Environment.NewLine;
			else
				m_finalReport += m_fileLibraryAddendaName + ": transferred " + fileLibraryAddendaNodes.Count + " nodes." + Environment.NewLine;
		}

		/// <summary>
		/// Combines the RegLibraryAddenda.xml file into the RegLibrary.xml file.
		/// The only difference from the File Library is that some nodes may actually be changes to
		/// existing data, rather than new data.
		/// </summary>
		private void UpdateRegLibrary()
		{
			var OkToDeleteRegAddenda = true;
			var ctOmittedNodes = 0;

			// See if there is a RegLibraryAddenda.xml file:
			if (!File.Exists(m_regLibAddPath))
			{
				// There is no addenda file:
				m_finalReport += RegLibraryAddendaName + " file not found: " + RegLibraryName + " not changed.\n";
				return;
			}

			// There is an addenda file, so load it:
			m_xmlRegLibraryAddenda = new XmlDocument();
			m_xmlRegLibraryAddenda.Load(m_regLibAddPath);

			// Open Reg Library:
			m_xmlRegLibrary = new XmlDocument();
			if (File.Exists(m_regLibPath))
			{
				// There is a library file, so load it:
				m_xmlRegLibrary.Load(m_regLibPath);
			}
			else
			{
				// There is no library file, so initiate an XML structure for an empty library:
				m_xmlRegLibrary.LoadXml("<RegLibrary>\r\n</RegLibrary>");
			}

			var regLibraryNode = m_xmlRegLibrary.SelectSingleNode("RegLibrary");
			var firstReg = regLibraryNode.SelectSingleNode("Component[1]") as XmlElement;

			// Transfer new addenda or edit existing data in main library structure:
			var regLibraryAddendaNode = m_xmlRegLibraryAddenda.SelectSingleNode("RegLibrary");
			var regLibraryAddendaNodes = regLibraryAddendaNode.SelectNodes("Component");

			foreach (XmlElement addendaElement in regLibraryAddendaNodes)
			{
				var compId = addendaElement.GetAttribute("Id");
				var compGuid = addendaElement.GetAttribute("ComponentGuid");

				// See if current Addenda node has a match in the main library:
				var matchNode = regLibraryNode.SelectSingleNode("Component[@Id=\"" + compId + "\"]") as XmlElement;
				if (matchNode != null)
				{
					if (matchNode.GetAttribute("ComponentGuid") != compGuid)
					{
						m_finalReport += "ERROR in " + RegLibraryAddendaName + ": Component with ID " + compId + " has a match in " + RegLibraryName + " with the wrong GUID.\n";
						OkToDeleteRegAddenda = false;
						ctOmittedNodes++;
						continue;
					}
					// Replace KeyHeader and Root in existing Library node with data from Addenda node:
					matchNode.SetAttribute("KeyHeader", addendaElement.GetAttribute("KeyHeader"));
					matchNode.SetAttribute("Root", addendaElement.GetAttribute("Root"));
				}
				else
				{
					// There isn't a match for this Addenda node, so simply add it to the library:
					var elementClone = m_xmlRegLibrary.ImportNode(addendaElement, true);
					regLibraryNode.InsertBefore(elementClone, firstReg);
				}
			}
			var ctTranferredNodes = regLibraryAddendaNodes.Count - ctOmittedNodes;

			if (regLibraryAddendaNodes.Count == 0)
				m_finalReport += RegLibraryAddendaName + " contains no data!" + Environment.NewLine;
			else
				m_finalReport += RegLibraryAddendaName + ": transferred " + ctTranferredNodes + " nodes." + Environment.NewLine;

			// Save modified Reg Library:
			if (ctTranferredNodes > 0)
			{
				if (File.Exists(m_regLibPath))
					if ((File.GetAttributes(m_regLibPath) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
						throw new Exception("The file " + m_regLibPath + " is read-only. Did you forget to check it out of Perforce?");

				var settings = new XmlWriterSettings { Indent = true };
				var xmlWriter = XmlWriter.Create(m_regLibPath, settings);

				m_xmlRegLibrary.Save(xmlWriter);
				xmlWriter.Close();
			}

			// Remove Reg Library Addenda file:
			if (OkToDeleteRegAddenda)
				File.Delete(m_regLibAddPath);
		}
	}
}
