del FW_EC_40.mmp.wxs.log
del WixLinkFW_EC_40.log
ProcessWixMMs.js FW_EC_40.mm.wxs
"%WIX%bin\candle" -nologo FW_EC_40.mmp.wxs >FW_EC_40.mmp.wxs.log
"%WIX%bin\light" -nologo FW_EC_40.mmp.wixobj -out FW_EC_40.msm >WixLinkFW_EC_40.log
