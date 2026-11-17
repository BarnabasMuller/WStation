@echo off
rem =====================================================================
rem  Golden rogzites (docs/dev/PLAN-3.0.md 14.2): a 2.2.8 valaszai a generalt
rem  mintaadaton -> tests\golden. Reszletek: tools\golden\README.md
rem  Kell hozza: Windows (.NET Framework 4.8 fordito) es a .NET 10 SDK.
rem =====================================================================
setlocal
cd /d "%~dp0..\.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (echo HIBA: nem talalom a C# forditot: %CSC% & exit /b 1)
set "WORK=%TEMP%\wstation-golden"
if exist "%WORK%" rmdir /s /q "%WORK%"
mkdir "%WORK%"

echo [1/4] Mintaadat...
dotnet run --project tools\adatgen -c Release -- "%WORK%\minta"
if errorlevel 1 goto :err
dotnet run --project tools\adatgen -c Release --no-build -- "%WORK%\minta_onteszt"
if errorlevel 1 goto :err

echo [2/4] Rogzito forditasa a 2.2.8 forrasaibol...
rem ugyanazok a kapcsolok es hivatkozasok, mint a build.bat-ban; a nyelvi fajlok beagyazva (a "_" kezdetuek nelkul)
set REFS=/r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:lib\Microsoft.Web.WebView2.Core.dll /r:lib\Microsoft.Web.WebView2.WinForms.dll
set LRES=
for %%f in (web\lang\*.json) do call :addlang %%~nxf
"%CSC%" /nologo /nowarn:0219,0649 /optimize+ /platform:x64 /target:exe /main:Idojaras.GoldenRogzito /out:"%WORK%\rogzito.exe" %REFS% %LRES% src\megjelenito\*.cs src\kozos\*.cs tools\golden\Rogzito.cs
if errorlevel 1 goto :err

echo [3/4] Valaszok rogzitese...
"%WORK%\rogzito.exe" "%WORK%\minta" tests\golden
if errorlevel 1 goto :err

echo [4/4] A 2.2.8 ontesztje a mintaadaton...
"%WORK%\rogzito.exe" --onteszt "%WORK%\minta_onteszt" tests\golden\onteszt_2.2.8.txt
if errorlevel 1 goto :err

echo.
echo Kesz: tests\golden
exit /b 0

:err
echo.
echo HIBA - lasd fent.
exit /b 1

:addlang
set "LN=%~1"
if "%LN:~0,1%"=="_" goto :eof
set LRES=%LRES% /resource:web\lang\%LN%,web/lang/%LN%
goto :eof
