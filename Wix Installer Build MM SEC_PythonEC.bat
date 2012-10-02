del SEC_PythonEC.mmp.wxs.log
del WixLinkSEC_PythonEC.log
ProcessWixMMs.js SEC_PythonEC.mm.wxs
"%WIX%bin\candle" -nologo SEC_PythonEC.mmp.wxs >SEC_PythonEC.mmp.wxs.log
"%WIX%bin\light" -nologo SEC_PythonEC.mmp.wixobj -out SEC_PythonEC.msm >WixLinkSEC_PythonEC.log
