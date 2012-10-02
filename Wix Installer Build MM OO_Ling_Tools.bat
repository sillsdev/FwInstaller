del OO_Ling_Tools.mmp.wxs.log
del WixLinkOO_Ling_Tools.log
ProcessWixMMs.js OO_Ling_Tools.mm.wxs
"%WIX%bin\candle" -nologo OO_Ling_Tools.mmp.wxs >OO_Ling_Tools.mmp.wxs.log
"%WIX%bin\light" -nologo OO_Ling_Tools.mmp.wixobj -out OO_Ling_Tools.msm >WixLinkOO_Ling_Tools.log
