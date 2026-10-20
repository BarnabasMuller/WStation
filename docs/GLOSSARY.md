# Glossary (Hungarian → English)

The code, file names and comments are in Hungarian. These are the terms you will meet most often.

## Programs and folders

| Hungarian | English | Notes |
|---|---|---|
| Letöltő | Downloader | `Letöltő.exe`, `src/letolto` |
| Megjelenítő | Viewer | `Megjelenítő.exe`, `src/megjelenito` |
| Telepítő | Installer | `WStation_setup_v*.exe`, `src/telepito` |
| közös | shared | `src/kozos` |
| Időjárás | Weather | installed root folder |
| appdata_letöltő / appdata_megjelenítő | Downloader / Viewer app data | settings, state, logs |
| beállítások (`beallitasok.ini`) | settings | Downloader settings file |
| napló (`lekerdezes_naplo.log`) | log | |
| lekérdezési állapot | query state | which days are downloaded |
| mentés, mentések | backup(s) | `mentesek\mentes_*.zip` |
| felület (`felulet.json`) | user interface (state) | |
| szárazság (`szarazsag.json`) | drought (configuration) | |
| kalibráció | calibration | rain gauge |
| preset(ek) | preset(s) | named parameter sets |
| változásnapló | change log | |
| ütemezés, Feladatütemező | schedule, Task Scheduler | `Utemezes.cs` |
| őr (`Guard`) | guard | write guard |
| nyelv (`Nyelv.cs`) | language | |
| önteszt (`--teszt`) | self-test | |
| fordítás, sablon | translation, template | `web/lang/_sablon.json` |

## Command-line switches

| Switch | Meaning |
|---|---|
| `--teszt` | run the self-test |
| `--bongeszo` | open in the browser instead of a window |
| `--mappa <dir>` | use this installed folder |
| `--web <dir>` | serve UI files from disk (development) |
| `--port <n>` | fixed port |
| `--nowait` | Downloader: no prompts, for scheduled runs |
| `--mentes [--most]` | Downloader: backup (now) |
| `--csendes`, `--felmeres`, `--atvetel`, `--parancsikon`, `--utemezes`, `--cel` | Installer: silent, survey only, import from, shortcut, schedule minutes, target folder |

## Weather and data

| Hungarian | English |
|---|---|
| mérés, mérések | measurement(s) |
| hőmérséklet (hom), hőérzet | temperature, feels-like |
| harmatpont | dew point |
| páratartalom (pára) | humidity |
| légnyomás | (air) pressure |
| szél, széllökés (lökés), szélirány | wind, gust, wind direction |
| csapadék, eső, intenzitás | precipitation, rain, rate |
| esőesemény, felhőszakadás, zápor | rain event, cloudburst, shower |
| napsugárzás, napsütés, napenergia | solar radiation, sunshine, solar energy |
| front, zivatar | front, thunderstorm |
| fagy, hőség, hőségnap, trópusi éjszaka, téli nap | frost, heat, hot day, tropical night, ice day |
| napi összesítő | daily summary |
| pillanatnyi állapot | current conditions |
| gyanús (mérés), jelölés | suspicious (measurement), flag |
| adathiány, kiesés | data gap, outage |
| idővonal, naptár, hőtérkép | timeline, calendar, heatmap |
| rekord, időszak | record, period |
| figyelő | watch bar (alerts in the UI) |

## Drought and garden

| Hungarian | English |
|---|---|
| párolgás (ET₀) | (reference) evapotranspiration |
| talajvíz-mérleg | soil water balance |
| kiszáradás (%) | depletion (%) |
| stresszhatár (RAW) | stress threshold (readily available water) |
| szántóföldi vízkapacitás, hervadáspont | field capacity, wilting point |
| gyökérzóna | root zone |
| lefolyás | runoff |
| profil, talajfelszín, gyep, bokor, cserép, fa | profile, soil surface, lawn, shrub, pot, tree |
| locsolás, öntözés, öntözési program | watering, irrigation, irrigation program |
| vízigény, vízfogyasztás, vízdíj, csatornadíj | water demand, water use, water price, sewer fee |
| tűzveszély (FWI) | fire danger (Fire Weather Index) |
| hőösszeg (GDD) | heat sum (growing degree days) |
| páranyomás-hiány (VPD) | vapour pressure deficit |
| kerti napló | garden diary |
| futás, futónapló | running, running log |
