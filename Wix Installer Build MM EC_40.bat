del EC_40.mmp.wxs.log
del WixLinkEC_40.log
ProcessWixMMs.js EC_40.mm.wxs
"%WIX%bin\candle" -nologo -sw1006 -sw1086 -ext "%WIX%bin\WixUtilExtension.dll" EC_40.mmp.wxs >EC_40.mmp.wxs.log
"%WIX%bin\light" -nologo -sw1072 -ext "%WIX%bin\WixUtilExtension.dll" EC_40.mmp.wixobj -out EC_40.msm >WixLinkEC_40.log
