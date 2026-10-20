# Translating WStation

*Magyarul: [FORDITAS.md](FORDITAS.md)*

The UI is written in Hungarian. Every other language is **one JSON file** – no code changes are needed.

## 1. Interface, Downloader and Installer texts

1. Copy `web/lang/_sablon.json` to `web/lang/<code>.json` (two-letter ISO 639-1 code, lower case, e.g. `it.json`).
2. Fill in `_meta`:
   ```json
   "_meta": { "code": "it", "name": "Italiano", "locale": "it-IT",
              "dirs": ["N","NNE","NE","ENE","E","ESE","SE","SSE","S","SSO","SO","OSO","O","ONO","NO","NNO"] }
   ```
   - `name` – the language's own name (shown in the language list),
   - `locale` – number and date format (month and day names come from it automatically),
   - `dirs` – the 16 compass points, clockwise from north.
3. Translate the entries: `"Hungarian text": "translation"`. Empty values fall back to Hungarian.
4. Run `build.bat`. The language appears in **Settings › Language** and in the installer.

While developing (`Megjelenítő.exe --bongeszo --web web`) the file is read from disk, no rebuild needed.

## How matching works

- A text that matches a key exactly (ignoring surrounding spaces and a trailing `.:!?`) is replaced whole.
- Otherwise every key found inside the text is replaced (longest first), so numbers and units stay in place:
  `"Csapadék a kiválasztott időszakban 12,3 mm"` → `"Rain in the selected period 12.3 mm"`.
- Keys shorter than 5 characters without a space (e.g. `"perc"`, `"nap"`) are only replaced inside short labels
  (at most four words), to avoid half-translated sentences.
- Where the Hungarian source is split by numbers or markup, the keys are fragments (e.g. `"A feltöltéshez kb"`);
  translate them as fragments.
- Keys with `{name}` placeholders are whole-sentence templates (e.g. `"Ma {time} óta"`): keep every placeholder in the
  translation – their order may change freely (e.g. `"Since {time} today"`); the program fills in the values.

## 2. Guides (txt)

Create `docs/<code>/` with translated copies of `OLVASS_EL.txt`, `Letolto_leiras.txt`, `Megjelenito_leiras.txt`
and `VALTOZASOK.txt` (same file names). Keep the version headers in the form `== 2.1 (2026-10-19) ==`; the
installer reads "what's new" from them. Installed file names come from the language JSON
(`"OLVASS_EL.txt": "README.txt"`, …). `build.bat` embeds every `docs/<code>/*.txt`; languages without guides get
the Hungarian ones.

## 3. Keeping translations complete

```
powershell -ExecutionPolicy Bypass -File tools\forditas_kulcsok.ps1 -Out <folder>
```

lists the untranslated phrases for every language (`<code>_hianyzik.txt`) and refreshes `_sablon.json`.
