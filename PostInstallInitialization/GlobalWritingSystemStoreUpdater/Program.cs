using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.Win32;

namespace GlobalWritingSystemStoreUpdater
{
	class Program
	{
		private const int FwVersion = 8;	// Registry key to get RootCodeDir

		static void Main()
		{
			try
			{
				DateTime now = DateTime.UtcNow;
				UpdateGlobalWritingSystemStore(now);
			}
			catch (Exception e)
			{
				/* There is no longer a console - we don't want a DOS window popping up during installation!
				Console.WriteLine("Error: {0}", e.Message);
				 */
				Environment.Exit(1);
			}
		}

		// Copy ldml files from Templates to global WritingSystemStore if not already there and make them useable.
		private static void UpdateGlobalWritingSystemStore(DateTime dateTime)
		{
			string wsStorePath = GlobalWritingSystemStoreDirectory;
			if (!Directory.Exists(wsStorePath))
				SetCommonAppDataPermissions(wsStorePath);
			foreach (string src in Directory.GetFiles(TemplatesDirectory, "*.ldml"))
			{
				try
				{
					string dest = Path.Combine(wsStorePath, Path.GetFileName(src));
					if (!File.Exists(dest))
					{
						File.Copy(src, dest, true);
						File.SetAttributes(dest, FileAttributes.Normal);
						UpdateWritingSystem(dest, dateTime);
					}
				}
				catch (Exception e)
				{
					// if anything goes wrong, just skip this file
					/* There is no longer a console - we don't want a DOS window popping up during installation!
					Console.WriteLine("Warning: unable to properly update the global writing system, {0}; {1}",
						Path.GetFileNameWithoutExtension(src), e.Message);
					 */
				}
			}
		}

		private static void UpdateProjectWritingSystemStore(string projectName, DateTime dateTime)
		{
			string wsStorePath = Path.Combine(Path.Combine(ProjectsDirectory, projectName), "WritingSystemStore");
			foreach (string wsPath in Directory.GetFiles(wsStorePath, "*.ldml"))
			{
				try
				{
					UpdateWritingSystem(wsPath, dateTime);
				}
				catch (Exception e)
				{
					// if anything goes wrong, just skip this file
					/* There is no longer a console - we don't want a DOS window popping up during installation!
					Console.WriteLine("Warning: unable to properly update the {0} writing system, {1}; {2}",
						projectName, Path.GetFileNameWithoutExtension(wsPath), e.Message);
					 */
				}
			}
		}

		private static void SetCommonAppDataPermissions(string path)
		{
			DirectoryInfo di = Directory.CreateDirectory(path);
			DirectorySecurity ds = di.GetAccessControl();
			var sid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
			AccessRule rule = new FileSystemAccessRule(sid, FileSystemRights.Write | FileSystemRights.ReadAndExecute
				| FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
				PropagationFlags.InheritOnly, AccessControlType.Allow);
			bool modified;
			ds.ModifyAccessRule(AccessControlModification.Add, rule, out modified);
			di.SetAccessControl(ds);
		}

		private static void UpdateWritingSystem(string path, DateTime dateTime)
		{
			XElement ldmlElem = XElement.Load(path);
			ldmlElem.Element("identity").Element("generation").Attribute("date").Value = string.Format("{0:s}", dateTime);
			ldmlElem.Save(path);
		}

		private static string CommonAppDataFolder(string appName)
		{
			string path = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
			return Path.Combine(Path.Combine(path, "SIL"), appName);
		}

		private static string GetDirectory(string pathValue)
		{
			string rootDir = null;
			RegistryKey fwKey = Registry.LocalMachine.OpenSubKey(string.Format("Software\\SIL\\FieldWorks\\{0}", FwVersion));
			if (fwKey != null)
			{
				rootDir = (string)fwKey.GetValue(pathValue);
			}
			if (string.IsNullOrEmpty(rootDir))
				throw new ApplicationException(string.Format("The registry value {0} is not configured properly", pathValue));

			return rootDir;
		}

		private static string CodeDirectory
		{
			get
			{
				return GetDirectory("RootCodeDir");
			}
		}

		private static string TemplatesDirectory
		{
			get
			{
				return Path.Combine(CodeDirectory, "Templates");
			}
		}

		private static string GlobalWritingSystemStoreDirectory
		{
			get
			{
				return CommonAppDataFolder("WritingSystemStore");
			}
		}

		private static string ProjectsDirectory
		{
			get
			{
				return GetDirectory("ProjectsDir");
			}
		}
	}
}
