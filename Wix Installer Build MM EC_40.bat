del EC_40.mmp.wxs.log
del WixLinkEC_40.log
ProcessWixMMs.js EC_40.mm.wxs
candle -nologo -sw1044 EC_40.mmp.wxs >EC_40.mmp.wxs.log
light -nologo -sw1054 wixca.wixlib EC_40.mmp.wixobj -out EC_40.msm >WixLinkEC_40.log
