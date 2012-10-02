del ICUECHelp.mmp.wxs.log
del WixLinkICUECHelp.log
ProcessWixMMs.js ICUECHelp.mm.wxs
candle -nologo ICUECHelp.mmp.wxs -sw1044 >ICUECHelp.mmp.wxs.log
light -nologo wixca.wixlib ICUECHelp.mmp.wixobj -out ICUECHelp.msm >WixLinkICUECHelp.log
