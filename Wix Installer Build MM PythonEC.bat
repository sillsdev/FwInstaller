del PythonEC.mmp.wxs.log
del WixLinkPythonEC.log
ProcessWixMMs.js PythonEC.mm.wxs
"%WIX%bin\candle" -nologo PythonEC.mmp.wxs >PythonEC.mmp.wxs.log
"%WIX%bin\light" -nologo -sice:ICE03 PythonEC.mmp.wixobj -out PythonEC.msm >WixLinkPythonEC.log
