#include <windows.h>
#include <stdio.h>
#include <msi.h>
#include <msiquery.h>
#include <tchar.h>

extern bool FileExists(_TCHAR * pszDirectory, _TCHAR * pszFileName);

// Detects the highest existing database migration script version and stores the number
// in the installer variable MAX_DBMIG_VER.
extern "C" __declspec(dllexport) UINT GetHighestDbMigrationVersion(MSIHANDLE hInstall)
{

	try
	{
		const int kcchStringBufLen = 2048;

		// See if an older installation of FW was detected:
		_TCHAR pszOlderInstallDir[kcchStringBufLen] = { 0 };
		DWORD cch = kcchStringBufLen;
		MsiGetProperty(hInstall, _T("OLDER_FW_INSTALL_PATH"), pszOlderInstallDir, &cch);
		if (pszOlderInstallDir[0] == 0)
		{
			// No installation of FW was detected, so set MAX_DBMIG_VER to a higher value
			// than could possibly exist, so that no migration scripts will be installed:
			MsiSetProperty(hInstall, _T("MAX_DBMIG_VER"), _T("999999"));
			return ERROR_SUCCESS;
		}

		// Get the path to the data migration scripts:
		_TCHAR pszOlderDataMigDir[kcchStringBufLen] = { 0 };
		cch = kcchStringBufLen;
		MsiGetProperty(hInstall, _T("OLDDATAMIGRATIONDIR"), pszOlderDataMigDir, &cch);
		// Remove final backslash, if it exists:
		if (pszOlderDataMigDir[_tcslen(pszOlderDataMigDir) - 1] == '\\')
			pszOlderDataMigDir[_tcslen(pszOlderDataMigDir) - 1] = 0;

		// Iterate over all known SQL migration script file names in order, to see which
		// is the first one that does not exist:
		unsigned int iMig;
		for (iMig = 200006; iMig < 200261; iMig++)
		{
			// Generated migration script file name:
			const size_t kFileNameLength = 19; // E.g. "200005To200006.sql" + terminating zero.
			_TCHAR pszFileName[kFileNameLength];
			_stprintf_s(pszFileName, kFileNameLength, _T("%uTo%u.sql"), (iMig-1), iMig);

			// See if the script file exists:
			if (!FileExists(pszOlderDataMigDir, pszFileName))
			{
				// It does not, so set installer variable MAX_DBMIG_VER to current script version minus 1:
				const size_t kDataMigVersion = 7;
				_TCHAR pszDataMigVersion[kDataMigVersion];
				_stprintf_s(pszDataMigVersion, kDataMigVersion, _T("%u"), (iMig-1));
				MsiSetProperty(hInstall, _T("MAX_DBMIG_VER"), pszDataMigVersion);
				return ERROR_SUCCESS;
			}
		}

	}
	catch (...)
	{
	}
	MsiSetProperty(hInstall, _T("MAX_DBMIG_VER"), _T("0"));
	return ERROR_SUCCESS;
}
