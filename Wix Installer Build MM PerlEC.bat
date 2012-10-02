del PerlEC.mmp.wxs.log
del WixLinkPerlEC.log
ProcessWixMMs.js PerlEC.mm.wxs
candle -nologo -sw1044 PerlEC.mmp.wxs >PerlEC.mmp.wxs.log
light -nologo PerlEC.mmp.wixobj -out PerlEC.msm >WixLinkPerlEC.log
