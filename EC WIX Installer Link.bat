del EcWixLink.log

"%WIX%bin\light" -sw1055 -sw1056 -sice:ICE03;ICE08;ICE30;ICE47;ICE48;ICE57;ICE60;ICE69;ICE82;ICE83 -nologo Ec.wixobj EcActions.wixobj EcFeatures.wixobj EcFiles.wixobj EcProcessedMergeModules.wixobj EcUI.wixobj -out SetupEC.msi >EcWixLink.log