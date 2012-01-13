del *.wxs.log
del *.wixobj

"%WIX%bin\candle" -nologo Actions.wxs >Actions.wxs.log
"%WIX%bin\candle" -nologo CopyFiles.wxs -sw1044 >CopyFiles.wxs.log
"%WIX%bin\candle" -nologo Environment.wxs >Environment.wxs.log
"%WIX%bin\candle" -nologo Features.wxs >Features.wxs.log
"%WIX%bin\candle" -nologo FW.wxs >FW.wxs.log
"%WIX%bin\candle" -nologo FwUI.wxs >FwUI.wxs.log
"%WIX%bin\candle" -nologo PatchCorrections.wxs >PatchCorrections.wxs.log
"%WIX%bin\candle" -nologo ProcessedAutoFiles.wxs -sw1044 >ProcessedAutoFiles.wxs.log
"%WIX%bin\candle" -nologo -ext "%WIX%bin\WixUtilExtension.dll" ProcessedFiles.wxs -sw1044 >ProcessedFiles.wxs.log
"%WIX%bin\candle" -nologo ProcessedMergeModules.wxs >ProcessedMergeModules.wxs.log
"%WIX%bin\candle" -nologo Registry.wxs >Registry.wxs.log
"%WIX%bin\candle" -nologo Shortcuts.wxs >Shortcuts.wxs.log

"WIX Installer Link.bat"