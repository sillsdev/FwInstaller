del EC*.wxs.log
del EC*.wixobj

"%WIX%bin\candle" -nologo EC.wxs >EC.wxs.log
"%WIX%bin\candle" -nologo EcActions.wxs >EcActions.wxs.log
"%WIX%bin\candle" -nologo EcFeatures.wxs >EcFeatures.wxs.log
"%WIX%bin\candle" -nologo EcFiles.wxs -sw1044 >EcFiles.wxs.log
"%WIX%bin\candle" -nologo EcProcessedMergeModules.wxs >EcProcessedMergeModules.wxs.log
"%WIX%bin\candle" -nologo EcUI.wxs >EcUI.wxs.log

"EC WIX Installer Link.bat"