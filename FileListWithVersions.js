/*

JScript to generate a rcursive list of all directory's files.

The output file is "__FILES__.txt". Scanning begins from the same directory as this file.

*/

var shellObj = new ActiveXObject("WScript.Shell");
var fso = new ActiveXObject("Scripting.FileSystemObject");

// Get script path details:
var iLastBackslash = WScript.ScriptFullName.lastIndexOf("\\");
var ScriptPath = WScript.ScriptFullName.slice(0, iLastBackslash);

var tso = fso.OpenTextFile("__FILES__.txt", 2, true, -1);
ScanFiles(tso, ScriptPath);
tso.Close();

WScript.Echo("Done.");

function ScanFiles(tso, SourceFolder)
{
	var FileAndFolderTree = GetFileAndFolderTree(SourceFolder);
	TreeOutput(FileAndFolderTree, tso);
}

// Recursively output a folder tree.
function TreeOutput(Tree, tso)
{
	var FolderPath = Tree.FolderPath;
	var FolderName = FolderPath.slice(FolderPath.lastIndexOf("\\") + 1)

	tso.WriteLine(FolderName);

	var Files = Tree.FileList;
	var file;
	// Find longest file name and largest size:
	var LongestNameSize = 0;
	var LargestSize = 0;
	for (file = 0; file < Files.length; file++)
	{
		var curFile = Files[file];
		if (curFile.LongName.length > LongestNameSize)
			LongestNameSize = curFile.LongName.length;
		if (curFile.Size > LargestSize)
			LargestSize = curFile.Size;
	}

	// Output files:
	for (file = 0; file < Files.length; file++)
	{
		var curFile = Files[file];

		var NamePadding = "";
		for (np = LongestNameSize; np > curFile.LongName.length; np--)
			NamePadding += " ";

		var SizePadding = "";
		for (sp = LargestSize.toString().length; sp > curFile.Size.toString().length; sp--)
			SizePadding += " ";

		tso.WriteLine("  " + curFile.LongName + NamePadding + "  " + curFile.Size + SizePadding + "  " + curFile.Version);
	}

	// Recurse over subfolders:
	var Subfolders = Tree.SubfolderList;
	var folder;
	for (folder = 0; folder < Subfolders.length; folder++)
		TreeOutput(Subfolders[folder], tso);
}

// Recurses given FolderPath and returns an object containing two arrays:
// 1) array of objects containing full names, path strings, sizes and versions of files in the folder;
// 2) array of immediate subfolders, which recursively contain their files and subfolders.
// The returned object also includes the folder path for itself.
function GetFileAndFolderTree(FolderPath)
{
	var Results = new Object();
	Results.FolderPath = FolderPath;
	Results.FileList = new Array();
	Results.SubfolderList = new Array();

	// Check if current Folder is a Subversion metadata folder:
//	if (FolderPath.slice(-4) == ".svn")
//		return Results; // Don't include SVN folders.

	// Add files in current folder:
	var Folder = fso.GetFolder(FolderPath);
	var FileIterator = new Enumerator(Folder.files);
	for (; !FileIterator.atEnd(); FileIterator.moveNext())
	{
		var CurrentFile = FileIterator.item();

		var FileObject = new Object();
		FileObject.Path = CurrentFile.Path;
		FileObject.LongName = CurrentFile.Name;
		FileObject.Size = CurrentFile.Size;
		FileObject.Version = fso.GetFileVersion(CurrentFile.Path);
		Results.FileList.push(FileObject);
	}

	// Now recurse all subfolders:
	var SubfolderIterator = new Enumerator(Folder.SubFolders);
	for (; !SubfolderIterator.atEnd(); SubfolderIterator.moveNext())
	{
		var CurrentFolderPath = SubfolderIterator.item().Path;
		Results.SubfolderList.push(GetFileAndFolderTree(CurrentFolderPath));
	}
	return Results;
}
