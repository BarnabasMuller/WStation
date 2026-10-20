// Weather Underground PWS adatletöltő
// Fordítás: a projekt gyökerében a build.bat (a Windows beépített .NET Framework 4.8 C# fordítójával)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

[assembly: System.Reflection.AssemblyTitle("Weather Underground PWS adatletolto")]
[assembly: System.Reflection.AssemblyProduct("WStation – Letöltő")]
[assembly: System.Reflection.AssemblyVersion("2.2.3.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.2.3.0")]

namespace WuLetolto
{
    class FatalException : Exception
    {
        public int Code;
        public FatalException(string msg, int code) : base(msg) { Code = code; }
    }

    class BudgetException : Exception
    {
        public BudgetException(string msg) : base(msg) { }
    }

    class SaveException : Exception
    {
        public SaveException(string msg, Exception inner) : base(msg, inner) { }
    }

    // ------------------------------------------------------------------ Napló

    static class Log
    {
        static string path;

        public static void Init(string p)
        {
            path = p;
            try
            {
                // 5 MB felett a régi naplót félreteszi, hogy ne nőjön a végtelenségig
                if (File.Exists(p) && new FileInfo(p).Length > 5 * 1024 * 1024)
                {
                    string old = p + ".regi";
                    if (File.Exists(old)) Guard.Delete(old);
                    Guard.Move(p, old);
                }
            }
            catch { }
        }

        public static void Info(string m) { Write("INFO ", m, ConsoleColor.Gray); }
        public static void Ok(string m) { Write("OK   ", m, ConsoleColor.Green); }
        public static void Warn(string m) { Write("FIGY ", m, ConsoleColor.Yellow); }
        public static void Error(string m) { Write("HIBA ", m, ConsoleColor.Red); }

        static void Write(string level, string msg, ConsoleColor color)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " [" + level + "] " + msg;
            try
            {
                ConsoleColor prev = Console.ForegroundColor;
                Console.ForegroundColor = color;
                // a képernyőn a választott nyelven; ha a kimenetet a Megjelenítő olvassa (átirányítva), magyarul marad
                Console.WriteLine(Console.IsOutputRedirected ? line : Langs.T(line));
                Console.ForegroundColor = prev;
            }
            catch { }
            if (path == null) return;
            try { Guard.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(true)); }
            catch { }
        }
    }

    // ------------------------------------------------------------------ Beállítások

    static class Cfg
    {
        const string AppDirNameForMsg = "appdata_letöltő";
        public static string ApiKey = "";
        public static string StationId = "";
        public static string Units = "m";
        public static int BackfillDays = 30;
        public static DateTime? StartDate;
        public static int MaxCallsPerRun = 300;
        public static int DailyCallLimit = 1450;
        public static int CallDelayMs = 2100;
        public static int MinMinutesBetweenRecent = 5;
        public static bool SaveCurrent = true;
        public static bool SaveDailySummary = true;
        public static char Delim = ';';
        public static string Dec = ",";
        public static bool WaitAtEnd = true;
        public static string ApiBaseUrl = "https://api.weather.com";
        public static int BackupDays = 7;
        public static int BackupKeep = 8;
        public static bool AlFrost = true, AlPres = true, AlWind = true, AlRain = true, AlHeat = true;
        public static double FrostT = 1, PresDrop = 4, GustT = 60, RainT = 30, HeatT = 33;
        public static int AlRepeat = 6, QuietFrom = 22, QuietTo = 7;

        const string Template =
@"# =====================================================================
#  Weather Underground PWS letöltő - beállítások
#  A '#' kezdetű sorok megjegyzések. Formátum: Kulcs=Érték
# =====================================================================

# Az API kulcsod. Itt találod / generálod:
#   https://www.wunderground.com/member/api-keys
ApiKey=

# Az időjárás-állomás azonosítója (pl. IBUDAP123)
StationId=

# Mértékegység: a mérések mindig metrikusan (°C, km/h, mm, hPa) töltődnek le;
# a kijelzés egységét a Megjelenítő Beállítások › Mértékegységek részében lehet állítani.
Units=m

# Első futáskor (és ha hiány van) hány napra visszamenőleg töltse le
# a legrészletesebb (kb. 5 perces) méréseket.
BackfillDays=30

# Opcionális kezdő dátum (ÉÉÉÉ-HH-NN). Ha meg van adva, felülírja a BackfillDays-t.
# Pl. az állomás üzembe helyezésének napja, ha mindent le akarsz tölteni.
StartDate=

# Egy futás alatt legfeljebb ennyi API hívás (a többit a következő futás pótolja)
MaxCallsPerRun=300

# A Weather Underground limitje 1500 hívás/nap. Ennél ne legyen nagyobb.
DailyCallLimit=1450

# Két hívás közti szünet ezredmásodpercben (a limit 30 hívás/perc)
CallDelayMs=2100

# A pillanatnyi és 24 órás lekérdezést legfeljebb ennyi percenként futtatja
# (az állomások kb. 5 percenként frissítenek, gyakrabban nincs értelme)
MinMinutesBetweenRecent=5

# Mentse-e külön fájlba a pillanatnyi állapotot és a napi összesítőket
SaveCurrent=true
SaveDailySummary=true

# CSV formátum. Magyar Excelhez: elválasztó ; és tizedesjel ,
# (tabulátorhoz írd: tab)
CsvDelimiter=;
DecimalSeparator=,

# Futás végén várjon-e Enter lenyomására (ütemezett futásnál add meg a --nowait kapcsolót)
WaitAtEnd=true

# Az API címe (normál esetben ne módosítsd)
ApiBaseUrl=https://api.weather.com

# Automatikus biztonsági mentés (zip) a data mappáról és a beállításokról
# az appdata_letöltő\mentesek mappába. Hány naponta (0 = soha), és hányat tartson meg.
MentesNaponta=7
MentesekSzama=8
# A Letöltő üzeneteinek és riasztásainak nyelve (hu, en, de, es …; a Megjelenítő Beállítások › Nyelv állítja)
Nyelv=hu

# --- Riasztások (Windows-értesítés minden futáskor, ütemezve is) ---
# Fagyveszély: a hőmérséklet legfeljebb ennyi °C
RiasztasFagy=true
FagyHatar=1
# Gyors nyomásesés: legalább ennyi hPa 3 óra alatt
RiasztasNyomas=true
NyomasEses3h=4
# Erős szél: széllökés legalább ennyi km/h az utolsó órában
RiasztasSzel=true
LokesHatar=60
# Felhőszakadás: csapadékintenzitás legalább ennyi mm/h
RiasztasEso=true
EsoIntenzitas=30
# Hőség: a hőmérséklet legalább ennyi °C
RiasztasHoseg=true
HosegHatar=33
# Ugyanarra az eseményre legfeljebb ennyi óránként szól; csendes órák (ekkor nem szól)
RiasztasIsmetlesOra=6
CsendesTol=22
CsendesIg=7

# --- Felhőzet-ellenőrzés (opcionális) ---
# A legközelebbi repülőtér METAR-jelentései (NOAA Aviation Weather Center, ingyenes, kulcs nélkül),
# csak a Megjelenítő felhőzet-becslésének ellenőrzéséhez. Kikapcsolva (vagy internet nélkül) semmi nem áll le.
MetarEllenorzes=false
MetarKod=LHBP
";

        public static void Load(string path, bool interactive)
        {
            if (!File.Exists(path))
            {
                Guard.WriteAllText(path, Template.Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(true));
                Log.Info("A beállításfájl nem létezett, létrehoztam: " + Path.GetFileName(path));
            }
            else
            {
                try
                {
                    var added = Kozos.IniMerge.Merge(path, Template);
                    if (added.Count > 0) Log.Info("Új beállítások kerültek a beállításfájlba: " + string.Join(", ", added));
                }
                catch (Exception ex) { Log.Warn("A beállításfájl nem bővíthető: " + ex.Message); }
            }

            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) { Log.Warn("Értelmezhetetlen sor a beállításokban, kihagyom: " + line); continue; }
                d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            ApiKey = Str(d, "ApiKey", "");
            StationId = Str(d, "StationId", "").ToUpperInvariant();
            // A Megjelenítő minden számítása metrikus adatot vár (ET₀, FWI, rekordok, riasztási küszöbök): más egység a CSV-t elrontaná.
            string units = Str(d, "Units", "m").ToLowerInvariant();
            if (units != "m") Log.Warn("A beallitasok.ini Units=" + units + " értéke már nem támogatott: a letöltés metrikus (m), a kijelzés egysége a Megjelenítőben állítható.");
            Units = "m";
            BackfillDays = Int(d, "BackfillDays", 30, 0, 36500);
            MaxCallsPerRun = Int(d, "MaxCallsPerRun", 300, 1, 100000);
            DailyCallLimit = Int(d, "DailyCallLimit", 1450, 1, 100000);
            CallDelayMs = Int(d, "CallDelayMs", 2100, 0, 600000);
            MinMinutesBetweenRecent = Int(d, "MinMinutesBetweenRecent", 5, 0, 1440);
            SaveCurrent = Bool(d, "SaveCurrent", true);
            SaveDailySummary = Bool(d, "SaveDailySummary", true);
            WaitAtEnd = Bool(d, "WaitAtEnd", true);
            ApiBaseUrl = Str(d, "ApiBaseUrl", "https://api.weather.com").TrimEnd('/');
            BackupDays = Int(d, "MentesNaponta", 7, 0, 365);
            AlFrost = Bool(d, "RiasztasFagy", true); AlPres = Bool(d, "RiasztasNyomas", true); AlWind = Bool(d, "RiasztasSzel", true);
            AlRain = Bool(d, "RiasztasEso", true); AlHeat = Bool(d, "RiasztasHoseg", true);
            FrostT = Dbl(d, "FagyHatar", 1); PresDrop = Dbl(d, "NyomasEses3h", 4); GustT = Dbl(d, "LokesHatar", 60); RainT = Dbl(d, "EsoIntenzitas", 30); HeatT = Dbl(d, "HosegHatar", 33);
            AlRepeat = Int(d, "RiasztasIsmetlesOra", 6, 1, 168); QuietFrom = Int(d, "CsendesTol", 22, 0, 24); QuietTo = Int(d, "CsendesIg", 7, 0, 24);
            BackupKeep = Int(d, "MentesekSzama", 8, 1, 1000);
            string nyelv = Str(d, "Nyelv", "hu").Trim();
            Langs.Current = nyelv == "hu" || Langs.Exists(nyelv) ? nyelv : "hu";

            string delim = Str(d, "CsvDelimiter", ";");
            if (delim.Equals("tab", StringComparison.OrdinalIgnoreCase) || delim == "\\t") Delim = '\t';
            else if (delim.Length == 1) Delim = delim[0];
            else { Log.Warn("Érvénytelen CsvDelimiter (" + delim + "), ';' karaktert használok."); Delim = ';'; }

            Dec = Str(d, "DecimalSeparator", ",");
            if (Dec != "," && Dec != ".") { Log.Warn("Érvénytelen DecimalSeparator, ',' karaktert használok."); Dec = ","; }
            if (Dec[0] == Delim)
            {
                Log.Warn("A tizedesjel és a CSV elválasztó ugyanaz (" + Dec + "), ezért tizedespontot használok.");
                Dec = ".";
            }

            string sd = Str(d, "StartDate", "");
            StartDate = null;
            if (sd.Length > 0)
            {
                DateTime t;
                if (DateTime.TryParseExact(sd, new[] { "yyyy-MM-dd", "yyyyMMdd", "yyyy.MM.dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                    StartDate = t.Date;
                else
                    Log.Warn("Érvénytelen StartDate (" + sd + "), figyelmen kívül hagyom. Formátum: ÉÉÉÉ-HH-NN");
            }

            // Hiányzó kötelező adatok: interaktív futásnál megkérdezzük és elmentjük
            if (ApiKey.Length == 0 && interactive)
            {
                ApiKey = Ask("Add meg a Weather Underground API kulcsot: ");
                if (ApiKey.Length > 0) SetValue(path, "ApiKey", ApiKey);
            }
            if (StationId.Length == 0 && interactive)
            {
                StationId = Ask("Add meg az állomás azonosítóját (pl. IBUDAP123): ").ToUpperInvariant();
                if (StationId.Length > 0) SetValue(path, "StationId", StationId);
            }
            if (ApiKey.Length == 0 || StationId.Length == 0)
                throw new FatalException("Hiányzik az ApiKey vagy a StationId. Töltsd ki az " + AppDirNameForMsg + "\\" + Path.GetFileName(path) + " fájlban (vagy indítsd el a Letöltőt dupla kattintással), majd indítsd újra a programot.", 1);
            foreach (char c in StationId)
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                    throw new FatalException("Érvénytelen állomásazonosító: " + StationId, 1);
            if (ApiKey.Length != 32)
                Log.Warn("Az API kulcs hossza szokatlan (" + ApiKey.Length + " karakter, általában 32). Ellenőrizd, ha hibát kapsz.");
        }

        static string Ask(string prompt)
        {
            Console.Write(prompt);
            string s = Console.ReadLine();
            return s == null ? "" : s.Trim();
        }

        static void SetValue(string path, string key, string value)
        {
            var lines = ReadLines(path).ToList();
            bool found = false;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = key + "=" + value;
                    found = true;
                    break;
                }
            }
            if (!found) lines.Add(key + "=" + value);
            Guard.WriteAllLines(path, lines, new UTF8Encoding(true));
            Log.Info(key + " elmentve a beállításfájlba.");
        }

        static IEnumerable<string> ReadLines(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, Encoding.UTF8, true))
            {
                var res = new List<string>();
                string l;
                while ((l = sr.ReadLine()) != null) res.Add(l);
                return res;
            }
        }

        static string Str(Dictionary<string, string> d, string k, string def)
        {
            string v;
            return d.TryGetValue(k, out v) && v != null ? v : def;
        }

        static int Int(Dictionary<string, string> d, string k, int def, int min, int max)
        {
            string v;
            if (!d.TryGetValue(k, out v) || v.Length == 0) return def;
            int r;
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) || r < min || r > max)
            {
                Log.Warn("Érvénytelen érték: " + k + "=" + v + " (megengedett: " + min + ".." + max + "), az alapértéket használom: " + def);
                return def;
            }
            return r;
        }

        static double Dbl(Dictionary<string, string> d, string k, double def)
        {
            string v; double r;
            if (d.TryGetValue(k, out v) && double.TryParse(v.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out r)) return r;
            return def;
        }

        static bool Bool(Dictionary<string, string> d, string k, bool def)
        {
            string v;
            if (!d.TryGetValue(k, out v) || v.Length == 0) return def;
            v = v.ToLowerInvariant();
            if (v == "true" || v == "1" || v == "igen" || v == "yes") return true;
            if (v == "false" || v == "0" || v == "nem" || v == "no") return false;
            Log.Warn("Érvénytelen érték: " + k + "=" + v + " (true/false), az alapértéket használom.");
            return def;
        }
    }

    // ------------------------------------------------------------------ CSV

    static class Csv
    {
        public static List<List<string>> Parse(string text, char d)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var f = new StringBuilder();
            bool q = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (q)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { f.Append('"'); i++; }
                        else q = false;
                    }
                    else f.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == d) { row.Add(f.ToString()); f.Clear(); }
                else if (c == '\r') { }
                else if (c == '\n') { row.Add(f.ToString()); f.Clear(); rows.Add(row); row = new List<string>(); }
                else f.Append(c);
            }
            if (f.Length > 0 || row.Count > 0) { row.Add(f.ToString()); rows.Add(row); }
            rows.RemoveAll(r => r.All(x => x.Trim().Length == 0));
            return rows;
        }

        public static string Esc(string s, char d)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(d) >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        public static char DetectDelimiter(string headerLine, char preferred)
        {
            char[] cands = { preferred, ';', ',', '\t' };
            char best = preferred;
            int bestCount = 0;
            foreach (char c in cands)
            {
                int n = headerLine.Count(x => x == c);
                if (n > bestCount) { best = c; bestCount = n; }
            }
            return best;
        }
    }

    // ------------------------------------------------------------------ CSV tábla (memóriában, kulcs szerint egyedi sorok)

    class Table
    {
        public readonly string FilePath;
        public readonly string Name;
        readonly Func<Dictionary<string, string>, string> keyOf;
        readonly Comparison<Dictionary<string, string>> cmp;
        readonly string[] required;
        readonly string[] preferred;

        public List<string> Cols = new List<string>();
        readonly HashSet<string> colSet = new HashSet<string>();
        public Dictionary<string, Dictionary<string, string>> Rows = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        public bool Existed;
        public bool Dirty;
        public int Added, Updated;

        public Table(string path, string name, string[] preferred, string[] required,
                     Func<Dictionary<string, string>, string> keyOf, Comparison<Dictionary<string, string>> cmp)
        {
            FilePath = path; Name = name; this.preferred = preferred; this.required = required; this.keyOf = keyOf; this.cmp = cmp;
        }

        void AddCol(string c)
        {
            if (colSet.Add(c)) Cols.Add(c);
        }

        public void Load()
        {
            string fn = Path.GetFileName(FilePath);
            if (!File.Exists(FilePath))
            {
                Existed = false;
                foreach (string c in preferred) AddCol(c);
                Log.Info(Name + ": " + fn + " még nem létezik, létrehozom.");
                return;
            }

            string text = null;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                        text = sr.ReadToEnd();
                    break;
                }
                catch (IOException ex)
                {
                    if (attempt >= 4)
                        throw new FatalException(Name + ": nem tudom beolvasni a(z) " + fn + " fájlt (" + ex.Message + "). Ha meg van nyitva más programban, zárd be.", 3);
                    Log.Warn(Name + ": " + fn + " nem olvasható (zárolva?), újrapróbálom...");
                    Thread.Sleep(3000);
                }
            }

            text = text.TrimStart('﻿');
            if (text.Trim().Length == 0)
            {
                Existed = false;
                foreach (string c in preferred) AddCol(c);
                Dirty = true;
                Log.Warn(Name + ": " + fn + " üres, új fájlként kezelem.");
                return;
            }

            int nl = text.IndexOf('\n');
            string headerLine = nl >= 0 ? text.Substring(0, nl) : text;
            char d = Csv.DetectDelimiter(headerLine, Cfg.Delim);
            if (d != Cfg.Delim)
            {
                Log.Warn(Name + ": a fájl '" + (d == '\t' ? "tab" : d.ToString()) + "' elválasztót használ, mentéskor a beállított '" + Cfg.Delim + "' lesz.");
                Dirty = true;
            }
            var rows = Csv.Parse(text, d);
            var header = rows[0].Select(h => h.Trim()).ToList();

            var missing = required.Where(r => !header.Contains(r)).ToList();
            if (missing.Count > 0)
            {
                BackupBad("hiányzó oszlop(ok): " + string.Join(", ", missing));
                return;
            }

            foreach (string h in header) if (h.Length > 0) AddCol(h);
            int bad = 0;
            for (int i = 1; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r.Count != header.Count) bad++;
                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int j = 0; j < header.Count && j < r.Count; j++)
                    if (header[j].Length > 0) row[header[j]] = r[j];
                string key = keyOf(row);
                if (key == null) { bad++; continue; }
                if (Rows.ContainsKey(key)) Dirty = true; // duplikátum: mentéskor kiszűrődik
                Rows[key] = row;
            }
            if (bad > 0)
            {
                Log.Warn(Name + ": " + bad + " sor hibás vagy hiányos volt a fájlban (javítva / kihagyva).");
                Dirty = true;
            }
            Existed = true;
            Log.Info(Name + ": " + fn + " beolvasva, " + Rows.Count + " sor.");
        }

        void BackupBad(string why)
        {
            string bak = Path.Combine(Path.GetDirectoryName(FilePath),
                Path.GetFileNameWithoutExtension(FilePath) + "_hibas_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + Path.GetExtension(FilePath));
            try { Guard.Move(FilePath, bak); }
            catch (Exception ex)
            {
                throw new FatalException(Name + ": a fájl nem felismerhető formátumú (" + why + "), és át sem tudom nevezni (" + ex.Message + "). Zárd be, ha nyitva van.", 3);
            }
            Log.Warn(Name + ": a fájl nem felismerhető formátumú (" + why + "). Félretettem ezen a néven: " + Path.GetFileName(bak) + " és újat kezdek.");
            Existed = false;
            Rows.Clear(); Cols.Clear(); colSet.Clear();
            foreach (string c in preferred) AddCol(c);
        }

        public void Merge(List<KeyValuePair<string, string>> rec)
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in rec)
            {
                row[kv.Key] = kv.Value;
                AddCol(kv.Key);
            }
            string key = keyOf(row);
            if (key == null) return;
            Dictionary<string, string> old;
            if (Rows.TryGetValue(key, out old))
            {
                bool same = row.Count == old.Count && row.All(kv => { string v; return old.TryGetValue(kv.Key, out v) && v == kv.Value; });
                if (same) return;
                // a régi sor olyan mezőit, amik az új válaszban nincsenek, megtartjuk
                foreach (var kv in old) if (!row.ContainsKey(kv.Key)) row[kv.Key] = kv.Value;
                Updated++;
            }
            else Added++;
            Rows[key] = row;
            Dirty = true;
        }

        // csak a legutóbbi max sor marad (a pillanatnyi állapotból a Megjelenítő csak a legfrissebbet használja)
        public void KeepLatest(int max)
        {
            if (Rows.Count <= max) return;
            var list = Rows.ToList();
            list.Sort((a, b) => cmp(a.Value, b.Value));
            foreach (var kv in list.Take(list.Count - max)) Rows.Remove(kv.Key);
            Dirty = true;
        }

        public HashSet<string> DaysPresent(string station)
        {
            var set = new HashSet<string>();
            foreach (var r in Rows.Values)
            {
                string s, t;
                if (!r.TryGetValue("stationID", out s) || !string.Equals(s, station, StringComparison.OrdinalIgnoreCase)) continue;
                if (r.TryGetValue("obsTimeLocal", out t) && t.Length >= 10) set.Add(t.Substring(0, 10).Replace("-", ""));
            }
            return set;
        }

        public void Save()
        {
            if (!Dirty) return;
            var list = Rows.Values.ToList();
            list.Sort(cmp);
            var sb = new StringBuilder();
            sb.Append(string.Join(Cfg.Delim.ToString(), Cols.Select(c => Csv.Esc(c, Cfg.Delim)))).Append("\r\n");
            foreach (var r in list)
            {
                for (int i = 0; i < Cols.Count; i++)
                {
                    if (i > 0) sb.Append(Cfg.Delim);
                    string v;
                    if (r.TryGetValue(Cols[i], out v)) sb.Append(Csv.Esc(v, Cfg.Delim));
                }
                sb.Append("\r\n");
            }

            // Előbb ideiglenes fájlba írunk, majd egy lépésben cseréljük: így áramszünet
            // vagy hiba esetén sem sérül a meglévő adatbázis.
            string tmp = FilePath + ".tmp";
            Guard.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(true));
            Exception last = null;
            for (int attempt = 1; attempt <= 6; attempt++)
            {
                try
                {
                    if (File.Exists(FilePath)) Guard.Replace(tmp, FilePath, null);
                    else Guard.Move(tmp, FilePath);
                    Dirty = false;
                    Existed = true;
                    return;
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                    last = ex;
                    if (attempt == 1)
                        Log.Warn(Name + ": " + Path.GetFileName(FilePath) + " nem írható (meg van nyitva Excelben?). Zárd be, 30 másodpercig újrapróbálom...");
                    Thread.Sleep(5000);
                }
            }
            try { Guard.Delete(tmp); } catch { }
            throw new SaveException(Name + ": nem sikerült menteni: " + Path.GetFileName(FilePath) + " (" + last.Message + ")", last);
        }
    }

    // ------------------------------------------------------------------ Állapot / lekérdezési napló (JSON)

    class StationState
    {
        public SortedDictionary<string, string> Days = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public DateTime? LastRecentUtc, LastDailyUtc, LastSuccessUtc;
        public int? TzOffsetMin;
        public string LastObsLocal;
    }

    class State
    {
        public string FilePath;
        public Dictionary<string, StationState> Stations = new Dictionary<string, StationState>(StringComparer.OrdinalIgnoreCase);
        public string CallDay = "";
        public int CallCount;
        public List<object> Runs = new List<object>();
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static State Load(string path)
        {
            var st = new State { FilePath = path };
            if (!File.Exists(path))
            {
                Log.Info("Nincs korábbi lekérdezési állapotfájl (" + Path.GetFileName(path) + ") - első futás, vagy törölték.");
                return st;
            }
            try
            {
                var root = (Dictionary<string, object>)Js.DeserializeObject(File.ReadAllText(path, Encoding.UTF8));
                st.CallDay = Convert.ToString(Get(root, "apiCallDay")) ?? "";
                st.CallCount = Convert.ToInt32(Get(root, "apiCallCount") ?? 0);
                var runs = Get(root, "runs") as object[];
                if (runs != null) st.Runs = runs.ToList();
                var stations = Get(root, "stations") as Dictionary<string, object>;
                if (stations != null)
                {
                    foreach (var kv in stations)
                    {
                        var s = (Dictionary<string, object>)kv.Value;
                        var ss = new StationState();
                        var days = Get(s, "days") as Dictionary<string, object>;
                        if (days != null) foreach (var dk in days) ss.Days[dk.Key] = Convert.ToString(dk.Value);
                        ss.LastRecentUtc = Dt(Get(s, "lastRecentUtc"));
                        ss.LastDailyUtc = Dt(Get(s, "lastDailyUtc"));
                        ss.LastSuccessUtc = Dt(Get(s, "lastSuccessUtc"));
                        ss.LastObsLocal = Get(s, "lastObsLocal") as string;
                        object tz = Get(s, "tzOffsetMin");
                        if (tz != null) ss.TzOffsetMin = Convert.ToInt32(tz);
                        st.Stations[kv.Key] = ss;
                    }
                }
                Log.Info("Lekérdezési állapot beolvasva (" + st.Runs.Count + " korábbi futás nyilvántartva).");
            }
            catch (Exception ex)
            {
                string bak = path + ".hibas_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                try { Guard.Move(path, bak); } catch { }
                Log.Warn("Az állapotfájl sérült (" + ex.Message + "), félretettem: " + Path.GetFileName(bak) + ". A CSV alapján újraépítem.");
                st = new State { FilePath = path };
            }
            return st;
        }

        static object Get(Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) ? v : null;
        }

        static DateTime? Dt(object o)
        {
            DateTime t;
            var s = o as string;
            if (s != null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t)) return t.ToUniversalTime();
            return null;
        }

        static string Iso(DateTime? t)
        {
            return t.HasValue ? t.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : null;
        }

        public StationState Station(string id, out bool isNew)
        {
            StationState s;
            isNew = !Stations.TryGetValue(id, out s);
            if (isNew) { s = new StationState(); Stations[id] = s; }
            return s;
        }

        public void CountCall()
        {
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (CallDay != today) { CallDay = today; CallCount = 0; }
            CallCount++;
        }

        public int CallsToday()
        {
            return CallDay == DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ? CallCount : 0;
        }

        public void Save()
        {
            var stations = new Dictionary<string, object>();
            foreach (var kv in Stations)
            {
                var s = kv.Value;
                stations[kv.Key] = new Dictionary<string, object>
                {
                    { "lastSuccessUtc", Iso(s.LastSuccessUtc) },
                    { "lastRecentUtc", Iso(s.LastRecentUtc) },
                    { "lastDailyUtc", Iso(s.LastDailyUtc) },
                    { "lastObsLocal", s.LastObsLocal },
                    { "tzOffsetMin", s.TzOffsetMin },
                    { "days", s.Days.ToDictionary(x => x.Key, x => (object)x.Value) }
                };
            }
            while (Runs.Count > 200) Runs.RemoveAt(0);
            var root = new Dictionary<string, object>
            {
                { "info", "A Letolto program allapotfajlja. Ha torlod, a CSV alapjan ujraepul." },
                { "apiCallDay", CallDay },
                { "apiCallCount", CallCount },
                { "stations", stations },
                { "runs", Runs }
            };
            string tmp = FilePath + ".tmp";
            Guard.WriteAllText(tmp, PrettyJson(Js.Serialize(root)), new UTF8Encoding(false));
            if (File.Exists(FilePath)) Guard.Replace(tmp, FilePath, null);
            else Guard.Move(tmp, FilePath);
        }

        static string PrettyJson(string json)
        {
            var sb = new StringBuilder();
            int ind = 0;
            bool q = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (q)
                {
                    sb.Append(c);
                    if (c == '\\') { sb.Append(json[++i]); }
                    else if (c == '"') q = false;
                    continue;
                }
                switch (c)
                {
                    case '"': q = true; sb.Append(c); break;
                    case '{':
                    case '[': sb.Append(c).Append('\n').Append(' ', ++ind * 2); break;
                    case '}':
                    case ']': sb.Append('\n').Append(' ', --ind * 2).Append(c); break;
                    case ',': sb.Append(c).Append('\n').Append(' ', ind * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------ API kliens

    class Api
    {
        readonly State st;
        public int Calls;
        public static int NetErrors;   // végleges hálózati hibák száma ebben a futásban (a kilépési kódhoz)
        DateTime lastCall = DateTime.MinValue;

        public Api(State st) { this.st = st; }

        // Visszaadja a HTTP státuszt, -1 = hálózati hiba
        public int Get(string path, string query, string what, out string body)
        {
            if (Calls >= Cfg.MaxCallsPerRun)
                throw new BudgetException("Elértem a futásonkénti hívási limitet (" + Cfg.MaxCallsPerRun + ").");
            if (st.CallsToday() >= Cfg.DailyCallLimit)
                throw new BudgetException("Elértem a napi hívási limitet (" + Cfg.DailyCallLimit + " / nap, UTC szerint).");

            string url = Cfg.ApiBaseUrl + path + "?" + query + "&format=json&units=" + Cfg.Units
                         + "&numericPrecision=decimal&apiKey=" + Uri.EscapeDataString(Cfg.ApiKey);
            body = null;
            int status = -1;
            int rateLimitRetries = 0;

            for (int attempt = 1; attempt <= 4; attempt++)
            {
                if (lastCall != DateTime.MinValue)
                {
                    int wait = Cfg.CallDelayMs - (int)(DateTime.UtcNow - lastCall).TotalMilliseconds;
                    if (wait > 0) Thread.Sleep(wait);
                }
                lastCall = DateTime.UtcNow;

                bool reached = false;
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    req.Timeout = 30000;
                    req.ReadWriteTimeout = 60000;
                    req.UserAgent = "Letolto/1.1";
                    req.Accept = "application/json";
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        reached = true;
                        status = (int)resp.StatusCode;
                        body = ReadBody(resp);
                    }
                }
                catch (WebException ex)
                {
                    var resp = ex.Response as HttpWebResponse;
                    if (resp != null)
                    {
                        reached = true;
                        status = (int)resp.StatusCode;
                        try { body = ReadBody(resp); } catch { body = null; }
                        resp.Close();
                    }
                    else
                    {
                        status = -1;
                        Log.Warn(what + ": hálózati hiba (" + ex.Status + ": " + ex.Message + ")" + (attempt < 4 ? ", újrapróbálom..." : ""));
                        if (attempt < 4) { Thread.Sleep(5000 * attempt); continue; }
                        NetErrors++;
                        return -1;
                    }
                }

                if (reached) { Calls++; st.CountCall(); }

                if (status == 429)
                {
                    if (++rateLimitRetries > 2)
                        throw new BudgetException("A szerver többször is túl sok kérést jelzett (429).");
                    Log.Warn(what + ": túl sok kérés (429), várok 65 másodpercet...");
                    Thread.Sleep(65000);
                    attempt--;
                    continue;
                }
                if (status >= 500 && attempt < 4)
                {
                    Log.Warn(what + ": szerverhiba (" + status + "), újrapróbálom...");
                    Thread.Sleep(5000 * attempt);
                    continue;
                }
                break;
            }

            if (status == 401)
                throw new FatalException("Az API kulcs érvénytelen vagy lejárt (401). Ellenőrizd az ApiKey sort a beallitasok.ini fájlban. Kulcs: https://www.wunderground.com/member/api-keys", 2);
            if (status == 403)
                throw new FatalException("Hozzáférés megtagadva (403). Az API kulcs nem jogosult erre a lekérdezésre (a PWS API kulcs csak saját, aktívan adatot küldő állomással működik).", 2);
            return status;
        }

        static string ReadBody(HttpWebResponse resp)
        {
            using (var s = resp.GetResponseStream())
            {
                if (s == null) return "";
                using (var sr = new StreamReader(s, Encoding.UTF8)) return sr.ReadToEnd();
            }
        }
    }

    // ------------------------------------------------------------------ Főprogram

    static class Program
    {
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 200 };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AllocConsole();
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int n);

        static int Main(string[] args)
        {
            bool noWait = args.Any(a => a.Equals("--nowait", StringComparison.OrdinalIgnoreCase)
                                     || a.Equals("/nowait", StringComparison.OrdinalIgnoreCase)
                                     || a.Equals("--csendes", StringComparison.OrdinalIgnoreCase));
            // A program ablak nélküli (winexe), így ütemezve nem villan fel konzolablak.
            // Dupla kattintásra (kérdezhet, kiírja a haladást) saját konzolablakot nyit;
            // parancssorból indítva a meglévő konzolra ír; a Megjelenítő átirányított kimenetét megtartja.
            if (GetStdHandle(-11) == IntPtr.Zero)
            {
                if (!AttachConsole(-1) && !noWait) AllocConsole();
            }
            // Átirányított kimenetnél (a Megjelenítő Frissítés gombja) a Console.OutputEncoding beállítása kivételt dobhat,
            // és a kimenet a Windows ANSI kódlapján menne ki (ő, ű → �). Ilyenkor saját UTF-8 író kell (BOM nélkül).
            if (Console.IsOutputRedirected)
                try { Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true }); } catch { }
            else
                try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            if (Console.IsErrorRedirected)
                try { Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true }); } catch { }
            if (args.Any(a => a.Equals("--teszt-ertesites", StringComparison.OrdinalIgnoreCase)))
            {
                Alerts.Notify("WStation – próba", "Így fognak kinézni a riasztások (fagy, nyomásesés, szél, felhőszakadás, hőség).");
                return 0;
            }
            try { Console.Title = "Weather Underground adatletöltő"; } catch { }

            // Mappaszerkezet: <programmappa>\data\ (CSV adatok), <programmappa>\appdata_letöltő\ (beállítások, napló, állapot)
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i].Equals("--mappa", StringComparison.OrdinalIgnoreCase)) dir = Path.GetFullPath(args[i + 1]);
            string appDir = Path.Combine(dir, AppDirName), dataDir = Path.Combine(dir, "data");
            Guard.SetRoot(dir);   // innentől a program csak a saját mappájába írhat
            int code;
            Mutex mutex = null;
            bool owned = false;
            try
            {
                Guard.CreateDirectory(appDir);
                Guard.CreateDirectory(dataDir);
                Log.Init(Path.Combine(appDir, "lekerdezes_naplo.log"));
                MigrateOld(dir, appDir, dataDir);
                // csak biztonsági mentés (a Megjelenítő indítja: induláskor, ha esedékes, ill. „Mentés most”)
                if (args.Any(a => a.Equals("--mentes", StringComparison.OrdinalIgnoreCase)))
                {
                    var ini = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string iniPath = Path.Combine(appDir, "beallitasok.ini");
                    if (File.Exists(iniPath))
                        foreach (var raw in File.ReadAllLines(iniPath, Encoding.UTF8))
                        {
                            string l = raw.Trim(); int eq = l.IndexOf('=');
                            if (l.Length > 0 && l[0] != '#' && eq > 0) ini[l.Substring(0, eq).Trim()] = l.Substring(eq + 1).Trim();
                        }
                    string v; int n;
                    if (ini.TryGetValue("MentesNaponta", out v) && int.TryParse(v, out n)) Cfg.BackupDays = Math.Max(0, n);
                    if (ini.TryGetValue("MentesekSzama", out v) && int.TryParse(v, out n)) Cfg.BackupKeep = Math.Max(1, n);
                    bool force = args.Any(a => a.Equals("--most", StringComparison.OrdinalIgnoreCase));
                    Backup.Weekly(appDir, dataDir, force);
                    return 0;
                }
                mutex = new Mutex(false, "WuPwsLetolto_" + StableHash(dir.ToLowerInvariant()));
                try
                {
                    owned = mutex.WaitOne(0);
                    if (!owned)
                    {
                        // pl. a Frissítés gomb épp akkor indít, amikor az ütemezett letöltés fut: megvárjuk
                        Log.Info("Épp fut egy másik letöltés (pl. ütemezett feladat), megvárom (legfeljebb 15 percig)...");
                        owned = mutex.WaitOne(TimeSpan.FromMinutes(15));
                    }
                }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned)
                {
                    Log.Warn("A másik letöltés 15 perc alatt sem fejeződött be. Ez a példány kilép.");
                    code = 4;
                }
                else if (args.Any(a => a.Equals("--metar", StringComparison.OrdinalIgnoreCase)))
                    code = MetarFetch.Run(appDir, dataDir, true) ? 0 : 6;   // csak a METAR (kézi próba)
                else
                {
                    code = Run(appDir, dataDir, !noWait);
                    // a felhőzet-ellenőrzés soha nem befolyásolja a kilépési kódot és a fő letöltést
                    try { MetarFetch.Run(appDir, dataDir, false); } catch (Exception ex) { Log.Warn("METAR: " + ex.Message); }
                }
            }
            catch (FatalException fe) { Log.Error(fe.Message); code = fe.Code; }
            catch (SaveException se) { Log.Error(se.Message); code = 3; }
            catch (UnauthorizedAccessException ue)
            {
                Log.Error("Nincs írási jogom ebben a mappában: " + dir + " (" + ue.Message + "). Tedd a programot egy saját mappába (pl. Dokumentumok\\Időjárás).");
                code = 5;
            }
            catch (Exception ex) { Log.Error("Váratlan hiba: " + ex); code = 9; }
            finally
            {
                if (owned) try { mutex.ReleaseMutex(); } catch { }
                if (mutex != null) mutex.Dispose();
            }

            if (!noWait && Cfg.WaitAtEnd && !Console.IsInputRedirected)
            {
                Console.WriteLine();
                Console.WriteLine("Nyomj Entert a kilépéshez...");
                try { Console.ReadLine(); } catch { }
            }
            return code;
        }

        static string StableHash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h.ToString("x8");
        }

        // A WU szerint a nap "állomás helyi ideje" szerinti, ezért a mai napot az állomás időzónájában számoljuk
        static DateTime StationToday(StationState stn)
        {
            if (stn.TzOffsetMin.HasValue) return DateTime.UtcNow.AddMinutes(stn.TzOffsetMin.Value).Date;
            return DateTime.Now.Date;
        }

        public const string AppDirName = "appdata_letöltő";

        // Régi (egy mappás) elrendezés: a programmappa gyökerében lévő fájlokat a helyükre teszi
        static void MigrateOld(string dir, string appDir, string dataDir)
        {
            var moved = new List<string>();
            foreach (var n in new[] { "idojaras_adatok.csv", "napi_osszesito.csv", "pillanatnyi_allapot.csv" })
                MoveIfAbsent(Path.Combine(dir, n), Path.Combine(dataDir, n), moved);
            foreach (var n in new[] { "beallitasok.ini", "lekerdezes_allapot.json", "lekerdezes_naplo.log", "lekerdezes_naplo.log.regi" })
                MoveIfAbsent(Path.Combine(dir, n), Path.Combine(appDir, n), moved);
            if (moved.Count > 0) Log.Info("Régi elrendezés átalakítva, áthelyezett fájlok: " + string.Join(", ", moved));
        }

        static void MoveIfAbsent(string from, string to, List<string> moved)
        {
            if (!File.Exists(from) || File.Exists(to)) return;
            try { Guard.Move(from, to); moved.Add(Path.GetFileName(from)); }
            catch (Exception ex) { Log.Warn("Nem tudtam áthelyezni: " + Path.GetFileName(from) + " (" + ex.Message + ")"); }
        }

        static int Run(string appDir, string dir, bool interactive)
        {
            Log.Info("===== Indítás (Letöltő " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + ") =====");
            Cfg.Load(Path.Combine(appDir, "beallitasok.ini"), interactive && !Console.IsInputRedirected);
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; }
            catch { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }

            string sid = Cfg.StationId;
            Log.Info("Állomás: " + sid + ", mértékegység: " + Cfg.Units);
            DateTime startedUtc = DateTime.UtcNow;

            var state = State.Load(Path.Combine(appDir, "lekerdezes_allapot.json"));
            bool stationIsNew;
            var stn = state.Station(sid, out stationIsNew);

            var obs = new Table(Path.Combine(dir, "idojaras_adatok.csv"), "Részletes mérések",
                new[] { "stationID", "obsTimeLocal", "obsTimeUtc", "epoch", "tz" },
                new[] { "stationID", "obsTimeLocal", "epoch" },
                r => Key(r, "stationID", "epoch"), CompareByEpoch);
            obs.Load();

            Table cur = null, daily = null;
            if (Cfg.SaveCurrent)
            {
                cur = new Table(Path.Combine(dir, "pillanatnyi_allapot.csv"), "Pillanatnyi állapot",
                    new[] { "letoltve", "stationID", "obsTimeLocal", "obsTimeUtc", "epoch" },
                    new[] { "stationID", "obsTimeLocal", "epoch" },
                    r => Key(r, "stationID", "epoch"), CompareByEpoch);
                cur.Load();
            }
            if (Cfg.SaveDailySummary)
            {
                daily = new Table(Path.Combine(dir, "napi_osszesito.csv"), "Napi összesítő",
                    new[] { "datum", "stationID", "obsTimeLocal", "obsTimeUtc", "epoch", "tz" },
                    new[] { "datum", "stationID" },
                    r => Key(r, "stationID", "datum"),
                    (a, b) =>
                    {
                        int c = string.CompareOrdinal(V(a, "stationID"), V(b, "stationID"));
                        return c != 0 ? c : string.CompareOrdinal(V(a, "datum"), V(b, "datum"));
                    });
                daily.Load();
            }

            var api = new Api(state);
            var pending = new Dictionary<string, string>(); // a CSV sikeres mentése után kerül az állapotba
            int historyDone = 0, historyTodo = 0;
            string result = "ok";
            Exception error = null;

            Action saveAll = () =>
            {
                bool obsOk = true;
                foreach (var t in new[] { obs, cur, daily })
                {
                    if (t == null) continue;
                    try { t.Save(); }
                    catch (SaveException se)
                    {
                        Log.Error(se.Message);
                        if (t == obs) obsOk = false;
                        result = "mentesi_hiba";
                    }
                }
                if (obsOk)
                {
                    foreach (var kv in pending) stn.Days[kv.Key] = kv.Value;
                    pending.Clear();
                }
                state.Save();
            };

            try
            {
                DateTime now = DateTime.UtcNow;
                // ha valamelyik CSV új / törölték, a friss adatokat azonnal újra kell kérni
                bool recentDue = !stn.LastRecentUtc.HasValue || !obs.Existed || (cur != null && !cur.Existed)
                                 || (now - stn.LastRecentUtc.Value).TotalMinutes >= Cfg.MinMinutesBetweenRecent;

                // 1) Pillanatnyi állapot - ebből tudjuk meg az állomás helyi idejét is
                if (recentDue)
                {
                    string body;
                    int s = api.Get("/v2/pws/observations/current", "stationId=" + sid, "Pillanatnyi állapot", out body);
                    var list = s == 200 ? JsonList(body, "observations") : new List<Dictionary<string, object>>();
                    if (list.Count > 0)
                    {
                        var o = list[0];
                        UpdateTz(stn, o);
                        stn.LastObsLocal = Convert.ToString(GetVal(o, "obsTimeLocal"));
                        if (cur != null)
                        {
                            var rec = Flatten(o);
                            rec.Insert(0, new KeyValuePair<string, string>("letoltve", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv)));
                            rec.Add(new KeyValuePair<string, string>("units", Cfg.Units));
                            cur.Merge(rec);
                            cur.KeepLatest(500);
                        }
                        Log.Ok("Pillanatnyi állapot: utolsó mérés " + stn.LastObsLocal + " (helyi idő).");
                    }
                    else LogNoData("Pillanatnyi állapot", s, body, "az állomás jelenleg nem küld adatot (offline?), vagy hibás az állomásazonosító");
                }
                else
                {
                    Log.Info("Döntés: az utolsó friss lekérdezés " + (int)(now - stn.LastRecentUtc.Value).TotalMinutes
                             + " perce volt (< " + Cfg.MinMinutesBetweenRecent + " perc), a pillanatnyi és 24 órás lekérdezést kihagyom - még nincs új adat.");
                }

                DateTime today = StationToday(stn);
                Reconcile(stn, stationIsNew, obs, today, sid);

                // 2) Utolsó 24 óra, kb. 5 perces felbontás (egy hívás)
                if (recentDue)
                {
                    string body;
                    int s = api.Get("/v2/pws/observations/all/1day", "stationId=" + sid, "Utolsó 24 óra", out body);
                    var list = s == 200 ? JsonList(body, "observations") : new List<Dictionary<string, object>>();
                    int a0 = obs.Added;
                    foreach (var o in list) obs.Merge(ObsRecord(o));
                    if (list.Count > 0)
                    {
                        UpdateTz(stn, list[list.Count - 1]);
                        Log.Ok("Utolsó 24 óra: " + list.Count + " mérés, ebből új: " + (obs.Added - a0) + ".");
                    }
                    else LogNoData("Utolsó 24 óra", s, body, "nincs mérés az utolsó 24 órából");
                    stn.LastRecentUtc = now;
                    today = StationToday(stn);
                }

                // 3) Napi összesítők (utolsó 7 nap) - óránként egyszer elég
                if (daily != null)
                {
                    if (!stn.LastDailyUtc.HasValue || !daily.Existed || (now - stn.LastDailyUtc.Value).TotalMinutes >= 60)
                    {
                        string body;
                        int s = api.Get("/v2/pws/dailysummary/7day", "stationId=" + sid, "Napi összesítő (7 nap)", out body);
                        var list = s == 200 ? JsonList(body, "summaries") : new List<Dictionary<string, object>>();
                        int a0 = daily.Added, u0 = daily.Updated;
                        foreach (var o in list)
                        {
                            var rec = Flatten(o);
                            string local = Convert.ToString(GetVal(o, "obsTimeLocal")) ?? "";
                            if (local.Length < 10) continue;
                            rec.Insert(0, new KeyValuePair<string, string>("datum", local.Substring(0, 10)));
                            rec.Add(new KeyValuePair<string, string>("units", Cfg.Units));
                            daily.Merge(rec);
                        }
                        if (list.Count > 0) Log.Ok("Napi összesítő: " + list.Count + " nap, új: " + (daily.Added - a0) + ", frissített: " + (daily.Updated - u0) + ".");
                        else LogNoData("Napi összesítő", s, body, "nincs napi összesítő");
                        stn.LastDailyUtc = now;
                    }
                    else Log.Info("Döntés: a napi összesítőt egy órán belül már lekértem, most kihagyom.");
                }

                saveAll();

                // 4) Hiányzó napok pótlása a legrészletesebb (history/all) lekérdezéssel
                var todo = PlanHistory(stn, today);
                historyTodo = todo.Count;
                if (todo.Count == 0)
                    Log.Info("Döntés: nincs hiányzó nap a beállított időszakban, előzmény-lekérdezés nem kell.");
                else
                {
                    Log.Info("Döntés: " + todo.Count + " napot kell letölteni részletes előzményként (" +
                             todo[todo.Count - 1].ToString("yyyy-MM-dd", Inv) + " ... " + todo[0].ToString("yyyy-MM-dd", Inv) + "), a legfrissebbel kezdem.");
                    if (todo.Count > Cfg.MaxCallsPerRun - api.Calls)
                        Log.Info("Ez több, mint amennyi hívás ebbe a futásba belefér - a maradékot a következő futások töltik le.");
                }

                int sinceSave = 0;
                foreach (var day in todo)
                {
                    string dk = day.ToString("yyyyMMdd", Inv);
                    string label = "Előzmény " + day.ToString("yyyy-MM-dd", Inv);
                    string body;
                    int s = api.Get("/v2/pws/history/all", "stationId=" + sid + "&date=" + dk, label, out body);
                    bool final = day <= today.AddDays(-2);

                    if (s == 200 || s == 204)
                    {
                        var list = s == 200 ? JsonList(body, "observations") : new List<Dictionary<string, object>>();
                        int a0 = obs.Added;
                        foreach (var o in list) obs.Merge(ObsRecord(o));
                        if (list.Count > 0)
                            Log.Ok(label + ": " + list.Count + " mérés, új: " + (obs.Added - a0) + ".");
                        else
                            Log.Info(label + ": nincs adat ezen a napon (az állomás nem küldött).");
                        if (!final) pending[dk] = "reszleges:" + today.ToString("yyyyMMdd", Inv);
                        else if (list.Count == 0) pending[dk] = "nincs_adat";
                        else
                        {
                            // töredékes nap (pl. az állomás vagy a WU kiesése): legfeljebb kétszer, néhány nap múlva újra lekérjük,
                            // hátha a WU utólag pótolta; utána végleges
                            string prevState; stn.Days.TryGetValue(dk, out prevState);
                            int tries = HianyosProba(prevState);
                            pending[dk] = list.Count < 0.8 * TypicalCount(stn) && tries < 2
                                ? "hianyos:" + (tries + 1) + ":" + today.ToString("yyyyMMdd", Inv) + ":" + list.Count
                                : "ok:" + list.Count;
                            if (pending[dk].StartsWith("hianyos")) Log.Info(label + ": töredékes nap (" + list.Count + " mérés), legalább 2 nap múlva újra megpróbálom (" + (tries + 1) + "/2).");
                        }
                        historyDone++;
                    }
                    else if (s == 400)
                    {
                        Log.Warn(label + ": a szerver elutasította a kérést (400) - " + Short(body) + ". Ezt a napot többet nem kérdezem le.");
                        pending[dk] = "hiba400";
                        historyDone++;
                    }
                    else if (s == 404)
                    {
                        Log.Warn(label + ": nem található (404) - " + Short(body) + ". Következő futáskor újra próbálom.");
                    }
                    else
                    {
                        Log.Warn(label + ": sikertelen (" + (s < 0 ? "hálózati hiba" : "HTTP " + s) + "). Következő futáskor újra próbálom.");
                    }

                    if (++sinceSave >= 10) { saveAll(); sinceSave = 0; }
                }
            }
            catch (BudgetException be)
            {
                Log.Warn(be.Message + " A hiányzó adatokat a következő futás pótolja.");
                result = "limit";
            }
            catch (Exception ex)
            {
                error = ex;
                result = "hiba: " + ex.Message;
            }

            if (obs.Added > 0 || obs.Updated > 0) stn.LastSuccessUtc = DateTime.UtcNow;
            try { Alerts.Check(obs, sid, appDir); }
            catch (Exception ex) { Log.Warn("A riasztások ellenőrzése nem sikerült: " + ex.Message); }
            try { Backup.Weekly(appDir, dir); }
            catch (Exception ex) { Log.Warn("A biztonsági mentés nem sikerült: " + ex.Message); }
            state.Runs.Add(new Dictionary<string, object>
            {
                { "start", startedUtc.ToString("o", Inv) },
                { "end", DateTime.UtcNow.ToString("o", Inv) },
                { "station", sid },
                { "apiCalls", api.Calls },
                { "newRows", obs.Added },
                { "updatedRows", obs.Updated },
                { "historyDaysFetched", historyDone },
                { "historyDaysRemaining", Math.Max(0, historyTodo - historyDone) },
                { "result", result }
            });
            saveAll();

            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();

            Log.Info("----- Összegzés -----");
            Log.Info("API hívások most: " + api.Calls + " (ma összesen: " + state.CallsToday() + " / " + Cfg.DailyCallLimit + ")");
            Log.Info("Részletes mérések (idojaras_adatok.csv): +" + obs.Added + " új, " + obs.Updated + " frissített, összesen " + obs.Rows.Count + " sor");
            if (cur != null) Log.Info("Pillanatnyi állapot (pillanatnyi_allapot.csv): összesen " + cur.Rows.Count + " sor");
            if (daily != null) Log.Info("Napi összesítő (napi_osszesito.csv): összesen " + daily.Rows.Count + " nap");
            int remaining = Math.Max(0, historyTodo - historyDone);
            if (remaining > 0) Log.Warn("Még " + remaining + " nap letöltése van hátra - futtasd újra a programot később.");
            if (result == "mentesi_hiba")
            {
                Log.Error("Nem minden adatot sikerült menteni. A következő futás újra letölti a hiányzó részt.");
                return 3;
            }
            if (Api.NetErrors > 0)
            {
                // a Megjelenítő a kilépési kódból ismeri fel (nem a naplószövegből, ami nyelvtől és kódlaptól függ)
                Log.Warn("Kész, de " + Api.NetErrors + " lekérdezés hálózati hiba miatt elmaradt (van internet?).");
                return 6;
            }
            Log.Ok("Kész.");
            return 0;
        }

        // A korábbi lekérdezések állapotát összeveti a CSV tényleges tartalmával
        static void Reconcile(StationState stn, bool stationIsNew, Table obs, DateTime today, string sid)
        {
            var present = obs.DaysPresent(sid);
            if (!obs.Existed)
            {
                if (stn.Days.Count > 0)
                {
                    Log.Warn("A CSV adatbázis nem létezik / új, de a napló szerint már töltöttem le adatot - a korábbi állapotot törlöm, mindent újra letöltök.");
                    stn.Days.Clear();
                }
                return;
            }

            var lost = stn.Days.Where(kv => kv.Value.StartsWith("ok") && !present.Contains(kv.Key)).Select(kv => kv.Key).ToList();
            foreach (var k in lost) stn.Days.Remove(k);
            if (lost.Count > 0)
                Log.Warn(lost.Count + " korábban letöltött nap hiányzik a CSV-ből (törölték?), ezeket újra letöltöm.");

            if (stationIsNew && present.Count > 0)
            {
                string limit = today.AddDays(-2).ToString("yyyyMMdd", Inv);
                int n = 0;
                foreach (var dk in present)
                    if (string.CompareOrdinal(dk, limit) <= 0 && !stn.Days.ContainsKey(dk)) { stn.Days[dk] = "ok:csv"; n++; }
                Log.Info("A lekérdezési állapot újraépítve a meglévő CSV alapján: " + n + " nap már megvan.");
            }
        }

        // Mely napokat kell (újra) letölteni, a legfrissebbtől visszafelé
        static List<DateTime> PlanHistory(StationState stn, DateTime today)
        {
            var res = new List<DateTime>();
            DateTime start = Cfg.StartDate.HasValue ? Cfg.StartDate.Value : today.AddDays(-Cfg.BackfillDays);
            string todayKey = today.ToString("yyyyMMdd", Inv);
            for (DateTime d = today.AddDays(-1); d >= start; d = d.AddDays(-1))
            {
                string s;
                stn.Days.TryGetValue(d.ToString("yyyyMMdd", Inv), out s);
                bool isFinal = s != null && (s.StartsWith("ok") || s == "nincs_adat" || s == "hiba400");
                if (isFinal) continue;
                // töredékes nap: az utolsó próba után legalább 2 nappal kérjük újra
                if (s != null && s.StartsWith("hianyos:"))
                {
                    var p = s.Split(':'); DateTime last;
                    if (p.Length >= 3 && DateTime.TryParseExact(p[2], "yyyyMMdd", Inv, DateTimeStyles.None, out last) && (today - last).TotalDays < 2) continue;
                }
                // a tegnapi napot naponta egyszer töltjük le (éjfél után), másnap még egyszer véglegesen
                if (d == today.AddDays(-1) && s == "reszleges:" + todayKey) continue;
                res.Add(d);
            }
            return res;
        }

        // a "hianyos:<próba>:<nap>:<db>" állapot próbaszáma
        static int HianyosProba(string s)
        {
            int n;
            if (s == null || !s.StartsWith("hianyos:")) return 0;
            var p = s.Split(':');
            return p.Length > 1 && int.TryParse(p[1], out n) ? n : 0;
        }

        // egy teljes nap szokásos mérésszáma ennél az állomásnál (az eddigi "ok:N" napok mediánja; alapból 288 = 5 percenként)
        static int TypicalCount(StationState stn)
        {
            var ns = new List<int>();
            foreach (var v in stn.Days.Values) { int n; if (v.StartsWith("ok:") && int.TryParse(v.Substring(3), out n) && n > 0) ns.Add(n); }
            if (ns.Count < 5) return 288;
            ns.Sort();
            return ns[ns.Count / 2];
        }

        static void UpdateTz(StationState stn, Dictionary<string, object> o)
        {
            DateTime loc, utc;
            string l = Convert.ToString(GetVal(o, "obsTimeLocal"));
            string u = Convert.ToString(GetVal(o, "obsTimeUtc"));
            if (l == null || u == null) return;
            if (DateTime.TryParseExact(l, "yyyy-MM-dd HH:mm:ss", Inv, DateTimeStyles.None, out loc) &&
                DateTime.TryParse(u, Inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc))
            {
                stn.TzOffsetMin = (int)(Math.Round((loc - utc).TotalMinutes / 15.0) * 15);
            }
        }

        static void LogNoData(string what, int status, string body, string meaning)
        {
            if (status == 200 || status == 204) Log.Warn(what + ": " + meaning + ".");
            else if (status < 0) Log.Warn(what + ": hálózati hiba, most kihagyom.");
            else Log.Warn(what + ": HTTP " + status + " - " + Short(body));
        }

        static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(üres válasz)";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 200 ? s.Substring(0, 200) + "..." : s;
        }

        // ---------------- JSON segédek

        static List<Dictionary<string, object>> JsonList(string body, string prop)
        {
            var res = new List<Dictionary<string, object>>();
            if (string.IsNullOrWhiteSpace(body)) return res;
            object root;
            try { root = Js.DeserializeObject(body); }
            catch (Exception ex) { Log.Warn("Értelmezhetetlen JSON válasz: " + ex.Message); return res; }
            var d = root as Dictionary<string, object>;
            object arr;
            if (d == null || !d.TryGetValue(prop, out arr) || arr == null) return res;
            var en = arr as IEnumerable;
            if (en == null || arr is string) return res;
            foreach (var item in en)
            {
                var di = item as Dictionary<string, object>;
                if (di != null) res.Add(di);
            }
            return res;
        }

        static object GetVal(Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) ? v : null;
        }

        static List<KeyValuePair<string, string>> ObsRecord(Dictionary<string, object> o)
        {
            var rec = Flatten(o);
            rec.Add(new KeyValuePair<string, string>("units", Cfg.Units));
            return rec;
        }

        // A beágyazott objektumokat (pl. "metric") lapítja: a mezőnevek prefix nélkül kerülnek oszlopba,
        // ütközés esetén "szülő_mező" néven.
        static List<KeyValuePair<string, string>> Flatten(Dictionary<string, object> o)
        {
            var res = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>();
            FlattenInto(o, "", res, seen);
            return res;
        }

        static void FlattenInto(Dictionary<string, object> o, string parent, List<KeyValuePair<string, string>> res, HashSet<string> seen)
        {
            foreach (var kv in o)
            {
                if (kv.Value is Dictionary<string, object>) continue;
                string name = kv.Key;
                if (seen.Contains(name)) name = (parent.Length > 0 ? parent : "x") + "_" + name;
                seen.Add(name);
                res.Add(new KeyValuePair<string, string>(name, Fmt(kv.Value)));
            }
            foreach (var kv in o)
            {
                var child = kv.Value as Dictionary<string, object>;
                if (child != null) FlattenInto(child, kv.Key, res, seen);
            }
        }

        static string Fmt(object v)
        {
            if (v == null) return "";
            if (v is string) return (string)v;
            if (v is bool) return (bool)v ? "true" : "false";
            if (v is decimal || v is double || v is float)
            {
                string s = Convert.ToString(v, Inv);
                return Cfg.Dec == "." ? s : s.Replace(".", Cfg.Dec);
            }
            if (v is int || v is long) return Convert.ToString(v, Inv);
            if (v is Dictionary<string, object>) return Js.Serialize(v);
            var en = v as IEnumerable;
            if (en != null)
            {
                var parts = new List<string>();
                foreach (var x in en) parts.Add(Fmt(x));
                return string.Join("|", parts);
            }
            return Convert.ToString(v, Inv);
        }

        // ---------------- tábla segédek

        static string V(Dictionary<string, string> r, string k)
        {
            string v;
            return r.TryGetValue(k, out v) && v != null ? v : "";
        }

        static string Key(Dictionary<string, string> r, string a, string b)
        {
            string x = V(r, a).Trim(), y = V(r, b).Trim();
            if (x.Length == 0 || y.Length == 0) return null;
            return x.ToUpperInvariant() + "|" + y;
        }

        static int CompareByEpoch(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            int c = string.Compare(V(a, "stationID"), V(b, "stationID"), StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            long ea, eb;
            long.TryParse(V(a, "epoch"), NumberStyles.Integer, Inv, out ea);
            long.TryParse(V(b, "epoch"), NumberStyles.Integer, Inv, out eb);
            return ea.CompareTo(eb);
        }
    }

    // ------------------------------------------------------------------ Heti biztonsági mentés

    // ------------------------------------------------------------------ METAR (opcionális felhőzet-ellenőrzés a Megjelenítőhöz)
    // NOAA Aviation Weather Center: https://aviationweather.gov/api/data/metar (ingyenes, kulcs nélkül, kb. 15 nap visszamenőleg)
    // Eredmény: data\metar_<ICAO>.csv (epoch;obsTimeUtc;icao;cover;okta;cb;raw). Minden hiba csak figyelmeztetés.
    static class MetarFetch
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static bool Run(string appDir, string dataDir, bool force)
        {
            var ini = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string iniPath = Path.Combine(appDir, "beallitasok.ini");
            if (File.Exists(iniPath))
                foreach (var raw in File.ReadAllLines(iniPath, Encoding.UTF8))
                {
                    string l = raw.Trim(); int eq = l.IndexOf('=');
                    if (l.Length > 0 && l[0] != '#' && eq > 0) ini[l.Substring(0, eq).Trim()] = l.Substring(eq + 1).Trim();
                }
            string v;
            bool on = ini.TryGetValue("MetarEllenorzes", out v) && (v.ToLowerInvariant() == "true" || v == "1");
            if (!on && !force) return true;
            string icao = ini.TryGetValue("MetarKod", out v) ? v.Trim().ToUpperInvariant() : "LHBP";
            if (!System.Text.RegularExpressions.Regex.IsMatch(icao, "^[A-Z]{4}$")) { Log.Warn("METAR: érvénytelen repülőtér-kód (" + icao + "), 4 betű kell, pl. LHBP."); return false; }
            string path = Path.Combine(dataDir, "metar_" + icao + ".csv");
            // a meglévő sorok (epoch szerint)
            var rows = new SortedDictionary<long, string>();
            if (File.Exists(path))
                foreach (var l in File.ReadAllLines(path, Encoding.UTF8).Skip(1))
                {
                    long t; int sc = l.IndexOf(';');
                    if (sc > 0 && long.TryParse(l.Substring(0, sc), NumberStyles.Integer, Inv, out t)) rows[t] = l;
                }
            int hours = rows.Count == 0 ? 360 : 72;   // először 15 nap, utána az utolsó 3 nap
            string body;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; }
            catch { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("https://aviationweather.gov/api/data/metar?ids=" + icao + "&format=json&hours=" + hours);
                req.Timeout = 15000; req.ReadWriteTimeout = 15000; req.UserAgent = "WStation";
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) body = sr.ReadToEnd();
            }
            catch (Exception ex) { Log.Warn("METAR (" + icao + "): nem elérhető (" + ex.Message + "). A felhőzet-ellenőrzés most kimarad, minden más rendben."); return false; }
            int added = 0;
            try
            {
                var js = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var arr = js.DeserializeObject(body) as object[];
                if (arr == null) { Log.Warn("METAR (" + icao + "): nincs adat (ismeretlen repülőtér?)."); return false; }
                foreach (var o in arr.OfType<Dictionary<string, object>>())
                {
                    object ot, raw, cover, clouds;
                    if (!o.TryGetValue("obsTime", out ot)) continue;
                    long t = Convert.ToInt64(ot, Inv);
                    string rawOb = o.TryGetValue("rawOb", out raw) ? Convert.ToString(raw) : "";
                    string cov = o.TryGetValue("cover", out cover) ? Convert.ToString(cover) : "";
                    double okta = OktaOf(cov);
                    if (o.TryGetValue("clouds", out clouds) && clouds is object[])
                        foreach (var c in ((object[])clouds).OfType<Dictionary<string, object>>())
                        { object cc; if (c.TryGetValue("cover", out cc)) okta = Math.Max(okta, OktaOf(Convert.ToString(cc))); }
                    if (double.IsNaN(okta)) continue;
                    bool cb = System.Text.RegularExpressions.Regex.IsMatch(rawOb, @"\b(FEW|SCT|BKN|OVC)\d{3}(CB|TCU)\b");
                    string line = t.ToString(Inv) + ";" + DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", Inv) + ";" + icao + ";" + cov + ";" +
                                  okta.ToString("0.0", Inv) + ";" + (cb ? "1" : "0") + ";" + rawOb.Replace(";", ",");
                    if (!rows.ContainsKey(t)) added++;
                    rows[t] = line;
                }
            }
            catch (Exception ex) { Log.Warn("METAR (" + icao + "): értelmezhetetlen válasz (" + ex.Message + ")."); return false; }
            // legfeljebb kb. 400 nap marad meg
            long keepFrom = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 400L * 86400;
            var sb = new StringBuilder("epoch;obsTimeUtc;icao;cover;okta;cb;raw\r\n");
            foreach (var kv in rows.Where(x => x.Key >= keepFrom)) sb.Append(kv.Value).Append("\r\n");
            Guard.WriteAllText(path + ".tmp", sb.ToString(), new UTF8Encoding(true));
            if (File.Exists(path)) Guard.Replace(path + ".tmp", path, null); else Guard.Move(path + ".tmp", path);
            Log.Ok("METAR (" + icao + "): " + added + " új jelentés, összesen " + rows.Count + ".");
            return true;
        }

        // a borultság oktában a METAR fedettségi kódjából (a rétegek közül a legnagyobb számít)
        static double OktaOf(string c)
        {
            switch ((c ?? "").Trim().ToUpperInvariant())
            {
                case "CAVOK": case "SKC": case "CLR": case "NSC": case "NCD": return 0;
                case "FEW": return 1.5;
                case "SCT": return 3.5;
                case "BKN": return 6;
                case "OVC": case "OVX": case "VV": return 8;
                default: return double.NaN;
            }
        }
    }

    static class Backup
    {
        public static void Weekly(string appDir, string dataDir, bool force = false)
        {
            if (Cfg.BackupDays <= 0 && !force) return;
            string dir = Path.Combine(appDir, "mentesek");
            Guard.CreateDirectory(dir);
            var zips = new DirectoryInfo(dir).GetFiles("mentes_*.zip").OrderBy(f => f.LastWriteTimeUtc).ToList();
            if (!force && zips.Count > 0 && (DateTime.UtcNow - zips[zips.Count - 1].LastWriteTimeUtc).TotalDays < Cfg.BackupDays) return;

            string name = Path.Combine(dir, "mentes_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture) + ".zip");
            string tmp = name + ".tmp";
            string baseDir = Path.GetDirectoryName(dataDir.TrimEnd('\\'));
            using (var fs = Guard.OpenWrite(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
            {
                foreach (var f in Directory.GetFiles(dataDir, "*.csv")) Add(zip, f, "data/" + Path.GetFileName(f));
                foreach (var f in Directory.GetFiles(appDir).Where(f => f.EndsWith(".ini") || f.EndsWith(".json")))
                    Add(zip, f, Path.GetFileName(appDir) + "/" + Path.GetFileName(f));
                string viewApp = Path.Combine(baseDir, "appdata_megjelenítő");
                if (Directory.Exists(viewApp))
                    foreach (var f in Directory.GetFiles(viewApp).Where(f => f.EndsWith(".ini") || f.EndsWith(".json")))
                        Add(zip, f, Path.GetFileName(viewApp) + "/" + Path.GetFileName(f));
                foreach (var sub in new[] { "presetek" })
                {
                    string p = Path.Combine(viewApp, sub);
                    if (Directory.Exists(p))
                        foreach (var f in Directory.GetFiles(p)) Add(zip, f, Path.GetFileName(viewApp) + "/" + sub + "/" + Path.GetFileName(f));
                }
            }
            Guard.Move(tmp, name);
            zips = new DirectoryInfo(dir).GetFiles("mentes_*.zip").OrderBy(f => f.LastWriteTimeUtc).ToList();
            while (zips.Count > Cfg.BackupKeep) { Guard.Delete(zips[0].FullName); zips.RemoveAt(0); }
            Log.Ok("Biztonsági mentés kész: mentesek\\" + Path.GetFileName(name) + " (" + (new FileInfo(name).Length / 1024) + " KB).");
        }

        static void Add(System.IO.Compression.ZipArchive zip, string file, string entry)
        {
            var e = zip.CreateEntry(entry, System.IO.Compression.CompressionLevel.Optimal);
            e.LastWriteTime = File.GetLastWriteTime(file);
            using (var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var dst = e.Open()) src.CopyTo(dst);
        }
    }
    // ------------------------------------------------------------------ Riasztások (Windows-értesítés)

    static class Alerts
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static double Num(Dictionary<string, string> r, string k)
        {
            string v; double x;
            if (r.TryGetValue(k, out v) && double.TryParse(v.Replace(",", "."), NumberStyles.Float, Inv, out x)) return x;
            return double.NaN;
        }

        public static void Check(Table obs, string station, string appDir)
        {
            long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            var rows = obs.Rows.Values
                .Where(r => { string s; return r.TryGetValue("stationID", out s) && string.Equals(s, station, StringComparison.OrdinalIgnoreCase); })
                .Select(r => new { r, t = (long)Num(r, "epoch") })
                .Where(x => x.t >= now - 4 * 3600)
                .OrderBy(x => x.t).ToList();
            if (rows.Count == 0 || now - rows[rows.Count - 1].t > 2 * 3600) return;   // nincs friss adat
            var lastRow = rows[rows.Count - 1];
            var msgs = new List<string[]>();   // kulcs, cím, szöveg

            double tl = Num(lastRow.r, "tempLow"), th = Num(lastRow.r, "tempHigh");
            if (Cfg.AlFrost && !double.IsNaN(tl) && tl <= Cfg.FrostT)
                msgs.Add(new[] { "fagy", "Fagyveszély", "A hőmérséklet most " + tl.ToString("0.0", Inv).Replace('.', ',') + " °C. Védd a fagyérzékeny növényeket!" });
            if (Cfg.AlHeat && !double.IsNaN(th) && th >= Cfg.HeatT)
                msgs.Add(new[] { "hoseg", "Hőség", "A hőmérséklet " + th.ToString("0.0", Inv).Replace('.', ',') + " °C. Locsolj, és figyelj a cserepes növényekre!" });

            Func<Dictionary<string, string>, double> pres = r => (Num(r, "pressureMax") + Num(r, "pressureMin")) / 2;
            var ago = rows.Where(x => x.t <= lastRow.t - 3 * 3600 + 900).LastOrDefault();
            if (Cfg.AlPres && ago != null)
            {
                double dp = pres(ago.r) - pres(lastRow.r);
                if (!double.IsNaN(dp) && dp >= Cfg.PresDrop)
                    msgs.Add(new[] { "nyomas", "Gyorsan esik a légnyomás", "−" + dp.ToString("0.0", Inv).Replace('.', ',') + " hPa 3 óra alatt: front vagy vihar közeledhet." });
            }
            var hour = rows.Where(x => x.t >= lastRow.t - 3600).ToList();
            double gust = hour.Select(x => Num(x.r, "windgustHigh")).Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Max();
            if (Cfg.AlWind && !double.IsNaN(gust) && gust >= Cfg.GustT)
                msgs.Add(new[] { "szel", "Erős szél", "Széllökés " + gust.ToString("0") + " km/h (" + (gust / 1.852).ToString("0") + " csomó) az utolsó órában." });
            double rate = rows.Where(x => x.t >= lastRow.t - 1800).Select(x => Num(x.r, "precipRate")).Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Max();
            if (Cfg.AlRain && !double.IsNaN(rate) && rate >= Cfg.RainT)
                msgs.Add(new[] { "eso", "Felhőszakadás", "Csapadékintenzitás " + rate.ToString("0") + " mm/h." });

            if (msgs.Count == 0) return;
            int h = DateTime.Now.Hour;
            bool quiet = Cfg.QuietFrom != Cfg.QuietTo && (Cfg.QuietFrom < Cfg.QuietTo ? h >= Cfg.QuietFrom && h < Cfg.QuietTo : h >= Cfg.QuietFrom || h < Cfg.QuietTo);
            if (quiet) { Log.Info("Riasztás csendes órában, nem jelzem: " + string.Join(", ", msgs.Select(m => m[1]))); return; }

            // ismétlésvédelem: riasztasok.json { kulcs: utolsó jelzés (UTC) }
            string path = Path.Combine(appDir, "riasztasok.json");
            var js = new System.Web.Script.Serialization.JavaScriptSerializer();
            var sent = new Dictionary<string, object>();
            try { if (File.Exists(path)) sent = (Dictionary<string, object>)js.DeserializeObject(File.ReadAllText(path)); } catch { }
            var due = msgs.Where(m =>
            {
                object o; DateTime last;
                return !sent.TryGetValue(m[0], out o) || !DateTime.TryParse(Convert.ToString(o), Inv, DateTimeStyles.RoundtripKind, out last) || (DateTime.UtcNow - last.ToUniversalTime()).TotalHours >= Cfg.AlRepeat;
            }).ToList();
            if (due.Count == 0) return;
            foreach (var m in due) sent[m[0]] = DateTime.UtcNow.ToString("o", Inv);
            try { Guard.WriteAllText(path, js.Serialize(sent)); } catch { }
            string title = due.Count == 1 ? due[0][1] : string.Join(" · ", due.Select(m => m[1]));
            string text = string.Join("\n", due.Select(m => due.Count == 1 ? m[2] : m[1] + ": " + m[2]));
            Log.Warn("Riasztás: " + title + " – " + text.Replace("\n", " | "));
            Notify("WStation – " + title, text);
        }

        // Windows-értesítés (Windows 11-en a jobb alsó sarokban, az értesítési központba is bekerül)
        public static void Notify(string title, string text)
        {
            try
            {
                using (var ni = new System.Windows.Forms.NotifyIcon())
                {
                    try { ni.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); }
                    catch { ni.Icon = System.Drawing.SystemIcons.Information; }
                    ni.Text = "WStation";
                    ni.Visible = true;
                    title = Langs.T(title); text = Langs.T(text);   // a Beállításokban választott nyelven
                    ni.ShowBalloonTip(15000, title.Length > 63 ? title.Substring(0, 63) : title, text.Length > 255 ? text.Substring(0, 255) : text, System.Windows.Forms.ToolTipIcon.Warning);
                    var until = DateTime.Now.AddSeconds(8);
                    while (DateTime.Now < until) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(100); }
                    ni.Visible = false;
                }
            }
            catch (Exception ex) { Log.Warn("Az értesítés nem jeleníthető meg: " + ex.Message); }
        }
    }}
