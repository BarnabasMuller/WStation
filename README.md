# WStation – home weather station downloader & viewer

Download and analyse the weather data of a home weather station. Uses the
[Weather Underground PWS API](https://www.wunderground.com/member/api-keys) to download the data,
and shows it in an offline Windows app with charts, statistics and gardening tools.

*Magyarul: [README.hu.md](README.hu.md)*

The project was built for a station in Budaörs, Hungary ("Budaörsi időjárás" = "Budaörs weather"),
but works with any Weather Underground personal weather station. The user interface is available in
**Hungarian, English, German and Spanish**, and new languages can be added with a single JSON file.

## What is in the box

Three Windows programs, built from this repository with one command:

| Program | File | What it does |
|---|---|---|
| **Downloader** | `Letöltő.exe` | Downloads the station's data from the WU PWS API into CSV files. Decides itself what is worth querying (current conditions, daily summary, missing days in 5-minute detail), keeps a log, respects the API limits, can run from Windows Task Scheduler and sends Windows notifications (frost, pressure drop, strong wind, cloudburst, heat). |
| **Viewer** | `Megjelenítő.exe` | A local app (WebView2 + Apache ECharts) that works without internet: timeline with six linked charts, calendar heatmap, drought module, rain and wind statistics, records, reports. |
| **Installer** | `WStation_setup_v2.2.x.exe` | A single exe containing both programs and the guides. Handles fresh installs, updates, old folder layouts and running programs; suggests the language from Windows. |

### Viewer highlights

- **Timeline** – temperature/dew point, humidity, pressure, wind/gusts, rain, solar/UV; suspicious values and data gaps marked; front and thunderstorm detector; a band for your own formulas.
- **Drought & watering** – FAO-56 Penman–Monteith evapotranspiration and a soil water balance per plant type (lawn, shrubs, pots, trees), watering advice, saved irrigation programs, rain gauge calibration, water use and water demand with costs.
- **Fire danger** – Canadian Fire Weather Index (FFMC, DMC, DC, ISI, BUI, FWI) with a step-by-step "today's calculation" table; vapour pressure deficit; growing degree days.
- **Statistics** – any number of date ranges (combined or side by side), records, month comparison, hour × day heatmaps, overlaid periods.
- **For sailors** – wind in km/h, m/s, knots, mph or fractional Beaufort; wind rose; windy afternoons.
- **Every computed value has an ƒx button** showing the formula, its parameters with standard values, a reset button and named presets.
- Units: °C/°F, mm/inch, km/h, m/s, knots, mph, Beaufort, hPa/inHg. Adjustable minimum font size.

## Quick start (users)

1. Build the installer (see below) or get `WStation_setup_v2.2.x.exe` from a release.
2. Run it and choose a folder; it creates an `Időjárás` folder with both programs.
3. Start `Letöltő.exe` once: it asks for your Weather Underground **API key** and **station ID**
   (a PWS API key works with your own station that is actively sending data).
4. Start `Megjelenítő.exe`. The **Refresh** button downloads new data.

The guides shipped with the installer are in [`docs/`](docs) (Hungarian) and
[`docs/en`](docs/en), [`docs/de`](docs/de), [`docs/es`](docs/es).

## Building

Requirements: Windows 10/11 with .NET Framework 4.8 (built in). Nothing else to install, no internet needed.

```
build.bat
```

Output:

- `dist\WStation_setup_v2.2.x.exe` – the installer,
- `dist\bin\Megjelenito.exe`, `dist\bin\Letolto.exe` – the two programs, for testing.

`build.bat` uses the C# compiler that ships with Windows (`csc.exe`, C# 5). All web files, language files and
guides are embedded into the executables.

## Testing

```
dist\bin\Megjelenito.exe --teszt --mappa <folder that contains a data\ subfolder>
```

The self-test runs 40 checks (CSV merging and de-duplication, cache, daily rain, gaps, daylight saving time,
downsampling, quality flags, FAO-56 and FWI reference examples, rain calibration, date-range filter,
irrigation schedules, language files, write guard). Checks that need the developer's sample data are skipped
without it.

For UI work, run the Viewer against the source folder so edits appear after a browser reload:

```
dist\bin\Megjelenito.exe --bongeszo --web web --mappa <installed folder>
```

## Repository layout

```
build.bat                 one-command build (Windows built-in C# compiler)
src/
  megjelenito/            Viewer: data loading, analysis, drought engine, local HTTP API, WebView2 window, self-test
  letolto/                Downloader: WU API client, CSV storage, backups, alerts
  telepito/               Installer (WinForms)
  kozos/                  shared code: write guard, languages, Task Scheduler, ini merging
web/                      Viewer UI (HTML/CSS/JS + ECharts), embedded into the exe
  lang/                   translations: en.json, de.json, es.json, _sablon.json (template for new languages)
docs/                     user guides shipped with the installer (hu at the top level; en/, de/, es/)
lib/                      Microsoft WebView2 SDK DLLs
tools/                    helper scripts (translation key extraction)
```

## Documentation for developers

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) – components, data flow, HTTP API, front-end modules. **Start here.**
- [CONTRIBUTING.md](CONTRIBUTING.md) – conventions, file encodings, adding features and tests
- [docs/GLOSSARY.md](docs/GLOSSARY.md) – the source code is written in Hungarian; this maps the common terms
- [docs/TRANSLATING.md](docs/TRANSLATING.md) – adding a new language
- [docs/en/VALTOZASOK.txt](docs/en/VALTOZASOK.txt) – change log

## Privacy and safety

- The API key and station ID are stored only in the installed folder (`appdata_letöltő\beallitasok.ini`), never in the source.
- The Viewer's HTTP server listens on `localhost` only, on a random port; state-changing requests need a custom header and the same origin.
- All programs write only inside their own folder (exceptions: the desktop shortcut, the scheduled task, and an Excel export saved where the user chooses).

## Licence

Copyright © 2026 the WStation contributors.

This program is free software: you can redistribute it and/or modify it under the terms of the
**GNU General Public License version 3** as published by the Free Software Foundation. See [LICENSE](LICENSE).
Bundled third-party components are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
