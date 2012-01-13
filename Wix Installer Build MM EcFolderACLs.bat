del EcFolderACLs.mm.wxs.log
del WixLinkEcFolderACLs.log
ProcessWixMMs.js EcFolderACLs.mm.wxs
"%WIX%bin\candle" -nologo -ext "%WIX%bin\WixUtilExtension.dll" EcFolderACLs.mm.wxs >EcFolderACLs.mm.wxs.log
"%WIX%bin\light" -nologo -sw1072 -sw1079 -ext "%WIX%bin\WixUtilExtension.dll" EcFolderACLs.mm.wixobj -out EcFolderACLs.msm >WixLinkEcFolderACLs.log
