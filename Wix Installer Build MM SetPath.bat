del SetPath.mmp.wxs.log
del WixLinkSetPath.log
ProcessWixMMs.js SetPath.mm.wxs
candle -nologo SetPath.mmp.wxs >SetPath.mmp.wxs.log
light -nologo SetPath.mmp.wixobj -out SetPath.msm >WixLinkSetPath.log
