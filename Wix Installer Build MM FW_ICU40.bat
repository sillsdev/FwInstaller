del FW_ICU40.mmp.wxs.log
del WixLinkFW_ICU40.log
ProcessWixMMs.js FW_ICU40.mm.wxs
"%WIX%bin\candle" -nologo FW_ICU40.mmp.wxs >FW_ICU40.mmp.wxs.log
"%WIX%bin\light" -nologo FW_ICU40.mmp.wixobj -out FW_ICU40.msm >WixLinkFW_ICU40.log
