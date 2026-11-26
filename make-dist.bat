@echo off
:: Build unsigned Microsoft Store MSIX packages (x64 + arm64) into dist-store\.
:: Usage: make-dist.bat [-Version 6.11.20]
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0make-dist.ps1" %*
exit /b %errorlevel%
