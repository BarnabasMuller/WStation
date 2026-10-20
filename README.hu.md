# WStation – Budaörsi időjárás

Házi meteorológiai állomás adatainak letöltése és elemzése. Az adatokat a
[Weather Underground PWS API](https://www.wunderground.com/member/api-keys) adja, a megjelenítés internet
nélkül, egy Windows-programban történik.

*English: [README.md](README.md)*

Egy budaörsi állomáshoz készült, de bármely Weather Underground házi állomással működik. A felület
**magyar, angol, német és spanyol** nyelvű, és új nyelv egyetlen JSON-fájllal hozzáadható.

## Mi van benne?

| Program | Fájl | Feladata |
|---|---|---|
| **Letöltő** | `Letöltő.exe` | A WU PWS API-ból CSV-be tölti az állomás adatait; maga dönti el, mit érdemes lekérdezni (pillanatnyi állapot, napi összesítő, hiányzó napok 5 perces részletességgel), naplóz, figyel az API-limitekre, ütemezhető a Feladatütemezővel, és Windows-riasztásokat küld (fagy, nyomásesés, erős szél, felhőszakadás, hőség). |
| **Megjelenítő** | `Megjelenítő.exe` | Internet nélkül is működő felület (WebView2 + Apache ECharts): idővonal hat összekapcsolt grafikonnal, naptár-hőtérkép, szárazság-modul, eső- és szélstatisztika, rekordok, jelentések. |
| **Telepítő** | `WStation_setup_v2.2.x.exe` | Egyetlen exe, benne mindkét program és a leírások; kezeli az új telepítést, a frissítést, a régi mappaszerkezetet és a futó programot; a nyelvet a Windowsból ajánlja. |

Főbb tudnivalók a Megjelenítőről:

- **Idővonal** – hőmérséklet és harmatpont, pára, légnyomás, szél és lökés, csapadék, napsugárzás és UV; jelölt gyanús értékek és adathiányok; front- és zivatardetektor; saját képlet sáv.
- **Szárazság és locsolás** – FAO-56 Penman–Monteith párolgás és talajvíz-mérleg növénytípusonként (gyep, bokrok, cserepek, fák), locsolási javaslat, mentett öntözési programok, csapadékmérő-kalibráció, vízfogyasztás és vízigény költséggel.
- **Tűzveszély** – kanadai FWI (FFMC, DMC, DC, ISI, BUI, FWI) lépésenkénti „Mai számítás” táblázattal; páranyomás-hiány; hőösszeg.
- **Statisztika** – akárhány időszak (együtt vagy egymás mellett), rekordok, hónapok összevetése, óra × nap hőtérkép, egymásra rajzolt időszakok.
- **Vitorlázóknak** – szél km/h-ban, m/s-ban, csomóban, mph-ban vagy tört Beaufortban; szélrózsa; szeles délutánok.
- **Minden számolt érték mellett ƒx gomb**: képlet, paraméterek szabványos értékkel, visszaállítás, elnevezett presetek.

## Gyors kezdés

1. Készítsd el a telepítőt (lásd lent), vagy töltsd le egy kiadásból.
2. Indítsd el, válassz mappát – létrejön egy `Időjárás` mappa a programokkal.
3. Indítsd el egyszer a `Letöltő.exe`-t: megkérdezi a Weather Underground **API kulcsot** és az **állomás azonosítóját**.
4. Indítsd el a `Megjelenítő.exe`-t; a **Frissítés** gomb tölti le az új adatokat.

A telepítővel érkező leírások: [`docs/`](docs) (magyar), [`docs/en`](docs/en), [`docs/de`](docs/de), [`docs/es`](docs/es).

## Buildelés és tesztelés

Windows 10/11 (.NET Framework 4.8, alapból megvan) – semmit nem kell telepíteni, internet sem kell.

```
build.bat
dist\bin\Megjelenito.exe --teszt --mappa <mappa, benne data\>
```

Eredmény: `dist\WStation_setup_v2.2.x.exe`, valamint `dist\bin\Megjelenito.exe` és `dist\bin\Letolto.exe`.

## Fejlesztőknek

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) – felépítés, adatfolyam, HTTP API, felületi modulok (angolul)
- [CONTRIBUTING.md](CONTRIBUTING.md) – szabályok, kódolások, új funkció és teszt (angolul)
- [docs/GLOSSARY.md](docs/GLOSSARY.md) – a magyar nyelvű kód gyakori kifejezései angolul
- [docs/FORDITAS.md](docs/FORDITAS.md) – új nyelv hozzáadása

## Licenc

GNU General Public License 3.0 – lásd [LICENSE](LICENSE). A beágyazott külső komponensek: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
