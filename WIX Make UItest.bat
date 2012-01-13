"%WIX%bin\candle" -nologo FwUI.wxs
"%WIX%bin\candle" -nologo UItest.wxs
"%WIX%bin\light" FwUI.wixobj UItest.wixobj -out UiTest.msi