/*
JScript to process various WIX fragments, as follows:

1) Remove all TE-only features their sub-features from Features.wxs.

2) Adjust the UI module: change the yellow bitmaps to green, and remove any special TE behavior.

3) Adjust the FW main module: change the external .cab file name so it doesn't clash with the version including TE.
(The .cab file made with this installer turns out to be incompatible with the one for the main BTE installer,
because the order of files inside the .cab is different. There doesn't appear to be any reason for this.)

4) Remove the Translation Editor directory and all its subdirectories from AutoProcessedFiles.wxs.

5) Examine every component to see if it is referred to in Features.wxs. If it isn't
then delete it.

The processed files are written to <filename>_No_TE.wxs.
*/

var fso = new ActiveXObject("Scripting.FileSystemObject");
var shellObj = new ActiveXObject("WScript.Shell");

// Get root path details:
var iLastBackslash = WScript.ScriptFullName.lastIndexOf("\\");
var ScriptPath = WScript.ScriptFullName.slice(0, iLastBackslash);

var RootFolder = ScriptPath;
// If the script path contains a subfolder called Installer, then set the root folder back one notch before that:
iLastBackslash = ScriptPath.lastIndexOf("\\Installer");
if (iLastBackslash > 0)
{
	if (ScriptPath.slice(iLastBackslash + 1, iLastBackslash + 10) == "Installer")
		RootFolder = ScriptPath.slice(0, iLastBackslash + 1).toLowerCase();
}
var InstallerFolder = fso.BuildPath(RootFolder, "Installer");

// Set up the features XML parsers:
var xmlFeatures = GetXmlParser(fso.BuildPath(InstallerFolder, "Features.wxs"));
var xmlFeaturesSet = new Array();
xmlFeaturesSet.push(xmlFeatures);

// Set up the files XML parsers:
var xmlFiles = GetXmlParser(fso.BuildPath(InstallerFolder, "Files.wxs"));
var xmlAutoFiles = GetXmlParser(fso.BuildPath(InstallerFolder, "AutoFiles.wxs"));
// The AutoFiles file contains several feature references that will need filtering:
xmlFeaturesSet.push(xmlAutoFiles);

// Include the PatchCorrections:
var xmlPatchCorrections = GetXmlParser(fso.BuildPath(InstallerFolder, "PatchCorrections.wxs"));
// The PatchCorrections file may contain feature references that will need filtering:
xmlFeaturesSet.push(xmlPatchCorrections);

// 1) Remove TE feature, all subfeatures, and all references to it:
RemoveFeature("TE");
RemoveFeatureEndingWith("_TE");

// 2) Adjust the UI module: change the yellow bitmaps to green, and remove any special TE behavior.
var xmlUI = GetXmlParser(fso.BuildPath(InstallerFolder, "FwUI.wxs"));
var TopBitmap = xmlUI.selectSingleNode("//wix:Binary[@SourceFile='Binary\\FieldWorks.topyellow.bmp']");
TopBitmap.setAttribute("SourceFile", "Binary\\FieldWorks.topgreen.bmp");
var SideBitmap = xmlUI.selectSingleNode("//wix:Binary[@SourceFile='Binary\\FieldWorks.sideyellowfabric.bmp']");
SideBitmap.setAttribute("SourceFile", "Binary\\FieldWorks.sidegreenfabric.bmp");
var LocalizationTEEvents = xmlUI.selectNodes("//wix:Publish[contains(@Value, '_TE') and substring-after(@Value, '_TE') = '']");
LocalizationTEEvents.removeAll();

// 3) Adjust the FW main module: change the external .cab file names so they don't clash with the version including TE.
var xmlFW = GetXmlParser(fso.BuildPath(InstallerFolder, "FW.wxs"));
var ExternalCabMediaNodes = xmlFW.selectNodes("//wix:Media[@EmbedCab='no']");
var n;
for (n = 0; n < ExternalCabMediaNodes.length; n++)
{
	var CabMediaNode = ExternalCabMediaNodes[n];
	var OriginalFileName = CabMediaNode.getAttribute("Cabinet");
	// Remove .cab extension:
	var FileName = OriginalFileName.slice(0, -4);
	// Make sure we have enough room for the "SE" addition in 8.3 format:
	if (FileName.length > 5)
		FileName = FileName.slice(0, 6);
	FileName += "SE.cab";

	// Make sure name will not be duplicated:
	var index = 1;
	var matches = xmlFW.selectNodes("//wix:Media[@EmbedCab='no' and @Cabinet='" + FileName + "']");
	while (matches.length > 0)
	{
		FileName = FileName.slice(0, 5) + index + FileName.slice(6); // WARNING: This will fail if we have more than 9 cabinets with the same 8.3 name.
		index++;
		matches = xmlFW.selectNodes("//wix:Media[@EmbedCab='no' and @Cabinet='" + FileName + "']");
	}

	CabMediaNode.setAttribute("Cabinet", FileName);
	var CabSearchNode = xmlFW.selectSingleNode("//wix:FileSearch[@Name='" + OriginalFileName + "']");
	CabSearchNode.setAttribute("Name", FileName);
}

// 4) Remove the Translation Editor directory and all its subdirectories:
var TeDirectory = xmlAutoFiles.selectNodes("//wix:Directory[@LongName='Translation Editor']");
TeDirectory.removeAll();
TeDirectory = xmlFiles.selectNodes("//wix:Directory[@LongName='Translation Editor']");
TeDirectory.removeAll();

// 5) Examine every component to see if it is referred to in any features. If it isn't then delete it:
TestAndRemoveComponents(xmlAutoFiles);
TestAndRemoveComponents(xmlFiles);
TestAndRemoveComponents(xmlPatchCorrections);

// Do the same for Registry components:
var xmlRegistry = GetXmlParser(fso.BuildPath(InstallerFolder, "Registry.wxs"));
TestAndRemoveComponents(xmlRegistry);

// Do the same for CopyFiles components:
var xmlCopyFiles = GetXmlParser(fso.BuildPath(InstallerFolder, "CopyFiles.wxs"));
TestAndRemoveComponents(xmlCopyFiles);

// Do the same for Shortcuts components:
var xmlShortcuts = GetXmlParser(fso.BuildPath(InstallerFolder, "Shortcuts.wxs"));
TestAndRemoveComponents(xmlShortcuts);
DeleteNode(xmlShortcuts, "//wix:Directory[@Id='TEMenu']");

// Save the new XML files:
xmlFiles.save(fso.BuildPath(InstallerFolder, "Files_No_TE.wxs"));
xmlAutoFiles.save(fso.BuildPath(InstallerFolder, "AutoFiles_No_TE.wxs"));
xmlFeatures.save(fso.BuildPath(InstallerFolder, "Features_No_TE.wxs"));
xmlRegistry.save(fso.BuildPath(InstallerFolder, "Registry_No_TE.wxs"));
xmlCopyFiles.save(fso.BuildPath(InstallerFolder, "CopyFiles_No_TE.wxs"));
xmlShortcuts.save(fso.BuildPath(InstallerFolder, "Shortcuts_No_TE.wxs"));
xmlUI.save(fso.BuildPath(InstallerFolder, "FwUI_No_TE.wxs"));
xmlFW.save(fso.BuildPath(InstallerFolder, "FW_No_TE.wxs"));
xmlPatchCorrections.save(fso.BuildPath(InstallerFolder, "PatchCorrections_No_TE.wxs"));

// Deletes from the features list the given feature, any sub-features, and references to that feature.
function RemoveFeature(FeatureName)
{
	var f;
	for (f = 0; f < xmlFeaturesSet.length; f++)
	{
		var Feature = xmlFeaturesSet[f].selectNodes("//wix:Feature[@Id='" + FeatureName + "']");
		Feature.removeAll();
		var FeatureRef = xmlFeaturesSet[f].selectNodes("//wix:FeatureRef[@Id='" + FeatureName + "']");
		FeatureRef.removeAll();
	}
}

// Deletes from the features list any feature ending with the given string, along with any sub-features,
// and references to that feature.
function RemoveFeatureEndingWith(FeatureNameEnd)
{
	var XPathStringEnd = "contains(@Id, '" + FeatureNameEnd + "') and substring-after(@Id, '" + FeatureNameEnd + "') = ''";
	var f;
	for (f = 0; f < xmlFeaturesSet.length; f++)
	{
		var Features = xmlFeaturesSet[f].selectNodes("//wix:Feature[" + XPathStringEnd + "]");
		Features.removeAll();
		var FeatureRefs = xmlFeaturesSet[f].selectNodes("//wix:FeatureRef[" + XPathStringEnd + "]");
		FeatureRefs.removeAll();
	}
}

// Tests every component in the given file to see if it is referred to in Features.
// If it isn't then delete it.
function TestAndRemoveComponents(xmlFile)
{
	// Get all component nodes:
	var ComponentNodes = xmlFile.selectNodes("//wix:Component");

	for (i = 0; i < ComponentNodes.length; i++)
	{
		// Get current component Id:
		var Component = ComponentNodes[i];
		var ComponentId = Component.getAttribute("Id");
		// check against Features:
		var f;
		var found = false;
		for (f = 0; f < xmlFeaturesSet.length; f++)
		{
			var FeatureCompRef = xmlFeaturesSet[f].selectSingleNode("//wix:ComponentRef[@Id='" + ComponentId + "']");
			if (FeatureCompRef != null)
			{
				found = true;
				break;
			}
		}
		if (!found)
			Component.selectSingleNode("..").removeChild(Component);
	}
}

// Set up an XML parser from the given XML file, including namespaces that are in WIX:
function GetXmlParser(FileName)
{
	var xmlFile = new ActiveXObject("Msxml2.DOMDocument.6.0");
	xmlFile.async = false;
	xmlFile.preserveWhiteSpace = true;
	xmlFile.setProperty("SelectionNamespaces", 'xmlns:wix="http://schemas.microsoft.com/wix/2006/wi"');
	xmlFile.load(FileName);

	if (xmlFile.parseError.errorCode != 0)
	{
		var myErr = xmlFile.parseError;
		ReportError("XML error in " + FileName + ": " + myErr.reason + "\non line " + myErr.line + " at position " + myErr.linepos);
		WScript.Quit(-1);
	}
	return xmlFile;
}

function DeleteNode(xmlFile, XPath)
{
	var CondemnedNode = xmlFile.selectSingleNode(XPath);
	CondemnedNode.selectSingleNode("..").removeChild(CondemnedNode);
}

// Creates error log file:
function ReportError(text)
{
	var tso = fso.OpenTextFile("StripOutTe.log", 2, true, -1);
	tso.WriteLine(Date() + ": " + text);
	tso.Close();
}
