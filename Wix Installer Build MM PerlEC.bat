del PerlEC.mmp.wxs.log
del WixLinkPerlEC.log
ProcessWixMMs.js PerlEC.mm.wxs
"%WIX%bin\candle" -nologo -sw1006 -sw1086 PerlEC.mmp.wxs >PerlEC.mmp.wxs.log
"%WIX%bin\light" -nologo -sice:ICE03 PerlEC.mmp.wixobj -out PerlEC.msm >WixLinkPerlEC.log
