@rem Compute the MD5 checksums of the relevant files, and adjust the output for WinMD5.
..\bin\md5sums.exe -u Flex_M.cab SetupFW.msi | ..\bin\sed.exe -e 's/\*/ /' > MD5SUM.md5
..\bin\md5sums.exe -u Flex_MSE.cab SetupFW_SE.msi | ..\bin\sed.exe -e 's/\*/ /' > MD5SUM_SE.md5
..\bin\md5sums.exe -u SetupEC.msi | ..\bin\sed.exe -e 's/\*/ /' > MD5SUM_EC.md5