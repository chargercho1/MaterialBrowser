@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe

rem Default build: ~700 KB, the engine is taken from the gecko\ folder next to
rem the exe. Set MBR_EMBED=1 to also pack that runtime into the exe, so the exe
rem alone is enough to run. Note that the single-file build is ~140 MB and
rem Smart App Control refuses to launch an unsigned binary that large.
if exist gecko.zip if "%MBR_EMBED%"=="1" (
  copy /y build.rsp build.full.rsp >nul
  echo /resource:gecko.zip,MaterialBrowser.gecko.zip>>build.full.rsp
  "%CSC%" @build.full.rsp
  echo BUILT: single file with the engine inside
  goto installer
)

"%CSC%" @build.rsp
echo BUILT: engine read from the gecko\ folder

:installer
rem Setup.exe is the installer, and the same program renamed to Uninstall.exe
rem inside the installed folder is the uninstaller.
"%CSC%" -nologo -target:winexe -out:Setup.exe -r:System.Windows.Forms.dll Install.cs
echo BUILT: Setup.exe (installer and uninstaller)
goto done

:done
echo EXIT: %ERRORLEVEL%
endlocal