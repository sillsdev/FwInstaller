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

candle -nologo CopyFiles_No_TE.wxs -sw1044 >CopyFiles_No_TE.wxs.log
candle -nologo Features_No_TE.wxs >Features_No_TE.wxs.log
candle -nologo Fw_No_TE.wxs >Fw_No_TE.wxs.log
candle -nologo FwUI_No_TE.wxs >FwUI_No_TE.wxs.log
candle -nologo PatchCorrections_No_TE.wxs >PatchCorrections_No_TE.wxs.log
candle -nologo ProcessedFiles_No_TE.wxs -sw1044 >ProcessedFiles_No_TE.wxs.log
candle -nologo ProcessedAutoFiles_No_TE.wxs -sw1044 >ProcessedAutoFiles_No_TE.wxs.log
candle -nologo Registry_No_TE.wxs >Registry_No_TE.wxs.log
candle -nologo Shortcuts_No_TE.wxs >Shortcuts_No_TE.wxs.log

del WixLink_No_TE.log
light -nologo wixca.wixlib Actions.wixobj CopyFiles_No_TE.wixobj Environment.wixobj Features_No_TE.wixobj FW_No_TE.wixobj FwUI_No_TE.wixobj PatchCorrections_No_TE.wixobj ProcessedMergeModules.wixobj ProcessedFiles_No_TE.wixobj ProcessedAutoFiles_No_TE.wixobj Registry_No_TE.wixobj Shortcuts_No_TE.wixobj -out SetupFW_SE.msi >WixLink_No_TE.log