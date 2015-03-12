using System.Xml;

namespace GenerateFilesSource
{
	internal sealed class TargetsFileData
	{
		internal string FilePath;
		internal readonly XmlDocument XmlDoc = new XmlDocument();
		internal XmlNamespaceManager XmlnsManager;
	}
}