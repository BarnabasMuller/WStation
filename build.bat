@echo off
rem =====================================================================
rem  Budaorsi idojaras - build egy paranccsal
rem  A Windows beepitett C# forditojat hasznalja, nem kell semmit telepiteni.
rem  Eredmeny:  dist\Idojaras_telepito_v2.1.exe   (ezt kell odaadni / elinditani)
rem             dist\bin\                    (a ket program kulon, teszteleshez)
rem =====================================================================
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (echo HIBA: nem talalom a C# forditot: %CSC% & exit /b 1)
if not exist dist\bin mkdir dist\bin
if not exist build mkdir build
set COMMON=/nologo /optimize+ /platform:x64 /win32manifest:src\app.manifest

set VREFS=/r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:lib\Microsoft.Web.WebView2.Core.dll /r:lib\Microsoft.Web.WebView2.WinForms.dll
set VRES=/resource:web\index.html,web/index.html /resource:web\app.js,web/app.js /resource:web\i18n.js,web/i18n.js /resource:web\app.css,web/app.css /resource:web\echarts.min.js,web/echarts.min.js /resource:web\calendar.js,web/calendar.js /resource:web\week.js,web/week.js /resource:web\params.js,web/params.js /resource:web\drought.js,web/drought.js /resource:web\explain.js,web/explain.js /resource:web\fx.js,web/fx.js /resource:web\calib.js,web/calib.js /resource:web\irrigation.js,web/irrigation.js /resource:web\water.js,web/water.js /resource:web\changes.js,web/changes.js /resource:web\insights.js,web/insights.js /resource:web\rain.js,web/rain.js /resource:web\wind.js,web/wind.js
set VRES=%VRES% /resource:web\ranges.js,web/ranges.js /resource:web\stats.js,web/stats.js /resource:web\run.js,web/run.js /resource:web\formula.js,web/formula.js /resource:web\diary.js,web/diary.js /resource:web\report.js,web/report.js /resource:web\settings.js,web/settings.js
set VRES=%VRES% /resource:lib\Microsoft.Web.WebView2.Core.dll,lib/Microsoft.Web.WebView2.Core.dll /resource:lib\Microsoft.Web.WebView2.WinForms.dll,lib/Microsoft.Web.WebView2.WinForms.dll /resource:lib\WebView2Loader.dll,lib/WebView2Loader.dll
rem nyelvi fajlok: minden web\lang\*.json magatol bekerul (uj nyelvhez eleg egy uj fajl, lasd docs\FORDITAS.md)
set LRES=
for %%f in (web\lang\*.json) do call :addlang %%~nxf
set VRES=%VRES% %LRES%

echo [1/5] Ikon...
"%CSC%" /nologo /target:winexe /platform:x64 /out:build\ikonkeszito.exe %VREFS% src\megjelenito\*.cs src\kozos\*.cs
if errorlevel 1 goto :err
start "" /wait build\ikonkeszito.exe --ikon build\app.ico
if not exist build\app.ico goto :err

echo [2/5] Megjelenito...
"%CSC%" %COMMON% /target:winexe /win32icon:build\app.ico /out:dist\bin\Megjelenito.exe %VREFS% %VRES% src\megjelenito\*.cs src\kozos\*.cs
if errorlevel 1 goto :err

echo [3/5] Letolto...
"%CSC%" %COMMON% /target:winexe /win32icon:build\app.ico /out:dist\bin\Letolto.exe /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll %LRES% src\letolto\*.cs src\kozos\IniMerge.cs src\kozos\Guard.cs src\kozos\Nyelv.cs
if errorlevel 1 goto :err

echo [4/5] Telepito...
set TRES=/resource:dist\bin\Letolto.exe,bin/Letolto.exe /resource:dist\bin\Megjelenito.exe,bin/Megjelenito.exe
set TRES=%TRES% /resource:docs\OLVASS_EL.txt,docs/OLVASS_EL.txt /resource:docs\Letolto_leiras.txt,docs/Letolto_leiras.txt /resource:docs\Megjelenito_leiras.txt,docs/Megjelenito_leiras.txt /resource:docs\VALTOZASOK.txt,docs/VALTOZASOK.txt
rem leirasok forditasai: docs\KOD\*.txt (uj nyelvhez eleg egy uj almappa)
for /d %%d in (docs\*) do for %%f in (%%d\*.txt) do call :adddoc %%~nxd %%~nxf
"%CSC%" %COMMON% /target:winexe /win32icon:build\app.ico /out:dist\Idojaras_telepito_v2.1.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll %TRES% %LRES% src\telepito\*.cs src\kozos\Utemezes.cs src\kozos\Guard.cs src\kozos\Nyelv.cs
if errorlevel 1 goto :err

echo [5/5] Takaritas...
rmdir /s /q build

echo.
echo Kesz: dist\Idojaras_telepito_v2.1.exe
exit /b 0

:err
echo.
echo FORDITASI HIBA - lasd fent.
exit /b 1

:addlang
rem a "_" kezdetu fajlok (pl. _sablon.json) nem kerulnek be
set "LN=%~1"
if "%LN:~0,1%"=="_" goto :eof
set LRES=%LRES% /resource:web\lang\%LN%,web/lang/%LN%
goto :eof

:adddoc
set TRES=%TRES% /resource:docs\%~1\%~2,docs/%~1/%~2
goto :eof
