REM Shared Merge Modules:
call "Wix Installer Build MM EC_40.bat"
call "Wix Installer Build MM EcFolderACLs.bat"
call "Wix Installer Build MM ICU040.bat"
call "Wix Installer Build MM ICUECHelp.bat"
call "Wix Installer Build MM Managed Install Fix.bat"
call "Wix Installer Build MM PerlEC.bat"
call "Wix Installer Build MM PythonEC.bat"
call "Wix Installer Build MM SetPath.bat"

REM FW-only Merge Modules:
call "Wix Installer Build MM FW_EC_40.bat"
call "Wix Installer Build MM FW_ICU40.bat"
call "Wix Installer Build MM FW_PerlEC.bat"
call "Wix Installer Build MM FW_PythonEC.bat"

REM SEC-only Merge Modules:
call "Wix Installer Build MM MS KB908002 Fix.bat"
call "Wix Installer Build MM OO_Ling_Tools.bat"
call "Wix Installer Build MM SEC_EC_40.bat"
call "Wix Installer Build MM SEC_ICU40.bat"
call "Wix Installer Build MM SEC_PerlEC.bat"
call "Wix Installer Build MM SEC_PythonEC.bat"