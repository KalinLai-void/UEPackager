@echo off
setlocal
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /win32icon:"%~dp0App.ico" /out:"%~dp0UE-Packaging-Automation.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "%~dp0Program.cs" "%~dp0PlatformOrderList.cs"
exit /b %errorlevel%

