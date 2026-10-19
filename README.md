# Budaörsi időjárás

*Weather Underground PWS downloader and viewer for Windows – Hungarian, English, German and Spanish UI.*

Két program egy csomagban egy házi meteorológiai állomáshoz (Weather Underground PWS):

- **Letöltő** – a Weather Underground PWS API-ból CSV-be tölti az állomás adatait; maga dönti el, mit érdemes
  lekérdezni (pillanatnyi állapot, napi összesítő, hiányzó napok részletesen), naplóz, ütemezhető, Windows-riasztásokat küld.
- **Megjelenítő** – helyi, internet nélkül is működő felület (WebView2 + ECharts): idővonal, naptár-hőtérkép,
  szárazság (FAO-56 párolgás és talajvíz-mérleg, kanadai FWI tűzveszély, VPD, hőösszeg), locsolási javaslat
  és öntözési programok, vízfogyasztás/vízigény, eső- és szélstatisztikák (vitorlázáshoz tört Beaufort),
  rekordok, futóruházat-tanácsadó, havi jelentés és évkönyv.
- **Telepítő** – egyetlen exe, minden esetet kezel (üres mappa, régebbi verzió, régi elrendezés, futó program).

A felület **többnyelvű** (magyar, angol, német, spanyol), és új nyelv egyetlen JSON-fájllal hozzáadható –
lásd [docs/FORDITAS.md](docs/FORDITAS.md). Mértékegységek: °C/°F, mm/inch, km/h, m/s, csomó, mph, Beaufort, hPa/inHg.

## Buildelés

```
build.bat
```

A Windows beépített C# fordítóját használja (.NET Framework 4.8, `csc.exe`), így nem kell semmit telepíteni,
és internet sem kell hozzá. Eredmény:

- `dist\Idojaras_telepito_v2.1.exe` – a telepítő (ezt kell elindítani),
- `dist\bin\Megjelenito.exe`, `dist\bin\Letolto.exe` – a két program külön, teszteléshez.

## Első használat

1. Indítsd el a telepítőt, válassz mappát (alapból a telepítő mellé `Időjárás` mappa készül).
2. Indítsd el egyszer a `Letöltő.exe`-t: megkérdezi a Weather Underground **API kulcsot** és az **állomás azonosítóját**.
   API kulcs: <https://www.wunderground.com/member/api-keys> (a PWS API kulcs saját, aktívan adatot küldő állomással működik).
3. Indítsd el a `Megjelenítő.exe`-t.

Az API kulcs és az állomás csak a telepített mappa `appdata_letöltő\beallitasok.ini` fájljába kerül, a forráskódban nincs.

```
Időjárás\
  Megjelenítő.exe, Letöltő.exe, leírások
  data\                  ← a mérések (CSV)
  appdata_letöltő\       ← a Letöltő beállításai (API kulcs), naplója, mentései
  appdata_megjelenítő\   ← a Megjelenítő beállításai, cache, napló
```

A programok csak a saját mappájukba írnak (kivétel: asztali parancsikon, ütemezett feladat, Excel-export a választott helyre).

## Mappák

| Mappa | Tartalom |
|---|---|
| `src\megjelenito\` | Megjelenítő: adatbetöltés, tisztítás, cache (`Data.cs`), API (`Api.cs`), szárazság-motor: FAO-56 ET₀, talajvíz-mérleg, SCS-lefolyás, FWI, GDD, öntözési programok (`Drought.cs`), elemzések (`Analysis.cs`), csapadék-kalibráció (`Calib.cs`), beállítás-szerkesztés, változásnapló, frissítés-ellenőrzés (`Maint.cs`), helyi szerver, ablak, önteszt |
| `src\letolto\` | Letöltő: letöltés, mentés, Windows-riasztások |
| `src\telepito\` | Telepítő |
| `src\kozos\` | közös kód: írás-őr (`Guard.cs`), nyelvek (`Nyelv.cs`), Feladatütemező (`Utemezes.cs`), ini-frissítés |
| `web\` | a felület (az exe-be ágyazva): `index.html`, `app.css`, `*.js`, `echarts.min.js` (5.5.1, Apache-2.0) |
| `web\lang\` | nyelvi fájlok (`en.json`, `de.json`, `es.json`, `_sablon.json` új nyelvhez) |
| `docs\` | a telepítőbe kerülő leírások (magyarul, valamint `docs\en`, `docs\de`, `docs\es`) |
| `lib\` | Microsoft WebView2 SDK 1.0.4258.31 DLL-ek (licenc: `lib\WebView2_LICENSE.txt`) |
| `tools\` | `forditas_kulcsok.ps1` – a fordítatlan kifejezések listája, a nyelvi sablon frissítése |

## Tesztelés

```
dist\bin\Megjelenito.exe --teszt --mappa <programmappa>
```

40 ellenőrzés (egyesítés és duplikátumszűrés, cache, napi csapadék, hiányok, nyári időszámítás, ritkítás,
minőségjelzők, FAO-56 és FWI referenciapéldák, kalibráció, időszak-szűrő, öntözési programok, nyelvi fájlok,
írás-őr). Mérési adat nélkül a mintaadatra épülő ellenőrzések kimaradnak.

Telepítés felület nélkül: `Idojaras_telepito_v2.1.exe --csendes [<célmappa>] [--atvetel <régi adatok>] [--parancsikon] [--utemezes <perc>] [--felmeres]`

Megjelenítő kapcsolók: `--bongeszo` · `--mappa <út>` · `--port <szám>` · `--web <út>` (a felület fájljait lemezről olvassa, fejlesztéshez).
Letöltő kapcsolók: `--nowait` (ütemezett futáshoz) · `--mappa <út>` · `--mentes [--most]`.

## Változások

Lásd [docs/VALTOZASOK.txt](docs/VALTOZASOK.txt).
