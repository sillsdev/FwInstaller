del SEC_PerlEC.mmp.wxs.log
del WixLinkSEC_PerlEC.log
ProcessWixMMs.js SEC_PerlEC.mm.wxs
"%WIX%bin\candle" -nologo SEC_PerlEC.mmp.wxs >SEC_PerlEC.mmp.wxs.log
"%WIX%bin\light" -nologo SEC_PerlEC.mmp.wixobj -out SEC_PerlEC.msm >WixLinkSEC_PerlEC.log
