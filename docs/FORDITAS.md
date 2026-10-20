# Fordítás – új nyelv hozzáadása

*English: [TRANSLATING.md](TRANSLATING.md)*

A program magyarul készül; minden más nyelv **egy-egy fájl**, a kódot nem kell módosítani.

## 1. A felület, a Letöltő és a telepítő szövegei

1. Másold le a `web/lang/_sablon.json` fájlt `web/lang/<kód>.json` néven (pl. `it.json`, `fr.json`).
   A kód kétbetűs (ISO 639-1), kisbetűs.
2. Töltsd ki a `_meta` részt:
   ```json
   "_meta": { "code": "it", "name": "Italiano", "locale": "it-IT",
              "dirs": ["N","NNE","NE","ENE","E","ESE","SE","SSE","S","SSO","SO","OSO","O","ONO","NO","NNO"],
              "files": { "viewer": "Visualizzatore.exe", "downloader": "Scaricatore.exe" } }
   ```
   - `name`: a nyelv neve a saját nyelvén (ez jelenik meg a listában),
   - `locale`: a szám- és dátumformátumhoz (pl. `it-IT`),
   - `dirs`: a 16 égtáj rövidítése, északtól az óramutató járása szerint,
   - `files`: a két program fájlneve ezen a nyelven (a telepítő ezen a néven másolja be őket;
     ha üres vagy hiányzik, a magyar `Megjelenítő.exe` / `Letöltő.exe` marad).
3. Írd be a fordításokat: minden sor `"magyar szöveg": "fordítás"`. Ami üresen marad, az magyarul jelenik meg.
   - A hónapok és a napok nevét a program magától adja a `locale` alapján.
   - Ahol a magyar szöveg darabokban áll (pl. `"A feltöltéshez kb"`), a fordítás is darab legyen.
4. Futtasd a `build.bat`-ot. A nyelv megjelenik a **Beállítások › Nyelv** listában és a telepítőben.

Fejlesztés közben (`Megjelenítő.exe --bongeszo --web <a web mappa>`) a nyelvi fájl azonnal, build nélkül is betöltődik.

## 2. A leírások (txt)

Hozz létre egy `docs/<kód>/` mappát, és tedd bele a lefordított fájlokat ugyanazon a néven:
`OLVASS_EL.txt`, `Letolto_leiras.txt`, `Megjelenito_leiras.txt`, `VALTOZASOK.txt`.
A `VALTOZASOK.txt`-ben a verziófejléc maradjon `== 2.1 (2026-10-19) ==` formájú (a telepítő ebből olvassa az újdonságokat).
A telepített fájlnevek a nyelvi JSON-ból jönnek (`"OLVASS_EL.txt": "README.txt"` stb.).
A `build.bat` minden `docs/<kód>/*.txt`-t magától beágyaz; ha egy nyelvhez nincs leírás, a magyar kerül fel.

## 3. Új magyar szöveg után

Ha a felületre új magyar szöveg kerül, a `tools/forditas_kulcsok.ps1` kigyűjti a még le nem fordított
kifejezéseket (a `web` és `src` mappából), és frissíti a `_sablon.json`-t.
