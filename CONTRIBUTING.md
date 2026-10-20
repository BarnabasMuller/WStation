# Contributing

Thanks for your interest! This is a small hobby project; issues and pull requests are welcome.
Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) first.

## Getting started

1. Clone the repository on Windows 10/11 (the build uses the .NET Framework 4.8 compiler that ships with Windows).
2. Run `build.bat`. It produces `dist\bin\Megjelenito.exe`, `dist\bin\Letolto.exe` and the installer.
3. You need measurements to see anything: either install the app and run the Downloader with your own
   Weather Underground API key and station, or point the Viewer at an existing installed folder:
   ```
   dist\bin\Megjelenito.exe --bongeszo --web web --mappa <installed folder>
   ```
   `--bongeszo` opens the UI in your browser, `--web web` serves the UI straight from the source folder, so
   JavaScript/CSS changes only need a browser reload. C# changes need `build.bat` and a restart.
4. Run the self-test before sending changes:
   ```
   dist\bin\Megjelenito.exe --teszt --mappa <folder with data\>
   ```

## Language of the code

Identifiers, comments, log messages and the UI source text are **Hungarian**. Please keep new code consistent
with its surroundings. [docs/GLOSSARY.md](docs/GLOSSARY.md) lists the common terms. Comments in English are fine
in new files if you don't speak Hungarian – but UI strings must be written in Hungarian and then added to the
translation files (see below).

## Conventions

- **C# 5 only** (the built-in `csc.exe`): no `$""` interpolation, no `?.`, no expression-bodied members, no tuples syntax.
- **Encodings:** C# and `.txt` guides are UTF-8 **with BOM** (the compiler and Notepad rely on it);
  web files and JSON are UTF-8 without BOM; `build.bat` must keep **CRLF** line endings.
  `.editorconfig` and `.gitattributes` describe this.
- **File writes go through `Guard`** (`src/kozos/Guard.cs`). Never call `File.WriteAllText` etc. directly,
  except for a location the user picked in a dialog.
- **Metric inside, convert at the edge:** compute in °C, mm, km/h, hPa; convert only for display with the
  helpers in `web/app.js` (`tconv`, `rtext`, `wtext`, `ptext`, `qtext`…) or in the export.
- **Validate user input on both sides:** in the UI (`params.js`: `RULES`, `checkField`) and on the server
  (`Drought.cs` `Validate`, `Settings.Check`, `Calib.Validate`). Invalid values must not be saved.
- **Settings changes** that users make in the UI should be logged with `logChange` / `ChangeLog.Add` so they can be undone.
- New web files must be added to `index.html` **and** to the resource list in `build.bat`.
- New settings in `megjelenito.ini` or `beallitasok.ini` go into the template string; `IniMerge` adds them to
  existing installations on update.

## Adding UI text

1. Write the text in Hungarian in the JS/HTML/C# source.
2. Run `powershell -ExecutionPolicy Bypass -File tools\forditas_kulcsok.ps1 -Out <folder>`; it lists the
   untranslated phrases per language and refreshes `web/lang/_sablon.json`.
3. Add the translations to `web/lang/en.json`, `de.json`, `es.json`.

See [docs/TRANSLATING.md](docs/TRANSLATING.md) for how phrase matching works.

## Tests

`src/megjelenito/SelfTest.cs` contains the checks run by `--teszt`. Add a check with `Ok(condition, title, detail)`.
Checks that depend on the developer's private sample data are guarded by `if (sample)` and are skipped otherwise.

## Privacy

Never commit measurement data, `beallitasok.ini`, API keys, station IDs or logs. `.gitignore` excludes the usual
places (`data/`, `appdata_*`, `minta_adat/`, `dist/`).

## Licence

By contributing you agree that your contributions are licensed under the GNU GPL v3.0 (see [LICENSE](LICENSE)).
