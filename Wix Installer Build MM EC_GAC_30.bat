del EC_GAC_30.mmp.wxs.log
del WixLinkEC_GAC_30.log
ProcessWixMMs.js EC_GAC_30.mm.wxs
candle -nologo -sw1044 EC_GAC_30.mmp.wxs >EC_GAC_30.mmp.wxs.log
light -nologo -sw1054 wixca.wixlib EC_GAC_30.mmp.wixobj -out EC_GAC_30.msm >WixLinkEC_GAC_30.log
