del *.wxs.log
del *.wixobj

candle -nologo Actions.wxs >Actions.wxs.log
candle -nologo CopyFiles.wxs -sw1044 >CopyFiles.wxs.log
candle -nologo Environment.wxs >Environment.wxs.log
candle -nologo Features.wxs >Features.wxs.log
candle -nologo FW.wxs >FW.wxs.log
candle -nologo FwUI.wxs >FwUI.wxs.log
candle -nologo PatchCorrections.wxs >PatchCorrections.wxs.log
candle -nologo ProcessedAutoFiles.wxs -sw1044 >ProcessedAutoFiles.wxs.log
candle -nologo ProcessedFiles.wxs -sw1044 >ProcessedFiles.wxs.log
candle -nologo ProcessedMergeModules.wxs >ProcessedMergeModules.wxs.log
candle -nologo Registry.wxs >Registry.wxs.log
candle -nologo Shortcuts.wxs >Shortcuts.wxs.log

"WIX Installer Link.bat"