@echo off
rem Builds the GUI wow-faction-lock-retrier.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /optimize+ /target:winexe /platform:anycpu /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%~dp0wow-faction-lock-retrier.exe" "%~dp0src\WowFactionLockRetrier.cs"
