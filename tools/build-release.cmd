@echo off
rem --------------------------------------------------------------------------
rem  Builds everything a new machine needs: the application, then the installer
rem  with that application inside it.
rem
rem  Run from the root of the repository:  tools\build-release.cmd
rem
rem  Out comes release\acvayrecords-setup.exe: one file, nothing to install
rem  first, and no administrator rights needed to run it.
rem --------------------------------------------------------------------------

setlocal

set DOTNET=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe
if not exist "%DOTNET%" set DOTNET=dotnet

echo.
echo === 1 of 3  building the application ===
"%DOTNET%" publish src\RecordKeeper.App\RecordKeeper.App.csproj -c Release -o dist --nologo
if errorlevel 1 goto failed

echo.
echo === 2 of 3  putting it inside the installer ===
if not exist "src\RecordKeeper.Setup\payload" mkdir "src\RecordKeeper.Setup\payload"
copy /y "dist\acvayrecords.exe" "src\RecordKeeper.Setup\payload\acvayrecords.exe" >nul
if errorlevel 1 goto failed

echo.
echo === 3 of 3  building the installer ===
"%DOTNET%" publish src\RecordKeeper.Setup\RecordKeeper.Setup.csproj -c Release -o release --nologo
if errorlevel 1 goto failed

echo.
echo Done. The installer to hand out is:
dir /b release\acvayrecords-setup.exe
exit /b 0

:failed
echo.
echo BUILD FAILED
exit /b 1
