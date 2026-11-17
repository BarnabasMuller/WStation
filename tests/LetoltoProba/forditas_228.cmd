@echo off
rem A 2.2.8 letoltojenek es a probainditonak a forditasa egy ideiglenes mappaba (PLAN-3.0 1.7, osszevetes a 2.2.8-cal).
rem A letolto a release/2.x agrol, ugyanugy, mint a regi build.bat [3/5] lepese (.NET Framework 4.8, a Windows beepitett csc.exe-je);
rem a probaindito forrasa ugyanaz a LetoltoProba.cs, amelyik a 3.0-s tesztekben is fut.
rem Hasznalat (a repo gyokerebol): tests\LetoltoProba\forditas_228.cmd ^<celmappa^>
rem Utana pl.: ^<celmappa^>\LetoltoProba.exe ^<celmappa^>\Letolto.exe ok keresek.txt --nowait --mappa ^<adatmappa^>
setlocal
cd /d "%~dp0..\.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (echo HIBA: nem talalom a C# forditot: %CSC% & exit /b 1)
if "%~1"=="" (echo Hasznalat: tests\LetoltoProba\forditas_228.cmd ^<celmappa^> & exit /b 1)
set "CEL=%~f1"
if exist "%CEL%\forras" rmdir /s /q "%CEL%\forras"
mkdir "%CEL%\forras"
git archive --format=tar release/2.x src/letolto src/kozos src/app.manifest web/lang > "%CEL%\forras.tar" || exit /b 1
tar -x -f "%CEL%\forras.tar" -C "%CEL%\forras" || exit /b 1
del "%CEL%\forras.tar"
set LRES=
for %%f in ("%CEL%\forras\web\lang\*.json") do call :addlang "%%~f" %%~nxf
"%CSC%" /nologo /optimize+ /platform:x64 /win32manifest:"%CEL%\forras\src\app.manifest" /target:winexe /out:"%CEL%\Letolto.exe" /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll %LRES% "%CEL%\forras\src\letolto\*.cs" "%CEL%\forras\src\kozos\IniMerge.cs" "%CEL%\forras\src\kozos\Guard.cs" "%CEL%\forras\src\kozos\Nyelv.cs" || exit /b 1
"%CSC%" /nologo /optimize+ /platform:x64 /target:exe /out:"%CEL%\LetoltoProba.exe" tests\LetoltoProba\LetoltoProba.cs || exit /b 1
echo Kesz: %CEL%\Letolto.exe, %CEL%\LetoltoProba.exe
exit /b 0

:addlang
rem a "_" kezdetu fajlok (pl. _sablon.json) nem kerulnek be - mint a build.bat-ban
set "LN=%~2"
if "%LN:~0,1%"=="_" goto :eof
set LRES=%LRES% /resource:"%~1",web/lang/%LN%
goto :eof
