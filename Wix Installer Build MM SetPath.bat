del SetPath.mmp.wxs.log
del WixLinkSetPath.log
ProcessWixMMs.js SetPath.mm.wxs
"%WIX%bin\candle" -nologo -sw1006 -sw1086 SetPath.mmp.wxs >SetPath.mmp.wxs.log
"%WIX%bin\light" -nologo -sw1079 SetPath.mmp.wixobj -out SetPath.msm >WixLinkSetPath.log
