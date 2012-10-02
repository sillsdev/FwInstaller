del PythonEC.mmp.wxs.log
del WixLinkPythonEC.log
ProcessWixMMs.js PythonEC.mm.wxs
candle -nologo -sw1044 PythonEC.mmp.wxs >PythonEC.mmp.wxs.log
light -nologo PythonEC.mmp.wixobj -out PythonEC.msm >WixLinkPythonEC.log
