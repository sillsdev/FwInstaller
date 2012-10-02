del SEC_EC_40.mmp.wxs.log
del WixLinkSEC_EC_40.log
ProcessWixMMs.js SEC_EC_40.mm.wxs
"%WIX%bin\candle" -nologo SEC_EC_40.mmp.wxs >SEC_EC_40.mmp.wxs.log
"%WIX%bin\light" -nologo SEC_EC_40.mmp.wixobj -out SEC_EC_40.msm >WixLinkSEC_EC_40.log
