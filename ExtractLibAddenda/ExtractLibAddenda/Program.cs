/*
 * This program is for use in panic situations where a FieldWorks installer has been released,
 * but the ArchiveAndBuildPatch.exe was not run before the next build overwrote the
 * FileLibraryAddenda file. In such cases, new GUIDs will have ben assigned to files new
 * since the previous release, and this will break any attempts to make a patch now or in
 * the future. This program will reverse engineer the current SetupFW.msi file in the Installer
 * folder and create the FileLibraryAddenda that was used to build it. So you will probably need
 * to copy the SetupFW.msi that was released on top of the one currently in the Installer folder.
 * The output file is __FileLibraryAddenda.xml, which is the reverse-engineered library.
 *
 */

using System;
using System.IO;
using System.Linq;
using System.Xml;

namespace ExtractLibAddenda
{
	class Program
	{
		public static string ExeFolder;

		static void Main(string[] args)
		{
			// Get our .exe folder path:
			var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
			if (exePath == null)
				throw new Exception("So sorry, don't know where we are!");
			ExeFolder = Path.GetDirectoryName(exePath);

			// Test that installer given on command line exists:
			if (args.Count() < 1)
				throw new Exception("Must specify installer file in command line.");

			var installerFilePath = args[0];

			if (!File.Exists(installerFilePath))
				throw new Exception("Installer file '" + installerFilePath + "' does not exist.");

			var libExtractor = new LibraryExtractor(installerFilePath);
			libExtractor.ExtractFileLibrary();



/*
			// Test against existing Library (for use in testing this program):
			string errors = "";
			var xmlLib = new XmlDocument();
			xmlLib.Load("FileLibrary.xml");
			var xmlNewAddenda = new XmlDocument();
			xmlNewAddenda.Load("__FileLibraryAddenda.xml");

			var libNodes = xmlLib.SelectNodes("//File");

			foreach (XmlElement libNode in libNodes)
			{
				var path = libNode.GetAttribute("Path");
				var componentGuid = libNode.GetAttribute("ComponentGuid");
				var longName = libNode.GetAttribute("LongName");
				var directoryId = libNode.GetAttribute("DirectoryId");
				var featureList = libNode.GetAttribute("FeatureList");

				var newNode = xmlNewAddenda.SelectSingleNode("//File[@ComponentGuid=\"" + componentGuid + "\"]") as XmlElement;
				if (newNode == null)
				{
					errors += "File with Guid " + componentGuid + " exists in Library but not in new Addenda." + Environment.NewLine;
					continue;
				}
				var newPath = newNode.GetAttribute("Path");
				var newLongName = newNode.GetAttribute("LongName");
				var newDirectoryId = newNode.GetAttribute("DirectoryId");
				var newFeatureList = newNode.GetAttribute("FeatureList");

				if (path != newPath)
				{
					errors += "File with Guid " + componentGuid + " has path '" + path + "' in Library but in new Addenda it is '" +
							  newPath + "'." + Environment.NewLine;
					continue;
				}
				if (newLongName != longName)
					errors += "File with path " + path + " has LongName '" + longName + "' in Library but in new Addenda it is '" + newLongName + "'." + Environment.NewLine;
				if (newDirectoryId != directoryId)
					errors += "File with path " + path + " has DirectoryId '" + directoryId + "' in Library but in new Addenda it is '" + newDirectoryId + "'." + Environment.NewLine;
				if (newFeatureList != featureList)
					errors += "File with path " + path + " has FeatureList '" + featureList + "' in Library but in new Addenda it is '" + newFeatureList + "'." + Environment.NewLine;
			}

			libNodes = xmlNewAddenda.SelectNodes("//File");

			foreach (XmlElement libNode in libNodes)
			{
				var componentGuid = libNode.GetAttribute("ComponentGuid");

				var oldNode = xmlLib.SelectSingleNode("//File[@ComponentGuid=\"" + componentGuid + "\"]") as XmlElement;
				if (oldNode == null)
				{
					errors += "File with Guid " + componentGuid + " exists in new Addenda but not in Library." + Environment.NewLine;
					continue;
				}
			}
			var output = new StreamWriter("__Errors.txt");
			output.WriteLine(errors);
			output.Close();
 */
		}
	}
}
