# Architecture

This document explains how WStation fits together. The source code (identifiers and comments) is written in
Hungarian; [GLOSSARY.md](GLOSSARY.md) maps the most common terms.

## Overview

```
 Weather Underground PWS API
            │  HTTPS (api.weather.com, API key)
            ▼
 ┌──────────────────────┐   CSV files    ┌──────────────────────────────────────────────┐
 │ Downloader           │ ─────────────▶ │ Viewer (Megjelenítő.exe)                      │
 │ Letöltő.exe          │   data\*.csv   │  ┌───────────┐  ┌──────────┐  ┌────────────┐  │
 │ src/letolto          │                │  │ Loader &  │→ │ Analysis │→ │ HTTP API   │  │
 └──────────────────────┘                │  │ cache     │  │ engines  │  │ localhost  │  │
     ▲ Task Scheduler / Refresh button   │  └───────────┘  └──────────┘  └─────┬──────┘  │
     └───────────────────────────────────│                                     │ JSON   │
                                         │  WebView2 window ◀── web/*.js (ECharts)│        │
                                         └──────────────────────────────────────────────┘
```

- The **Downloader** is a separate executable, so Windows Task Scheduler can run it without the Viewer.
  The Viewer's *Refresh* button also starts it (hidden window) and parses its output for progress.
- The **Viewer** is a C# WinForms host. It loads the CSVs, computes everything in memory, serves a small
  JSON API on `localhost`, and shows the HTML/JS user interface in a WebView2 control (or in the default
  browser if WebView2 is missing).
- The **Installer** embeds both executables, the guides and the language files.

All three are compiled by `build.bat` with the C# 5 compiler that ships with .NET Framework 4.8.
There are no NuGet packages or project files; the web UI, language files and WebView2 DLLs are embedded as
resources.

## Installed folder layout

```
Időjárás\
  Megjelenítő.exe, Letöltő.exe, guides (*.txt)
  data\                    measurements written by the Downloader
    idojaras_adatok.csv      main table, ~5-minute observations (WU "observations/all" + "history/all")
    napi_osszesito.csv       WU daily summaries (last 7 days)
    pillanatnyi_allapot.csv  one row of current conditions per run
  appdata_letöltő\         Downloader state
    beallitasok.ini          settings: ApiKey, StationId, limits, alerts, backup, language (Nyelv)
    lekerdezes_allapot.json  which days are downloaded and when
    lekerdezes_naplo.log     readable log
    riasztasok.json          last alert times (repeat protection)
    mentesek\                zip backups
  appdata_megjelenítő\     Viewer state
    megjelenito.ini          thresholds (hot day, gaps, quality rules, detectors…)
    felulet.json             UI state: tab, theme, language, units, filters…
    szarazsag.json           drought model configuration (profiles, soil, FWI, irrigation programs)
    kalibracio.json          rain gauge calibration rows
    presetek\                named parameter presets
    naplo.json, futas.json   garden diary, running log
    valtozasnaplo.json       settings change log (for undo)
    cache\, lib\, WebView2\  binary cache, extracted DLLs, browser profile (safe to delete)
```

`src/megjelenito/Layout.cs` resolves these paths and migrates older single-folder layouts.
`src/kozos/Guard.cs` wraps every file write and refuses paths outside the installed folder.

## Downloader (`src/letolto/Letolto.cs`)

1. Reads `beallitasok.ini` (asks for API key / station ID on first interactive run).
2. Reads `lekerdezes_allapot.json` and the existing CSV, then decides what to query:
   - `v2/pws/observations/current` and `observations/all/1day` at most every 5 minutes,
   - `v2/pws/dailysummary/7day` at most hourly,
   - `v2/pws/history/all` for each missing day in the back-fill window, newest first,
   - while staying within per-run and daily call limits (HTTP 429 handled with back-off).
3. Merges rows by (station, epoch), writes the CSVs atomically (retrying if Excel has them open).
4. Runs weekly backups and Windows notifications (`Alerts`) with quiet hours and repeat protection.

Exit codes are listed in the user guide. A named mutex prevents two instances from writing at once.

## Viewer back end (`src/megjelenito`)

| File | Responsibility |
|---|---|
| `Program.cs` | entry point, command-line switches, single-instance lock, WebView2 window or browser fallback |
| `Server.cs` | `HttpListener` on `localhost:<random port>`; serves embedded `web/*` files and routes `/api/*` |
| `Api.cs` | `AppState` (loaded data, download/reload status) and the API router |
| `Data.cs` | CSV loading, de-duplication, binary cache, quality flags, gaps, daily/monthly/hourly aggregates |
| `Settings.cs` | `megjelenito.ini` template, parsing, validation ranges for editable keys |
| `Calib.cs` | rain gauge calibration: station sums per period, factor, applied to rain before aggregation |
| `Analysis.cs` | rain events, front/thunderstorm detector, records, wind statistics, hourly heatmaps, data quality, formula variables, date-range filter |
| `Drought.cs` | FAO-56 ET₀, two-layer soil water balance per profile, SCS runoff, Canadian FWI, VPD, GDD, irrigation programs; config storage and validation |
| `Maint.cs` | change log, update check, editing Downloader/Viewer ini keys from the UI |
| `Extras.cs` | UI state store, Excel (CSV) export |
| `Json.cs` | fast JSON writer for large numeric arrays |
| `SelfTest.cs` | `--teszt`: 40 checks, written to `appdata_megjelenítő\teszt_eredmeny.txt` |

### Data model

`Dataset` holds the observations as columns (`float[][] C`, indexed by `Col.*`) with Unix times `T`,
WU quality status `Qc`, computed `Flags` (bit mask, see `Flag`), local `Day`/`Hour` indices, and aggregates
(`Days`, `DaysRaw`, `Months`, `Hours`). "Clean" statistics skip rows whose flags match the relevant mask
(e.g. `Flag.Temp` for temperature). Nothing is ever deleted.

Processing order on every load (`Loader.Process`): calibration (raw rain kept in `RawPtot/RawPrate`) →
flags → gaps → daily aggregates (clean and raw) → monthly/hourly aggregates. The CSV parsing result is cached
in `cache\` and only changed files are re-read.

### HTTP API

All endpoints return JSON. Requests other than GET must carry the header `X-Idojaras: 1` and, if present,
an `Origin` equal to the server's own; this keeps other web pages from changing settings.

| Endpoint | Purpose |
|---|---|
| `GET /api/status` | load / download progress |
| `POST /api/refresh`, `POST /api/reload` | run the Downloader then reload, or reload only |
| `GET /api/meta` | station, period, files, gaps, settings, calibration state |
| `GET /api/today` | latest conditions and today's extremes |
| `GET /api/series?from&to&n&clean` | downsampled (LTTB + min/max band) chart series |
| `GET /api/days?clean` | daily aggregates (calendar, weekly summary, statistics) |
| `GET /api/records`, `/api/hourly`, `/api/quality` | statistics; accept `ranges=YYYY-MM-DD_YYYY-MM-DD,…` |
| `GET /api/wind`, `/api/rainevents`, `/api/events`, `/api/vars`, `/api/at` | wind stats, rain events, front/storm events, formula variables, weather at a moment |
| `GET/POST /api/drought`, `/api/drought/config`, `/api/drought/reset` | drought model results and configuration |
| `GET /api/presets`, `POST /api/drought/preset`, `/api/drought/preset-delete` | named presets |
| `GET/POST /api/ini`, `POST /api/ini/preview` | edit thresholds in `megjelenito.ini`; preview flag counts without saving |
| `GET/POST /api/calib` | rain gauge calibration |
| `GET/POST /api/ui`, `/api/store/naplo`, `/api/store/futas` | UI state, garden diary, running log |
| `GET/POST /api/alerts`, `POST /api/alerts/test` | Downloader alert settings, test notification |
| `GET/POST /api/schedule`, `/api/backup`, `/api/dlsettings` | Task Scheduler, backups, Downloader ini keys |
| `GET/POST /api/changelog`, `GET/POST /api/update`, `GET /api/langs` | change log, update check/install, available languages |
| `POST /api/export`, `GET /api/export.csv`, `POST /api/open` | Excel export, open a folder in Explorer |

## Viewer front end (`web/`)

Plain ES2017 JavaScript, no build step, no framework. Files are loaded in order by `index.html` and share
globals (`UI`, `META`, `DR`, helper functions). Each tab renders lazily when it becomes visible.

| File | Responsibility |
|---|---|
| `app.js` | core: formatting, units (`tconv`, `rtext`, `wtext`, `qv/qu/qd`), API helpers, Overview cards, Timeline, tabs, theme, ECharts hook |
| `i18n.js` | translation engine (see below) |
| `calendar.js`, `week.js` | calendar heatmap, shared daily data, weekly summary |
| `params.js` | parameter tables with validation, formula symbols, presets (shared by the ? and ƒx windows) |
| `drought.js`, `explain.js` | Drought tab, help windows, FWI step-by-step explanation, depletion explanation |
| `fx.js` | ƒx windows for computed values; suspicious-measurement editor |
| `calib.js`, `irrigation.js`, `water.js` | rain calibration card, irrigation programs, Water sub-tab |
| `insights.js`, `changes.js` | watch bar, sky, timeline markers; "What changed" bar |
| `rain.js`, `wind.js`, `ranges.js`, `stats.js` | Rain, Wind and Statistics tabs; date-range picker |
| `run.js`, `formula.js`, `diary.js`, `report.js`, `settings.js` | Running advisor, custom formula band, garden diary and alerts, reports, Settings tab |

Internally all values are metric; conversion happens only for display (`units()` in `app.js`).

## Internationalisation

The UI is authored in Hungarian. Other languages are **phrase dictionaries**: `web/lang/<code>.json` maps
Hungarian phrases to translations and carries `_meta` (name, locale, compass directions).

- In the browser, `i18n.js` translates text nodes, `title`/`placeholder` attributes, chart options and tooltip
  output after rendering (a `MutationObserver` catches new content). Whole texts are matched first, then known
  phrases inside longer texts; short single words are only translated in short labels to avoid mixed-language
  sentences. Month and day names come from `Intl` with the language's locale.
- C# code uses the same files through `src/kozos/Nyelv.cs` (`Langs.T()`): the Downloader translates its console
  messages and notifications, the Installer its window. The log file stays Hungarian on purpose (the Viewer
  parses the Downloader's output).
- `build.bat` embeds every `web/lang/*.json` and every `docs/<code>/*.txt`, so adding a language needs no code
  change. See [TRANSLATING.md](TRANSLATING.md).

## Installer (`src/telepito/Telepito.cs`)

Surveys the target folder (empty / older / same / newer version / old single-folder layout / not empty),
backs up settings, replaces the executables (refusing if they are running), copies the guides in the chosen
language, merges new keys into existing ini files, creates the shortcut and the scheduled task, and writes the
chosen language into `felulet.json` and `beallitasok.ini`. `--csendes` runs it without a window (used in tests).
