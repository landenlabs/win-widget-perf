@echo off

echo Build and install WinWidgetPerf (portable, compressed single-file)

:: Kill widget if running, so it can be re-built
taskkill /IM WinWidgetPerf.exe /F 2>nul

:: Portable self-contained single-file build:
::   --self-contained true                  bundle the .NET runtime (runs without an install)
::   PublishSingleFile                       emit one .exe
::   EnableCompressionInSingleFile           compress the bundled assemblies (smaller .exe)
::   IncludeNativeLibrariesForSelfExtract    pull native libraries inside the .exe
::   IncludeAllContentForSelfExtract         bundle Assets (logo) inside the .exe too
dotnet publish WinWidgetPerf.csproj -c Release -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:IncludeAllContentForSelfExtract=true

:: Make directory to hold the portable exe
mkdir c:\opt\bin\winwidgets 2>nul
xcopy /E /Y bin\Release\net10.0-windows10.0.17763.0\win-x64\publish\* c:\opt\bin\winwidgets\

echo start "" c:\opt\bin\winwidgets\WinWidgetPerf.exe > c:\opt\bin\WinWidgetPerf.bat
