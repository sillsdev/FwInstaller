del SEC_ICU40.mmp.wxs.log
del WixLinkSEC_ICU40.log
ProcessWixMMs.js SEC_ICU40.mm.wxs
"%WIX%bin\candle" -nologo SEC_ICU40.mmp.wxs >SEC_ICU40.mmp.wxs.log
"%WIX%bin\light" -nologo SEC_ICU40.mmp.wixobj -out SEC_ICU40.msm >WixLinkSEC_ICU40.log
