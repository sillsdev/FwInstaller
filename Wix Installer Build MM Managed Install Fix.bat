del "Managed Install Fix.mmp.wxs.log"
del "WixLinkManaged Install Fix.log"
ProcessWixMMs.js "Managed Install Fix.mm.wxs"
"%WIX%bin\candle" -nologo -sw1006 -sw1086 "Managed Install Fix.mmp.wxs" >"Managed Install Fix.mmp.wxs.log"
"%WIX%bin\light" -nologo "Managed Install Fix.mmp.wixobj" -out "Managed Install Fix.msm" >"WixLinkManaged Install Fix.log"
