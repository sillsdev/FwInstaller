wscript StripOutTE.js

del CopyFiles_No_TE.wixobj
del Features_No_TE.wixobj
del Fw_No_TE.wixobj
del FwUI_No_TE.wixobj
del PatchCorrections_No_TE.wixobj
del ProcessedFiles_No_TE.wixobj
del ProcessedAutoFiles_No_TE.wixobj
del Registry_No_TE.wixobj
del Shortcuts_No_TE.wixobj

"%WIX%bin\candle" -nologo CopyFiles_No_TE.wxs -sw1044 >CopyFiles_No_TE.wxs.log
"%WIX%bin\candle" -nologo Features_No_TE.wxs >Features_No_TE.wxs.log
"%WIX%bin\candle" -nologo Fw_No_TE.wxs >Fw_No_TE.wxs.log
"%WIX%bin\candle" -nologo FwUI_No_TE.wxs >FwUI_No_TE.wxs.log
"%WIX%bin\candle" -nologo PatchCorrections_No_TE.wxs >PatchCorrections_No_TE.wxs.log
"%WIX%bin\candle" -nologo -ext "%WIX%bin\WixUtilExtension.dll" ProcessedFiles_No_TE.wxs -sw1044 >ProcessedFiles_No_TE.wxs.log
"%WIX%bin\candle" -nologo ProcessedAutoFiles_No_TE.wxs -sw1044 >ProcessedAutoFiles_No_TE.wxs.log
"%WIX%bin\candle" -nologo Registry_No_TE.wxs >Registry_No_TE.wxs.log
"%WIX%bin\candle" -nologo Shortcuts_No_TE.wxs >Shortcuts_No_TE.wxs.log

del WixLink_No_TE.log
"%WIX%bin\light" -ext "%WIX%bin\WixUtilExtension.dll" -sw1055 -sw1056 -sice:ICE03;ICE47;ICE48;ICE57;ICE60;ICE69;ICE82;ICE83 -nologo Actions.wixobj CopyFiles_No_TE.wixobj Environment.wixobj Features_No_TE.wixobj FW_No_TE.wixobj FwUI_No_TE.wixobj PatchCorrections_No_TE.wixobj ProcessedMergeModules.wixobj ProcessedFiles_No_TE.wixobj ProcessedAutoFiles_No_TE.wixobj Registry_No_TE.wixobj Shortcuts_No_TE.wixobj -out SetupFW_SE.msi >WixLink_No_TE.log