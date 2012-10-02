del WixLinkEC_40.log
del WixLinkSEC_EC_40.log
del WixLinkFW_EC_40.log
del "WixLinkMS KB908002 Fix.log"
del WixLinkEcFolderACLs.log
del WixLinkSetPath.log
del WixLinkICU040.log
del WixLinkSEC_ICU.log
del WixLinkFW_ICU.log
del WixLinkICUECHelp.log
del "WixLinkManaged Install Fix.log"
del WixLinkPerlEC.log
del WixLinkSEC_PerlEC.log
del WixLinkFW_PerlEC.log
del WixLinkPythonEC.log
del WixLinkSEC_PythonEC.log
del WixLinkFW_PythonEC.log
del WixLinkOoLingTools.log

WScript.exe ProcessWixMMs.js release EC_40.mm.wxs
WScript.exe ProcessWixMMs.js release SEC_EC_40.mm.wxs
WScript.exe ProcessWixMMs.js release FW_EC_40.mm.wxs
WScript.exe ProcessWixMMs.js release "MS KB908002 Fix.mm.wxs"
WScript.exe ProcessWixMMs.js release EcFolderACLs.mm.wxs
WScript.exe ProcessWixMMs.js release SetPath.mm.wxs
WScript.exe ProcessWixMMs.js release ICU040.mm.wxs
WScript.exe ProcessWixMMs.js release SEC_ICU40.mm.wxs
WScript.exe ProcessWixMMs.js release FW_ICU40.mm.wxs
WScript.exe ProcessWixMMs.js release ICUECHelp.mm.wxs
WScript.exe ProcessWixMMs.js release "Managed Install Fix.mm.wxs"
WScript.exe ProcessWixMMs.js release PerlEC.mm.wxs
WScript.exe ProcessWixMMs.js release SEC_PerlEC.mm.wxs
WScript.exe ProcessWixMMs.js release FW_PerlEC.mm.wxs
WScript.exe ProcessWixMMs.js release PythonEC.mm.wxs
WScript.exe ProcessWixMMs.js release SEC_PythonEC.mm.wxs
WScript.exe ProcessWixMMs.js release FW_PythonEC.mm.wxs
WScript.exe ProcessWixMMs.js release OO_Ling_Tools.mm.wxs

candle -nologo EC_40.mmp.wxs -sw1044 >EC_40.mmp.wxs.log
candle -nologo SEC_EC_40.mmp.wxs -sw1044 >SEC_EC_40.mmp.wxs.log
candle -nologo FW_EC_40.mmp.wxs -sw1044 >FW_EC_40.mmp.wxs.log
candle -nologo "MS KB908002 Fix.mmp.wxs" -sw1044 >"MS KB908002 Fix.mmp.wxs.log"
candle -nologo EcFolderACLs.mmp.wxs -sw1044 >EcFolderACLs.mmp.wxs.log
candle -nologo SetPath.mmp.wxs >SetPath.mmp.wxs.log
candle -nologo ICU040.mmp.wxs -sw1044 >ICU040.mmp.wxs.log
candle -nologo SEC_ICU40.mmp.wxs -sw1044 >SEC_ICU40.mmp.wxs.log
candle -nologo FW_ICU40.mmp.wxs -sw1044 >FW_ICU40.mmp.wxs.log
candle -nologo ICUECHelp.mmp.wxs -sw1044 >ICUECHelp.mmp.wxs.log
candle -nologo "Managed Install Fix.mmp.wxs" -sw1044 >"Managed Install Fix.mmp.wxs.log"
candle -nologo PerlEC.mmp.wxs -sw1044 >PerlEC.mmp.wxs.log
candle -nologo SEC_PerlEC.mmp.wxs -sw1044 >SEC_PerlEC.mmp.wxs.log
candle -nologo FW_PerlEC.mmp.wxs -sw1044 >FW_PerlEC.mmp.wxs.log
candle -nologo PythonEC.mmp.wxs -sw1044 >PythonEC.mmp.wxs.log
candle -nologo SEC_PythonEC.mmp.wxs -sw1044 >SEC_PythonEC.mmp.wxs.log
candle -nologo FW_PythonEC.mmp.wxs -sw1044 >FW_PythonEC.mmp.wxs.log
candle -nologo OO_Ling_Tools.mmp.wxs -sw1044 >OO_Ling_Tools.mmp.wxs.log

light -nologo wixca.wixlib EC_40.mmp.wixobj -out EC_40.msm >WixLinkEC_40.log
light -nologo SEC_EC_40.mmp.wixobj -out SEC_EC_40.msm >WixLinkSEC_EC_40.log
light -nologo FW_EC_40.mmp.wixobj -out FW_EC_40.msm >WixLinkFW_EC_40.log
light -nologo "MS KB908002 Fix.mmp.wixobj" -out "MS KB908002 Fix.msm" >"WixLinkMS KB908002 Fix.log"
light -nologo wixca.wixlib EcFolderACLs.mmp.wixobj -out EcFolderACLs.msm >WixLinkEcFolderACLs.log
light -nologo SetPath.mmp.wixobj -out SetPath.msm >WixLinkSetPath.log
light -nologo wixca.wixlib ICU040.mmp.wixobj -out ICU040.msm >WixLinkICU040.log
light -nologo SEC_ICU40.mmp.wixobj -out SEC_ICU40.msm >WixLinkSEC_ICU.log
light -nologo FW_ICU40.mmp.wixobj -out FW_ICU40.msm >WixLinkFW_ICU.log
light -nologo wixca.wixlib ICUECHelp.mmp.wixobj -out ICUECHelp.msm >WixLinkICUECHelp.log
light -nologo "Managed Install Fix.mmp.wixobj" -out "Managed Install Fix.msm" >"WixLinkManaged Install Fix.log"
light -nologo wixca.wixlib PerlEC.mmp.wixobj -out PerlEC.msm >WixLinkPerlEC.log
light -nologo SEC_PerlEC.mmp.wixobj -out SEC_PerlEC.msm >WixLinkSEC_PerlEC.log
light -nologo FW_PerlEC.mmp.wixobj -out FW_PerlEC.msm >WixLinkFW_PerlEC.log
light -nologo wixca.wixlib PythonEC.mmp.wixobj -out PythonEC.msm >WixLinkPythonEC.log
light -nologo SEC_PythonEC.mmp.wixobj -out SEC_PythonEC.msm >WixLinkSEC_PythonEC.log
light -nologo FW_PythonEC.mmp.wixobj -out FW_PythonEC.msm >WixLinkFW_PythonEC.log
light -nologo OO_Ling_Tools.mmp.wixobj -out OO_Ling_Tools.msm >WixLinkOoLingTools.log
