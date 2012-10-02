del ICU040.mmp.wxs.log
del WixLinkICU040.log
ProcessWixMMs.js ICU040.mm.wxs
candle -nologo ICU040.mmp.wxs -sw1044 >ICU040.mmp.wxs.log
light -nologo -sw1054 wixca.wixlib ICU040.mmp.wixobj -out ICU040.msm >WixLinkICU040.log
