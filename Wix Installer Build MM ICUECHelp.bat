del ICUECHelp.mmp.wxs.log
del WixLinkICUECHelp.log
ProcessWixMMs.js ICUECHelp.mm.wxs
"%WIX%bin\candle" -nologo ICUECHelp.mmp.wxs -sw1044 >ICUECHelp.mmp.wxs.log
"%WIX%bin\light" -nologo ICUECHelp.mmp.wixobj -out ICUECHelp.msm >WixLinkICUECHelp.log
