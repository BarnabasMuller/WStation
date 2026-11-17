// Golden rögzítő (PLAN-3.0 14.2/2): a 2.2.8 forrásaival EGYÜTT fordul (régi fordító, C# 5), saját belépési ponttal.
// A mintaadaton közvetlenül hívja az Api.Handle-t (HTTP és ablak nélkül), a válaszokat a tests/golden mappába írja.
// A 2.x forrásai ehhez nem változnak. Futtatás: tools\golden\rogzit.cmd (lásd README.md).
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Idojaras
{
    static class GoldenRogzito
    {
        // Egy hívás: azonosító (ez a válaszfájl neve is), útvonal, lekérdezés ("a=b&c=d" vagy ""), megjegyzés.
        class Hivas
        {
            public string Id, Path, Query, Note;
            public int Status;
            public Hivas(string id, string path, string query, string note) { Id = id; Path = path; Query = query; Note = note; }
        }

        // Rögzített időpontok (UTC, ezredmásodperc) a mintaadat 2025-09-01 … 2026-10-31 időszakán belül.
        const string Jul1 = "1782864000000";   // 2026-07-01 00:00 UTC
        const string Jul8 = "1783468800000";   // 2026-07-08 00:00 UTC
        const string Aug1 = "1785542400000";   // 2026-08-01 00:00 UTC
        const string Jan15 = "1768478400000";  // 2026-01-15 12:00 UTC
        const string Nyar = "2026-06-01_2026-08-31";
        const string KetIdoszak = "2025-12-01_2026-02-28,2026-06-01_2026-08-31";

        static List<Hivas> Hivasok()
        {
            var h = new List<Hivas>();
            Action<string, string, string, string> A = (id, path, q, note) => h.Add(new Hivas(id, path, q, note));

            // --- állapot és alapadatok
            A("status", "/api/status", "", "állapot (a mappa útvonala helyettesítve)");
            A("meta", "/api/meta", "", "alapadatok, hiányok, jelölések");
            A("today", "/api/today", "", "a „Ma” kártya");
            A("langs", "/api/langs", "", "nyelvek listája");

            // --- idősorok
            A("series_alap", "/api/series", "", "teljes időszak, ritkítva");
            A("series_tiszta", "/api/series", "clean=1", "teljes időszak, gyanús értékek nélkül");
            A("series_het", "/api/series", "from=" + Jul1 + "&to=" + Jul8, "egy hét, nyers pontok");
            A("series_het_tiszta", "/api/series", "from=" + Jul1 + "&to=" + Jul8 + "&clean=1", "egy hét, tisztítva");
            A("series_honap_n200", "/api/series", "from=" + Jul1 + "&to=" + Aug1 + "&n=200", "egy hónap, 200 pontra ritkítva");
            A("series_n_hatar", "/api/series", "n=5", "a pontszám alsó korlátja (50)");

            // --- napi adatok
            A("days_tiszta", "/api/days", "", "napi adatok, tisztítva");
            A("days_nyers", "/api/days", "clean=0", "napi adatok, nyersen");

            // --- szárazság, locsolás
            A("drought", "/api/drought", "", "szárazság és locsolás az alapbeállításokkal");
            A("presets", "/api/presets", "", "mentett előbeállítások");

            // --- elemzések
            A("rainevents_tiszta", "/api/rainevents", "", "esőesemények");
            A("rainevents_nyers", "/api/rainevents", "clean=0", "esőesemények nyers adatból");
            A("events", "/api/events", "", "események");
            A("records_alap", "/api/records", "", "rekordok");
            A("records_2026", "/api/records", "year=2026", "rekordok egy évre");
            A("records_nyers", "/api/records", "clean=0", "rekordok nyers adatból");
            A("records_idoszak", "/api/records", "ranges=" + Nyar, "rekordok egy időszakra");
            A("wind_alap", "/api/wind", "", "szélelemzés, teljes időszak");
            A("wind_honap", "/api/wind", "from=" + Jul1 + "&to=" + Aug1, "szélelemzés, egy hónap");
            A("hourly_temp", "/api/hourly", "", "óránkénti hőtérkép (alap: hőmérséklet)");
            A("hourly_hum", "/api/hourly", "metric=hum", "");
            A("hourly_wind", "/api/hourly", "metric=wind", "");
            A("hourly_sun", "/api/hourly", "metric=sun", "");
            A("hourly_pres", "/api/hourly", "metric=pres", "");
            A("hourly_dew", "/api/hourly", "metric=dew", "");
            A("hourly_vpd", "/api/hourly", "metric=vpd", "");
            A("hourly_ismeretlen", "/api/hourly", "metric=nincsilyen", "ismeretlen mérőszám");
            A("hourly_temp_idoszak", "/api/hourly", "metric=temp&ranges=" + KetIdoszak, "két időszakra szűkítve");
            A("quality_alap", "/api/quality", "", "adatminőség");
            A("quality_idoszak", "/api/quality", "ranges=" + Nyar, "adatminőség egy időszakra");
            A("vars_ora_het", "/api/vars", "from=" + Jul1 + "&to=" + Jul8, "változórács, óránként, egy hét");
            A("vars_nap", "/api/vars", "res=day", "változórács, naponta, teljes időszak");
            A("vars_5perc_het", "/api/vars", "res=5perc&from=" + Jul1 + "&to=" + Jul8, "változórács, 5 percenként, egy hét");
            A("at_utolso", "/api/at", "", "az utolsó mérés értékei");
            A("at_januar", "/api/at", "t=" + Jan15, "egy téli időpont értékei");

            // --- felhőzet
            A("cloud_alap", "/api/cloud", "", "felhőzet-becslés, az utolsó 7 nap");
            A("cloud_het", "/api/cloud", "from=" + Jul1 + "&to=" + Jul8, "felhőzet-becslés, egy nyári hét");
            A("cloud_now", "/api/cloud/now", "", "felhőzet most");
            A("cloud_days", "/api/cloud/days", "", "felhőzet naponta");
            A("cloud_metar", "/api/cloud/metar", "", "METAR-összevetés (a mintában nincs METAR)");

            // --- beállítások és tárolók olvasása (GET)
            A("ui", "/api/ui", "", "felületi beállítások");
            A("store_naplo", "/api/store/naplo", "", "kerti napló");
            A("store_futas", "/api/store/futas", "", "futónapló");
            A("alerts", "/api/alerts", "", "riasztások beállításai");
            A("changelog", "/api/changelog", "", "változásnapló");
            A("dlsettings", "/api/dlsettings", "", "a letöltő beállításai (kitalált kulccsal)");
            A("ini", "/api/ini", "", "megjelenito.ini a felület számára");
            A("calib", "/api/calib", "", "kalibráció");
            A("backup", "/api/backup", "", "biztonsági mentés állapota (a mintában nincs mentés)");
            A("update", "/api/update", "", "frissítés keresése (a mintamappában nincs telepítő)");

            // --- állapotkódok: ismeretlen útvonal, és csak POST-tal hívható végpontok GET-tel
            A("hiba_ismeretlen", "/api/nincs-ilyen", "", "404");
            foreach (var p in new[] { "alerts/test", "backup/copy", "backup/restore", "open", "reload", "refresh", "refresh/cancel",
                                      "export", "drought/config", "drought/reset", "drought/preset", "drought/preset-delete" })
                A("hiba_get_" + p.Replace('/', '_').Replace('-', '_'), "/api/" + p, "", "405: csak POST");
            return h;
        }

        // Nem rögzített végpontok – és miért.
        static readonly string[][] NemRogzitett =
        {
            new[] { "GET /api/schedule", "gépfüggő: a Windows Feladatütemezőjét kérdezi le" },
            new[] { "POST /api/ui, /api/store/naplo, /api/store/futas, /api/alerts, /api/changelog, /api/dlsettings, /api/ini, /api/ini/preview, /api/calib", "módosító hívás" },
            new[] { "POST /api/schedule, /api/backup, /api/update", "módosító hívás, külső programot indít" },
            new[] { "POST /api/alerts/test, /api/backup/copy, /api/backup/restore, /api/open, /api/export", "párbeszédablakot nyit vagy külső programot indít" },
            new[] { "POST /api/reload, /api/refresh, /api/refresh/cancel, /api/cloud/metar", "újratöltést vagy letöltést indít" },
            new[] { "POST /api/drought/config, /api/drought/reset, /api/drought/preset, /api/drought/preset-delete", "módosító hívás" },
        };

        // Idő- vagy futásfüggő mezők: a rögzített fájlban rögzített helyettesítő érték áll, az összevetés név szerint kihagyja őket.
        static readonly string[][] Idofuggo =
        {
            new[] { "meta", "loadMs", "a betöltés mért ideje" },
            new[] { "meta", "loadedAt", "a betöltés időpontja (rendszeróra)" },
            new[] { "today", "now.ageMin", "a legfrissebb mérés kora a rendszerórához képest" },
        };

        const string MappaJel = "<MAPPA>";

        static int Main(string[] args)
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
            if (args.Length == 3 && args[0] == "--onteszt") return Onteszt(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Használat: rogzito <mintaadat mappa> <kimeneti mappa>");
                Console.Error.WriteLine("           rogzito --onteszt <mintaadat mappa> <kimeneti fájl>");
                return 2;
            }
            string dir = Path.GetFullPath(args[0]).TrimEnd('\\'), outDir = Path.GetFullPath(args[1]);

            // ugyanaz az indítás, mint a Program.Main-ben – ablak és szerver nélkül
            var L = Layout.For(dir);
            Guard.SetRoot(dir);
            AppState.L = L;
            L.Ensure();
            AppState.RefreshAsync(false);
            for (int i = 0; i < 1200 && AppState.Status == "loading"; i++) Thread.Sleep(100);
            if (AppState.Status != "ready")
            {
                Console.Error.WriteLine("A mintaadat nem töltődött be: " + AppState.Status + " – " + AppState.Message);
                return 1;
            }
            Thread.Sleep(500);   // az induláskori mentés-ellenőrzés (háttérszál) fusson le, mielőtt hívni kezdünk

            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "*.json")) File.Delete(f);
            var utf8 = new UTF8Encoding(false);
            var list = Hivasok();
            long bytes = 0;
            foreach (var h in list)
            {
                int status;
                string body = Api.Handle(h.Path, ParseQuery(h.Query), "GET", out status);
                h.Status = status;
                body = Normalizal(h.Id, body ?? "", dir);
                File.WriteAllText(Path.Combine(outDir, h.Id + ".json"), body, utf8);
                bytes += utf8.GetByteCount(body);
            }
            File.WriteAllText(Path.Combine(outDir, "hivasok.json"), HivasokJson(list), utf8);
            File.WriteAllText(Path.Combine(outDir, "mintaadat.sha256"), Osszegek(dir), utf8);
            Console.WriteLine("Rögzítve: " + list.Count + " hívás, " + (bytes / 1024) + " kB – " + outDir);
            return 0;
        }

        static NameValueCollection ParseQuery(string q)
        {
            var c = new NameValueCollection();
            foreach (var part in q.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) c[part] = ""; else c[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
            return c;
        }

        // A válasz bájtra a 2.2.8-é, kivéve: a mintamappa útvonala (gépfüggő) és az időfüggő mezők értéke.
        static string Normalizal(string id, string body, string dir)
        {
            foreach (var form in new[] { dir.Replace("\\", "\\\\"), dir, dir.Replace('\\', '/') })
                body = Regex.Replace(body, Regex.Escape(form), MappaJel, RegexOptions.IgnoreCase);
            if (id == "meta")
            {
                body = Regex.Replace(body, "\"loadMs\":-?\\d+", "\"loadMs\":0");
                body = Regex.Replace(body, "\"loadedAt\":\"[^\"]*\"", "\"loadedAt\":\"\"");
            }
            if (id == "today") body = Regex.Replace(body, "\"ageMin\":-?\\d+", "\"ageMin\":0");
            return body;
        }

        static string Q(string s) { return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }

        // Kézzel formázva (hívásonként egy sor), hogy a változás a különbségnézetben olvasható legyen.
        static string HivasokJson(List<Hivas> list)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"forras\": \"WStation 2.2.8, tools/golden\",\n");
            sb.Append("  \"mappaJel\": " + Q(MappaJel) + ",\n");
            sb.Append("  \"hivasok\": [\n");
            for (int i = 0; i < list.Count; i++)
            {
                var h = list[i];
                sb.Append("    {\"id\": " + Q(h.Id) + ", \"metodus\": \"GET\", \"utvonal\": " + Q(h.Path) + ", \"lekerdezes\": " + Q(h.Query)
                    + ", \"allapot\": " + h.Status + ", \"fajl\": " + Q(h.Id + ".json") + ", \"megjegyzes\": " + Q(h.Note) + "}"
                    + (i < list.Count - 1 ? "," : "") + "\n");
            }
            sb.Append("  ],\n");
            sb.Append("  \"idofuggo\": [\n");
            for (int i = 0; i < Idofuggo.Length; i++)
                sb.Append("    {\"id\": " + Q(Idofuggo[i][0]) + ", \"mezo\": " + Q(Idofuggo[i][1]) + ", \"ok\": " + Q(Idofuggo[i][2]) + "}" + (i < Idofuggo.Length - 1 ? "," : "") + "\n");
            sb.Append("  ],\n");
            sb.Append("  \"nemRogzitett\": [\n");
            for (int i = 0; i < NemRogzitett.Length; i++)
                sb.Append("    {\"vegpont\": " + Q(NemRogzitett[i][0]) + ", \"ok\": " + Q(NemRogzitett[i][1]) + "}" + (i < NemRogzitett.Length - 1 ? "," : "") + "\n");
            sb.Append("  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        // A mintaadat fájljainak SHA-256 összege (csak a generátor kimenete: data\ és a letöltő ini-je).
        static string Osszegek(string dir)
        {
            var sb = new StringBuilder();
            var files = Directory.GetFiles(Path.Combine(dir, Layout.DataName), "*.csv")
                .Concat(Directory.GetFiles(Path.Combine(dir, Layout.DlAppName), "*.ini"))
                .OrderBy(f => f.Substring(dir.Length).Replace('\\', '/'), StringComparer.Ordinal);
            using (var sha = SHA256.Create())
                foreach (var f in files)
                {
                    byte[] hash = sha.ComputeHash(File.ReadAllBytes(f));
                    sb.Append(string.Concat(hash.Select(b => b.ToString("x2")))).Append("  ")
                      .Append(f.Substring(dir.Length + 1).Replace('\\', '/')).Append("\n");
                }
            return sb.ToString();
        }

        // A 2.2.8 öntesztje a mintaadaton: pontonként az eredmény (a mért idők és számok nélkül) és az összesítés.
        static int Onteszt(string dir, string outFile)
        {
            var old = Console.Out;
            var sw = new StringWriter();
            Console.SetOut(sw);
            int code;
            try { code = SelfTest.Run(Layout.For(dir)); }
            finally { Console.SetOut(old); }
            var sb = new StringBuilder();
            sb.Append("WStation 2.2.8 – az önteszt eredménye a generált mintaadaton (tools/golden)\n");
            sb.Append("A pontok részletei (mért idők, darabszámok) nélkül; ez az S1 kapujának viszonyítási alapja.\n\n");
            foreach (var raw in sw.ToString().Replace("\r", "").Split('\n'))
            {
                string line = raw;
                if (line.StartsWith("[OK]") || line.StartsWith("[HIBA]") || line.StartsWith("[--]"))
                {
                    int cut = line.IndexOf(" – ", StringComparison.Ordinal);
                    if (cut > 0) line = line.Substring(0, cut);
                    sb.Append(line.TrimEnd()).Append("\n");
                }
                else if (line.StartsWith("Összesen:")) sb.Append("\n").Append(line.TrimEnd()).Append("\n");
            }
            sb.Append("Kilépési kód: " + code + "\n");
            File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine(sb.ToString());
            return 0;
        }
    }
}
