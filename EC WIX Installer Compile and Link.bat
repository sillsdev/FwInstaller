del EC*.wxs.log
del EC*.wixobj

candle -nologo EC.wxs >EC.wxs.log
candle -nologo EcActions.wxs >EcActions.wxs.log
candle -nologo EcFeatures.wxs >EcFeatures.wxs.log
candle -nologo EcUI.wxs >EcUI.wxs.log
candle -nologo EcFiles.wxs -sw1044 >EcFiles.wxs.log
candle -nologo EcProcessedMergeModules.wxs >EcProcessedMergeModules.wxs.log

"EC WIX Installer Link.bat"