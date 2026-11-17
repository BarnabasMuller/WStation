# Golden rögzítő

A 3.0 átírás biztosítéka (`docs/dev/PLAN-3.0.md` 14.2): a **2.2.8** válaszai a generált mintaadaton. A 3.0 összevető
tesztje ugyanazokat a hívásokat teszi az új útválasztón, és az eredményt ezekhez hasonlítja.

## Mit állít elő (`tests/golden/`)

| Fájl | Tartalom |
|---|---|
| `hivasok.json` | a hívások listája: azonosító, útvonal, lekérdezés, állapotkód, válaszfájl; az időfüggő mezők; a nem rögzített végpontok indoklással |
| `<azonosító>.json` | egy-egy hívás válasza, bájtra úgy, ahogy a 2.2.8 adta (két kivétellel, lásd lent) |
| `onteszt_2.2.8.txt` | a 2.2.8 öntesztjének eredménye a mintaadaton, pontonként – az S1 kapujának viszonyítási alapja |
| `mintaadat.sha256` | a generált mintaadat fájljainak ellenőrzőösszege – a rögzítés erre az adatra vonatkozik |

Két dolog tér el a 2.2.8 nyers válaszától:
- a mintamappa (gépfüggő) útvonala helyén `<MAPPA>` áll (`status`, `meta`);
- az idő- és futásfüggő mezők értéke rögzített: `meta.loadMs` = 0, `meta.loadedAt` = "", `today.now.ageMin` = 0.
  Ezeket az összevetés név szerint kihagyja (`hivasok.json` › `idofuggo`).

## Futtatás

Windowson, a repó gyökeréből (kell a .NET Framework 4.8 fordítója – a Windows része – és a .NET 10 SDK):

```
tools\golden\rogzit.cmd
```

A szkript a `%TEMP%\wstation-golden` mappában dolgozik: legenerálja a mintaadatot (`tools/adatgen`), a 2.2.8 forrásaiból
(`src/megjelenito`, `src/kozos`) és a `Rogzito.cs`-ből a régi fordítóval – a `build.bat` kapcsolóival – konzolprogramot fordít,
majd lefuttatja. A rögzítő az `Api.Handle`-t közvetlenül hívja, HTTP és ablak nélkül. A 2.x forrásai ehhez nem változnak.

A rögzítés megismételhető: kétszer futtatva a `tests/golden` tartalma bájtra azonos.

## Mikor kell újra futtatni

- Ha a mintaadat-generátor változik (más lesz a `mintaadat.sha256`).
- Ha egy szándékos számítási javítás után a golden fájlokat frissíteni kell – ez mindig külön, megindokolt lépés (terv, M10).
  Ilyenkor a rögzítőt a **javított** forrásból kell fordítani; a 2.2.8-as alapállapot a git történetében marad.

## Amit nem rögzít

Csak olvasó (GET) hívásokat rögzít. A módosító, párbeszédablakot nyitó, külső programot indító és a gépfüggő végpontok
listája indoklással a `hivasok.json` › `nemRogzitett` részében van; a csak POST-tal hívható végpontok GET-es elutasítása
(405) viszont rögzítve van.
