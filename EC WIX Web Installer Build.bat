WScript.exe EcProcessMergeModules.js web
"%WIX%bin\candle" -nologo EcWebProcessedMergeModules.wxs >EcWebProcessedMergeModules.wxs.log
"%WIX%bin\light" -sw1055 -sw1056 -sice:ICE03;ICE08;ICE30;ICE47;ICE48;ICE57;ICE60;ICE69;ICE82;ICE83 -nologo Ec.wixobj EcActions.wixobj EcFeatures.wixobj EcUI.wixobj EcWebProcessedMergeModules.wixobj EcFiles.wixobj -out WebSetupEC.msi >EcWixWebLink.log