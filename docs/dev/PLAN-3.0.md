# WStation 3.0 – architektúra- és megvalósítási terv

*Állapot: terv, a fejlesztés nem indult el. Kiindulás: `v2.2` ág, `2cd62af` (2.2.8). Készült: 2026-11-13.*
*Ez a fájl helyi (a `.gitignore` `TERV*.md` szabálya miatt nem kerül a repóba). Ha a fejlesztés elindul, a 4–13. fejezet
`docs/ARCHITECTURE-3.md` néven angolul bekerül a repóba; a személyes részek (eszközök, hálózat) nem.*

**Kinek szól:** annak, aki a kódot írja (jellemzően egy kisebb modell, munkacsomagonként). A 16. fejezet munkacsomagjai önállóan
kiadhatók; mindegyik megmondja, mit kell elolvasni ebből a fájlból. A 17. fejezet szabályai minden munkacsomagra érvényesek.

---

> **Ez a WStation 3.0 fejlesztési terve.** A repóban ez a mérvadó változat; a munkacsomagok megbízásai erre hivatkoznak.
> A „TERV_4” hivatkozások a 2.x fejlesztés helyi jegyzeteire utalnak (nincsenek a repóban); a szükséges tartalmuk itt szerepel.

## 0. Összefoglaló

A WStation ma három Windows-program (.NET Framework 4.8): a Letöltő (WU → CSV), a Megjelenítő (helyi webszerver + WebView2 ablak) és a
telepítő. A 3.0 célja, hogy **ugyanaz a kód** fusson
- Windows-gépen, önállóan (ahogy ma) – **helyi mód**;
- egy mindig bekapcsolt gépen (Synology NAS, más Linux, akár Windows) ablak nélkül, hálózatról böngészőből elérve – **szerver mód**;
- és a Windows-program tudjon a szerverhez kapcsolódni is – **távoli (NAS) mód**;
- a szerver megbízhatóan vezéreljen egy öntözőrendszert (programok, szabályok, automata mód, szimuláció).

A megoldás lényege: **egy repó, egy közös mag (`WStation.Core`), vékony gazdaprogramok** (asztali, szerver, letöltő, telepítő). Nincs fork és nincs
platformonkénti ág. A mag nem tud a platformról; amit a platform ad (ablak, értesítés, ütemező, szelep), azt felületeken (interface) át kapja.

### 0.1 A felhasználó döntései (2026-11-13)
| # | Döntés |
|---|---|
| D1 | A NAS a központ, **de** a helyi (önálló Windows) változat ettől függetlenül teljes értékű marad. A Windows-programban állítható: helyi vagy NAS mód. |
| D2 | Lehessen **adatbázisok / WU API-kulcsok között váltani** (több állomás, több adatmappa) → „munkaterület” (6. fejezet). |
| D3 | A szerveren **két szerep**: rendszergazda és néző, külön jelszóval. |
| D4 | Öntözés: **programok + okos kihagyás**, környezeti feltételekkel (szélnél halasztás, csak éjjel stb.); **teljesen automata mód** megadható szabályokkal; **szimuláció** („hogyan locsolt volna tavaly nyáron ezekkel a szabályokkal”), több változat összevetésével. |
| D5 | A Windows-változat is **modern .NET-re** áll át (egy közös alap); a telepítő nagyobb lesz. |
| D6 | A szelepvezérlés **helyi** legyen (nem felhő). Eszköz: 6/8 zónás, hálózati tápos, wifis Tuya/SmartLife vezérlő (pontos típus és képességek az eszköz megérkezése után – 11.9). |
| D7 | Az összes mérés **egy fájlban** marad; a Letöltő memóriaigényét hozzáfűzéssel kell megoldani (TERV_4 21.1). |
| D8 | A felület általános hibakezelőt kap; a mentésből legyen „API-kulcs nélküli másolat” (TERV_4 21.2–21.3). |

### 0.2 Célkörnyezet
- A cél-NAS: 64 bites ARM, kb. 1 GB memória, nincs hivatalos Docker, régi (4.4-es) Linux-kernel – ezért kell a K0 próba (15. fejezet, B. függelék).
- A mérőállomás csak a Weather Undergroundra tölt fel; az adat a WU API-ról jön. A közvetlen állomás→szerver fogadás helye a tervben megvan (8.6), de nem része a 3.0-nak.

### 0.3 A legfontosabb tervezési elvek
1. **A mag tiszta és platformfüggetlen.** Nincs benne ablak, Windows-hívás, fix elérési út-elválasztó, globális változó a munkaterület adataira.
2. **A meglévő viselkedés megmarad.** Az átírás első lépése bizonyíthatóan ugyanazt adja, mint a 2.2.8 (rögzített API-válaszokkal összevetve, 14.2).
3. **Alapból zárva.** Minden API-útvonal egy táblában van, szerepkörrel; ami nincs a táblában, nem létezik. A szerver mód jelszó nélkül nem indul.
4. **A víz a legdrágább hiba.** A szelepnél minden nyitás időkorlátos az eszközben is; a döntő kód tiszta függvény, ugyanaz fut élesben és szimulációban.
5. **Egy író.** Egy munkaterület adatait egyszerre egy folyamat írja (zárfájl); egy szelepvezérlőt egyszerre egy motor vezérel (bérlet).
6. **Kis lépések, kapukkal.** Minden szakasz végén mérhető feltétel van; a NAS-ra szánt munka előtt egy tízperces próba dönti el, hogy az út járható-e (15. fejezet, K0).

---

## 1. Célok és nem célok

**Cél (3.0–3.2):**
- közös mag, három üzemmód (helyi, szerver, távoli);
- több munkaterület, váltás a felületről;
- szerver mód jelszavas belépéssel, két szereppel, titkosított kapcsolattal;
- a Letöltő beépítve a szerverbe (saját ütemező), Windowson továbbra is külön program a Feladatütemezőhöz;
- értesítések platformtól függetlenül (Windows-értesítés, a felület értesítési listája, e-mail / ntfy / webhook);
- öntözés: zónák, programok, szabályok, automata mód, szimuláció, helyi szelepvezérlés, biztonsági korlátok;
- a 2.x telepítések adatvesztés nélkül frissülnek; egy 2.x mappa átmásolható a szerverre.

**Nem cél (most):**
- internetre kitett üzem (a szerver csak belső hálózatra készül; távolról VPN-nel);
- felhős Tuya-vezérlés; több szelepvezérlő-típus (egy helyi meghajtó + a szimulált);
- az állomás közvetlen fogadása (0.2);
- adatbázis-motor (marad a CSV + bináris gyorsítótár);
- mobilalkalmazás (a webes felület telefonon is használható);
- a webes felület keretrendszerre átírása (marad a sima JS, fordítási lépés nélkül).

---

## 2. Fogalmak

| Fogalom | Jelentés |
|---|---|
| **mag** (`WStation.Core`) | Minden, ami nem platformfüggő: adat, számítás, letöltés, API, felület-fájlok, öntözési logika. |
| **gazda** (host) | Indítható program, amely a magot egy környezetbe ülteti: `Desktop` (ablak), `Server` (háttérszolgáltatás), `Downloader` (parancssori letöltő), `Installer`. |
| **otthonmappa** (home) | A telepítés gyökere: programfájlok mellett a közös beállítások (`rendszer\`) és a munkaterületek. |
| **munkaterület** (workspace) | Egy állomás adatai és beállításai egy mappában: `data\`, `appdata_letöltő\`, `appdata_megjelenítő\`. Saját API-kulcs, állomás, időzóna, öntözés. Egy 2.x telepítés mappája **pontosan egy munkaterület**. |
| **helyi mód** | Asztali gazda, a mag ugyanabban a folyamatban, a felület csak a saját gépről érhető el (mint ma). |
| **szerver mód** | Szerver gazda, ablak nélkül; belépés jelszóval; ütemező, értesítések, öntözés. |
| **távoli mód** | Asztali gazda, amely nem indít magot, hanem egy szerver felületét mutatja az ablakában. |
| **szerep** | `admin` (mindent állíthat) vagy `nezo` (csak megtekint). Helyi módban mindig `admin`. |
| **vezérlő** | Fizikai öntözésvezérlő eszköz (pl. a 8 zónás Tuya-doboz). |
| **zóna** | Egy szelepkör; egy vezérlő egy csatornája; egy szárazság-profilhoz köthető (gyep, bokrok…). |
| **program** | Mikor, melyik zóna, mennyit (meglévő fogalom: `DProgram` + `DSchedule`). |
| **szabálykészlet** | Feltételek, amelyek egy tervezett locsolást kihagynak, halasztanak vagy átméreteznek; automata módban ezek döntenek. |
| **futás** (run) | Egy zóna egy konkrét nyitása: tervezett → fut → kész / sikertelen. |
| **forgatókönyv** | Szimulációhoz elmentett program + szabálykészlet együttes. |

---

## 3. Üzemmódok és topológia

```
 (A) HELYI MÓD – önálló Windows                (B) SZERVER MÓD – NAS / mindig bekapcsolt gép
 ┌───────────────────────────────┐             ┌──────────────────────────────────────────────┐
 │ Megjelenítő.exe (Desktop)     │             │ wstation-server (Server)                      │
 │  ┌─────────────────────────┐  │             │  ┌────────────────────────────────────────┐  │
 │  │ WStation.Core           │  │             │  │ WStation.Core                           │  │
 │  │  API + felület          │  │             │  │  API + felület  ← belépés, szerepek     │  │
 │  └───────────▲─────────────┘  │             │  │  Ütemező: letöltés, mentés, riasztás,   │  │
 │   WebView2 ablak (localhost)  │             │  │           öntözési motor                │  │
 └───────────────────────────────┘             │  └───────▲───────────────────┬────────────┘  │
 Letöltő.exe (Downloader) ◀─ Feladatütemező    └──────────│───────────────────│───────────────┘
        │ ugyanaz a Core.Download                  HTTPS │ belső hálózat     │ helyi hálózat (Tuya)
        ▼                                                 │                   ▼
   munkaterület mappa                       böngésző / telefon /        szelepvezérlő
                                            (C) Megjelenítő.exe TÁVOLI MÓDBAN
```

- **(A)** pontosan a mai működés; az öntözésből itt a tervezés, a szimuláció és a kézi indítás érhető el, az automata futtatás nem (ahhoz mindig
  bekapcsolt gép kell – 11.2).
- **(B)** a szerver egyetlen folyamat. Nincs külön letöltő program és nincs rendszerütemező: a mag ütemezője futtatja a feladatokat.
- **(C)** a Windows-program a szerver címére navigál; helyben nem tölt le és nem számol. A mód a programban váltható (9.1).
- (A) és (B) adatai függetlenek. Átvitel a kettő között: biztonsági mentés letöltése / visszaállítása, vagy a munkaterület mappájának másolása (13.3).

---

## 4. Repó és GitHub

### 4.1 Egy repó, nincs fork, nincs platform-ág
A két változat (Windows és NAS) kódjának kb. 90%-a közös. Ha két forkban vagy két hosszú életű ágban élnének, minden hibajavítást kétszer
kellene megcsinálni, és a két változat néhány hónap alatt szétcsúszna. Ezért:
- **egy repó** (`BarnabasMuller/WStation`), **egy fő ág** (`main`), benne a mag és mindegyik gazda;
- a platformkülönbség **mappákban és projektekben** él, nem ágakban;
- **fork** csak arra való, hogy egy külső közreműködő a saját másolatában dolgozzon és onnan küldjön módosítást (pull request).

### 4.2 Ágak
| Ág | Szerep |
|---|---|
| `main` | Mindig fordítható és tesztelt. A 3.0 fejlesztése itt folyik, pull requesteken át. |
| `release/2.x` | A 2.2.8 karbantartása, amíg a 3.0 ki nem jön (csak hibajavítás). A mostani `v2.2` ágból lesz. |
| `feat/<rövid-név>` | Egy munkacsomag ága; rövid életű; a `main`-be squash-sel olvad be. |
| `release/3.x` | Csak ha a 3.0 kiadása után párhuzamosan kell javítani és fejleszteni. |

Előkészítő lépés (egyszeri, a felhasználó jóváhagyásával): a `v2.2` beolvasztása a `main`-be (előre tekerés), majd `release/2.x` létrehozása.
Címkék: `v3.0.0` formában (SemVer). Előzetes kiadás: `v3.0.0-alpha.1`.

### 4.3 Mappaszerkezet
```
WStation/
├─ src/
│  ├─ WStation.Core/            közös mag (net10.0)            ← a kód ~90%-a
│  │   ├─ Common/               Guard, IniFile, Json, Log, Clock, Lang, NumFmt, FileLock
│  │   ├─ Workspaces/           Workspace, WorkspaceRegistry, Ctx (környezet), Layout
│  │   ├─ Data/                 Col, Flag, Dataset, Tz, Loader, ObservationStore, Calib
│  │   ├─ Analysis/             Analysis, Cloud, Export
│  │   ├─ Drought/              modell, DroughtStore, BalanceStepper, FWI
│  │   ├─ Download/             DownloadEngine, WuClient, DlConfig, DlState, Metar
│  │   ├─ Backup/               BackupService (készítés, visszaállítás, kulcs nélküli másolat)
│  │   ├─ Jobs/                 Scheduler, Job, JobState
│  │   ├─ Notify/               INotifier, NotificationCenter, Alerts (riasztási szabályok)
│  │   ├─ Irrigation/           modell, Planner, Rules, Simulator, Engine, Journal, Drivers/
│  │   ├─ Api/                  ApiRequest/Response, RouteTable, kezelők, IAuth, Roles
│  │   └─ Web/                  (csak a beágyazás: a ../../web fájljai erőforrásként)
│  ├─ WStation.Desktop/         Windows-ablak (net10.0-windows): WebView2, helyi/távoli mód, párbeszédablakok
│  ├─ WStation.Downloader/      Windows parancssori letöltő (net10.0-windows): Feladatütemezőhöz, Windows-értesítés
│  ├─ WStation.Server/          szerver gazda (net10.0): Kestrel, belépés, munkamenetek, ütemező indítása
│  └─ WStation.Installer/       Windows-telepítő (net48 marad – 9.4)
├─ web/                         a felület (változatlan helyen; JS, CSS, lang/*.json)
├─ tests/
│  ├─ WStation.Tests/           egységtesztek (xUnit)
│  ├─ golden/                   rögzített API-válaszok a 2.2.8-ból (14.2)
│  └─ data/                     generált mintaadat (nem valódi mérés)
├─ packaging/
│  ├─ synology/                 indítószkript, csomagleírás, telepítési útmutató
│  ├─ linux/                    systemd egység, telepítőszkript
│  └─ windows/                  a telepítő tartalmának összeállítása
├─ tools/                       forditas_kulcsok.ps1, golden rögzítő, adatgenerátor
├─ docs/                        felhasználói leírások (hu/en/de/es), ARCHITECTURE, API, SECURITY
├─ .github/workflows/           ci.yml, release.yml
├─ Directory.Build.props        közös verzió, nyelvi szint, figyelmeztetések
└─ WStation.sln
```

**Függőségi szabály (gépi ellenőrzéssel, 14.5):** a `Core` semmire nem hivatkozik a `src` többi projektjéből, és nem használhat
`System.Windows.Forms`, `Microsoft.Web.WebView2`, `Microsoft.Win32`, `DllImport` elemet. A gazdák csak a `Core`-ra hivatkoznak, egymásra nem.

### 4.4 Verzió és kiadás
- Egy verziószám van, a `Directory.Build.props`-ban; minden program és a felület (`/api/sys/info`) ezt mutatja.
- A `release.yml` egy `v*` címkére lefordít és tesztel, majd **egy** GitHub-kiadás vázlatát készíti el ezekkel a fájlokkal:
  - `WStation_setup_v3.x.y.exe` – Windows-telepítő (asztali program + letöltő);
  - `wstation-server-v3.x.y-linux-arm64.tar.gz` – NAS / ARM Linux;
  - `wstation-server-v3.x.y-linux-x64.tar.gz` – PC / minigép Linux;
  - `wstation-server-v3.x.y-win-x64.zip` – szerver mód Windowson (annak, akinek nincs NAS-a);
  - `SHA256SUMS.txt` – ellenőrzőösszegek (a felület frissítés előtt ezt mutatja – TERV_4 20.3).
- A kiadást a felhasználó teszi közzé (a vázlatot átnézi). Címkét csak helyi gépről lehet feltenni (a felhős munkamenet nem tud – meglévő korlát).
- **CI (`ci.yml`)** minden pull requestre: fordítás Windowson és Linuxon, egységtesztek, golden tesztek, a függőségi szabály ellenőrzése,
  a fordítási kulcsok ellenőrzése (nincs lefordítatlan új kulcs a `_sablon.json`-hoz képest), titokkeresés (API-kulcs minta, állomásazonosító).
- **Az építési lánc védelme:** a munkafolyamatok külső lépései rögzített változatra (commit-azonosítóra) hivatkoznak; a munkafolyamat
  jogosultsága alapból csak olvasás, a kiadásé csak a kiadás írása; titok a CI-ben nincs (nincs kódaláírás, nincs feltöltés máshová);
  a külső csomagok (WebView2, xUnit) verziója rögzített, zárolófájllal.

### 4.5 Közreműködés
- Minden változás pull request; a `main` védett (CI zöld + egy jóváhagyás – egyszemélyes projektnél a felhasználó saját jóváhagyása).
- Hibajegyek címkéi: `platform:windows`, `platform:server`, `area:irrigation`, `area:download`, `security`.
- A személyes adat szabálya változatlan: API-kulcs, állomásazonosító, valódi mérés, e-mail, helyi kulcs, IP-cím nem kerülhet a repóba.

---

## 5. Technológiai választások (indoklással)

| Kérdés | Választás | Miért |
|---|---|---|
| Futtatókörnyezet | **.NET 10 (LTS)**, a programmal együtt szállítva (self-contained) | Windowson és Linuxon (x64, ARM64) is fut; a felhasználónak nem kell semmit telepítenie. Ha a NAS régi rendszerkönyvtára (glibc) nem elég hozzá: lásd K0 és 10.6. |
| Nyelv | C# 12–14, `Nullable` kikapcsolva az átvett fájlokban, bekapcsolva az újakban | Az átvett kód ne változzon feleslegesen; az új kód szigorúbb. |
| Külső csomagok | A magban **nincs** külső csomag. Gazdák: `Microsoft.Web.WebView2` (Desktop), ASP.NET Core (Server, a .NET része). Teszt: xUnit. | A „nincs függőség” eddigi elve megmarad: kisebb támadási felület, nincs ellátási lánc kockázat. |
| JSON | Saját vékony réteg a `System.Text.Json` fölött (`Core.Common.Json`), amely a régi `JavaScriptSerializer` viselkedését adja | 31 hívási hely függ a régi alakoktól (`Dictionary<string, object>`, `object[]`); a fájlformátumok nem változhatnak (7.2). |
| HTTP – asztali | `HttpListener`, csak `localhost` (a mai `Server.cs`) | Bevált, kicsi, nem kell hozzá semmi. |
| HTTP – szerver | **Kestrel** (ASP.NET Core), minimális „mindent elkapó” végponttal, amely a mag útválasztóját hívja | Hálózatra kitett szolgáltatáshoz érett, karbantartott szerver kell (időkorlátok, méretkorlátok, HTTPS). A mag nem tud róla. |
| Felület | Marad a mostani `web/` (sima JS, ECharts), új modulokkal | Nem cél az átírás. |
| Adattárolás | CSV (hozzáfűzéssel) + bináris gyorsítótár; beállítások ini / JSON | Emberi szemmel olvasható, Excelben megnyitható, a 2.x-szel azonos. |
| Tesztek | xUnit egységtesztek + a beépített önteszt (`--teszt`) megmarad terepi hibakereséshez | A CI-ben gyors visszajelzés; a felhasználó gépén is futtatható ellenőrzés. |
| Fordítás | `dotnet build` / `dotnet publish` a **.NET 10 SDK**-val; a `build.bat` egy vékony burkoló marad | A Windows beépített fordítója (a mai módszer) modern .NET-et nem tud fordítani. **Változás:** a fejlesztői gépre telepíteni kell az ingyenes .NET SDK-t (a felhasználó gépén ma csak futtatókörnyezetek vannak, SDK nincs); a kiadásokat a GitHub Actions is elkészíti. |
| Telepítő | **Marad .NET Framework 4.8-on** (a Windows része), a tartalma egy zip a .NET 10-es programokkal | A telepítőnek akkor is el kell indulnia, ha a gépen nincs .NET 10; így kicsi marad és a bevált kódja megmarad (9.4). |
| Szelep-meghajtó | Saját C# megvalósítás a Tuya helyi protokolljára (3.3 / 3.4 / 3.5), a `tinytuya` (MIT) alapján | Nincs érett, karbantartott .NET-könyvtár; a protokoll kicsi. Licenc a `THIRD_PARTY_NOTICES.md`-be. |

---

## 6. Munkaterületek és a környezet (`Ctx`)

### 6.1 A mai állapot és a gond
A Megjelenítő kódja ma **globális statikus változókból** olvassa, melyik mappával dolgozik: `AppState.L` (útvonalak), `AppState.S`
(beállítások), `AppState.Data` (betöltött adat), `Tz.Zone` / `Tz.Iana` (időzóna), `Langs.Current`, `Guard.Root`, a `DroughtStore` és a
`Cloud` gyorsítótárai, a Letöltőben a `Cfg` összes mezője. Egy folyamat így egyszerre egyetlen mappát tud kiszolgálni. A D2 (több munkaterület)
és a szerver (az öntözés munkaterülete mindig be legyen töltve, miközben valaki egy másikat nézeget) ezt nem engedi meg.

### 6.2 Megoldás: környezethez kötött állapot, a hívási helyek átírása nélkül
- Új osztály: `WorkspaceContext` – egy munkaterület **minden** futás közbeni állapota: `Layout L`, `Settings S`, `Dataset Data`, betöltési
  állapot (`Status`, `Message`, `Progress`, letöltési mezők), `TimeZoneInfo Zone` + `Iana`, a szárazság-beállítás gyorsítótára, a felhőzet
  burkológörbéje, a kalibráció, a tárolók zárjai.
- Új statikus belépési pont: `Ctx.Current` – `AsyncLocal<WorkspaceContext>`. Minden API-kérés és minden ütemezett feladat a legelején
  belép a saját munkaterületébe: `using (Ctx.Enter(ws)) { … }`. A .NET ezt a környezetet továbbviszi a szálkészletbe adott munkákra
  (`ThreadPool.QueueUserWorkItem`) és az új szálakra is, tehát a háttérben futó betöltés is jó munkaterületet lát.
- A meglévő statikus mezők **tulajdonsággá** válnak, amelyek a `Ctx.Current`-ből olvasnak. Példa:
  ```csharp
  static class AppState {
      public static Layout L   { get { return Ctx.Current.L; } }
      public static Settings S { get { return Ctx.Current.S; } set { Ctx.Current.S = value; } }
      public static Dataset Data { get { return Ctx.Current.Data; } set { Ctx.Current.Data = value; } }
      …
  }
  static class Tz { public static TimeZoneInfo Zone { get { return Ctx.Current.Zone; } } … }
  ```
  Így a több ezer hívási hely (`AppState.L.App`, `Tz.Local(t)` …) **nem változik**; csak az állapot helye.
- `Ctx.Current` belépés nélkül **kivételt dob** („nincs munkaterület-környezet”) – nem ad vissza alapértelmezést. Egy elfelejtett belépés így
  az első tesztnél kiderül, nem csendben a rossz mappába ír.
- **Szabály:** a magban nem lehet más írható statikus mező, csak ami tényleg folyamatszintű (naplózó, nyelvi fájlok gyorsítótára, a
  munkaterület-nyilvántartás). Ezt teszt ellenőrzi (14.5).
- A `ref`-fel átadott mezők (`Interlocked.CompareExchange(ref busy, …)`, `Interlocked.Increment(ref DownloadSerial)`) a környezet
  **mezői** maradnak (tulajdonságra nem lehet `ref`-et venni): ezeknél a hívási hely `Ctx.Current.busy`-ra változik.

**Leltár – a mai írható statikus állapot sorsa** (a `2cd62af` kódon felmérve; a 2.1 csomag ezt a listát követi):
| Hol | Mező | Hova kerül |
|---|---|---|
| `AppState` (`Api.cs`) | `L`, `S`, `Data`, `Status`, `Message`, `Progress`, `DownloadMessage`, `DownloadOk`, `DownloadSerial`, `busy`, `backupChecked`, `cancelDownload` | `WorkspaceContext` |
| `Tz` (`Data.cs`) | `Zone`, `Iana` | `WorkspaceContext` |
| `Loader` (`Data.cs`) | `gapSec` | `WorkspaceContext` (a beállításból jön) |
| `Cloud` | `envFor`, `env` (burkológörbe gyorsítótára) | `WorkspaceContext` |
| `DroughtStore` | `cached`, `RepairNote` | `WorkspaceContext` |
| `Export` (`Extras.cs`) | `Ci`, `Dirs` (a kivitel nyelve) | a kérés paramétere (nem állapot) |
| `Langs` (`Nyelv.cs`) | `Current` | a Letöltő / értesítések nyelve: `WorkspaceContext`; a felület nyelve a kliensé (a szerver magyarul válaszol, a felület fordít – ez marad) |
| `Langs` | `Dir`, `Cache` | folyamatszintű (megengedett) |
| `Guard` | `root` | folyamatszintű, több gyökérrel (6.3) + munkaterületenkénti őr |
| `WuLetolto.Cfg`, `Log` (`Letolto.cs`) | az összes beállítás- és naplómező | `DlConfig` példány, ill. a közös `Log` (8.1, 7.6) |
| `Program` | `Window`, `ServerUrl`; `EmbeddedLibs.libDir`; `AppIcon.cached` | a Desktop gazdában maradnak |
| `JsonStore`, `UiSettings`, `ChangeLog`, `DroughtStore` | `Lock` (zárobjektumok) | maradhatnak közös zárak (helyesek, csak durvábbak); nem állapot |
| `Settings.Editable`, `TextKeys`, `Analysis.NumKeys` … | csak olvasott táblák | maradnak |

### 6.3 A munkaterület a lemezen
```
<otthonmappa>\
  Megjelenítő.exe, Letöltő.exe, leírások, app\ (futtatókörnyezet)      ← Windows
  rendszer\                         közös, nem munkaterülethez kötött
    munkateruletek.json               nyilvántartás: [{id, nev, mappa, alap}]
    kapcsolat.json                    asztali: helyi / távoli mód, szervercím (9.1)
    szerver.json                      szerver: kapu, kapcsolati mód, engedélyezett gépnevek (10.2)
    felhasznalok.json                 szerver: szerepek, jelszókivonatok (10.3)
    munkamenetek.json                 szerver: élő munkamenetek (kivonatolva)
    naplo\                            rendszernapló, belépési napló
  data\, appdata_letöltő\, appdata_megjelenítő\      ← az ALAP munkaterület (a 2.x telepítés változatlanul)
  munkateruletek\
    <id>\data\, appdata_letöltő\, appdata_megjelenítő\   ← további munkaterületek
```
- Az alap munkaterület mappája maga az otthonmappa – **egy 2.x telepítés frissítés után módosítás nélkül működik**.
- Új munkaterület: `munkateruletek\<id>\`, ahol az `id` 8 karakteres véletlen (`[a-z0-9]`); a megjelenített név szabad szöveg.
- Munkaterület csatolása máshonnan (pl. egy másik mappában lévő régi telepítés): csak asztali módban, mappaválasztóval; a nyilvántartás az
  abszolút útvonalat tárolja. Szerveren csak az otthonmappán belüli munkaterület engedélyezett.
- A `Guard` gyökere nem egy mappa, hanem **engedélyezett gyökerek listája**: az otthonmappa és a csatolt munkaterületek. Az írás továbbra is
  csak ezeken belül lehetséges; emellett minden `WorkspaceContext` saját ellenőrzést kap (`ws.Guard`), hogy egy munkaterület kódja ne
  írhasson egy másikéba.
- Linuxon a mappanevek ugyanazok (ékezettel, UTF-8). A fájlnevek kis- és nagybetűre érzékenyek: a kód a `Layout` állandóit használja,
  sehol nem ír be kézzel mappanevet.

### 6.4 Életciklus
- Induláskor csak az alap munkaterület töltődik be; a többi az első használatkor (lusta betöltés).
- Amelyikhez ütemezett feladat tartozik (letöltés, öntözés), az betöltve marad. A többit a rendszer 30 perc tétlenség után kiüríti a
  memóriából (az 1 GB-os NAS miatt); a következő kérés újra betölti a gyorsítótárból.
- Váltás a felületen: a fejlécben legördülő lista. A választás a **munkamenethez** tartozik (más felhasználót nem érint), és a felület
  beállításai közé is mentődik.
- Munkaterület létrehozása, átnevezése, törlése: `admin`. A törlés a mappát nem törli, csak a nyilvántartásból veszi ki, és a mappát
  `törölt_<dátum>_<id>` névre nevezi át (visszahozható); végleges törlés csak kézzel, fájlkezelőből.

### 6.5 API-címek
- Munkaterülethez kötött végpontok: `/api/ws/{id}/…` (pl. `/api/ws/alap/today`). A felület `api()` és `post()` segédfüggvénye teszi elé az
  előtagot – a hívási helyek a `web/` fájljaiban nem változnak.
- Rendszerszintű végpontok: `/api/sys/…` (belépés, munkaterületek, állapot, felhasználók, frissítés).
- A régi `/api/…` címek nem maradnak meg (a felület és a szerver mindig egy verzió; külső felhasználója nincs).
- A munkaterület-azonosító az URL-ben szigorúan `[a-z0-9]{1,16}`; minden más 404. Az azonosítóból **soha** nem képződik közvetlenül
  útvonal: a nyilvántartásból keressük ki a mappát.

---

## 7. A mag részletei

### 7.1 Mi honnan jön (a mai fájlok sorsa)
| Mai fájl | Új hely | Változás |
|---|---|---|
| `kozos/Guard.cs` | `Core/Common/Guard.cs` | több gyökér; `ReplaceRetry` (TERV_4 20.7); a zár fájlalapú (7.4); a telepítő is fordítja → net48-kompatibilis marad |
| `kozos/IniMerge.cs` | `Core/Common/IniFile.cs` | + közös ini-olvasó / -író (ma három helyen van hasonló kód) |
| `kozos/Nyelv.cs` | `Core/Common/Lang.cs` | a rendszer nyelvének lekérdezése a gazdától jön; a telepítő (net48) is fordítja, ezért a JSON-olvasás itt `#if NETFRAMEWORK` ágon a régi `JavaScriptSerializer`, különben az új `Json` |
| `kozos/Utemezes.cs` | `Desktop/…/TaskSchedulerWin.cs` + `Installer` | Windows-specifikus; a magban csak az `ITaskScheduling` felület |
| `megjelenito/Json.cs` (`JW`) | `Core/Common/JsonWriter.cs` | változatlan |
| – | `Core/Common/Json.cs` | új: a `JavaScriptSerializer` pótlása (7.2) |
| `megjelenito/Layout.cs` | `Core/Workspaces/Layout.cs` | `\\` helyett `Path.Combine`; a WebView2-könyvtárak kicsomagolása a Desktopba kerül |
| `megjelenito/Data.cs` | `Core/Data/*.cs` (Col, Flag, Dataset, Tz, Loader) | `Tz`: IANA-név elsődlegesen (7.5); `Loader`: növekményes olvasás (8.3) |
| `megjelenito/Settings.cs`, `Calib.cs`, `Cloud.cs`, `Analysis.cs`, `Drought.cs` | `Core/…` | csak a statikus állapot kerül a környezetbe; `Drought`: `BalanceStepper` kiemelése (11.6) |
| `megjelenito/Api.cs` | `Core/Api/*` | az óriási `switch` útvonaltáblává (10.4); `AppState` → `WorkspaceContext` |
| `megjelenito/Server.cs` | `Core/Api/HttpListenerFront.cs` (asztali) | a hitelesítés kiemelve `IAuth` mögé |
| `megjelenito/Maint.cs` | `Core/…` (ChangeLog, IniEdit, DlSettings) + `IUpdater` (gazda) | a frissítés gazdafüggő |
| `megjelenito/Extras.cs` | `Core/…` (UiSettings, Export, BackupService) | a párbeszédablakok `IFileDialogs` mögé |
| `megjelenito/Program.cs` | `Desktop/Program.cs`, `MainForm.cs` | + távoli mód |
| `megjelenito/SelfTest.cs` | `Core/SelfTest.cs` + `tests/` | a tesztek nagy része egységteszt lesz; az önteszt megmarad |
| `letolto/Letolto.cs` | `Core/Download/*` + `Downloader/Program.cs` | a `Cfg` statikus mezői `DlConfig` példánnyá; a konzol- és értesítéskezelés a gazdában |
| `telepito/Telepito.cs` | `Installer/` | a tartalom zip; új: mód választása, szerver cím (9.4) |

### 7.2 JSON-réteg (`Core.Common.Json`)
A `System.Web.Script.Serialization.JavaScriptSerializer` a modern .NET-ben nincs. A pótlásnak **ugyanazt kell adnia**, mert a meglévő
fájlokat (szarazsag.json, felulet.json, kalibracio.json, naplo.json, lekerdezes_allapot.json …) a régi írta, és a kód a régi alakokra épít.

```csharp
static class Json {
    static string Serialize(object o);                // mezők ÉS tulajdonságok, a deklaráció sorrendjében; null érték kiírva
    static object DeserializeObject(string s);        // Dictionary<string,object> | object[] | string | bool | int | long | decimal | null
    static T Deserialize<T>(string s);                // nyilvános mezők és tulajdonságok; ismeretlen kulcs figyelmen kívül; kis/nagybetű nem számít
}
```
Kötelező viselkedés (mindegyikre teszt):
- számok: egész → `int`, ha belefér, különben `long`; tizedes → `decimal` (a régi így adta; a kód `Convert.ToDouble`-lal olvassa);
- tömb → `object[]` (nem `List`); objektum → `Dictionary<string, object>` (a kulcsok sorrendje megmarad);
- `double.NaN` / végtelen kiírása: `null` (a régi hibát dobott; a kód eddig is kerülte) – olvasáskor `null` → a mező alapértéke marad;
- karakterkódolás: nem ASCII karakter változatlanul (UTF-8), a `<`, `>`, `&` `<` formában (a régi így írta; a fájlok bájtra egyezzenek, ahol lehet);
- méretkorlát és mélységkorlát (alapból 64 MB, 200 szint) – túllépésnél kivétel;
- dátum típus nincs használatban; ha `DateTime` kerülne bele, kivétel (ne keletkezzen `\/Date()\/` alak).
Ellenőrzés: a mintaadat összes beállításfájlját a régi és az új réteggel beolvasva és visszaírva az eredmény egyezik (14.2).

### 7.3 Számformázás nyelv szerint (`NumFmt`)
A NAS-on nincs nyelvi adatbázis (ICU), ezért a mag **nem használhat** `CultureInfo.GetCultureInfo("hu-HU")`-t számformázásra (ma 4 helyen
teszi: `Analysis.cs`, `Extras.cs`, `Letolto.cs`). Helyette `NumFmt.For(lang)`: a tizedesjel és az ezres elválasztó a nyelvi fájl `_meta`
részéből jön (`"dec": ",", "grp": " "`), a formázás saját kóddal. A .NET „invariáns” módban fut (`InvariantGlobalization=true`) minden
platformon – így Windowson és Linuxon biztosan ugyanaz az eredmény.

### 7.4 Fájlírás, zárak
- **Atomi csere újrapróbálással:** `Guard.ReplaceRetry(tmp, cél)` – 8 próbálkozás 0,25–2 s szünettel, végül másolás felülírással; minden
  JSON / ini / CSV mentés ezt hívja (ma 8 hely hív közvetlen `File.Replace`-t).
- **Munkaterület-zár (egy író):** `<munkaterület>\appdata_letöltő\.zar` fájl kizárólagos megnyitással (`FileShare.None`), a futás idejére.
  Ez váltja ki a mai névvel ellátott mutexet (az Linuxon folyamatok között nem megbízható, és két különböző felhasználó nevében futó
  folyamat nem látja egymásét). A zárfájlba a tartó folyamat azonosítója és indulási ideje kerül (diagnosztika).
  Időtúllépés után **nincs** zár nélküli továbbfutás (TERV_4 20.8): hiba.
- **Beállításfájl-zár:** ugyanez a módszer fájlonként (`<fájl>.zar`), rövid ideig.
- A zárfájlok nem kerülnek mentésbe; induláskor az árva zárfájl nem akadály (a zár a megnyitott fájlleíró, nem a fájl léte).

### 7.5 Időzóna
`Tz.Set(iana)`: először az IANA-nevet próbálja (`TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest")` – Linuxon ez a természetes, a
.NET 10 Windowson is érti), ha nem megy, a táblázat szerinti Windows-nevet. Ha egyik sem: hiba a beállításnál, nem csendes visszaesés a
gép időzónájára. A szerver nem függ a gép helyi idejétől: minden számítás a munkaterület időzónájában történik.

### 7.6 Naplózás
`Core.Common.Log`: szintek (hiba, figyelmeztetés, info, részletes), célpont a gazdától függ (fájl forgatással; konzol; a szerveren a
rendszernapló is). Minden sorban: idő (helyi + UTC-eltolás), szint, terület, munkaterület-azonosító. **Titok soha nem kerül naplóba**: a
naplózó az ismert titkok értékét (API-kulcs, helyi kulcs, munkamenet-azonosító) kimenet előtt kitakarja (`Log.RegisterSecret`).
A felület hibái (TERV_4 21.2): `POST /api/sys/clienterror` → ugyanebbe a naplóba, óránként legfeljebb 20 sor munkamenetenként.

---

## 8. Letöltés, ütemező, értesítések

### 8.1 A letöltő mint könyvtár
```csharp
class DownloadEngine {
    DownloadResult Run(WorkspaceContext ws, DownloadOptions opt, IProgress<string> progress, CancellationToken ct);
}
class DownloadResult { int Code; bool NetworkError; int NewRows; int ApiCalls; string Message; }
```
- A mai `WuLetolto.Program.Run` logikája ide kerül; a `Cfg` statikus mezői `DlConfig` példánnyá válnak (a munkaterület
  `beallitasok.ini`-jéből).
- A kilépési kódok (0, 1, 2, 3, 4, 5, 6, 9) megmaradnak `DownloadResult.Code`-ként; a Windows-letöltő ezt adja vissza kilépési kódként.
- Megszakítás: `CancellationToken` (ma a Megjelenítő megöli a folyamatot). A hálózati hívás, a várakozások és a mentések között ellenőrzi.
- A haladás sorai `IProgress<string>`-en mennek (ma a Megjelenítő a Letöltő kimenetét olvassa és szövegre illeszt – ez megszűnik:
  **helyi módban is a mag futtatja a letöltést a saját folyamatában**, a Frissítés gomb nem indít külön programot).
- A külön `Letöltő.exe` megmarad Windowson a Feladatütemezőnek (a gép bekapcsolásakor, a Megjelenítő nélkül is töltsön) és az első
  indítás kérdéseihez; ugyanazt a `DownloadEngine`-t hívja.

### 8.2 A mérések tárolása egy fájlban, kis memóriával (`ObservationStore`) – TERV_4 21.1
```csharp
class ObservationStore {
    void Open(string csvPath);                  // fejléc beolvasása; csonka utolsó sor levágása
    HashSet<long> LoadKeys(int tailDays);       // (állomás, epoch) kulcsok a fájl végéről; 0 = az egész
    void Append(IEnumerable<Row> rows);         // csak a fájl végénél későbbi sorok; fsync a végén
    void MergeRewrite(IEnumerable<Row> rows);   // régebbi / módosult sor, új oszlop: folyamszerű összefésülés ideiglenes fájlba, atomi csere
}
```
- Memória: mérésenként egy 64 bites szám → évi kb. 105 000 kulcs ≈ néhány MB.
- A fájl **mindig idő szerint rendezett** marad (a Megjelenítő erre épít); a hozzáfűzés előtt ellenőrzi, hogy az új sorok később vannak,
  mint az utolsó sor – különben összefésülés.
- Félbeszakadt hozzáfűzés: megnyitáskor az utolsó, sorvég nélküli sor levágódik (naplóbejegyzéssel); a hiányzó mérést a következő
  letöltés pótolja.
- A hozzáfűzés nem atomi csere, hanem közvetlen írás: előtte a zárat tartani kell, utána `Flush(true)`.
- Excelben megnyitva (Windows): a hozzáfűzés megosztott megnyitással megy; ha a fájl kizárólagosan zárolt, a mai újrapróbálás és üzenet marad.
- Oszlopbővítés (a WU új mezőt ad): összefésüléses újraírás az új fejléccel.
- Teszt: 5 évnyi generált adat – memória 100 MB alatt, egy futás 5 s alatt egy lassú gépen (a NAS-on mérve, K0).

### 8.3 Növekményes betöltés a Megjelenítő oldalán
Ma a `Loader` a megváltozott CSV-t teljesen újraolvassa. A NAS lassú processzorán egy 100 MB-os fájl óránkénti újraolvasása sok.
- A gyorsítótár fájlonként eltárolja: a feldolgozott hosszt (bájt), az addigi rész ellenőrzőösszegét (az első és az utolsó 64 kB kivonata),
  a sorok számát.
- Ha a fájl **nőtt**, és az eltárolt rész ellenőrzőösszege egyezik → csak a hozzáfűzött részt olvassa és fűzi az oszlopokhoz.
- Ha rövidebb lett, vagy az ellenőrzőösszeg nem egyezik (összefésüléses újraírás történt) → teljes újraolvasás, mint ma.
- Az aggregátumok (jelölők, napok, hónapok) egyelőre teljesen újraszámolódnak; ha a NAS-on ez 3 s-nál lassabb (K0 mérés), a napi
  aggregátumok csak az utolsó érintett naptól számolódjanak újra (külön munkacsomag, csak ha kell).

### 8.4 Ütemező (`Core.Jobs`) – a szerveren a Feladatütemező helyett
- Feladat: `{ id, munkaterület, típus, időköz vagy időpontok, legutóbbi futás, legutóbbi eredmény, következő futás }`; állapot a
  `rendszer\utemezo.json`-ban (újraindítás után a kimaradt futást pótolja – egyszer, nem annyiszor, ahány kimaradt).
- Típusok: `letoltes` (munkaterületenként; alapból 60 perc, a mai ütemezési beállításokkal), `mentes`, `riasztas` (letöltés után),
  `metar`, `ontozes` (a motor „ketyegése”, 30 s), `takaritas` (naplóforgatás, lejárt munkamenetek, régi futásnaplók).
- Egy munkaterület feladatai **sorban** futnak (egy szál munkaterületenként); különböző munkaterületekéi párhuzamosan.
- Minden feladat: időkorlát, kivételkezelés, ismételt hiba esetén növekvő várakozás (1, 5, 15, 60 perc), a 3. egymást követő hibánál értesítés.
- A feladat a munkaterület-zárat veszi fel (7.4), így a kézi Frissítés és az ütemezett letöltés nem ütközik.
- Asztali (helyi) módban az ütemező csak akkor fut, amikor a program nyitva van; a háttérletöltést továbbra is a Windows Feladatütemező
  + `Letöltő.exe` adja. A kettő a munkaterület-zár miatt nem ütközik.
- Az óra ugrása (nyári időszámítás, kézi átállítás, NTP): az ütemező monoton órával méri az időközöket; a „naponta 6:00-kor” típus a
  munkaterület helyi idejében értendő, a kimaradó órát (tavasz) a következő érvényes időpontban, a duplán előforduló órát (ősz) egyszer futtatja.

### 8.5 Értesítések (`Core.Notify`)
```csharp
interface INotifier { string Id { get; } bool Send(Notification n); }   // n: cím, szöveg, súlyosság, munkaterület, kulcs (ismétlésvédelemhez)
```
- A riasztási szabályok (fagy, nyomásesés, szél, felhőszakadás, hőség; új: „X órája nincs adat” – TERV_4 20.20; öntözési hibák) a magban
  vannak (`Core.Notify.Alerts`, a mai `WuLetolto.Alerts` logikája), a kézbesítés a gazdáé.
- Kézbesítők: `WindowsToast` (Desktop, Downloader), `InApp` (mindig: a felület értesítési listája, `ertesitesek.json`, az utolsó 200),
  `Smtp` (e-mail), `Ntfy` (HTTP POST egy ntfy-kiszolgálóra – telefonos push), `Webhook` (tetszőleges cím), `SynologyDsm` (a NAS saját
  értesítése, ha a `synodsmnotify` parancs elérhető).
- Csendes órák és ismétlésvédelem: a magban, kézbesítőtől függetlenül (ma is megvan).
- A riasztások kiértékelése **a frissen letöltött sorokból** dolgozik (mint ma a Letöltőben), nem a Megjelenítő betöltött adatából –
  így a Windows-letöltő önállóan (nyitott Megjelenítő nélkül) is tud riasztani.
- A kimenő címek (SMTP, ntfy, webhook) csak `admin` által állíthatók; a szerver ezeken kívül **csak** a `weather.com`,
  `aviationweather.gov` (METAR) és a szelepvezérlő helyi címére kezdeményez kapcsolatot (10.7).

### 8.6 Hely a jövőnek: közvetlen fogadás
Ha egyszer lesz olyan router vagy állomás, amely tud helyi szerverre küldeni: a `Core.Download`-ban egy `IObservationSource` felület
(`WuHistorySource` a mai; `WuUploadReceiver` a jövőbeli), mindkettő ugyanabba az `ObservationStore`-ba ír, forrásjelöléssel. A 3.0-ban
csak a felület és a WU-forrás készül el.

---

## 9. Gazdák

### 9.1 Asztali gazda (`WStation.Desktop`) – helyi és távoli mód
- Induláskor beolvassa a `rendszer\kapcsolat.json`-t: `{ "mod": "helyi" | "tavoli", "szerver": "https://nas.local:8443", "ujjlenyomat": "…" }`.
- **Helyi mód:** mint ma – a mag elindul a folyamatban, `HttpListenerFront` a `localhost` véletlen kapuján, indításonkénti azonosítóval;
  a WebView2 erre navigál. Szerep: `admin`.
- **Távoli mód:** nem indít magot; a WebView2 a szerver címére navigál, a belépés a szerver oldalán történik (a munkamenet-süti a
  WebView2 profiljában megmarad). A tanúsítvány ellenőrzése: ha a szerver saját aláírású tanúsítványt használ (10.2), az első
  kapcsolódáskor az ablak megmutatja az ujjlenyomatát, a felhasználó jóváhagyja, és onnantól **csak ezt** fogadja el
  (`ServerCertificateErrorDetected` eseményben az eltárolt ujjlenyomattal vetve össze). Más hibát (lejárt, más ujjlenyomat) nem enged át.
- **Módváltás:** a Beállítások fülön „Kapcsolat” kártya, amely csak az asztali ablakban jelenik meg. A felület a gazdával a WebView2
  üzenetcsatornáján beszél (`window.chrome.webview.postMessage`), csak három üzenettel: `mod-lekerdezes`, `mod-valtas`, `szerver-proba`.
  A gazda csak a saját eredetű (a beállított szerver vagy a `localhost`) oldaltól fogad üzenetet.
  A módváltást és a szervercím módosítását a gazda **saját, natív ablakban erősítteti meg** a felhasználóval (a cím kiírásával) –
  így egy esetleg hibás vagy feltört felület sem tudja a programot szó nélkül egy másik szerverre irányítani.
- Távoli módban a WebView2 csak a beállított szerver eredetén belül navigál; minden más hivatkozás a rendszer böngészőjében nyílik
  meg (csak http / https – a mai szabály).
- Ha távoli módban a szerver nem érhető el: saját (beágyazott) hibaoldal – „Újra” / „Váltás helyi módra” / „Szervercím módosítása”.
- Távoli módban a Windows Feladatütemező letöltési feladata kikapcsol (a szerver tölt le); helyi módra váltáskor a program felajánlja
  a visszakapcsolását.
- A fájl-párbeszédablakok (mentés másként, visszaállítás fájlból, mappaválasztás) csak helyi módban léteznek (`IFileDialogs`); távoli
  módban a böngészős letöltés / feltöltés működik (a WebView2 saját letöltéskezelőjével).
- Egy példány mappánként (marad); második indításkor az ablak előre jön (marad).

### 9.2 Windows-letöltő (`WStation.Downloader`)
Vékony héj: parancssor-értelmezés (a mai kapcsolók megmaradnak: `--nowait`, `--mappa`, `--mentes`, `--most`, `--metar`,
`--teszt-ertesites`), konzolkezelés, első indítás kérdései, `WindowsToast`. Új: `--munkaterulet <id>` (alapból az alap; `--mind` =
mindegyik sorban). A Feladatütemező feladata `--mind --nowait`.

### 9.3 Szerver gazda (`WStation.Server`)
Lásd 10. fejezet. Parancsok: `wstation-server run` (alapértelmezett), `setup` (első beállítás konzolról), `reset-admin` (jelszó
visszaállítása – csak a gépen ülve / SSH-n), `download --once`, `selftest`, `version`.

### 9.4 Telepítő (`WStation.Installer`) – marad .NET Framework 4.8
- Indok: a telepítőnek .NET 10 nélkül is el kell indulnia; a 4.8 minden támogatott Windows része. A telepítő kódja (áttelepítés, ütemezés,
  parancsikon, visszaállítás hibánál) kiforrott – nem írjuk újra.
- Változás: a beágyazott két exe helyett egy **zip** (a `Megjelenítő.exe`, `Letöltő.exe` és a közös `app\` futtatókörnyezet-mappa);
  kibontás ideiglenes mappába, majd a mai „egyben cserél, hibánál visszaáll” lépés.
- A telepítő a magból néhány fájlt **forrásként** fordít magába (`Guard.cs`, `Lang.cs`, `IniFile.cs`): ezek tetején megjegyzés áll
  (`// NET48: a telepítő is fordítja – C# 7.3, csak .NET Framework 4.8-ban is meglévő API`), és a CI a telepítőt is fordítja, tehát ha
  valaki ezekbe új nyelvi elemet tesz, a fordítás elbukik.
- Új kérdés a telepítőben: „Önálló használat ezen a gépen” / „Kapcsolódás egy WStation-szerverhez (cím)”. Az utóbbi a
  `kapcsolat.json`-t távoli módra állítja, és nem hoz létre letöltési feladatot.
- Méret: a futtatókörnyezet miatt a telepítő kb. 60–80 MB (ma kb. 3 MB). A Megjelenítő és a Letöltő **ugyanazt** a futtatókörnyezet-mappát
  használja (nem kétszer szállítjuk).
- **A telepített mappa rendezett marad – indítóprogramok.** A .NET 10-es programok sok fájlból állnak (a program, a mag, a futtatókörnyezet
  több száz fájlja). Ezek mind az `app\` almappába kerülnek. A mappa gyökerében csak két apró **indító** van a megszokott, nyelvfüggő néven
  (`Megjelenítő.exe`, `Letöltő.exe` / `Viewer.exe`, `Downloader.exe` …): ezek .NET Framework 4.8-as, néhány soros programok (a telepítővel
  egy eszközlánc), amelyek elindítják az `app\WStation.Desktop.exe`-t, ill. az `app\WStation.Downloader.exe`-t ugyanazokkal a kapcsolókkal.
  A letöltő indítója megvárja a programot, és **továbbadja a kilépési kódját** (a Feladatütemező és a régi szkriptek ezt figyelik); a
  konzolt a gyerekfolyamat örökli. A parancsikon, az ütemezett feladat és a `FindDownloader` így változatlan nevekkel működik.
  (Vizsgálandó egyszerűsítés: a .NET saját megoldása a futtatókörnyezet almappából való betöltésére – csak ha bizonyítottan működik.)
- A 2.x → 3.0 frissítés: a telepítő a régi exe-ket és a régi WebView2-könyvtárakat félreteszi (mint eddig), a mappaszerkezet változatlan,
  létrehozza a `rendszer\munkateruletek.json`-t az alap munkaterülettel.

---

## 10. Szerver mód és biztonság

### 10.1 Mit védünk, kitől
| Érték | Miért fontos |
|---|---|
| A szelepek vezérlése | Víz, pénz, kár a kertben vagy a házban. **Ez a legfontosabb.** |
| A NAS épsége | A szerver a NAS-on fut: egy hiba ne adjon hozzáférést a NAS többi adatához. |
| Titkok | WU API-kulcs, a szelepvezérlő helyi kulcsa, értesítési jelszavak, a felhasználók jelszavai. |
| A mérések és beállítások | Nem titkosak, de ne vesszenek el és ne lehessen elrontani őket. |

| Támadó | Mit tud | Védekezés |
|---|---|---|
| Másik eszköz a belső hálózaton (fertőzött okoseszköz, vendég) | eléri a szerver kapuját, lehallgathat titkosítatlan forgalmat | belépés, titkosított kapcsolat, próbálkozás-lassítás, szűk felület (10.2–10.4) |
| Rosszindulatú weboldal a felhasználó böngészőjében | kéréseket küldhet a belső hálózatra a felhasználó nevében | SameSite=Strict süti, saját fejléc + Origin-ellenőrzés, gépnév-ellenőrzés (DNS-átkötés ellen), CSP |
| Ellopott munkamenet / nyitva hagyott gép | a munkamenet jogaival bármit | rövidebb admin-munkamenet, kényes műveletnél jelszó újra, „kijelentkezés mindenhol” |
| Hamisított fájl (mentés, beállítás) | visszaállításkor a programba kerül | a visszaállítás ma is csak ismert helyre, méretkorláttal, tisztítva bont ki – marad |
| Hibás vagy hamis hálózati válasz (WU, METAR, szelepvezérlő) | értelmezési hiba, túl nagy válasz | méretkorlát, szigorú értelmezés, időkorlát; a szelepnél titkosított, hitelesített protokoll |
| Az internet | – | a szerver nem internetre szánt; a leírás kifejezetten tiltja a kapu kinyitását, távolról VPN |

Ami **nem** cél: védelem attól, aki rendszergazdaként hozzáfér a NAS-hoz vagy fizikailag a lemezekhez.

### 10.2 Kapcsolat (titkosítás)
A `rendszer\szerver.json` `kapcsolat` mezője:
| Mód | Működés | Mikor |
|---|---|---|
| `https-sajat` (**alap**) | A szerver az első beállításkor saját tanúsítványt készít (ECDSA P-256, a megadott gépnevekre és IP-címre), és maga szolgál ki HTTPS-t (alapból a 8443-as kapun). A böngésző először figyelmeztet; a Windows-program az ujjlenyomatot rögzíti (9.1). | Otthoni hálózat, külön beállítás nélkül. |
| `proxy` | A szerver csak a `127.0.0.1`-en figyel, titkosítatlanul; előtte a NAS saját fordított proxyja áll valódi tanúsítvánnyal. Az `X-Forwarded-*` fejléceket **csak** a helyi gépről jövő kérésnél veszi figyelembe. | Ha a NAS-on van érvényes tanúsítvány (pl. Synology DDNS). |
| `http-lan` | Titkosítatlan a belső hálózaton. Kifejezetten be kell kapcsolni; a felületen állandó figyelmeztetés látszik; **a szelepvezérlés, a jelszóváltás és a titkok szerkesztése ebben a módban tiltott.** | Csak próbához. |

- **Gépnév-ellenőrzés:** a kérés `Host` fejléce csak a beállított nevek egyike lehet (`engedelyezettNevek`: pl. `nas.local`, `192.168.1.20`),
  különben 421-es válasz. Ez véd a DNS-átkötéses támadástól (a mai `localhost`-ellenőrzés általánosítása).
- **Csak belső címen figyel:** a szerver induláskor megnézi a gép hálózati címeit, és csak magánhálózati címekhez (10/8, 172.16/12,
  192.168/16, IPv6 helyi) köt. Ha a gépnek nyilvános címe is van, azon nem figyel; ha csak nyilvános címe van, nem indul el, hanem
  érthető hibát ad (felülbírálni csak a `szerver.json` kézi szerkesztésével lehet, a felületről nem).
- TLS 1.2 és 1.3; a tanúsítvány 5 évre szól, lejárat előtt 60 nappal értesítés; csere parancssorból (`wstation-server renew-cert`),
  ilyenkor a Windows-program újra megerősítést kér.
- Válaszfejlécek minden oldalon: a mai CSP, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`,
  `Cache-Control: no-store` az API-n, `Strict-Transport-Security` `proxy` módban. CORS nincs (más eredet nem hívhatja).

### 10.3 Belépés és munkamenetek
- **Két szerep, két jelszó** (D3): `admin` és `nezo`. A belépőoldalon csak jelszómező van; a szerepet az dönti el, melyik jelszó egyezik.
  A két jelszó nem lehet azonos. A `nezo` szerep kikapcsolható. (A fájlformátum felhasználólistát tárol, így később névvel ellátott
  felhasználók is hozzáadhatók a formátum törése nélkül.)
- **Jelszótárolás:** PBKDF2-HMAC-SHA256, 16 bájt só, formátum: `pbkdf2-sha256$<körök>$<só>$<kivonat>`. A körök számát a szerver a
  jelszó beállításakor **a saját gépén méri ki** úgy, hogy egy ellenőrzés kb. 300 ms legyen, de legalább 150 000 (egy gyors gépen ez több
  millió, a NAS lassú ARM-processzorán kevesebb – a szám a kivonatban tárolódik, így gépcsere után is ellenőrizhető, és a következő
  jelszóváltáskor újramérődik). Belépéskor mindkét szerep kivonata kiszámolódik (az időzítés ne árulja el, melyik egyezett), az összevetés
  állandó idejű. Egyszerre egy jelszóellenőrzés fut – a többi sorban áll, legfeljebb 10; fölötte 429-es válasz. A gyenge processzor miatt
  a jelszó hossza a fő védelem: ezért a 12 karakteres alsó határ az adminnál.
- **Jelszószabály:** admin legalább 12 karakter, néző legalább 8; a 20 leggyakoribb jelszó és a „wstation” elutasítva. Nincs kötelező
  karakterosztály.
- **Első beállítás:** amíg nincs admin-jelszó, a szerver **csak a beállító oldalt** szolgálja ki, és az egyszeri kódot kér, amely a
  `rendszer\elso_inditas_kod.txt` fájlban és a konzolon / naplóban van (aki a NAS fájljaihoz hozzáfér, az a jogosult). A kód 30 percig
  érvényes, 5 hibás próbálkozás után új kód készül. Alternatíva: `wstation-server setup` a konzolról.
- **Elfelejtett jelszó:** `wstation-server reset-admin` a gépen (SSH / feladatütemező). A felületről nincs visszaállítás.
- **Munkamenet:** 32 véletlen bájt; a szerver csak a SHA-256 kivonatát tárolja (`munkamenetek.json`), szereppel, létrehozással, utolsó
  használattal, IP-címmel. Süti: `__Host-wst` (HTTPS-en; `Secure`, `HttpOnly`, `SameSite=Strict`, `Path=/`). Érvényesség: néző 30 nap
  csúszó, admin 7 nap csúszó, mindkettő legfeljebb 90 nap. Jelszóváltáskor az adott szerep összes munkamenete megszűnik.
- **Megerősítés jelszóval („kényes művelet”):** ha az admin utolsó jelszómegadása 15 percnél régebbi, ezeknél újra kéri:
  jelszavak és szerepek, kapcsolati mód, szelepvezérlő hozzáadása / helyi kulcs, az öntözési motor élesítése (árnyék → éles),
  visszaállítás mentésből, munkaterület törlése, értesítési célcímek.
- **Próbálkozás-lassítás:** IP-címenként az 5. hibás belépés után növekvő várakozás (2, 4, 8 … legfeljebb 300 s); ha 10 perc alatt
  összesen 30 hibás próbálkozás van, minden belépés 5 s késleltetést kap, és az admin értesítést kap. Fiókzárolás nincs (azzal ki lehetne
  zárni a gazdát).
- **Helyi mód:** nincs jelszó; marad a mai megoldás (indításonkénti azonosító, csak `localhost`), szerep: `admin`.
- A hitelesítés a mag `IAuth` felülete mögött van: `DesktopTokenAuth` (mai) és `PasswordAuth` (szerver). A kezelők csak a kapott
  szerepet látják.

### 10.4 Útvonaltábla – alapból minden tiltva
A mai `switch (path)` helyett egy deklaratív tábla; **ami nincs benne, 404**:
```csharp
Route.Get ("/api/ws/{ws}/today",            Role.Nezo,  Handlers.Today);
Route.Post("/api/ws/{ws}/drought/config",   Role.Admin, Handlers.DroughtConfig, Audit);
Route.Post("/api/ws/{ws}/irrigation/run",   Role.Admin, Handlers.IrrManualRun,  Audit | Encrypted);
Route.Post("/api/sys/users/password",       Role.Admin, Handlers.SetPassword,   Audit | Sudo | Encrypted);
Route.Post("/api/ws/{ws}/backup/restore",   Role.Admin, Handlers.Restore,       Audit | Sudo | MaxBody(200 MB));
Route.Post("/api/ws/{ws}/export",           Role.Admin, Handlers.ExportDialog,  DesktopOnly);
```
Jelzők: `Audit` (naplózás), `Sudo` (10.3), `Encrypted` (csak HTTPS-en vagy a saját gépről), `DesktopOnly` / `ServerOnly`,
`MaxBody(n)` (alapból 1 MB), `Slow` (hosszú művelet – háttérben fut, állapotlekérdezéssel).
Szabályok, mindegyikre automatikus teszt (14.4):
- GET soha nem módosít (a kezelő csak olvasó környezetet kap);
- nem GET kéréshez kell az `X-Idojaras: 1` fejléc és – ha van `Origin` – annak egyeznie kell a saját eredettel (marad);
- a `nezo` szerep csak GET-et hívhat, kivéve: saját felület-beállításai, kijelentkezés, felülethiba jelentése;
- a néző válaszaiból kimarad, ami nem rá tartozik (pl. a `/api/sys/status` nem adja vissza az útvonalakat, a naplót);
- hibaválasz: `{ "error": "<érthető üzenet>", "id": "<naplóazonosító>" }` – veremkivonat, útvonal, belső név soha (a `JW` íróval, TERV_4 20.4).
- A felület beállításai (`felulet.json`) szerepenként külön fájlban vannak: a néző témaváltása nem írja át az adminét.

Szerver-oldali korlátok (Kestrel): fejlécek 10 s alatt, törzs legalább 240 bájt/s, kérés legfeljebb 60 s (kivéve `Slow`), legfeljebb
100 kapcsolat, 32 kB fejléc. Feltöltés (visszaállítás) lemezre folyik, nem memóriába.

A mag és a két HTTP-előtét közti szerződés (a mag nem tud arról, melyik szerver hívja):
```csharp
class ApiRequest  { string Method, Path; NameValueCollection Query; string Host, Origin; bool Encrypted, Loopback; string RemoteIp;
                    string CookieValue; Func<Stream> Body; long? ContentLength; }
class ApiResponse { int Status; string ContentType; Dictionary<string,string> Headers; byte[] Body; Action<Stream> WriteBody; }  // nagy válasz (zip, csv) folyamként
interface IApiFront { void Start(FrontOptions o, Func<ApiRequest, ApiResponse> handler); void Stop(); }   // HttpListenerFront (asztali), KestrelFront (szerver)
```

### 10.5 Titkok
| Titok | Hol van | Megjegyzés |
|---|---|---|
| WU API-kulcs | a munkaterület `appdata_letöltő\beallitasok.ini` fájljában (mint ma) | a munkaterület mappája így hordozható marad Windows és NAS között |
| Szelepvezérlő helyi kulcsa, értesítési jelszavak (SMTP, ntfy) | a munkaterület `appdata_megjelenítő\titkok.json` fájljában | Linuxon a fájl joga 600; a szerver saját, jogosultság nélküli felhasználóként fut |
| Jelszókivonatok, munkamenetek, a tanúsítvány kulcsa | `rendszer\` | 600; mentésbe **nem** kerül |

- Az API **soha nem adja vissza** egy titok értékét: olvasáskor `{ "beallitva": true }`; íráskor az üres érték azt jelenti: marad.
- A mentés letöltése / másolása alapból **titkok nélküli** (D8); „teljes mentés” csak adminnak, megerősítéssel. A szerveren tárolt
  ütemezett mentések teljesek (hogy a visszaállítás működjön), és a munkaterületen belül maradnak.
- A naplózó kitakarja az ismert titkokat (7.6). A változásnapló titkot nem tárol (csak azt, hogy változott).
- Visszaállításkor a **helyben lévő titkok maradnak** (API-kulcs – ma is így van –, és ugyanígy a `titkok.json`); a mentésből csak akkor
  jönnek, ha a munkaterületen még nincs ilyen, és a mentés teljes volt.
- Az API-kulcs megadása és cseréje (új munkaterület varázsló, Beállítások) külön végpont (`ws/dlsettings/apikey`, POST, admin,
  `Encrypted`, `Audit`); a kulcs a kérésben megy, válaszban soha nem jön vissza.
- Lemezen titkosítás nincs: a NAS-on nincs olyan kulcstár, amely a szerver felhasználójától elkülönítené a kulcsot, így a titkosítás csak
  látszat lenne. A védelem a fájljog és a külön felhasználó.

### 10.6 Synology (és általában Linux) üzemeltetés
- **Telepítés:** a `wstation-server-…-linux-arm64.tar.gz` kibontása pl. a `/volume1/WStation/app` mappába; az adatok a
  `/volume1/WStation/adat` mappában (ez az otthonmappa). Indítás a DSM Feladatütemezőjével („rendszerindításkor”, saját felhasználó
  nevében) a `packaging/synology/indit.sh` szkripttel, amely újraindítja a szervert, ha kilép. Később: natív `.spk` csomag.
- **Felhasználó:** külön `wstation` felhasználó, amelynek csak a `/volume1/WStation` mappához van joga. A megosztott mappa a DSM-ben
  csak a rendszergazdának legyen látható (benne vannak a titkok).
- **Memória (1 GB-os NAS, ebből kb. 120 MB szabad – K0):** munkaállomás-szemétgyűjtés, memóriatakarékos beállítás, kemény korlát 250 MB (`runtimeconfig`), a nem
  használt munkaterületek kiürítése (6.4). Cél: nyugalomban 120 MB alatt egy 3 éves munkaterülettel.
- **Lemezkímélés:** a naplók pufferelve íródnak (legfeljebb 5 percenként, hiba és öntözési esemény azonnal); állapotfájl csak
  változáskor; a letöltés gyakorisága állítható. Így a lemezek a letöltések között aludhatnak.
- **A futtatókörnyezet és a régi rendszerkönyvtár (glibc) – a K0 kapu dönti el:**
  1. `.NET 10 linux-arm64` elindul → ez a végleges.
  2. Ha a NAS glibc-je túl régi (a közösségi adatok szerint a DSM 7.1-ben 2.26, a .NET 10-hez 2.27 kell): `linux-musl-arm64` kiadás, a
     csomagban szállított musl-betöltővel indítva (`./lib/ld-musl-aarch64.so.1 --library-path ./lib ./wstation-server`).
  3. Ha az sem megy: a csomag saját, újabb glibc-t visz magával, és azzal indul (ugyanilyen módon).
  4. Ha egyik sem: a szerver egy olcsó minigépen fut (x64 vagy ARM Linux, bármi, ami .NET 10-et futtat), a NAS csak tárhely.
  A .NET 8 nem megoldás: 2026 novemberétől nem kap javításokat.
  **A K0 döntése (2026-11-17): az 1. út**, DSM 7.2-től (B. függelék, „A K0 eredménye”). DSM 6.2-n nem indul.
- **Frissítés:** kézzel – új csomag letöltése, SHA-256 ellenőrzés, a `frissit.sh` leállítja a szervert, az `app` mappát félreteszi
  (`app.elozo`), kibontja az újat, elindítja; ha 60 s-on belül nem válaszol az állapotlekérdezés, visszaáll az előzőre.
- **Leállás:** SIGTERM-re a szerver befejezi a futó mentést, **lezár minden nyitott zónát**, kiírja az állapotot, és legfeljebb 20 s alatt kilép.

### 10.7 Kimenő kapcsolatok
A szerver csak ezekhez kapcsolódik: `api.weather.com` (HTTPS, tanúsítvány-ellenőrzéssel), `aviationweather.gov` (ha a METAR be van
kapcsolva), a beállított értesítési címek, a szelepvezérlő címe (csak magánhálózati IP-tartomány: 10/8, 172.16/12, 192.168/16).
Értesítési cím csak `https://` lehet, kivéve ha az admin kifejezetten engedélyez egy belső hálózati `http://` címet. Átirányítást a
szerver nem követ más gépre. Nincs telemetria, a felület nem tölt be külső erőforrást (CSP).

### 10.8 Ellenőrzési napló
`rendszer\naplo\audit.log` (soronként egy JSON): idő, szerep, munkamenet első 6 karaktere, IP, művelet, cél, eredmény. Ide kerül:
belépés / hibás belépés / kijelentkezés, minden `Audit` jelzős útvonal, minden szelepparancs (forrással: program, automata, kézi, helyreállítás).
Forgatás 5 MB-nál, 10 fájl. Csak admin olvashatja a felületen.

---

## 11. Öntözés

### 11.1 Alapelvek
1. **Kétszeres zárás:** minden nyitás időkorlátját az eszköz is tartja (az eszköz saját visszaszámlálója), és a motor is zár a végén.
   Ha a motor, a NAS vagy a wifi közben leáll, a víz akkor is elzáródik.
2. **Tiszta döntés, mellékhatásos végrehajtás:** a tervező (`Planner`) tiszta függvény – ugyanazokra a bemenetekre ugyanazt adja,
   nem olvas órát, fájlt, hálózatot. A végrehajtó (`Engine`) beszél az eszközzel. A szimuláció a tervezőt hívja – **ugyanazt a kódot**,
   amelyik élesben dönt.
3. **Minden döntés megmagyarázható:** a napló megmondja, melyik szabály miatt maradt ki, csúszott vagy rövidült egy locsolás.
4. **Kemény korlátok a szabályok fölött:** napi és futásonkénti felső határok, amelyeket semmilyen program, szabály vagy automata nem léphet át.
5. **Előbb árnyékban:** az új motor először csak naplózza, mit tenne; élesíteni külön, jelszóval megerősített lépés.

### 11.2 Hol fut
- Az automatikus végrehajtás (`Engine`) **csak szerver gazdában** fut (mindig bekapcsolt gép kell). A szerver gazda Windowson is futtatható.
- Asztali helyi módban: tervezés, szabályok, szimuláció, napló megtekintése; **kézi indítás** is lehetséges (az eszköz időkorlátjával),
  ha nincs másik gép, amely a vezérlőt bérli (11.8).
- Egy vezérlőt egyszerre egy motor vezérel: bérletfájl a munkaterületen (`ontozes.berlet`: gépazonosító, szívverés 30 s-onként);
  2 percnél frissebb idegen bérlet mellett a motor nem indul, és nem küld parancsot. A bérlet az **ugyanazt a munkaterület-mappát**
  használó folyamatokat védi egymástól (pl. kétszer elindított szerver). Ha egy másik gép a saját munkaterületéből nyit zónát (helyi
  módú Windows-program, a SmartLife alkalmazás, a doboz gombja), azt a motor **külső indításként** kezeli (11.7) – a korlátok arra is
  érvényesek. Távoli módban a kézi indítás mindig a szerveren át megy, tehát ott nincs második vezérlő.

### 11.3 Adatmodell (`appdata_megjelenítő\ontozes.json`, sémaverzióval)
```
Vezerlo   { id, nev, meghajto: "tuya-helyi" | "szimulalt", cim, eszkozAzonosito, protokoll, profil, biztonsagiOsztaly, utolsoProba }
Zona      { id, nev, vezerloId, csatorna, szarazsagProfilId, tipus: "szorofej" | "rotor" | "csepegteto",
            intenzitas_mm_h | hozam_l_h, terulet_m2, maxPercFutas, maxPercNap, maxLiterNap, aztatas: { szakaszPerc, szunetPerc },
            mod: "ki" | "kezi" | "program" | "automata", idoablakok: [ { tol, ig } | { napnyugtaUtan: perc, napkelteElott: perc } ] }
Program   { id, nev, zonaId, napok | gyakorisag, kezdes, mennyiseg, egyseg: "perc" | "mm" | "liter", idoszakok }     ← a mai DProgram + DSchedule
Szabaly   { id, nev, aktiv, zonak: [id] | "mind", ha: Feltetel, akkor: Muvelet }                                       ← 11.5
Automata  { zonaId, inditas: "stresszhatar" | szazalek, cel_szazalek, minNapKoz, maxNapKoz }
Korlat    { maxLiterNapOssz, maxEgyideju: 1, zonakKoztiSzunetMp: 10, adatMaxKorPerc: 180, adatNelkul: "alapprogram" | "kihagy" }
Allapot   { mod: "ki" | "arnyek" | "eles", szunetIg: dátum | null }
```
- A mai öntözési programok (`szarazsag.json` `programs` / `schedules`) átkerülnek ide (migráció, 13.2); a kézi locsolási napló marad a
  szárazság-beállításban, és a végrehajtott futások automatikusan bekerülnek a talajvíz-mérlegbe.
- **Egységek (TERV_4 18.7):** a zóna intenzitása (mm/óra) vagy hozama (l/óra) és területe alapján perc ↔ mm ↔ liter mindig átváltható; a
  felület mindhármat mutatja.
- **Főszelep / szivattyú:** a vezérlő kaphat egy `mesterCsatorna` értéket; ezt a motor minden zónanyitás előtt nyitja (ugyanúgy
  időkorláttal), és az utolsó zóna zárása után zárja.
- **Öntözési nap:** a napi korlátok (`maxPercNap`, `maxLiterNap`, `maxLiterNapOssz`) nem naptári napra, hanem **déltől délig** (helyi
  idő) számítanak – így egy éjszakán átnyúló locsolás egy napnak számít, és az éjfél nem „nullázza” a keretet a locsolás közepén.

### 11.4 Meghajtó-felület
```csharp
interface IValveDriver {
    DriverCaps Caps { get; }                                  // SajatIdokorlat, Visszaolvasas, AllapotErtesites, Esoerzekelo
    Task<ZoneState[]> ReadAllAsync(CancellationToken ct);     // minden csatorna: nyitva / zárva / ismeretlen, hátralévő idő
    Task<CmdResult> OpenAsync(int csatorna, TimeSpan idokorlat, CancellationToken ct);   // az időkorlát az ESZKÖZBE megy
    Task<CmdResult> CloseAsync(int csatorna, CancellationToken ct);
    Task<CmdResult> CloseAllAsync(CancellationToken ct);
    event Action<ZoneState> StateChanged;                     // ha az eszköz magától jelez
}
```
- `szimulalt`: memóriában; teszthez, árnyék módhoz, hardver nélküli fejlesztéshez. Hibák befecskendezhetők (nem válaszol, nyitva ragad).
- `tuya-helyi`: TCP a 6668-as kapun, 3.3 / 3.4 / 3.5 protokoll, a helyi kulccsal titkosítva; folyamatos kapcsolat szívveréssel; az
  eszköz adatpontjait (melyik szám melyik zóna kapcsolója, melyik az időkorlát) egy **eszközprofil** írja le (JSON, a `profil` mező), mert
  ez gyártónként más. A profil az eszköz megérkezése után készül egy „adatpont-felderítő” segédeszközzel (állapot lekérdezése, kapcsolás
  az alkalmazásból, különbség).
- **Biztonsági osztály:** `A` = az eszköz bizonyítottan magától zár (lásd a próbát); `B` = nem zár magától vagy nem ellenőrizhető.
  **`B` osztályú vezérlőn nincs automatikus üzem**, csak kézi indítás, legfeljebb 10 percre, figyelmeztetéssel.
- **Biztonsági próba** (a vezérlő hozzáadásakor és kérésre): 1) a motor megnyit egy zónát 60 s időkorláttal; 2) szándékosan bontja a
  kapcsolatot és 100 s-ig nem küld semmit; 3) újrakapcsolódik és visszaolvassa: ha a zóna zárva van → `A` osztály. A próba közben a
  felület kiírja, mi történik (kb. 2 perc, egy zóna 1 percig locsol).

### 11.5 Szabályok
Deklaratív, nem programnyelv – így ellenőrizhető, lefordítható, és nem lehet vele kárt okozni.
```
Feltetel := { mind: [Feltetel…] } | { barmelyik: [Feltetel…] } | { valtozo, ablak?, op, ertek }
op       := "<" | "<=" | ">" | ">=" | "==" | "!=" | "kozott"
Muvelet  := { kihagy } | { halaszt: percenkentUjra, legkesobb: "ablakVege" | perc, utana: "kihagy" | "futtat" }
          | { szoroz: szazalek } | { maxPerc: n } | { engedelyez }      (az „engedelyez” automata módban ad zöld utat)
```
| Változó | Jelentés | Ablak |
|---|---|---|
| `eso` | lehullott csapadék, mm | `1h`, `6h`, `24h`, `48h`, `7nap` |
| `esoMost` | pillanatnyi intenzitás, mm/h | – |
| `szel`, `lokes` | átlagszél és legnagyobb lökés, km/h | `10perc`, `30perc`, `1h` |
| `homerseklet`, `homersekletMin` | most, ill. az ablak minimuma, °C | – / `6h`, `24h` |
| `kiszaradas` | a zóna profiljának kiszáradása, % | – |
| `hiany` | a zóna vízhiánya, mm | – |
| `parolgasMa`, `parolgas7nap` | ET₀, mm | – |
| `napszak` | `ejjel` / `nappal` (napkelte–napnyugta) | – |
| `ora`, `honap`, `hetNapja` | helyi idő | – |
| `adatKor` | az utolsó mérés kora, perc | – |
| `eloreEso` | várható csapadék (csak ha lesz előrejelzés – TERV_4 20.21; addig nincs) | `24h` |

Kiértékelés: minden aktív, a zónára vonatkozó szabály kiértékelődik. Elsőbbség: `kihagy` > `halaszt` > `szoroz` / `maxPerc`; több
`szoroz` összeszorzódik, az eredmény 0–200% közé szorítva. A kimenet: a művelet **és a szabályok listája az értékekkel** (a naplóhoz).
A szabály mentéskor ellenőrződik (ismert változó, értelmes tartomány, ablak a változóhoz illik).

**Gyári szabálykészlet** (szerkeszthető, kikapcsolható):
| Szabály | Ha | Akkor |
|---|---|---|
| Elég eső esett | `eso[24h] ≥ 5` | kihagy |
| Épp esik | `esoMost > 0` | halaszt 30 percenként, az ablak végéig, utána kihagy |
| Szeles (csak szórófej / rotor) | `szel[10perc] ≥ 15` vagy `lokes[10perc] ≥ 30` | halaszt 10 percenként, az ablak végéig, utána kihagy |
| Fagyveszély | `homerseklet < 3` | kihagy |
| Még nedves a talaj | `kiszaradas < 20` | kihagy |
| Félig nedves | `kiszaradas` 20 és 40 között | szoroz 50% |
| Kánikula | `parolgas7nap ≥ 35` | szoroz 120% |

Az idősáv („csak éjjel”) nem szabály, hanem a zóna `idoablakok` beállítása: a tervező csak ablakon belül indít, és az ablak végén túl
nem locsol (a futás az ablak végén leáll).

**Régi adat** (`adatKor > adatMaxKorPerc`): a szabályok nem értékelhetők megbízhatóan. Alapértelmezés: az alapprogram lefut változtatás
nélkül (a növény ne száradjon ki egy internetkimaradás miatt), a kemény korlátokon belül, és értesítés megy. Állítható `kihagy`-ra.

### 11.6 Tervező, automata mód
```csharp
static class Planner {
    // tiszta függvény: a „most” és minden adat paraméterben jön
    static Plan Decide(PlanInput input);
}
class PlanInput { DateTime Most; IWeatherView Ido; ZoneSnapshot[] Zonak; Program[] Programok; Szabaly[] Szabalyok;
                  Automata[] Automatak; Korlat Korlat; RunHistory Elozmeny; }
class Plan      { PlannedRun[] Futasok; Decision[] Dontesek; }      // Dontes: zóna, eredmény, magyarázat (szabály + érték)
```
- `IWeatherView` csak a `Most` előtti méréseket adja ki (a szimulációban ez garantálja, hogy a döntés nem lát a jövőbe).
- **Program mód:** a program szerinti indulási időpontban a tervező alkalmazza a szabályokat → fut / rövidül / csúszik / kimarad.
- **Automata mód:** naponta, a zóna időablakának kezdetén: ha a kiszáradás elérte az indítási küszöböt (alapból a stresszhatár), és az
  utolsó locsolás óta eltelt a legkisebb napköz, a tervező annyi vizet ad, amennyi a célig (alapból 10% kiszáradás) kell – a zóna
  intenzitásából percre váltva, az áztatási szakaszokra bontva, a korlátokon belül. A szabályok ugyanúgy érvényesek (szél → halaszt stb.).
  Ha a leghosszabb napköz letelt, a küszöb alatt is locsol egy minimális adagot (védelem a modell tévedése ellen).
- **Talajvíz-mérleg léptető (`BalanceStepper`):** a `Drought.Compute` profilciklusából kiemelt tiszta lépés:
  `Step(state, napiBemenet, ontozes_mm) → state'`. Ugyanezt használja a mai számítás, az automata tervező (a mai állapothoz) és a
  szimuláció (napról napra, visszacsatolással). A kiemelés után a `Drought.Compute` eredménye bitre egyezzen a mostanival (golden teszt).
- **Áztatás (cycle & soak):** hosszú locsolás szakaszokra bontva (pl. 3 × 8 perc, 30 perc szünettel) – lejtőn, kötött talajon nem folyik el.
- **Sorrend:** egyszerre egy zóna (a víznyomás miatt; `maxEgyideju` állítható), zónák között 10 s szünet; ütközésnél a korábban
  tervezett megy előbb.
- **Friss adat a döntéshez:** az óránkénti letöltés a szél- és esőszabályokhoz kevés (a döntéskor akár egy órás lenne az adat). Ezért ha
  5 percen belül esedékes egy futás (vagy egy halasztott futás újrapróbája), és az utolsó mérés 10 percnél régebbi, a motor egy
  **pillanatnyi állapot** lekérést kér a letöltőtől (egy API-hívás; munkaterületenként legfeljebb 5 percenként egy, a napi kereten
  belül). A tervező ezután fut. Ha a lekérés nem sikerül, a „régi adat” szabálya dönt (11.5). A szimulációban ugyanez a múltbeli
  5 perces mérésekkel történik.

### 11.7 Végrehajtó (`Engine`)
30 másodpercenként fut (ütemező, 8.4). Lépései: bérlet megújítása → eszközállapot beolvasása → folyamatban lévő futások kezelése →
tervező hívása → esedékes futások indítása.

Egy futás állapotgépe:
```
TERVEZETT ─(esedékes, feltételek rendben)→ INDÍTÁS ─(parancs + eszköz-időkorlát elküldve)→ ELLENŐRZÉS ─(visszaolvasva: nyitva)→ FUT
   │ (szabály: kihagy)                         │ (3 sikertelen próbálkozás)                     │ (10 s alatt nem nyitott)          │ (idő letelt / ablak vége / vészleállítás)
   ▼                                           ▼                                                ▼                                    ▼
KIHAGYVA                                   SIKERTELEN + értesítés                           zárás, SIKERTELEN                 ZÁRÁS ─(visszaolvasva: zárva)→ KÉSZ
                                                                                                                                   │ (nem igazolható)
                                                                                                                                   ▼
                                                                                                        RIASZTÁS: zárás ismétlése 30 s-onként 30 percig + sürgős értesítés
```
- **Előre írt napló:** a parancs elküldése **előtt** a motor bejegyzi a szándékot (futásazonosító, zóna, tervezett vég) a
  `ontozes_naplo.jsonl` fájlba, lemezre kiírva. Újrainduláskor a befejezetlen futásokat az eszköz állapotával veti össze: nyitva és a terv
  szerint még futnia kell → átveszi; nyitva és lejárt → zár; zárva → „megszakadt”.
- **Induláskori rendrakás:** a motor indulásakor minden olyan nyitott zónát lezár, amelyhez nincs érvényes futása – kivéve, ha a zónát
  kívülről indították (lásd alább).
- **Külső indítás** (valaki a SmartLife alkalmazásból vagy a doboz gombjával nyit): a motor észreveszi (állapot-visszaolvasás), „külső
  futásként” naplózza, a zóna `maxPercFutas` korlátját erre is érvényesíti (lejártakor zár), és a napi keretbe beszámítja.
- **Az eszköz-időkorlát** mindig a tervezett időtartam + 60 s (a motor zár előbb; az eszköz a tartalék).
- **Pontos zárás:** a futás végére a motor külön időzítőt állít (nem a 30 másodperces ketyegésre vár), így a locsolás hossza
  másodpercre pontos; a ketyegés csak a tartalék, ha az időzítő elveszne (pl. újraindulás).
- **Kapcsolat az eszközzel:** egyetlen, tartós kapcsolat szívveréssel; megszakadáskor újrakapcsolódás növekvő várakozással (1, 2, 5,
  10, 30 s). Futó zóna mellett a megszakadt kapcsolat nem ok a pánikra (az eszköz időkorlátja él), de 15 perc után értesítés megy.
- **Kemény korlátok** (`Korlat`, zónánkénti `max…`): a tervező is betartja, a végrehajtó indítás előtt újra ellenőrzi (két független
  ellenőrzés). Túllépés esetén a futás rövidül vagy kimarad, értesítéssel.
- **Óra-ellenőrzés:** ha a rendszeróra nyilvánvalóan rossz (a program fordítási dátuma előtt jár), vagy két ketyegés között 10 percnél
  nagyobbat ugrik, a motor az automatikus indítást felfüggeszti, amíg az óra két egymást követő ketyegésben következetes; a futó zónákat
  az eszköz-időkorlát zárja.
- **Szünet és vészleállítás:** „Szünet eddig: dátum” (pl. nyaralás, javítás) – nincs indítás; „Vészleállítás” – minden zóna zárása
  azonnal, a sor törlése, a mód `ki`-re áll. Mindkettő admin, a felület tetején mindig elérhető.
- **Módok:** `ki` → `arnyek` (a motor mindent kiszámol és naplóz, parancsot nem küld) → `eles`. Élesítés feltétele: `A` biztonsági
  osztály, legalább 3 nap árnyék üzem, titkosított kapcsolat, admin jelszavas megerősítése.
- **Értesítések:** sikertelen indítás, nem igazolt zárás (sürgős), elért napi korlát, 3 egymást követő kihagyás adathiány miatt, a
  vezérlő 15 perce nem elérhető, külső indítás.

### 11.8 Biztonsági feltételek (mindegyikhez teszt tartozik – 14.6)
| # | Feltétel |
|---|---|
| B1 | Nincs nyitási parancs eszköz-oldali időkorlát nélkül. |
| B2 | `B` osztályú vezérlőn nincs automatikus indítás. |
| B3 | Egy zóna egy nap alatt nem locsol többet a `maxPercNap` / `maxLiterNap` értéknél – program, automata, kézi és külső indítás együtt. |
| B4 | Az összes zóna együtt nem lépi túl a `maxLiterNapOssz` értéket. |
| B5 | Egyszerre legfeljebb `maxEgyideju` zóna van nyitva. |
| B6 | Időablakon kívül nincs automatikus indítás, és a futás az ablak végén leáll. |
| B7 | Árnyék módban és `ki` módban a meghajtó `Open` hívása soha nem történik meg. |
| B8 | Idegen, friss bérlet mellett a motor nem küld parancsot. |
| B9 | Minden parancs előtt van naplóbejegyzés; újraindulás után nincs gazdátlan nyitott zóna. |
| B10 | A motor leállásakor (SIGTERM, kivétel) minden nyitott zónára megy zárási parancs. |
| B11 | A néző szerep semmilyen szelepparancsot nem tud kiváltani; titkosítatlan kapcsolaton admin sem. |
| B12 | A szimuláció soha nem hívja a meghajtót (típusszinten: a szimulátor nem kap `IValveDriver`-t). |

### 11.9 Ami az eszköztől függ (nyitott, az eszköz megérkezése után derül ki)
1. Tud-e a doboz zónánként időkorlátot fogadni helyi parancsban? (Ha nem → `B` osztály → vagy saját firmware (ESPHome) kell, vagy csak
   kézi üzem. Ezt a felhasználóval előre közölni kell.)
2. Melyik protokollverziót beszéli, és hány egyidejű helyi kapcsolatot enged (általában egyet – más helyi vezérlő nem futhat mellette).
3. Megmarad-e a doboz saját programja tartaléknak, és ütközik-e a külső vezérléssel (javaslat: a dobozban nincs program, vagy csak egy
   ritka „vésztartalék”).
4. Van-e esőérzékelő-bemenet és leolvasható-e.
Addig minden a `szimulalt` meghajtóval fejleszthető és tesztelhető; a Tuya-meghajtó külön munkacsomag, hardverkapuval (K4).

### 11.10 Szimuláció
- **Bemenet:** időszak (pl. 2026. május 1. – szeptember 30.), 1–4 forgatókönyv (program + szabálykészlet + automata + korlátok; névvel
  elmenthető), a zónák.
- **Működés:** a szimulátor a tényleges mérésekkel lépked az időben. Minden döntési időpontban a `Planner.Decide`-ot hívja egy olyan
  időjárás-nézettel, amely csak az addigi méréseket adja ki; a kiadott vizet beviszi a `BalanceStepper`-be; a következő döntés már az így
  alakult talajállapotot látja. A halasztást is lejátssza (10 percenként újra, a tényleges széllel).
- **Mindig számolt viszonyítás:** „öntözés nélkül” és „ahogy valójában történt” (a rögzített locsolásokkal).
- **Kimenet forgatókönyvenként és zónánként:** felhasznált víz (liter, m³, költség), futások száma, kihagyva / halasztva / rövidítve
  (szabályonként), stresszes napok száma (a kiszáradás a stresszhatár fölött), legnagyobb kiszáradás, elszivárgott víz (a talaj már tele
  volt), hasznosult arány. Grafikon: a kiszáradás görbéi egymáson; táblázat: forgatókönyvek egymás mellett; a futások listája magyarázattal.
- **Korlát, amit a felület kiír:** a szimuláció annyira pontos, amennyire a talajvíz-mérleg modellje; a szél a mérőhelyen mért, nem a
  szórófejnél; a múltbeli adat hiányos napjain becsült értékkel számol. A program nem méri a ténylegesen kifolyt vizet (nincs
  átfolyásmérő): ha a főcsap zárva volt, a napló és a talajvíz-mérleg akkor is locsolást könyvel – ezt a felhasználó a naplóban
  javíthatja („nem történt meg”).
- A szimuláció háttérfeladat (`Slow`), megszakítható; egy nyár × 4 forgatókönyv × 8 zóna a NAS-on is másodpercek kérdése.

---

## 12. A felület (`web/`) változásai

A felület marad sima JavaScript, fordítási lépés nélkül; a szerver és az asztali program ugyanazokat a fájlokat szolgálja ki.

- **Egy belépési pont a szerver felé:** minden API-hívás az `app.js` `api()` és `post()` függvényén megy át (ma is így van). Ezek teszik
  a cím elé a `/api/ws/<id>` előtagot, kezelik a 401-et (belépőoldalra irányít) és a 403-at (érthető üzenet), és továbbítják a hibát az
  általános hibakezelőnek.
- **Statikus fájlok** (`index.html`, `*.js`, `app.css`, `lang/*.json`): belépés nélkül is letölthetők – a kód nyílt forráskódú, adat
  nincs bennük; a belépőoldalnak is kell a fordítás. Minden adat az API-n jön, az pedig belépést kér. A statikus fájlok listája zárt
  (a fordításkor beágyazott `web/` mappa tartalma): ami nincs benne, 404.
- **Képességek:** induláskor a felület lekéri a `/api/sys/info`-t: `{ verzio, gazda: "asztali" | "szerver", szerep, kepessegek: [...] }`.
  A képességek listája dönti el, mi látszik: `fajlablak` (mentés másként), `mappanyitas`, `feladatutemezo`, `ontozes.vezerles`,
  `ontozes.motor`, `ertesites.windows`, `frissites.helyi`, `munkateruletek` … A felület **soha nem a platformra kérdez rá**, csak képességre.
- **Szerep:** néző módban a módosító vezérlők nem jelennek meg (nem csak letiltva – a szerver úgyis elutasítaná). Egy közös segédfüggvény
  (`csakAdmin(elem)`) jelöli őket; teszt ellenőrzi, hogy nézőként egyetlen módosító hívás sem indul.
- **Új elemek:** belépőoldal és első beállítás (külön, minimális HTML – a fő felület előtt töltődik, saját CSP-vel); munkaterület-választó
  a fejlécben; értesítési lista (harang); „Állapot” kártya (TERV_4 20.23); „Kapcsolat” kártya (csak asztali ablakban, 9.1);
  általános hibakezelő (`window.onerror`, `unhandledrejection` → üzenet + `POST /api/sys/clienterror`).
- **Új fül: Öntözés.** Alfülek: Áttekintés (ma mi lesz / mi volt, kézi indítás, szünet, vészleállítás), Zónák, Programok (a Szárazság
  fülről ide költözik), Szabályok, Automata, Napló (döntések magyarázattal), Szimuláció, Vezérlő (csak admin). A Szárazság fülön marad a
  talajvíz-mérleg és a javaslat; az „öntözéssel / öntözés nélkül” kapcsoló (TERV_4 18.7) a szimuláció „öntözés nélkül” sorával közös adat.
- **Fordítás:** minden új szöveg a meglévő rendszerben (`web/lang/*.json`, `tf()` sablonok); a szerver magyar hibaüzenetei kulcsként
  mennek, a felület fordítja. Új nyelvi kulcs nélkül nem megy át a CI.
- **Új fájlok:** `auth.js`, `workspaces.js`, `notify.js`, `errors.js`, `host.js`, `irrigation-*.js` (a mai `irrigation.js` bővül / bomlik).
  A `web/` mappa **egészében** beágyazódik a magba (a projektfájl mintával veszi fel – ma a `build.bat` kézzel sorolja fel a fájlokat,
  egy új fájl könnyen kimarad); a szkriptek betöltési sorrendjét továbbra is az `index.html` adja.

---

## 13. Kompatibilitás és áttérés

### 13.1 Ami nem változhat
- A munkaterület mappaszerkezete és a fájlok formátuma (CSV-oszlopok, ini-kulcsok, JSON-alakok). Egy 2.2.8 mappa a 3.0-val megnyitva
  ugyanazt mutatja.
- A számítások eredménye az átírás után (14.2). A TERV_4 20–21. pontjának számítási javításai **külön, az átírás utáni** lépések, hogy
  mindig tudjuk, egy eltérés szándékos-e.
- A Letöltő kilépési kódjai és parancssori kapcsolói; a Megjelenítő kapcsolói (`--mappa`, `--port`, `--web`, `--bongeszo`, `--teszt`).
- A felhasználói leírások és a nyelvfüggő fájlnevek rendszere.

### 13.2 Sémaverziók és migráció
- Minden JSON-beállításfájl kap egy `"sema": n` mezőt (ahol még nincs). Betöltéskor a mag lépésenként emeli a legújabbra; a migráció
  **előtt** a régi fájl `<név>.v<n>.bak` néven megmarad. Visszafelé nincs migráció: régebbi program újabb fájlt nem nyit meg, hanem
  érthető hibát ad.
- 3.0-s migrációk: `szarazsag.json` → az öntözési programok átkerülnek az `ontozes.json`-ba; minden olyan szárazság-profilhoz, amelyre
  program hivatkozik, létrejön egy **vezérlő nélküli zóna** (tervezni és szimulálni lehet vele; vezérelni csak a csatorna megadása után); `felulet.json` → szerepenkénti fájl;
  `rendszer\munkateruletek.json` létrehozása; a gyorsítótár verziója emelkedik (újraépül).
- A biztonsági mentés formátuma bővül (munkaterület-azonosító, sémaverziók, „titkok nélkül” jelző); a régi mentés visszaállítható.

### 13.3 Adat átvitele a gépek között
- **Windows → NAS (első beállítás):** a Windows-programban „Mentés másolása” (teljes mentés), a szerver felületén „Visszaállítás
  mentésből” – vagy a munkaterület mappájának egyszerű átmásolása a NAS-ra, leállított programok mellett.
- **NAS → Windows:** „Mentés letöltése” a szerverről, visszaállítás a helyi programban.
- Folyamatos kétirányú szinkron **nincs** (ütközéskezelés nélkül veszélyes); a NAS a központ (D1), a helyi mód önálló.
- A munkaterület-zár megakadályozza, hogy ugyanazt a mappát (pl. hálózati megosztáson át) két gép egyszerre írja; hálózati megosztáson
  lévő munkaterületet a program figyelmeztetéssel nyit meg.

### 13.4 A 2.3-ra gyűjtött javítások sorsa (TERV_4 18., 20., 21.)
| Tétel | Hova kerül |
|---|---|
| Memória (21.1), újrapróbáló mentés (20.7), fájlzár (20.8), hibakezelő (21.2), kulcs nélküli mentés (21.3) | **beépül az átírásba** (8.2, 7.4, 12., 10.5) |
| Számítási javítások (11.1, 20.10–20.14, 21.4), felhőzet-grafikon (21.5), „Mennyire pontos?” sorok (21.6) | az átírás **után**, külön munkacsomagok (M10) |
| A 18. pont kérései (idővonal, statisztika, szél, első letöltés dátuma, ikon, hőérzet) | 3.1 – a mag szétválasztása után olcsóbb megcsinálni |
| 18.7 öntözés (egységek, időszak-szerkesztő, vízigény / öntözési igény, programok összevetése, kapcsoló) | az Öntözés fül része (11–12.) |
| Új ötletek (20.20 nincs adat riasztás, 20.23 Állapot kártya, 20.24 hibajelentő csomag) | 8.5, 12., ill. 3.1 |

Ha a 3.0 előtt sürgős hiba jön elő a 2.2.8-ban: `release/2.x` ágon javítás, és a javítás a `main`-re is átkerül.

---

## 14. Tesztelés

### 14.1 Szintek
| Szint | Mit véd | Hol fut |
|---|---|---|
| Egységtesztek (xUnit) | képletek, JSON-réteg, tároló, zárak, szabálykiértékelő, tervező, útvonaltábla | CI: Windows + Linux |
| Golden tesztek | „az átírás nem változtatta meg az eredményt” | CI |
| Biztonsági tesztek | szerepek, belépés, fejlécek, korlátok | CI |
| Motor-tesztek szimulált meghajtóval | a 11.8 feltételei, hibabefecskendezéssel | CI |
| Önteszt (`--teszt` / `selftest`) | a telepített program a felhasználó gépén | kézzel, hibakereséshez |
| Kézi próba a célgépen | NAS (K0, K3), szelepvezérlő (K4) | a felhasználóval |
| Böngészős végigkattintás | a felület mind a 4 nyelven, néző és admin szerepben | kiadás előtt, a meglévő módszerrel |

### 14.2 Golden tesztek – az átírás biztosítéka
1. **Mintaadat-generátor** (`tools/adatgen`): rögzített véletlenmaggal 14 hónapnyi, a WU formátumával azonos CSV-t készít (napi és éves
   menet, eső-események, szélviharok, hiányok, hibás értékek, óraátállítás, állomás-újraindulás). **Nem valódi mérés**, a repóba kerülhet.
2. **Rögzítés a 2.2.8-ból:** egy egyszeri segédprogram a 2.2.8 forrásfájljaiból fordul (a régi fordítóval), saját belépési ponttal, és a
   mintaadaton minden olvasó végpontot meghív közvetlenül (`Api.Handle(...)`), a válaszokat a `tests/golden/` mappába írja. A kiadott
   2.2.8 kódja ehhez nem változik.
3. **Összevetés:** a 3.0 tesztje ugyanazokat a hívásokat teszi az új útválasztón, és a JSON-okat **szerkezetileg** veti össze: a számokat
   számként (a két futtatókörnyezet ugyanazt a számot néha más szöveggel írja ki), a kulcsok sorrendjét is. Az időfüggő mezők
   (`ageMin`, „most”) rögzített órával futnak (`Clock`), vagy név szerint ki vannak véve.
4. A beállításfájlok oda-vissza tesztje: a 2.2.8 által írt fájlokat az új JSON-réteg beolvassa és visszaírja – a tartalom egyezik.
5. A felhasználó a **saját valódi adatán** is lefuttathatja helyben (az eredmény nem kerül a repóba) – ez a legerősebb ellenőrzés a
   kiadás előtt.

### 14.3 Óra és determinizmus
A magban nincs közvetlen `DateTime.Now` / `UtcNow` (ma 36 helyen van): helyette `Clock.UtcNow` / `Clock.Now(ws)`, amely tesztben
rögzíthető és léptethető. A tervező (`Planner`) és a szimulátor egyáltalán nem olvas órát.

### 14.4 Biztonsági tesztek (automatikusak)
- Az útvonaltábla minden sorának van szerepe; ismeretlen útvonal 404; rossz metódus 405.
- Néző munkamenettel minden nem engedélyezett módosító útvonal 403; munkamenet nélkül minden `/api/` útvonal 401 (kivéve belépés, info).
- Hiányzó `X-Idojaras` fejléc vagy idegen `Origin` → 403; idegen `Host` → 421.
- Egy GET-kérés sem változtat fájlt (a teszt minden GET-et meghív, és összeveti a munkaterület fájljainak ellenőrzőösszegét).
- A válaszokban nincs titok (a teszt ismert titkokat állít be, és minden válaszban keresi őket).
- Belépés: lassítás működik; a két szerep jelszava felcserélve nem működik; kijelentkezés után a süti érvénytelen; jelszóváltás után a
  régi munkamenet érvénytelen.
- Méretkorlát: túl nagy törzs 413; lassú kérés megszakad.
- Útvonal-bejárás: `..`, abszolút útvonal, fordított perjel a statikus fájloknál és a munkaterület-azonosítóban → 404.

### 14.5 Szerkezeti tesztek
- A `WStation.Core` szerelvény nem hivatkozik tiltott névterekre (reflexióval ellenőrizve).
- A magban nincs írható statikus mező a megengedett listán kívül.
- A magban nincs `"\\"` útvonal-összefűzés és nincs `DateTime.Now` (forrásszintű keresés a CI-ben).
- Az `index.html` minden `<script>` és `<link>` hivatkozása létező, beágyazott fájlra mutat; a `web/lang/` minden nyelvi fájlja beágyazódott.
- Minden magyar felületi szövegnek van kulcsa a `_sablon.json`-ban (a meglévő `forditas_kulcsok.ps1` logikája CI-lépésként).

### 14.6 Öntözési tesztek
- A 11.8 minden feltételére legalább egy teszt, a `szimulalt` meghajtóval, hibák befecskendezésével: nem válaszoló eszköz, nyitva ragadt
  szelep, a motor leállítása futás közben és újraindítása, óraugrás, lejárt adat, idegen bérlet, külső indítás.
- Tulajdonságalapú teszt: véletlen programok, szabályok és időjárás mellett a korlátok (B3–B6) soha nem sérülnek.
- A szimuláció és az éles tervezés azonossága: egy rögzített nap bemenetére a szimulátor és a motor ugyanazt a tervet adja.
- A jövőbe látás tilalma: ha a döntési időpont utáni méréseket megváltoztatjuk, a döntés nem változik.

---

## 15. Szakaszok és kapuk

Minden szakasz végén **kapu** van: mérhető feltétel, amely nélkül a következő nem indul. A szakaszok kiadható állapotot adnak.

| Szakasz | Tartalom | Kapu |
|---|---|---|
| **K0 – próba a NAS-on** (a fejlesztés előtt, kb. fél óra a felhasználóval) | Egy apró próbaprogram (.NET 10, ARM64) másolása a NAS-ra és futtatása: elindul-e; rendszerkönyvtár verziója; HTTPS-hívás a WU felé; időzóna; fájlzár a köteten; 20 MB-os CSV beolvasásának ideje; memória. | Kiderül, a 10.6 négy útja közül melyik járható. Ha a 4. (más gép kell), a felhasználó dönt a folytatásról. |
| **S1 – átírás modern .NET-re** | Projektváz, JSON-réteg, a kód áthelyezése a magba, Windowson fordul és fut; a működés változatlan (még egy munkaterület, még a régi letöltő-indítás). | Golden tesztek zöldek; az önteszt minden pontja sikeres (ugyanannyi, mint a 2.2.8-on – a számot az 1.3 rögzíti); a felület végigkattintva ugyanaz. |
| **S2 – környezet és munkaterületek** | `Ctx`, `WorkspaceContext`, nyilvántartás, váltó a felületen, `/api/ws/{id}` címek, több `Guard`-gyökér. | Két munkaterület egyszerre betöltve, párhuzamos kérések nem keverednek (teszt); golden zöld. |
| **S3 – letöltő a magban** | `DownloadEngine`, `ObservationStore` (hozzáfűzés), növekményes betöltés, munkaterület-zár, `ReplaceRetry`, belső Frissítés. | 5 éves generált adat: memória < 100 MB; félbeszakított írás utáni helyreállítás; a `Letöltő.exe` kapcsolói és kilépési kódjai változatlanok. |
| **S4 – útvonaltábla, szerepek, képességek** | Deklaratív útvonalak, `IAuth`, szerepellenőrzés, képességlista, hibakezelő, felület-átállás. | A 14.4 tesztjei zöldek helyi módban; a felület néző szerepben (tesztkapcsolóval) nem mutat módosító vezérlőt. |
| **→ 3.0 kiadás (Windows)** | Telepítő az új tartalommal; frissítés 2.2.8-ról. | Frissítés és új telepítés próbája; a felhasználó valódi adatán a golden összevetés egyezik. |
| **S5 – szerver gazda** | Kestrel-előtét, jelszavas belépés, munkamenetek, első beállítás, HTTPS, ütemező, értesítések, Linux-csomag. | Linuxon (x64) teljes működés; a 14.4 tesztjei szerver módban; 72 órás futás memória- és leírószivárgás nélkül. |
| **K3 – a NAS-on** | Telepítés a cél-NAS-ra, a felhasználó adataival. | Egy hét futás: minden óránkénti letöltés megvan, memória a korlát alatt, a lemezek tudnak aludni. |
| **S6 – asztali távoli mód** | `kapcsolat.json`, tanúsítvány-rögzítés, módváltás, telepítő-kérdés. | Váltás helyi ↔ távoli adatvesztés nélkül; elérhetetlen szerver kezelése. |
| **→ 3.1 kiadás (szerver + távoli mód)** | | |
| **S7 – öntözés: modell, tervező, szimuláció** | Adatmodell, `BalanceStepper`, szabályok, tervező, szimulátor, Öntözés fül (vezérlés nélkül). | A 14.6 tervezői és szimulációs tesztjei; a `Drought.Compute` golden egyezik a léptető kiemelése után. |
| **S8 – motor és szimulált meghajtó** | Végrehajtó, napló, bérlet, árnyék mód, értesítések. | A 11.8 mind a 12 feltétele teszttel igazolva. |
| **K4 – a valódi eszköz** | Adatpont-felderítés, eszközprofil, Tuya-meghajtó, biztonsági próba. | Az eszköz `A` osztályú. Ha `B`: megállás, döntés a felhasználóval (saját firmware / csak kézi üzem). |
| **S9 – élesítés** | 2 hét árnyék mód a kertben, a napló átnézése, utána éles üzem egy zónán, majd a többin. | Nincs nem igazolt zárás; a döntések érthetők; a felhasználó jóváhagyja. |
| **→ 3.2 kiadás (öntözés)** | | |
| **M10 – számítási javítások és a 18. pont** | A TERV_4 nyitott tételei (13.4). | Tételenként teszt; a szándékos eltérések a golden fájlokban dokumentálva frissülnek. |

### 15.1 Előkészületek (a fejlesztés előtt, a felhasználó jóváhagyásával)
| # | Lépés | Ki |
|---|---|---|
| E1 ✅ (2026-11-16) | A `v2.2` ág beolvasztása a `main`-be (előre tekerés – a `main` a `v2.2` őse, 40 commit a különbség), `release/2.x` létrehozása, feltöltés. | helyi munkamenet; a feltöltéshez a felhasználó jóváhagyása |
| E2 ✅ (2026-11-16, SDK 10.0.404) | A .NET 10 SDK telepítése a fejlesztői gépre. | felhasználó (telepítő futtatása) |
| E3 ✅ (2026-11-17, DSM 7.2.2-n: a `linux-arm64` kiadás fut) | K0 próba a NAS-on (B. függelék). | fejlesztő készíti, felhasználó futtatja |
| E4 ✅ (2026-11-16) | Ennek a tervnek a repóba tétele `docs/dev/PLAN-3.0.md` néven, a 0.2 pont (saját eszközök) és a személyes utalások nélkül – különben a felhős munkamenetek nem látják. | helyi munkamenet; jóváhagyással |
| E5 | A nyitott kérdések megválaszolása (18. fejezet), amennyire az adott szakaszhoz kell. | felhasználó |

**Sorrendi szabályok:** S1 előtt nincs funkcióbővítés. S2 és S3 egymás után (ugyanazokat a fájlokat érintik). S5 és S7 egymással
párhuzamosan is mehet (más mappák), de két munkamenet ne írja ugyanazt a fájlt. M10 tételei S1 után bármikor, ha a golden frissítése
külön, megindokolt lépés.

**Nagyságrend (becslés, nem ígéret):** S1–S4 együtt akkora, mint a 2.2-es sorozat; S5–S6 hasonló; S7–S9 ennél valamivel kisebb, de a
valódi eszközzel való próbák naptári időt kérnek (hetek az árnyék üzem miatt).

---

## 16. Munkacsomagok (kiadhatók egy kisebb modellnek)

**Hogyan kell kiadni:** egy csomag = egy ág (`feat/…`) = egy pull request. A megbízásban szerepeljen: a csomag szövege innen, a
„Kötelező olvasmány” fejezetek ebből a fájlból, és a 17. fejezet. A csomag akkor kész, ha az „Elfogadás” minden pontja teljesül és a CI zöld.
Az erős modell (tervező) dolga: a kapuk kiértékelése, a pull requestek átnézése (különösen ★ jelűeknél), a csomagok közti illesztés.

**Egy megbízás sablonja:**
```
Feladat: WStation 3.0 – <szám> <név> munkacsomag.
Olvasd el: docs/dev/PLAN-3.0.md <fejezetek> és a 17. fejezet (kötelező szabályok).
Ág: feat/<rövid-név>, a main-ből. Egy pull request.
Tartalom: <a csomag „Tartalom” oszlopa>
Elfogadás: <a csomag „Elfogadás” oszlopa> + a CI zöld.
Ne csináld: más csomag tartalmát; logikai változtatást az átvett kódban; új külső csomagot; a 11.8 feltételeinek gyengítését.
Ha a terv és a kód ellentmond, vagy valami nincs megadva: állj meg, és írd le a kérdést a PR-ban.
A végén: a PR leírásában mit, miért, hogyan tesztelted, mi maradt ki.
```

Jelölés: ★ = biztonsági vagy adatvesztési kockázat – erős modellel írni vagy tüzetesen átnézni. ◆ = a felhasználó közreműködése kell.

### S0 – előkészítés
| # | Csomag | Tartalom | Elfogadás | Olvasmány |
|---|---|---|---|---|
| 0.1 ◆ | K0 próbaprogram | `tools/k0proba` a B. függelék szerint, két kiadásban, `proba.sh`-val; előzetes próba egy ARM64 Linuxon. | A felhasználó NAS-áról visszajön az eredményfájl; a 10.6 útjai közül egy kiválasztva és ide beírva. | B. függelék, 10.6 |
| 0.2 ◆ | Repó-előkészítés | E1 (ágak), E4 (terv a repóba, személyes részek nélkül), `CONTRIBUTING.md` frissítése az új ágrenddel. | A `main` a 2.2.8-at tartalmazza; `release/2.x` létezik; a terv a `docs/dev/` alatt olvasható. | 4.2, 15.1 |

### S1 – átírás modern .NET-re
| # | Csomag | Tartalom | Elfogadás | Olvasmány |
|---|---|---|---|---|
| 1.1 ✅ (2026-11-17, PR #1) | Projektváz | `WStation.sln`, `Directory.Build.props` (verzió, `InvariantGlobalization`, figyelmeztetések hibaként az új kódban), üres projektek a 4.3 szerint, `ci.yml` (fordítás Windows + Linux). A régi `build.bat` egyelőre marad. | A CI lefordítja az üres vázat mindkét rendszeren. | 4., 5. |
| 1.2 | Mintaadat-generátor | `tools/adatgen`: determinisztikus, a WU-oszlopokkal azonos CSV (14.2/1). | Kétszer futtatva bájtra azonos fájl; a 2.2.8 Megjelenítő megnyitja és minden fül mutat adatot. | 14.2 |
| 1.3 | Golden rögzítő | Segédprogram a 2.2.8 forrásaiból (régi fordítóval, saját belépési ponttal): minden olvasó végpont válasza a mintaadaton → `tests/golden/`. A végpontok és paramétereik listája egy fájlban (`tests/golden/hivasok.json`). | A golden mappa a repóban; a rögzítés megismételhető és azonos eredményt ad. | 14.2 |
| 1.4 ★ | JSON-réteg | `Core/Common/Json.cs` a 7.2 szerint, tesztekkel (típusok, sorrend, kódolás, korlátok, oda-vissza a 2.2.8 fájljain). | Minden 7.2-beli viselkedésre teszt; a mintaadat beállításfájljai oda-vissza egyeznek. | 7.2 |
| 1.5 | A mag áthelyezése | A `megjelenito/*.cs` és `kozos/*.cs` fájlok a `Core` mappáiba (7.1 tábla), névterek rendezése, `JavaScriptSerializer` → `Json`, `CultureInfo` számformázás → `NumFmt` (7.3), kódlap-regisztráció ahol kell. **Logikai változtatás nélkül.** A Windows-függő részek egyelőre `#if`-fel vagy ideiglenes felülettel a Desktopban. | A mag lefordul Windowson és Linuxon; a golden teszt zöld. | 7.1, 7.3, 17. |
| 1.6 | Asztali gazda | `WStation.Desktop`: a mai `Program.cs`, `MainForm`, `FallbackForm`, WebView2-kezelés; a felület fájljai erőforrásként a magból (a `web/` mappa mintával beágyazva). | A program indul, minden fül működik; az önteszt ugyanannyi sikeres pontot ad, mint a 2.2.8-on. | 9.1, 12. |
| 1.7 | Letöltő gazda (átmeneti) | A `Letolto.cs` változatlan logikával új projektben fordul (.NET 10); a Megjelenítő még külön folyamatként indítja. | Letöltés működik valódi kulccsal (◆); kilépési kódok változatlanok. | 9.2 |
| 1.8 | Telepítő tartalma | A telepítő zipből telepít (9.4); `packaging/windows` összeállítja a tartalmat; `release.yml` első változata. | Friss telepítés és frissítés 2.2.8-ról virtuális mappában: az adatok megmaradnak, a régi fájlok félretéve. | 9.4, 4.4 |
| 1.9 | Óra | `Clock` bevezetése, a `DateTime.Now/UtcNow` cseréje a magban (14.3). | Forráskeresés: nincs közvetlen órahívás a magban; golden zöld rögzített órával. | 14.3 |

**Megjegyzések az S1-hez**
- **Sorrend** (a függőségek miatt): 1.1 → 1.2 és 1.4 (párhuzamosan mehet) → 1.3 → 1.5 → 1.6 és 1.7 → 1.9 → 1.8. Az 1.1 egyedül megy
  elsőnek, mert az hozza létre a CI-t; az 1.5 csak akkor indulhat, ha az 1.3 golden fájljai a `main`-en vannak.
- **Hol futhat:** 1.1, 1.2, 1.4, 1.9 – bárhol (Linuxon is). 1.3 – csak Windowson (a 2.2.8 a .NET Framework fordítójával fordul; az
  1.3 az önteszt 2.2.8-as eredményét is rögzíti a golden mappába). 1.5 – Windowson (a golden összevetés miatt), a CI Linuxon is fordít.
  1.6, 1.8 – Windowson, a felhasználó végigkattintja (◆). 1.7 – Windowson, valódi kulccsal (◆).
- Amíg az 1.5 tart, a `release/2.x`-re ne kerüljön javítás (az áthelyezés miatt nehéz átvinni); ha mégis kell, az 1.5 beolvasztása után.
- Az 1.5 egyetlen nagy lépés, mert a fájlok kölcsönösen hivatkoznak egymásra (részenként nem fordulna). A pull requesten belül a
  commitok sorrendje: (1) csak áthelyezés `git mv`-vel, tartalmi változás nélkül – így a fájltörténet megmarad és a különbség üres;
  (2) névterek és `using` sorok; (3) `JavaScriptSerializer` → `Json`; (4) számformázás → `NumFmt`; (5) ami Windows-függő, ideiglenesen a
  gazdába. Minden commit után fordulnia kell.
- Az új fordító szigorúbb: ha egy régi azonosító újabb kulcsszóval ütközik (pl. a `Letolto.cs` `required` nevű mezője), **átnevezés**, nem
  a nyelvi szint visszavétele.
- A `.NET Framework` és a modern .NET néhány eltérése, amire figyelni kell: `Process.Start` alapértelmezése (`UseShellExecute` – a kód
  mindenhol kiírja, maradjon így); a régi kódlapok (852, 1250) csak regisztráció után érhetők el
  (`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` – csak a Windows-gazdában kell); a lebegőpontos számok szöveges alakja
  néha más (a golden összevetés ezért számként hasonlít); a `HttpListener` Linuxon is megvan, de a szerver nem azt használja.

### S2 – környezet és munkaterületek
| # | Csomag | Tartalom | Elfogadás | Olvasmány |
|---|---|---|---|---|
| 2.1 ★ | `Ctx` és `WorkspaceContext` | 6.2 szerint: az állapot kiemelése, a statikus mezők tulajdonsággá alakítása, belépés minden kérés és háttérmunka elején. | Teszt: két munkaterület párhuzamos kérései 1000 ismétlésben sem keverednek; belépés nélküli hozzáférés kivétel; a statikusmező-teszt zöld; golden zöld. | 6.1–6.2, 14.5 |
| 2.2 | Nyilvántartás és elrendezés | `WorkspaceRegistry`, `rendszer\munkateruletek.json`, lusta betöltés, kiürítés, több `Guard`-gyökér + munkaterületenkénti őr (6.3–6.4). | Létrehozás, átnevezés, „törlés” (átnevezéssel) teszttel; egy munkaterület kódja nem tud a másikba írni (teszt). | 6.3–6.4 |
| 2.3 | API-címek és felület | `/api/ws/{id}/…` és `/api/sys/…`; `api()` / `post()` előtag; munkaterület-választó a fejlécben; új munkaterület varázsló (név, API-kulcs, állomás). | A felület két munkaterület között vált újratöltés nélkül; a fordítások megvannak mind a 4 nyelven. | 6.5, 12. |

### S3 – letöltő a magban
| # | Csomag | Tartalom | Elfogadás | Olvasmány |
|---|---|---|---|---|
| 3.1 ★ | `ObservationStore` | 8.2 szerint, tesztekkel: hozzáfűzés, összefésülés, csonka sor, oszlopbővítés, zárolt fájl, áramszünet-szimuláció (az írás megszakítása tetszőleges bájtnál). | 5 éves adat: memória < 100 MB, hozzáfűzés < 1 s; bármely megszakítás után a fájl érvényes és rendezett; a Megjelenítő eredménye egyezik a régi módszerével. | 8.2 |
| 3.2 | `DownloadEngine` | A `Letolto.cs` logikája könyvtárként (8.1): `DlConfig` példány, `IProgress`, megszakíthatóság, eredménykódok; hamis WU-kiszolgálóval tesztelve (rögzített válaszok). | A letöltési terv (melyik napot kéri le) a régi kóddal azonos a tesztesetekben; kilépési kódok egyeznek. | 8.1 |
| 3.3 | Zárak és atomi írás | Munkaterület-zár, fájlzár, `ReplaceRetry` mindenhol (7.4). | Két folyamat egyszerre: a második vár vagy érthető hibával kilép; zárolt célfájl mellett az írás újrapróbál (teszt). | 7.4 |
| 3.4 | Növekményes betöltés | 8.3 szerint. | Hozzáfűzés után a betöltés csak az új részt olvassa (mérve); újraírás után teljes olvasás; az eredmény mindkét esetben azonos a teljes olvasáséval. | 8.3 |
| 3.5 | Belső Frissítés + Windows-letöltő | A Frissítés gomb a magban futtatja a letöltést; a `Letöltő.exe` vékony héj (9.2); `--munkaterulet`, `--mind`. | A haladás és a megszakítás működik; a Feladatütemező feladata változatlanul fut; régi kapcsolók működnek. | 8.1, 9.2 |
| 3.6 | Mentés | `BackupService` a magban (készítés, visszaállítás, **kulcs nélküli másolat**, figyelmeztetés). | A kulcs nélküli mentésben nincs titok (teszt keresi); visszaállítás után a helyi kulcs megmarad. | 10.5 |

### S4 – útvonaltábla, szerepek, képességek
| # | Csomag | Tartalom | Elfogadás | Olvasmány |
|---|---|---|---|---|
| 4.1 ★ | Útvonaltábla | A `switch` → `RouteTable` (10.4), jelzők, egységes hibaválasz, `IAuth`, `DesktopTokenAuth`. | 14.4 tesztjei helyi módban; golden zöld. | 10.4, 14.4 |
| 4.2 | Képességek és szerep a felületen | `/api/sys/info`, `csakAdmin`, képességfüggő megjelenítés; a platformfüggő gombok képesség mögé. | Tesztkapcsolóval néző szerepben a felület egyetlen módosító hívást sem indít (böngészős teszt). | 12. |
| 4.3 | Hibakezelő és értesítési lista | `errors.js`, `/api/sys/clienterror`, `NotificationCenter`, harang a fejlécben. | Szándékos JS-hiba üzenetet ad és a naplóba kerül; óránkénti korlát működik. | 7.6, 8.5, 12. |

### S5 – szerver gazda
| # | Csomag | Tartalom | Elfogadás | Olvasmány |
|---|---|---|---|---|
| 5.1 ★ | Kestrel-előtét | `WStation.Server`: mindent elkapó végpont → a mag útválasztója; korlátok (10.4); kapcsolati módok (10.2); gépnév-ellenőrzés; fejlécek. | 14.4 tesztjei szerver módban; idegen `Host` → 421. | 10.2, 10.4 |
| 5.2 ★ | Belépés és munkamenetek | `PasswordAuth`, jelszókivonat, munkamenet-tár, lassítás, megerősítés jelszóval, első beállítás kóddal, `reset-admin` (10.3). Belépő- és beállítóoldal. | 14.4 belépési tesztjei; a jelszókivonat formátuma tesztelt; időzítés-teszt (a két szerep ellenőrzése azonos idejű). | 10.3 |
| 5.3 ★ | Tanúsítvány | Saját tanúsítvány készítése és betöltése, ujjlenyomat kiírása, megújítás (10.2). | Böngészőből HTTPS-en elérhető; az ujjlenyomat egyezik a kiírttal. | 10.2 |
| 5.4 | Ütemező | `Core.Jobs` (8.4): feladatok, állapot, pótlás, hibakezelés, óraugrás. | Tesztek léptetett órával: kimaradt futás egyszer pótlódik; hibánál növekvő várakozás; nyári időszámítás-váltás. | 8.4 |
| 5.5 | Értesítések | `Alerts` a magban, kézbesítők: InApp, Smtp, Ntfy, Webhook, SynologyDsm; kimenő címek ellenőrzése (10.7). | Hamis kiszolgálóval tesztelve; a tiltott cím elutasítva; csendes órák működnek. | 8.5, 10.7 |
| 5.6 | Titkok és ellenőrzési napló | `titkok.json`, fájljogok, kitakarás a naplóban, `audit.log` (10.5, 10.8). | Teszt: titok nem jelenik meg válaszban, naplóban, mentésben. | 10.5, 10.8 |
| 5.7 | Csomagolás | `release.yml`: linux-arm64, linux-x64, win-x64 csomagok; `packaging/synology` és `packaging/linux` szkriptek; `frissit.sh` visszaállással; leállási sorrend (10.6). | Tiszta Linux-gépen a leírás szerint telepíthető és frissíthető; SIGTERM-re 20 s-on belül kilép. | 10.6, 4.4 |
| 5.8 | Állapot kártya, munkamenetek kezelése | `/api/sys/status`, a felületen Állapot kártya, élő munkamenetek listája, kijelentkezés mindenhol. | A néző nem lát útvonalat, naplót, munkamenetet. | 12., 10.4 |

### S6 – asztali távoli mód
| # | Csomag | Tartalom | Elfogadás | Olvasmány |
|---|---|---|---|---|
| 6.1 ★ | Távoli mód | `kapcsolat.json`, navigálás a szerverre, tanúsítvány-rögzítés, hibaoldal, üzenetcsatorna (9.1). | Rossz ujjlenyomatú szerverhez nem kapcsolódik; az üzenetcsatorna idegen eredettől nem fogad; módváltás oda-vissza. | 9.1 |
| 6.2 | Telepítő és Feladatütemező | Módválasztó kérdés; távoli módban nincs letöltési feladat (9.4). | Telepítés mindkét módban; váltáskor a feladat ki- / bekapcsol. | 9.4 |

### S7 – öntözés: modell, tervező, szimuláció
| # | Csomag | Tartalom | Elfogadás | Olvasmány |
|---|---|---|---|---|
| 7.1 | `BalanceStepper` | A profilciklus kiemelése tiszta lépéssé (11.6). | A `Drought.Compute` golden egyezik. | 11.6 |
| 7.2 | Adatmodell és migráció | `ontozes.json`, ellenőrzés, a programok átköltöztetése a `szarazsag.json`-ból (13.2), egységátváltás (11.3). | Régi fájl megnyitva: a programok megvannak, biztonsági másolat készült; hibás érték elutasítva érthető üzenettel. | 11.3, 13.2 |
| 7.3 ★ | Szabálykiértékelő | 11.5: feltételek, műveletek, elsőbbség, magyarázat; a gyári készlet. | Minden változóra és műveletre teszt; érvénytelen szabály mentése elutasítva. | 11.5 |
| 7.4 ★ | Tervező | `Planner.Decide` (11.6): program mód, automata mód, áztatás, sorrend, korlátok, régi adat. | 14.6 tervezői tesztjei, a tulajdonságalapú teszttel együtt. | 11.6, 11.8 |
| 7.5 | Szimulátor | 11.10: forgatókönyvek, léptetés, viszonyítási sorok, mutatók. | A jövőbe látás tesztje; a szimuláció és a tervező azonossága; egy nyár × 4 forgatókönyv 10 s alatt. | 11.10 |
| 7.6 | Öntözés fül (vezérlés nélkül) | Zónák, Programok, Szabályok, Automata, Szimuláció alfülek; az „öntözéssel / nélküle” kapcsoló. | Böngészős végigkattintás 4 nyelven; keskeny képernyőn sem lóg ki. | 12. |

### S8 – motor
| # | Csomag | Tartalom | Elfogadás | Olvasmány |
|---|---|---|---|---|
| 8.1 | Meghajtó-felület és szimulált meghajtó | `IValveDriver`, `szimulalt` hibabefecskendezéssel (11.4). | A szimulált meghajtó minden hibamódja tesztelhető. | 11.4 |
| 8.2 ★ | Végrehajtó | `Engine` (11.7): állapotgép, előre írt napló, helyreállítás, bérlet, külső indítás, óra-ellenőrzés, szünet, vészleállítás, módok. | A 11.8 mind a 12 feltétele teszttel; 10 000 véletlen forgatókönyv hibabefecskendezéssel: nincs megsértett feltétel. | 11.7–11.8 |
| 8.3 | Áttekintés, Napló, Vezérlő alfül | Kézi indítás, állapot élőben, döntési napló magyarázattal, vezérlő hozzáadása, biztonsági próba indítása, élesítés. | A néző nem lát vezérlőgombot; élesítés csak a feltételekkel (11.7). | 12., 11.7 |
| 8.4 ★◆ | Tuya helyi meghajtó | 11.4: protokoll 3.3 / 3.4 / 3.5, kapcsolat, szívverés, eszközprofil, adatpont-felderítő. Tesztek rögzített keretekkel. | A valódi eszközön (K4): állapot olvasása, nyitás időkorláttal, zárás, a biztonsági próba `A` osztályt ad. | 11.4, 11.9 |

### D – leírások (minden kiadáshoz)
| # | Csomag | Tartalom | Elfogadás |
|---|---|---|---|
| D1 | 3.0 leírások | A felhasználói leírások frissítése (hu, majd en / de / es): munkaterületek, új telepítő, mi változott; `VALTOZASOK.txt`; `docs/ARCHITECTURE.md` az új szerkezettel; `README`. | A négy nyelv egyezik; a telepítő az új leírásokat teszi fel. |
| D2 | 3.1 leírások | Szerver telepítése (Synology, általános Linux, Windows) lépésenként, képernyőképek nélkül is követhetően; **`docs/SECURITY.md`** (mit véd, mit nem, „ne nyiss kaput az internet felé”, VPN-javaslat, jelszó-visszaállítás); távoli mód a Windows-programban. | Egy kívülálló a leírásból telepíteni tudja a szervert. |
| D3 | 3.2 leírások | Öntözés: vezérlő hozzáadása, biztonsági próba, zónák, szabályok, automata mód, szimuláció, árnyék → éles; **mit tegyél, ha riasztás jön** (főcsap). | A riasztások szövege és a leírás ugyanazokat a lépéseket mondja. |

### M10 – a TERV_4 nyitott tételei
Egy tétel = egy kis csomag, a TERV_4 megfelelő pontjára hivatkozva; mindegyik a golden fájlok **szándékos, leírt** frissítésével jár, ha
számítást érint. Javasolt sorrend: 21.4 (Zambretti), 20.10 + 20.11 + 11.1 (napsugárzás), 20.14 (FWI déli óra), 20.12 (holdfázis),
21.5 (felhőzet-grafikon), 21.6 („Mennyire pontos?” sorok), majd a 18. pont kérései.

---

## 17. Szabályok a kód írójának (minden munkacsomagra)

1. **Egy csomag, egy cél.** Ne javíts mellékesen mást; ha hibát találsz, írd fel a pull request leírásába.
2. **Átvett kódot ne írj át stílus miatt.** Áthelyezéskor a logika, a változónevek és a megjegyzések maradnak; a különbség legyen átnézhető.
3. **A kód nyelve:** az azonosítók és megjegyzések a meglévő fájlokban magyarok maradnak; új fájlban is magyar megjegyzés, a nyilvános
   típus- és metódusnevek angolul (ahogy a meglévő kód is vegyes) – a `docs/GLOSSARY.md` bővül.
4. **A magban tilos:** `System.Windows.Forms`, `Microsoft.Win32`, `DllImport`, `Process.Start` (kivéve a gazdától kapott felületen át),
   közvetlen `DateTime.Now/UtcNow`, `Environment.Exit`, írható statikus mező, kézzel összefűzött útvonal (`"a\\b"`), `Thread.Sleep` a
   tervezőben, kultúrafüggő formázás (`ToString()` paraméter nélkül számra / dátumra).
5. **Fájlírás csak a `Guard`-on át,** atomi cserével és újrapróbálással; beállításfájl írása zár alatt.
6. **Minden új végpont az útvonaltáblába kerül szereppel;** GET nem módosít; a válasz a `JW` íróval készül; titok nem megy ki.
7. **Bemenet ellenőrzése a szerveren:** tartomány, hossz, karakterkészlet; a felület ellenőrzése csak kényelem.
8. **Hibaüzenet:** a felhasználónak érthető, teendővel; a részlet a naplóba, azonosítóval. Kivételt ne nyelj el szó nélkül (`catch { }` csak
   indokló megjegyzéssel).
9. **Felületi szöveg:** magyarul írod, kulcsot kap, egész mondatos sablonnal (`tf()`); nincs összefűzött mondat, nincs rag a szám után.
   A `_sablon.json` frissül; a három fordítás elkészül.
10. **Teszt nélkül nincs kész.** Új logika → egységteszt; hibajavítás → a hibát előbb reprodukáló teszt.
11. **Titok és személyes adat nem kerül a repóba:** tesztben kitalált kulcs (`TESZT-KULCS-…`), kitalált állomás (`ITESZT1`), generált adat.
12. **Öntözésnél:** a 11.8 feltételeit semmilyen kényelmi okból nem szabad gyengíteni; ha egy feltétel útban van, állj meg és kérdezz.
13. **Függőség:** új külső csomag csak a tervező jóváhagyásával.
14. **Commit és PR:** magyar, tárgyszerű üzenet; a PR leírásában: mit, miért, hogyan tesztelted, mi maradt ki.
15. **Ha a terv és a kód ellentmond,** vagy a terv valamit nem mond meg: ne találgass – írd le a kérdést, és a legkisebb, visszafordítható
    megoldást válaszd.

---

## 18. Kockázatok és nyitott kérdések

| # | Kockázat | Valószínűség | Hatás | Kezelés |
|---|---|---|---|---|
| R1 ✅ | A .NET 10 nem indul a cél-NAS-on (régi rendszerkönyvtár, 4.4-es kernel) | közepes | nagy | K0 próba a fejlesztés előtt; 10.6 négy útja; végső esetben minigép |
| R2 | 1 GB memória kevés (K0: kb. 120 MB szabad) | **közepes–nagy** | közepes | memóriakorlát, lusta betöltés, 8.2; K0 és K3 mérés |
| R3 | A szelepvezérlő nem tud eszköz-oldali időkorlátot | közepes | nagy (nincs automata üzem) | K4 előtt nincs ígéret; saját firmware mint tartalék; a döntés a felhasználóé |
| R4 | A Tuya helyi protokoll a firmware-frissítéssel változik | kicsi | közepes | a meghajtó verziót egyeztet; a SmartLife-ban az automatikus frissítés kikapcsolása; értesítés, ha a vezérlő nem elérhető |
| R5 | Az átírás észrevétlenül megváltoztat egy számítást | közepes | közepes | golden tesztek generált és valódi adaton (14.2) |
| R6 | A `Ctx` belépés kimarad valahol (rossz munkaterület) | közepes | nagy | belépés nélkül kivétel; párhuzamossági teszt; a munkaterület-őr megakadályozza az idegen írást |
| R7 | A JSON-réteg eltér a régitől, és egy régi fájl nem nyílik meg | közepes | nagy | oda-vissza tesztek a 2.2.8 fájljain; migráció előtt biztonsági másolat |
| R8 | A telepítő mérete és a víruskeresők (nagyobb, aláíratlan csomag) | közepes | kicsi | a SmartScreen-figyelmeztetés marad (kódaláírás nincs – korábbi döntés); a leírás elmagyarázza |
| R9 | Túl nagy a feladat, félúton elakad | közepes | közepes | minden szakasz végén kiadható állapot; a 3.0 önmagában (Windows) is értékes: memóriajavítás, munkaterületek |
| R10 | A szerver a belső hálózaton támadási felület | kicsi | nagy | 10. fejezet; alapból HTTPS és jelszó; a szelepvezérlés titkosítatlanul tiltott; nem internetre |
| R11 | Kétféle telepítés szétcsúszó verziói (Windows-kliens régebbi, mint a szerver) | közepes | kicsi | távoli módban a felületet a szerver adja – a kliens verziója szinte mindegy; a gazda–felület üzenetcsatorna verziózott |
| R12 | A nézőnek szánt jelszó kiszivárog | közepes | kicsi | a néző semmit nem módosíthat és titkot nem lát; külön jelszó, külön cserélhető |
| R13 | A DSM egy frissítése elrontja a futást, vagy a felhasználó DSM 6.2-n marad | kicsi | nagy | indításkor DSM-verzió-ellenőrzés érthető hibaüzenettel; tartalék: musl-os kiadás saját betöltővel (B. függelék, K0 eredménye) |
| R14 | A jelszókivonat a NAS-on lassú (K0: hidegen 10 000 kör 631 ms) | közepes | közepes | S6 előtt meleg újramérés; körszám-alsóhatár a NAS-on 50 000, belépés-korlátozás (10.3) |

**Nyitott kérdések (a felhasználónak, nem sürgősek):**
1. ~~A NAS-on melyik DSM-verzió fut?~~ Megválaszolva (K0): DSM 7.2.2.
2. Legyen-e a nézőnek joga a kerti naplóba és a futónaplóba írni? (A terv szerint nem; külön „szerkesztő” szerep később adható.)
3. Milyen értesítési csatorna kell a szerverről: e-mail, telefonos push (ntfy), a NAS saját értesítése? (A terv mindhármat tartalmazza;
   elég az elsőként szükségeset megcsinálni.)
4. A zónák adatai: hány zóna, melyik milyen (szórófej / csepegtető), mekkora terület, mennyi a hozam? (Az Öntözés fül beállításához.)
5. Maradjon-e a SmartLife alkalmazás is használatban a doboz mellett (kézi indításra)? (A terv kezeli „külső indításként”.)
6. A 3.0 (Windows) kiadása előtt a felhasználó valódi adatán a golden összevetés lefuttatása – ehhez egy alkalom kell.

---

## A. függelék – a mai útvonalak és az új szerepük

A 2.2.8 összes útvonala (`Api.cs` 172–275. sor és `Server.cs`), az új címmel és szereppel. `ws` = `/api/ws/{id}` előtag. Ahol egy mai
kezelő GET-et és POST-ot is fogad, a táblában két sor lesz. „–” = az adott metódus nincs.

| Mai útvonal | Új cím | GET | POST | Jelzők / megjegyzés |
|---|---|---|---|---|
| `/api/status` | `ws/status` | néző | – | |
| `/api/refresh`, `/api/reload`, `/api/refresh/cancel` | `ws/refresh` … | – | admin | a letöltés API-keretet fogyaszt |
| `/api/meta`, `/api/today`, `/api/series`, `/api/days` | `ws/…` | néző | – | |
| `/api/drought` | `ws/drought` | néző | – | |
| `/api/rainevents`, `/api/events`, `/api/records`, `/api/wind`, `/api/hourly`, `/api/quality`, `/api/vars`, `/api/at` | `ws/…` | néző | – | |
| `/api/cloud`, `/api/cloud/now`, `/api/cloud/days` | `ws/…` | néző | – | |
| `/api/cloud/metar` | `ws/cloud/metar` | néző | admin | a POST a magban futtatja a METAR-letöltést (nem külön program) |
| `/api/presets` | `ws/presets` | néző | – | |
| `/api/drought/config`, `/drought/reset`, `/drought/preset`, `/drought/preset-delete` | `ws/…` | – | admin | Audit |
| `/api/ini` | `ws/ini` | néző | admin | Audit; a GET csak nem titkos kulcsokat ad |
| `/api/ini/preview` | `ws/ini/preview` | – | admin | nem ment, de számításigényes |
| `/api/calib` | `ws/calib` | néző | admin | Audit |
| `/api/ui` | `ws/ui` | néző | néző | szerepenként külön fájl (10.4) |
| `/api/store/naplo`, `/api/store/futas` | `ws/store/…` | néző | admin | (nyitott kérdés: 18./2) |
| `/api/alerts` | `ws/alerts` | néző | admin | Audit |
| `/api/alerts/test` | `ws/alerts/test` | – | admin | |
| `/api/dlsettings` | `ws/dlsettings` | admin | admin | Audit |
| `/api/changelog` | `ws/changelog` | admin | admin | a POST a felület naplóbejegyzése – mezőnként méretkorlát (TERV_4 20.5) |
| `/api/backup` | `ws/backup` | admin | admin | |
| `/api/backup/latest.zip` | `ws/backup/latest.zip` | admin | – | alapból **titkok nélküli** másolat; `?teljes=1` → Sudo |
| `/api/backup/copy` | `ws/backup/copy` | – | admin | DesktopOnly (mentés ablak) |
| `/api/backup/restore` | `ws/backup/restore` | – | admin | Sudo, Audit; asztalin fájlablak, szerveren feltöltés (MaxBody 200 MB) |
| `/api/export` | `ws/export` | – | admin | DesktopOnly |
| `/api/export.csv` | `ws/export.csv` | néző | – | |
| `/api/schedule` | `sys/schedule` | admin | admin | asztalin a Windows Feladatütemező (képesség: `feladatutemezo`), szerveren az ütemező feladatai (`sys/jobs`) |
| `/api/update` | `sys/update` | admin | admin | a POST DesktopOnly (helyi telepítő indítása); szerveren csak a verzió |
| `/api/open` | `sys/open` | – | admin | DesktopOnly |
| `/api/langs` | `sys/langs` | nyilvános | – | a belépőoldalnak is kell |

**Új útvonalak (vázlat – a pontos alak a csomagokban dől el, de a szerep és a jelzők kötelezőek):**
| Cím | GET | POST | Jelzők |
|---|---|---|---|
| `sys/info` | nyilvános | – | csak: verzió, gazda, kell-e első beállítás, a munkamenet szerepe és képességei |
| `sys/login`, `sys/logout` | – | nyilvános / néző | lassítás; ServerOnly |
| `sys/setup` | – | nyilvános (egyszeri kóddal) | ServerOnly; csak amíg nincs admin |
| `sys/users/password` | – | admin | Sudo, Audit, Encrypted |
| `sys/sessions` | admin | admin | a POST: kijelentkeztetés |
| `sys/workspaces` | néző | admin | Audit; törlésnél Sudo |
| `sys/status` | néző (szűkített) / admin (teljes) | – | |
| `sys/jobs` | admin | admin | ServerOnly |
| `sys/notifications` | néző | admin | a POST: elvetés, beállítás |
| `sys/notify/targets` | admin | admin | Sudo, Audit, Encrypted (kimenő címek, jelszavak) |
| `sys/audit` | admin | – | |
| `sys/clienterror` | – | néző | óránkénti korlát |
| `sys/transport` | admin | admin | Sudo, Audit; ServerOnly |
| `ws/irrigation/state`, `/plan`, `/journal`, `/zones`, `/programs`, `/rules` | néző | admin | Audit |
| `ws/irrigation/run`, `/stop`, `/pause`, `/emergency-stop` | – | admin | Audit, Encrypted |
| `ws/irrigation/mode` | – | admin | Audit, Encrypted; élesítésnél Sudo |
| `ws/irrigation/controller` | admin | admin | Sudo, Audit, Encrypted (helyi kulcs) |
| `ws/irrigation/controller/safety-test` | – | admin | Audit, Encrypted, Slow |
| `ws/irrigation/simulate` | – | admin | Slow; nem módosít, de számításigényes |
| `ws/irrigation/scenarios` | néző | admin | |

---

## B. függelék – a K0 próba pontosan

**Cél:** fél órán belül kiderüljön, fut-e a .NET 10 a cél-NAS-on, és milyen csomagolásban. A próbához nem kell SSH.

**Amit a fejlesztő előkészít** (`tools/k0proba`, a `release.yml`-től függetlenül, kézzel):
- Egy kis konzolprogram, két kiadásban: `linux-arm64` (a rendszer saját könyvtárával) és `linux-musl-arm64` (a mellé csomagolt
  musl-betöltővel). Mindkettő önálló mappa.
- A program sorban elvégzi és kiírja (mindegyik külön `try`, a hiba nem állítja meg a többit):
  1. .NET-verzió, operációs rendszer leírása, processzorok száma, szabad memória;
  2. HTTPS-kérés a `https://api.weather.com/` címre (bármilyen HTTP-válasz = a titkosított kapcsolat működik);
  3. az `Europe/Budapest` időzóna betöltése és egy nyári / téli időpont átváltása;
  4. fájlzár: ugyanazt a fájlt kétszer nyitja meg kizárólagosan – a másodiknak hibát kell adnia;
  5. atomi csere (`File.Replace`) és hozzáfűzés a köteten;
  6. egy helyben generált 20 MB-os CSV beolvasásának ideje, és a memóriahasználat utána;
  7. jelszókivonat (PBKDF2) sebessége: hány kör fér 300 ms-ba;
  8. AES-GCM és ECDSA elérhetősége (a szelepprotokollhoz és a tanúsítványhoz);
  9. egy percig futó webszerver (Kestrel) a 18443-as kapun saját tanúsítvánnyal – a felhasználó a böngészőből megnyitja.
- Mellé egy `proba.sh`, amely mindkét kiadást lefuttatja, és az eredményt szövegfájlokba írja; ha kell, a
  `DOTNET_EnableWriteXorExecute=0` beállítással megismétli (régi kernelen ez segíthet).
- A csomagot a fejlesztő előbb egy ARM64 Linuxon kipróbálja (a musl-betöltős indítás pontos módja ott dől el), csak utána kerül a felhasználóhoz.

**Amit a felhasználó csinál:**
1. A NAS-on létrehoz egy `WStation` megosztott mappát, és bemásolja a `k0` mappát.
2. DSM › Vezérlőpult › Feladatütemező › Létrehozás › Ütemezett feladat › Felhasználó által megadott szkript; felhasználó: a saját
   (nem root) fiókja; parancs: `sh /volume1/WStation/k0/proba.sh`. Futtatás egyszer.
3. A `k0` mappában megjelenő `eredmeny_*.txt` fájlokat elküldi (titok nincs bennük).
4. A próba után a feladat törölhető.

**Kiértékelés:** melyik kiadás indult el; a 2–9. pontok eredménye; a 6. és 7. pont számai alapján a 8.3 (növekményes betöltés) és a
10.3 (körök száma) beállítása. Ha egyik kiadás sem indul: a 10.6 3. vagy 4. útja, a felhasználó döntésével.

### A K0 eredménye (2026-11-17) – a kapu teljesült
- **1. kör, DSM 6.2.4:** a `linux-arm64` kiadás nem indult (`GLIBCXX_3.4.21 not defined`: a rendszer libstdc++-a GCC 4.9-es szintű); a
  musl-os kiadás betöltő nélkül nem indulhatott. A NAS-on frissíteni a DSM-et kellett.
- **2. kör, DSM 7.2.2 (kernel 4.4.302):** a sima **`linux-arm64` kiadás minden lépése sikeres** (.NET 10.0.15) – a 10.6 **1. útja** a végleges.
  A musl-os út (saját betöltő + Alpine-könyvtárak, `indit.sh`-val) összeállítva, de nem kellett; tartalék, ha egy DSM-frissítés elrontaná (R13).
  Feltétel: **DSM 7.2 vagy újabb** (a telepítési leírásba és az indítószkript ellenőrzésébe).

| Lépés | Eredmény a cél-NAS-on |
|---|---|
| 1. környezet | 4 mag; a rendszer **643 MB** memóriát lát, ebből a mérés idején kb. **120 MB szabad**, a swapból már 0,5 GB foglalt (a DSM 7.2 maga). A próba indulás után 27 MB. |
| 2. HTTPS | működik (HTTP 401 a WU felől), kb. 2 s az első kérés |
| 3. időzóna | helyes (nyár 14:00, tél 13:00) |
| 4–5. fájlzár, csere | a kizárólagos zár működik; `File.Replace` és hozzáfűzés rendben |
| 6. 20 MB-os CSV | írás 1,0 s; beolvasás + feldolgozás **3,2 s** (135 158 sor), memória 62 → **113 MB** |
| 7. PBKDF2-SHA256 | 10 000 kör 631 ms – egyetlen, „hideg” mérés (benne a titkosító könyvtár betöltése), ezért csak felső becslés |
| 8. AES-GCM, ECDSA | elérhető, működik |
| 9. Kestrel HTTPS | elindult, 60 s-ig futott (104 MB); böngészőből nem próbáltuk – az S6-ban kell (a DSM tűzfala) |

**Következmények a tervre:**
- **Memória (10.6, R2):** a valóban szabad memória sokkal kevesebb az 1 GB-nál. A kemény korlát **250 MB**, a cél nyugalomban
  **120 MB alatt** egy 3 éves munkaterülettel. A 20 MB-os fájl naiv beolvasása +50 MB → egy 5 éves (kb. 100 MB-os) fájlt nem szabad
  egyben `double[]`-sorokká olvasni: oszlopos tárolás (`float`, ahol elég), és a 8.3 növekményes betöltése **kötelező**, nem opció.
  Az S3 kapujában ezt a cél-NAS-on kell mérni.
- **Betöltési idő:** kb. 6 MB/s feldolgozás → egy 100 MB-os fájl teljes újraolvasása kb. 16 s; indításkor elfogadható, óránként nem (8.3).
- **Jelszókivonat (10.3):** ha a hideg mérés közel áll a valósághoz, a „legalább 150 000 kör” a NAS-on kb. 9 s / ellenőrzés (és belépéskor
  két kivonat) – nem tartható. Az S6 előtt melegen újramérni (több ismétlés); ha marad a lassúság: a NAS-on a mért 300 ms-os
  körszám, de legalább 50 000, és a belépés-korlátozás (10.3) kompenzál (R14).

---

## C. függelék – mintafájlok

**`rendszer\szerver.json`**
```json
{ "sema": 1, "kapu": 8443, "kapcsolat": "https-sajat",
  "engedelyezettNevek": ["nas.local", "192.168.1.20"],
  "lemezkimeles": true, "naploSzint": "info" }
```

**`rendszer\munkateruletek.json`**
```json
{ "sema": 1, "alap": "alap",
  "lista": [ { "id": "alap", "nev": "Otthon", "mappa": "." },
             { "id": "k3f9x2ab", "nev": "Nyaraló", "mappa": "munkateruletek/k3f9x2ab" } ] }
```

**`rendszer\kapcsolat.json`** (asztali)
```json
{ "sema": 1, "mod": "tavoli", "szerver": "https://nas.local:8443", "ujjlenyomat": "SHA256:…" }
```

**Szabály és forgatókönyv (`ontozes.json` részlet)**
```json
{ "szabalyok": [
    { "id": "eso24", "nev": "Elég eső esett", "aktiv": true, "zonak": "mind",
      "ha": { "valtozo": "eso", "ablak": "24h", "op": ">=", "ertek": 5 }, "akkor": { "kihagy": true } },
    { "id": "szel", "nev": "Szeles – ne fújja el", "aktiv": true, "zonak": ["z1", "z2"],
      "ha": { "barmelyik": [ { "valtozo": "szel",  "ablak": "10perc", "op": ">=", "ertek": 15 },
                             { "valtozo": "lokes", "ablak": "10perc", "op": ">=", "ertek": 30 } ] },
      "akkor": { "halaszt": 10, "legkesobb": "ablakVege", "utana": "kihagy" } } ],
  "zonak": [
    { "id": "z1", "nev": "Gyep elöl", "vezerloId": "v1", "csatorna": 1, "szarazsagProfilId": "gyep", "tipus": "szorofej",
      "intenzitas_mm_h": 15, "terulet_m2": 60, "maxPercFutas": 30, "maxPercNap": 45, "maxLiterNap": 700,
      "aztatas": { "szakaszPerc": 10, "szunetPerc": 30 }, "mod": "program",
      "idoablakok": [ { "tol": "21:00", "ig": "06:00" } ] } ],
  "korlat": { "maxLiterNapOssz": 2500, "maxEgyideju": 1, "zonakKoztiSzunetMp": 10, "adatMaxKorPerc": 180, "adatNelkul": "alapprogram" },
  "allapot": { "mod": "arnyek", "szunetIg": null } }
```

**Egy döntés a naplóban (`ontozes_naplo.jsonl`, egy sor)**
```json
{ "t": "2027-07-14T21:00:00+02:00", "tipus": "dontes", "zona": "z1", "program": "p1", "eredmeny": "halasztva",
  "indok": [ { "szabaly": "szel", "valtozo": "lokes", "ablak": "10perc", "ertek": 34.2, "kuszob": 30 } ],
  "kovetkezoProba": "2027-07-14T21:10:00+02:00", "mod": "eles" }
```

---

## 19. Változásnapló ehhez a tervhez
- 2026-11-13: első változat (felmérés a `2cd62af` kódon; a felhasználó döntései D1–D8).
- 2026-11-13: finomítás a kóddal összevetve – statikus állapotok leltára (6.2), indítóprogramok (9.4), jelszókivonat kimérése (10.3),
  friss adat a döntéshez (11.6), öntözési nap, főszelep, csak belső címen figyelés (10.2), natív megerősítés módváltáskor (9.1),
  A–C. függelék (útvonalak szerepei, K0 próba, mintafájlok), előkészületek (15.1), megbízás-sablon (16.).
