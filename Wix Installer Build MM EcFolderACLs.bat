del EcFolderACLs.mm.wxs.log
del WixLinkEcFolderACLs.log
ProcessWixMMs.js EcFolderACLs.mm.wxs
candle -nologo EcFolderACLs.mm.wxs -sw1044 >EcFolderACLs.mm.wxs.log
light -nologo -sw1054 wixca.wixlib EcFolderACLs.mm.wixobj -out EcFolderACLs.msm >WixLinkEcFolderACLs.log
