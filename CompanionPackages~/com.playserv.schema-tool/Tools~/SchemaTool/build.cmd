@echo off
setlocal
set SCRIPT_DIR=%~dp0
if "%DOTNET%"=="" set DOTNET=dotnet

"%DOTNET%" publish "%SCRIPT_DIR%Source\PlayServ.Schema.Tool.csproj" ^
  --configuration Release ^
  --no-self-contained ^
  --output "%SCRIPT_DIR%runtime"

if errorlevel 1 exit /b %errorlevel%
echo PlayServ Schema Tool published to %SCRIPT_DIR%runtime
