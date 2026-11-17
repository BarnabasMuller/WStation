@echo off
rem =====================================================================
rem  WStation 3.0 - build egy paranccsal (PLAN-3.0 5. fejezet: a build.bat
rem  a dotnet alapu osszeallitas vekony burkoloja).
rem  Kell hozza: .NET 10 SDK (https://dotnet.microsoft.com/download).
rem  Eredmeny:  dist\WStation_setup_v<verzio>.exe   (a telepito)
rem             dist\SHA256SUMS.txt                (az ellenorzoosszege)
rem  A 2.x a release/2.x agon epul (ott a regi build.bat).
rem  A teljes leiras: packaging\windows\Telepito.proj
rem =====================================================================
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul || (echo HIBA: nem talalom a dotnet parancsot - telepitsd a .NET 10 SDK-t. & exit /b 1)

echo [1/4] Csomagok visszaallitasa (a zarolofajlok szerint)...
dotnet restore WStation.sln --locked-mode -nologo
if errorlevel 1 goto :err

echo [2/4] Forditas...
dotnet build WStation.sln -c Release --no-restore -nologo
if errorlevel 1 goto :err

echo [3/4] Tesztek...
dotnet test WStation.sln -c Release --no-build -nologo
if errorlevel 1 goto :err

echo [4/4] Telepito (programok + futtatokornyezet + inditok)...
dotnet msbuild packaging\windows\Telepito.proj -p:Configuration=Release -nologo -v:m
if errorlevel 1 goto :err

echo.
echo Kesz: a telepito a dist mappaban van.
dir /b dist\WStation_setup_v*.exe
exit /b 0

:err
echo.
echo HIBA - lasd fent.
exit /b 1
