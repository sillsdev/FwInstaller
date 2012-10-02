del ICU040.mmp.wxs.log
del WixLinkICU040.log
ProcessWixMMs.js ICU040.mm.wxs
"%WIX%bin\candle" -nologo -sw1006 -sw1086 -ext "%WIX%bin\WixUtilExtension.dll" ICU040.mmp.wxs >ICU040.mmp.wxs.log
"%WIX%bin\light" -nologo -sw1072 -ext "%WIX%bin\WixUtilExtension.dll" ICU040.mmp.wixobj -out ICU040.msm >WixLinkICU040.log
