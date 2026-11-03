@echo off
setlocal EnableDelayedExpansion
set SCRIPT_DIR=%~dp0
if "%DOTNET%"=="" set DOTNET=dotnet
set OUTPUT=%SCRIPT_DIR%runtime
set TEMP_OUTPUT=%TEMP%\playserv-schema-tool-%RANDOM%-%RANDOM%

mkdir "%TEMP_OUTPUT%" >nul 2>&1
"%DOTNET%" publish "%SCRIPT_DIR%Source\PlayServ.Schema.Tool.csproj" ^
  --configuration Release ^
  --no-self-contained ^
  --output "%TEMP_OUTPUT%"

if errorlevel 1 (
  set BUILD_EXIT=!errorlevel!
  rmdir /s /q "%TEMP_OUTPUT%" >nul 2>&1
  exit /b !BUILD_EXIT!
)

if exist "%OUTPUT%" rmdir /s /q "%OUTPUT%"
mkdir "%OUTPUT%"
xcopy "%TEMP_OUTPUT%\*" "%OUTPUT%\" /e /i /q /y >nul
set COPY_EXIT=!errorlevel!
rmdir /s /q "%TEMP_OUTPUT%" >nul 2>&1
if not "!COPY_EXIT!"=="0" exit /b !COPY_EXIT!

echo PlayServ Schema Tool published to %OUTPUT%
