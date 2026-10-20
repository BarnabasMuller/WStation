using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace Idojaras
{
    // Napló a beállítás-változásokról: appdata_megjelenítő\valtozasnaplo.json (a legutóbbi 2000 bejegyzés)
    static class ChangeLog
    {
        static readonly object Lock = new object();
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        static string FilePath { get { return Path.Combine(AppState.L.App, "valtozasnaplo.json"); } }

        static List<Dictionary<string, object>> Read()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var arr = Js.DeserializeObject(File.ReadAllText(FilePath, Encoding.UTF8)) as object[];
                    if (arr != null) return arr.OfType<Dictionary<string, object>>().ToList();
                }
            }
            catch { }
            return new List<Dictionary<string, object>>();
        }

        // revert: a visszaállításhoz szükséges adat (a felület értelmezi), lehet null
        public static void Add(string area, string key, string from, string to, object revert)
        {
            lock (Lock)
            {
                try
                {
                    var list = Read();
                    var e = new Dictionary<string, object>
                    {
                        { "t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) },
                        { "area", area }, { "key", key }, { "from", from }, { "to", to }
                    };
                    if (revert != null) e["revert"] = revert;
                    list.Add(e);
                    while (list.Count > 2000) list.RemoveAt(0);
                    Guard.CreateDirectory(AppState.L.App);
                    string tmp = FilePath + ".tmp";
                    Guard.WriteAllText(tmp, Js.Serialize(list), new UTF8Encoding(false));
                    if (File.Exists(FilePath)) Guard.Replace(tmp, FilePath, null); else Guard.Move(tmp, FilePath);
                }
                catch (Exception ex) { Program.Log("A változásnapló nem írható: " + ex.Message); }
            }
        }

        public static string Handle(string method, string body, out int status)
        {
            status = 200;
            if (method == "POST")
            {
                // a felület egyszerre több bejegyzést is küldhet: [{area,key,from,to,revert}]
                try
                {
                    var o = Js.DeserializeObject(body ?? "[]");
                    var items = o as object[] ?? new object[] { o };
                    foreach (var it in items.OfType<Dictionary<string, object>>())
                    {
                        object a, k, f, t, r;
                        it.TryGetValue("area", out a); it.TryGetValue("key", out k); it.TryGetValue("from", out f); it.TryGetValue("to", out t); it.TryGetValue("revert", out r);
                        Add(Convert.ToString(a), Convert.ToString(k), f == null ? null : Convert.ToString(f, CultureInfo.InvariantCulture), t == null ? null : Convert.ToString(t, CultureInfo.InvariantCulture), r);
                    }
                }
                catch { status = 400; return "{\"error\":\"érvénytelen napló-bejegyzés\"}"; }
            }
            lock (Lock) return Js.Serialize(Read());
        }
    }

    // Frissítés-ellenőrzés helyben: újabb telepítő a programmappában.
    // Csak a programmappa (a szülőmappa – pl. Letöltések, Asztal – nem), és csak a WStation telepítője (10.12).
    static class UpdateCheck
    {
        static Version Me { get { return Assembly.GetExecutingAssembly().GetName().Version; } }

        static List<string> Candidates()
        {
            var res = new List<string>();
            foreach (var pat in new[] { "WStation_setup*.exe", "Idojaras_telepito*.exe" })   // új és régi (2.2.1-ig) név
                try { res.AddRange(Directory.GetFiles(AppState.L.Base, pat)); } catch { }
            // a fájl saját termékneve is a WStationé legyen (egy véletlenül azonos nevű idegen exe ne kerüljön fel)
            return res.Where(p => { try { return FileVersionInfo.GetVersionInfo(p).ProductName == "WStation"; } catch { return false; } }).ToList();
        }

        static Version Ver(string path)
        {
            try { var v = FileVersionInfo.GetVersionInfo(path); return new Version(v.FileMajorPart, v.FileMinorPart, Math.Max(0, v.FileBuildPart)); }
            catch { return null; }
        }

        public static string Json(bool install, out int status)
        {
            status = 200;
            var me = new Version(Me.Major, Me.Minor, Math.Max(0, Me.Build));
            var best = Candidates().Select(p => new { p, v = Ver(p) }).Where(x => x.v != null && x.v > me).OrderByDescending(x => x.v).FirstOrDefault();
            var w = new JW().Obj().P("current", me.Build > 0 ? me.ToString(3) : me.ToString(2));
            if (best != null)
            {
                w.P("available", true).P("version", best.v.Build > 0 ? best.v.ToString(3) : best.v.ToString(2)).P("path", best.p);
                if (install)
                {
                    try
                    {
                        // a telepítő a futó programot nem tudja cserélni: elindítjuk, és a Megjelenítő bezárja magát
                        Process.Start(new ProcessStartInfo(best.p, "--cel \"" + AppState.L.Base + "\"") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(best.p) });
                        ChangeLog.Add("Frissítés", "program", me.ToString(), best.v.ToString(), null);
                        var win = Program.Window;
                        if (win != null && !win.IsDisposed) win.BeginInvoke((Action)(() => win.Close()));
                        else System.Threading.ThreadPool.QueueUserWorkItem(_ => { System.Threading.Thread.Sleep(800); Environment.Exit(0); });
                        w.P("started", true);
                    }
                    catch (Exception ex) { w.P("error", "A telepítő nem indítható: " + ex.Message); }
                }
            }
            else w.P("available", false);
            return w.End().ToString();
        }
    }

    // megjelenito.ini – a küszöbök és a gyanús-mérés szabályok szerkesztése a felületről (ƒx ablakok, Beállítások)
    static class IniEdit
    {
        static Dictionary<string, string> Parse(string body, out string err)
        {
            err = null;
            var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, object> d;
            try { d = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(body ?? "{}"); }
            catch { err = "érvénytelen kérés"; return null; }
            foreach (var kv in d)
            {
                string norm, msg = Settings.Check(kv.Key, Convert.ToString(kv.Value, CultureInfo.InvariantCulture), out norm);
                if (msg != null) { err = msg; return null; }
                res[kv.Key] = norm;
            }
            return res;
        }

        public static string Handle(string method, string body, bool preview, out int status)
        {
            status = 200;
            string ini = AppState.L.Settings;
            var cur = Settings.ReadIni(ini);
            if (method == "POST")
            {
                string err;
                var set = Parse(body, out err);
                if (set == null) { status = 400; return new JW().Obj().P("error", err).End().ToString(); }
                if (preview)
                {
                    // mentés nélkül: hány mérés lenne gyanús
                    var d = new Dictionary<string, string>(cur, StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in set) d[kv.Key] = kv.Value;
                    var s = Settings.FromDict(d, AppState.L, new Settings());
                    var ds = AppState.Data;
                    if (ds == null) { status = 503; return "{\"error\":\"nincs adat\"}"; }
                    int rows; var counts = Loader.FlagPreview(ds, s, out rows);
                    var w = new JW().Obj().P("n", ds.N).P("flagged", rows).Key("counts").Arr();
                    for (int b = 0; b < counts.Length; b++) w.Obj().P("code", Flag.Codes[b]).P("text", Flag.Texts[b]).P("count", counts[b]).End();
                    return w.EndArr().End().ToString();
                }
                var lines = File.Exists(ini) ? File.ReadAllLines(ini, Encoding.UTF8).ToList() : new List<string>();
                bool changed = false;
                foreach (var kv in set)
                {
                    string old; cur.TryGetValue(kv.Key, out old);
                    if (old != null && old.Replace(',', '.') == kv.Value) continue;
                    int idx = lines.FindIndex(l => l.TrimStart().StartsWith(kv.Key + "=", StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) lines[idx] = kv.Key + "=" + kv.Value; else lines.Add(kv.Key + "=" + kv.Value);
                    ChangeLog.Add("Küszöbök (megjelenito.ini)", kv.Key, old, kv.Value, new Dictionary<string, object> { { "type", "ini" }, { "key", kv.Key }, { "value", old } });
                    changed = true;
                }
                if (changed)
                {
                    Guard.WriteAllLines(ini, lines, new UTF8Encoding(true));
                    AppState.ReloadAsync();   // a jelölések és a statisztikák újraszámolása
                }
                cur = Settings.ReadIni(ini);
            }
            var defs = Settings.Defaults();
            var o = new JW().Obj().Key("values").Obj();
            var keys = Settings.Editable.Keys.Concat(Settings.TextKeys).ToList();
            foreach (var k in keys) { string v; o.P(k, cur.TryGetValue(k, out v) ? v : (defs.ContainsKey(k) ? defs[k] : "")); }
            o.End().Key("defaults").Obj();
            foreach (var k in keys) o.P(k, defs.ContainsKey(k) ? defs[k] : "");
            o.End().Key("ranges").Obj();
            foreach (var kv in Settings.Editable) { o.Key(kv.Key).Arr(); foreach (var x in kv.Value) o.Num(x, 2); o.EndArr(); }
            o.End();
            // a választható időzónák (Beállítások › Helyszín)
            o.Key("zones").Arr(); foreach (var z in Tz.Zones) o.Str(z[0]); o.EndArr();
            return o.End().ToString();
        }
    }

    // A Letöltő beállításfájljának (appdata_letöltő\beallitasok.ini) néhány kulcsa a Megjelenítőből szerkesztve
    static class DlSettings
    {
        static readonly Dictionary<string, Func<string, bool>> Allowed = new Dictionary<string, Func<string, bool>>
        {
            { "MentesNaponta", v => { int n; return int.TryParse(v, out n) && n >= 0 && n <= 365; } },
            { "MentesekSzama", v => { int n; return int.TryParse(v, out n) && n >= 1 && n <= 1000; } },
            { "Nyelv", v => v == "hu" || Langs.Exists(v) },
            // felhőzet-ellenőrzés a repülőtér METAR-jelentéseivel (opcionális)
            { "MetarEllenorzes", v => v == "true" || v == "false" },
            { "MetarKod", v => System.Text.RegularExpressions.Regex.IsMatch(v ?? "", "^[A-Z]{4}$") }
        };

        public static string Handle(string method, string body, out int status)
        {
            status = 200;
            string ini = AppState.L.DlIni;
            if (method == "POST")
            {
                Dictionary<string, object> d;
                try { d = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(body ?? "{}"); }
                catch { status = 400; return "{\"error\":\"érvénytelen kérés\"}"; }
                if (!File.Exists(ini)) { status = 400; return "{\"error\":\"Még nincs beallitasok.ini – előbb indítsd el egyszer a Letöltőt.\"}"; }
                var cur = Settings.ReadIni(ini);
                var lines = File.ReadAllLines(ini, Encoding.UTF8).ToList();
                foreach (var kv in d)
                {
                    Func<string, bool> ok;
                    if (!Allowed.TryGetValue(kv.Key, out ok)) continue;
                    string val = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                    if (!ok(val)) { status = 400; return "{\"error\":\"Érvénytelen érték: " + kv.Key + " = " + val.Replace("\"", "'") + "\"}"; }
                    string old; cur.TryGetValue(kv.Key, out old);
                    if (old == val) continue;
                    int idx = lines.FindIndex(l => l.TrimStart().StartsWith(kv.Key + "=", StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) lines[idx] = kv.Key + "=" + val; else lines.Add(kv.Key + "=" + val);
                    ChangeLog.Add("Letöltő beállítás", kv.Key, old, val, new Dictionary<string, object> { { "type", "dl" }, { "key", kv.Key }, { "value", old } });
                }
                Guard.WriteAllLines(ini, lines, new UTF8Encoding(true));
            }
            var c2 = Settings.ReadIni(ini);
            var w = new JW().Obj().P("hasIni", File.Exists(ini));
            foreach (var k in Allowed.Keys) { string v; w.P(k, c2.TryGetValue(k, out v) ? v : ""); }
            return w.End().ToString();
        }
    }
}
