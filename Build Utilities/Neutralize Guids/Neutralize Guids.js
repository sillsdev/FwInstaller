// A JavaScript to replace GUIDs in the specified file with the more neutral "????????-????-????-????-????????????" string.
// This allows comparisons to be done between versions of AutoFiles.wxs without the different GUIDs being counted as significant.

if (WScript.Arguments.Length != 1)
{
	WScript.Echo("ERROR: must specify path of file to process.");
	WScript.Quit(-1);
}

var FilePath = WScript.Arguments.Item(0);

var fso = new ActiveXObject("Scripting.FileSystemObject");
if (!fso.FileExists(FilePath))
{
	WScript.Echo("ERROR: '" + FilePath + "' does not exist.");
	WScript.Quit(-2);
}

var OutputFile = FilePath + ".NoGuid";
var tsoRead = fso.OpenTextFile(FilePath, 1, false);
var tsoWrite = fso.CreateTextFile(OutputFile, true);

while (!tsoRead.AtEndOfStream)
{
	var line = tsoRead.ReadLine();

	line = line.replace(/[a-z0-9]{8}(?:-[a-z0-9]{4}){3}-[a-z0-9]{12}/ig, "????????-????-????-????-????????????")

	tsoWrite.WriteLine(line);
}
tsoRead.Close();
tsoWrite.Close();
