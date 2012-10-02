/*

JScript to generate a partial WIX source, defining feature entries for orphaned components.

The contents of the output file "OphanedComponents.wxs" should be copied and pasted into
a suitable full WIX source. If pasted within Visual Studio, proper indentation
will be applied. (None is generated in the output of this script.)

*/

var fso = new ActiveXObject("Scripting.FileSystemObject");
var WixSources = new Array(); // Wix source files to examine.

// Get script path details:
var iLastBackslash = WScript.ScriptFullName.lastIndexOf("\\");
var ScriptPath = WScript.ScriptFullName.slice(0, iLastBackslash);

// Get Script root path:
var ScriptRootPath = ScriptPath.slice(0, ScriptPath.lastIndexOf("\\"));

WixSources.push("CopyFiles.wxs");
WixSources.push("Files.wxs");
WixSources.push("AutoFiles.wxs");
WixSources.push("Environment.wxs");
WixSources.push("Registry.wxs");
WixSources.push("Shortcuts.wxs");
WixSources.push("MergeModules.wxs");

// Read in current Feature Component References:
var FeaturesWxs = new ActiveXObject("Msxml2.DOMDocument.6.0");
FeaturesWxs.async = false;
FeaturesWxs.setProperty("SelectionNamespaces", 'xmlns:wix="http://schemas.microsoft.com/wix/2003/01/wi"');
FeaturesWxs.load("Features.wxs");
if (FeaturesWxs.parseError.errorCode != 0)
{
	var myErr = FeaturesWxs.parseError;
	WScript.Echo("XML error in Features.wxs: " + myErr.reason + "\non line " + myErr.line + " at position " + myErr.linepos);
	WScript.Quit();
}
// Also read in current Feature Component References from AutoFiles:
var AutoFeaturesWxs = new ActiveXObject("Msxml2.DOMDocument.6.0");
AutoFeaturesWxs.async = false;
AutoFeaturesWxs.setProperty("SelectionNamespaces", 'xmlns:wix="http://schemas.microsoft.com/wix/2003/01/wi"');
AutoFeaturesWxs.load("AutoFiles.wxs");
if (AutoFeaturesWxs.parseError.errorCode != 0)
{
	var myErr = AutoFeaturesWxs.parseError;
	WScript.Echo("XML error in AutoFiles.wxs: " + myErr.reason + "\non line " + myErr.line + " at position " + myErr.linepos);
	WScript.Quit();
}

var FeatureSources = new Array();
FeatureSources.push(FeaturesWxs);
FeatureSources.push(AutoFeaturesWxs);

var tso = fso.OpenTextFile("OphanedComponents.wxs", 2, true, -1);
tso.WriteLine('<!-- Add to a feature: -->');

for (index = 0; index < WixSources.length; index++)
{
	tso.WriteLine('<!-- From ' + WixSources[index] + ': -->');
	MakeOrphanedRefs(WixSources[index], tso);
}

tso.Close();

WScript.Echo("Done.");

function MakeOrphanedRefs(WixSourceFileName, tso)
{
	// Read in current Wix Source:
	var WixSource = new ActiveXObject("Msxml2.DOMDocument.6.0");
	WixSource.async = false;
	WixSource.setProperty("SelectionNamespaces", 'xmlns:wix="http://schemas.microsoft.com/wix/2003/01/wi"');
	WixSource.load(WixSourceFileName);
	if (WixSource.parseError.errorCode != 0)
	{
		var myErr = WixSource.parseError;
		WScript.Echo("XML error in " + WixSourceFileName + ": " + myErr.reason + "\non line " + myErr.line + " at position " + myErr.linepos);
		WScript.Quit();
	}

	var CandidateComponents = WixSource.selectNodes("//wix:Component");
	for (c = 0; c < CandidateComponents.length; c++)
	{
		var CurrentId = CandidateComponents[c].getAttribute("Id");

		var f;
		var found = false;
		for (f = 0; f < FeatureSources.length && !found; f++)
		{
			if (FeatureSources[f].selectSingleNode("//wix:ComponentRef[@Id='" + CurrentId + "']") != null)
			{
				found = true;
				break;
			}
		}
		if (!found)
			tso.WriteLine('<ComponentRef Id="' + CurrentId + '"/>');
	}
}
