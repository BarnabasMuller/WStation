using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace Idojaras
{
    // A program állapota: a betöltött adat, a betöltés és a letöltés folyamata
    static class AppState
    {
        public static Layout L = Layout.For(AppDomain.CurrentDomain.BaseDirectory);
        public static Settings S = new Settings();
        public static volatile Dataset Data;
        public static volatile string Status = "loading"; // loading | downloading | ready | nodata | error
        public static volatile string Message = "";
        public static volatile string Progress = "";
        public static volatile string DownloadMessage = "";
        public static volatile bool DownloadOk = true;
        public static long DownloadSerial;
        static int busy;
        static bool backupChecked;
        static volatile bool cancelDownload;
        const int DownloadTimeoutMin = 30;   // ennél tovább nem várunk a Letöltőre (12.3)

        // a futó letöltés megszakítása (a Frissítés melletti „Megszakítás” gomb); a már elmentett napok megmaradnak
        public static bool CancelDownload()
        {
            if (Status != "downloading") return false;
            cancelDownload = true;
            return true;
        }

        // download = true: előbb lefuttatja a Letöltőt (ha megvan), utána újraolvassa a CSV-ket
        public static void RefreshAsync(bool download)
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
            Status = download && L.FindDownloader() != null ? "downloading" : "loading";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { if (download) RunDownloader(); }
                catch (Exception ex) { DownloadOk = false; DownloadMessage = "A letöltő nem indítható: " + ex.Message; Interlocked.Increment(ref DownloadSerial); }
                DoLoad();
            });
        }

        public static void ReloadAsync() { RefreshAsync(false); }

        static void RunDownloader()
        {
            string exe = L.FindDownloader();
            if (exe == null) return;
            Status = "downloading";
            Progress = "A letöltő indul...";
            cancelDownload = false;
            bool netError = false;
            var psi = new ProcessStartInfo(exe, "--nowait")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = L.Base,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            using (var p = new Process { StartInfo = psi })
            {
                DataReceivedEventHandler onLine = (o, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    string line = e.Data;
                    if (line.Length > 28 && line[20] == '[') line = line.Substring(28); // időbélyeg és szint levágása
                    if (line.Contains("hálózati hiba")) netError = true;
                    if (!line.StartsWith("=====") && !line.StartsWith("-----")) Progress = line;
                };
                p.OutputDataReceived += onLine;
                p.ErrorDataReceived += onLine;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                // időkorláttal és megszakíthatóan (12.3); a Letöltő a CSV-t atomi cserével menti, így a leállítás nem ront el adatot
                var sw = System.Diagnostics.Stopwatch.StartNew();
                bool stopped = false, timedOut = false;
                while (!p.WaitForExit(500))
                {
                    if (cancelDownload || sw.Elapsed.TotalMinutes > DownloadTimeoutMin)
                    {
                        timedOut = !cancelDownload; stopped = true;
                        try { p.Kill(); } catch { }
                        p.WaitForExit(10000);
                        break;
                    }
                }
                if (stopped)
                {
                    DownloadOk = false;
                    DownloadMessage = timedOut ? "A letöltés fél óra alatt sem fejeződött be, ezért leállítottam. A már letöltött napok megmaradtak; a többit a következő frissítés pótolja."
                                               : "A letöltés megszakítva. A már letöltött napok megmaradtak; a többit a következő frissítés pótolja.";
                    cancelDownload = false;
                    Interlocked.Increment(ref DownloadSerial);
                    return;
                }
                p.WaitForExit();   // az aszinkron kimenet utolsó sorai (pl. „hálózati hiba”) is feldolgozódjanak
                int code = p.ExitCode;
                DownloadOk = code == 0 && !netError;
                if (code == 6) netError = true;   // a Letöltő 2.2-től kilépési kóddal jelzi a hálózati hibát
                switch (code)
                {
                    case 0: DownloadMessage = netError ? "A letöltés részben sikerült: hálózati hiba történt (van internet?)." : "Letöltés kész."; break;
                    case 6: DownloadMessage = "A letöltés részben sikerült: hálózati hiba történt (van internet?)."; break;
                    case 1: DownloadMessage = "A letöltőnek hiányzik az API kulcs vagy az állomás azonosítója. Indítsd el egyszer a Letöltő.exe-t dupla kattintással, és add meg őket."; break;
                    case 2: DownloadMessage = "Az API kulcs érvénytelen vagy lejárt. Ellenőrizd: appdata_letöltő\\beallitasok.ini"; break;
                    case 3: DownloadMessage = "Az adatfájl zárolva volt (nyitva van Excelben?), nem minden új adat mentődött."; break;
                    case 4: DownloadMessage = "A letöltő épp fut (pl. ütemezett feladatként). Próbáld újra kicsit később."; break;
                    case 5: DownloadMessage = "A letöltőnek nincs írási joga a mappához."; break;
                    default: DownloadMessage = "A letöltés hibával ért véget (kód " + code + "). Részletek: appdata_letöltő\\lekerdezes_naplo.log"; break;
                }
            }
            Interlocked.Increment(ref DownloadSerial);
        }

        static void DoLoad()
        {
            try
            {
                Status = "loading";
                Progress = "Beállítások beolvasása...";
                S = Settings.Load(L);
                var ds = Loader.Load(L, S, p => Progress = p);
                if (ds == null || ds.N == 0)
                {
                    Data = null;
                    Status = "nodata";
                    Message = ds == null
                        ? "Még nincs időjárás-adat a data mappában."
                        : "A CSV fájlokban nincs mérés ehhez az állomáshoz.";
                }
                else
                {
                    Data = ds;
                    Message = "";
                    Status = "ready";
                    if (!backupChecked) { backupChecked = true; System.Threading.ThreadPool.QueueUserWorkItem(_ => Api.BackupIfDue()); }
                }
            }
            catch (Exception ex)
            {
                Status = "error";
                Message = "Hiba az adatok betöltésekor: " + ex.Message;
                Program.Log(ex.ToString());
            }
            finally
            {
                Progress = "";
                Interlocked.Exchange(ref busy, 0);
            }
        }
    }
    static class Api
    {
        public const string Version = "2.2.7.2";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        delegate double Getter(int i);

        public static string Handle(string path, NameValueCollection q, string method, out int status)
        {
            return Handle(path, q, method, null, out status);
        }

        public static string Handle(string path, NameValueCollection q, string method, string body, out int status)
        {
            status = 200;
            switch (path)
            {
                case "/api/status": return StatusJson();
                case "/api/ui": return UiSettings.Handle(method, body, out status);
                case "/api/store/naplo": return JsonStore.Handle("naplo", method, body, out status);
                case "/api/store/futas": return JsonStore.Handle("futas", method, body, out status);
                case "/api/alerts": return AlertSettings.Handle(method, body, out status);
                case "/api/alerts/test":
                    if (method != "POST") { status = 405; return "{}"; }
                    return AlertSettings.Test(out status);
                case "/api/schedule": return ScheduleJson(method == "POST" ? (body ?? "") : null, q["minutes"]);
                case "/api/backup": return BackupJson(method, body, out status);
                case "/api/backup/copy":
                    if (method != "POST") { status = 405; return "{}"; }   // Mentés ablakot nyit: csak POST
                    return BackupTools.CopyLatest();
                case "/api/backup/restore":
                    if (method != "POST") { status = 405; return "{}"; }
                    return BackupTools.Restore();
                case "/api/changelog": return ChangeLog.Handle(method, body, out status);
                case "/api/update": return UpdateCheck.Json(method == "POST", out status);
                case "/api/dlsettings": return DlSettings.Handle(method, body, out status);
                case "/api/ini": return IniEdit.Handle(method, body, false, out status);
                case "/api/ini/preview": return IniEdit.Handle(method, body, true, out status);
                case "/api/calib": return Calib.Handle(method, body, out status);
                case "/api/langs": return Langs.ListJson();
                case "/api/open":
                    if (method != "POST") { status = 405; return "{}"; }
                    return OpenFolder(q["what"], out status);
                case "/api/reload":
                    // mellékhatásos kérés: csak POST (saját fejléccel), így egy idegen weboldal nem indíthatja el
                    if (method != "POST") { status = 405; return "{}"; }
                    AppState.RefreshAsync(false);
                    return StatusJson();
                case "/api/refresh/cancel":
                    if (method != "POST") { status = 405; return "{}"; }
                    AppState.CancelDownload();
                    return StatusJson();
                case "/api/refresh":
                    if (method != "POST") { status = 405; return "{}"; }   // letöltést indít (API-kvóta): csak POST
                    AppState.RefreshAsync(q["download"] != "0");
                    return StatusJson();
            }
            var ds = AppState.Data;
            if (ds == null) { status = 503; return StatusJson(); }
            switch (path)
            {
                case "/api/meta": return Meta(ds);
                case "/api/today": return Today(ds, AppState.S);
                case "/api/series": return Series(ds, AppState.S, q);
                case "/api/export":
                    if (method != "POST") { status = 405; return "{}"; }   // Mentés ablakot nyit: csak POST
                    return Export.SaveWithDialog(ds, q["raw"] == "1");
                case "/api/days": return Days(ds, q["clean"] != "0");
                case "/api/drought": return DroughtJson(ds);
                case "/api/rainevents": return Analysis.RainEventsJson(ds, q["clean"] != "0", AppState.S);
                case "/api/events": return Analysis.EventsJson(ds, AppState.S);
                case "/api/records": { int y; int.TryParse(q["year"] ?? "0", out y); return Analysis.RecordsJson(ds, AppState.S, q["clean"] != "0", y, q["ranges"]); }
                case "/api/wind": return Analysis.WindJson(ds, AppState.S, QLong(q, "from", ds.T[0] * 1000), QLong(q, "to", ds.T[ds.N - 1] * 1000));
                // felhőzet-becslés (TERV_4 5. pont)
                case "/api/cloud": return Cloud.SeriesJson(ds, AppState.S, QLong(q, "from", (ds.T[ds.N - 1] - 7 * 86400) * 1000), QLong(q, "to", (ds.T[ds.N - 1] + 1) * 1000));
                case "/api/cloud/now": return Cloud.NowJson(ds, AppState.S);
                case "/api/cloud/days": return Cloud.DaysJson(ds, AppState.S);
                case "/api/cloud/metar":
                    if (method == "POST")
                    {
                        // „Letöltés most”: a Letöltő csak a METAR-t kéri le (rövid időkorláttal; a fő letöltéshez nem nyúl)
                        string exe = AppState.L.FindDownloader();
                        if (exe == null) return "{\"error\":\"Nem találom a Letöltőt a programmappában.\"}";
                        var p = Process.Start(new ProcessStartInfo(exe, "--metar --nowait") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppState.L.Base });
                        if (!p.WaitForExit(40000)) { try { p.Kill(); } catch { } return "{\"error\":\"A METAR-letöltés nem fejeződött be 40 másodperc alatt.\"}"; }
                        if (p.ExitCode != 0) return "{\"error\":\"A METAR nem érhető el (nincs internet, vagy hibás a repülőtér kódja). Részletek: appdata_letöltő\\\\lekerdezes_naplo.log\"}";
                    }
                    return Cloud.MetarJson(ds, AppState.S, AppState.L);
                case "/api/hourly": return Analysis.HourlyJson(ds, q["metric"] ?? "temp", q["ranges"]);
                case "/api/quality": return Analysis.QualityJson(ds, q["ranges"]);
                case "/api/vars": return Analysis.VarsJson(ds, q);
                case "/api/at": return Analysis.AtJson(ds, QLong(q, "t", ds.T[ds.N - 1] * 1000));
                case "/api/drought/config":
                    if (method != "POST") { status = 405; return "{}"; }
                    try
                    {
                        // csonka / üres kérés (pl. "{}") ne állítsa vissza csendben az összes profilt és programot
                        var o = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(body ?? "") as Dictionary<string, object>;
                        if (o == null || !o.ContainsKey("version") || !o.ContainsKey("programs")) throw new Exception("hiányos adat");
                        DroughtStore.Save(DroughtStore.Parse(body));
                    }
                    catch (Exception ex) { status = 400; return new JW().Obj().P("error", "Érvénytelen beállítás: " + ex.Message).End().ToString(); }
                    return DroughtJson(ds);
                case "/api/drought/reset":
                    if (method != "POST") { status = 405; return "{}"; }
                    DroughtStore.Save(DConfig.Defaults());
                    return DroughtJson(ds);
                case "/api/drought/preset":
                    if (method != "POST") { status = 405; return "{}"; }
                    try { DroughtStore.SavePreset(q["kind"] ?? "", q["name"] ?? "", body); }
                    catch (Exception ex) { status = 400; return new JW().Obj().P("error", ex.Message).End().ToString(); }
                    return DroughtJson(ds);
                case "/api/presets": return DroughtStore.Serialize(DroughtStore.Presets());
                case "/api/drought/preset-delete":
                    if (method != "POST") { status = 405; return "{}"; }
                    DroughtStore.DeletePreset(q["kind"] ?? "", q["name"] ?? "");
                    return DroughtJson(ds);
            }
            status = 404;
            return "{\"error\":\"ismeretlen\"}";
        }

        // body: {kind, minutes, time, days[]} – vagy (régi forma) ?minutes=N
        static string ScheduleJson(string body, string minutesQ)
        {
            string msg = null, before = null;
            string exe = AppState.L.FindDownloader();
            if (body != null)
            {
                var spec = new Kozos.Utemezes.Spec();
                if (body.Trim().StartsWith("{"))
                {
                    try { spec = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Kozos.Utemezes.Spec>(body) ?? spec; }
                    catch { msg = "Érvénytelen ütemezés."; }
                }
                else { int m; int.TryParse(minutesQ ?? "", out m); spec.kind = m > 0 ? "interval" : "off"; spec.minutes = m; }
                if (msg == null)
                {
                    before = Kozos.Utemezes.Text(Kozos.Utemezes.QuerySpec());
                    if (exe == null && spec.kind != "off") msg = "Nem találom a Letöltő.exe-t a program mappájában.";
                    else
                    {
                        bool ok = Kozos.Utemezes.SetSpec(exe, spec, out msg);
                        if (ok) ChangeLog.Add("Automatikus letöltés", "ütemezés", before, Kozos.Utemezes.Text(spec), null);
                    }
                }
            }
            var s = Kozos.Utemezes.QuerySpec();
            bool ours = s.exe != null && exe != null && string.Equals(Path.GetFullPath(s.exe), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase);
            var w = new JW().Obj().P("kind", s.kind).P("minutes", s.minutes).P("time", s.time).P("text", Kozos.Utemezes.Text(s)).P("path", s.exe).P("ours", ours).P("hasDownloader", exe != null);
            w.Key("days").Arr(); foreach (var d in s.days) w.Str(d); w.EndArr();
            if (msg != null) w.P("message", msg);
            w.Key("choices").Arr(); foreach (var c in Kozos.Utemezes.Choices) w.Long(c); w.EndArr();
            return w.End().ToString();
        }

        // biztonsági mentés: állapot, beállítás, „Mentés most” (a mentést a Letöltő --mentes módban készíti)
        static string BackupJson(string method, string body, out int status)
        {
            status = 200;
            string dir = Path.Combine(AppState.L.DlApp, "mentesek");
            if (method == "POST")
            {
                string exe = AppState.L.FindDownloader();
                if (exe == null) return "{\"error\":\"Nem találom a Letöltő.exe-t.\"}";
                // nagy adatállománynál a tömörítés percekig tarthat: legfeljebb 10 percet várunk (2.2.7.2)
                using (var p = Process.Start(new ProcessStartInfo(exe, "--mentes --most") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppState.L.Base }))
                {
                    if (p == null) return "{\"error\":\"A Letöltő nem indítható.\"}";
                    if (!p.WaitForExit(600000))
                    {
                        try { p.Kill(); } catch { }
                        ChangeLog.Add("Biztonsági mentés", "kézi mentés", null, "időtúllépés", null);
                        return "{\"error\":\"A mentés 10 perc alatt sem készült el.\"}";
                    }
                    ChangeLog.Add("Biztonsági mentés", "kézi mentés", null, p.ExitCode == 0 ? "elkészült" : "hiba (" + p.ExitCode + ")", null);
                }
            }
            var zips = Directory.Exists(dir) ? new DirectoryInfo(dir).GetFiles("mentes_*.zip").OrderByDescending(f => f.LastWriteTimeUtc).ToList() : new List<FileInfo>();
            var ini = Settings.ReadIni(AppState.L.DlIni);
            string v;
            int days = ini.TryGetValue("MentesNaponta", out v) && int.TryParse(v, out days) ? days : 7;
            int keep = ini.TryGetValue("MentesekSzama", out v) && int.TryParse(v, out keep) ? keep : 8;
            var w = new JW().Obj().P("days", days).P("keep", keep).P("count", zips.Count);
            if (zips.Count > 0) w.P("last", zips[0].LastWriteTime.ToString("yyyy-MM-dd HH:mm", Inv)).P("lastSize", zips[0].Length / 1024);
            bool due = days > 0 && (zips.Count == 0 || (DateTime.UtcNow - zips[0].LastWriteTimeUtc).TotalDays >= days);
            w.P("due", due);
            return w.End().ToString();
        }

        // induláskor: ha a mentés esedékes, a háttérben elkészül (akkor is, ha nincs ütemezett letöltés)
        public static void BackupIfDue()
        {
            try
            {
                int st;
                var j = (Dictionary<string, object>)new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(BackupJson("GET", null, out st));
                if (!(j["due"] is bool) || !(bool)j["due"]) return;
                string exe = AppState.L.FindDownloader();
                if (exe == null) return;
                Process.Start(new ProcessStartInfo(exe, "--mentes") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppState.L.Base });
                Program.Log("Esedékes biztonsági mentés elindítva (induláskor).");
            }
            catch (Exception ex) { Program.Log("Az induláskori mentés-ellenőrzés nem sikerült: " + ex.Message); }
        }

        // csak a program saját mappáit nyitja meg (nem fogad tetszőleges útvonalat)
        static string OpenFolder(string what, out int status)
        {
            status = 200;
            string p = what == "data" ? AppState.L.Data
                     : what == "base" ? AppState.L.Base
                     : what == "backup" ? Path.Combine(AppState.L.DlApp, "mentesek")
                     : null;
            if (p == null) { status = 400; return "{\"error\":\"ismeretlen mappa\"}"; }
            if (!Directory.Exists(p))
                return "{\"error\":\"" + (what == "backup" ? "Még nincs biztonsági mentés (a Letöltő hetente készít)." : "A mappa nem létezik.") + "\"}";
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + p + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { return "{\"error\":\"" + ex.Message.Replace("\"", "'") + "\"}"; }
            return "{\"ok\":true}";
        }

        static string StatusJson()
        {
            var w = new JW().Obj()
                .P("status", AppState.Status).P("message", AppState.Message).P("progress", AppState.Progress)
                .P("dir", AppState.L.Base).P("dataDir", AppState.L.Data).P("hasData", AppState.Data != null).P("version", Version)
                .P("hasDownloader", AppState.L.FindDownloader() != null).P("downloadSerial", Interlocked.Read(ref AppState.DownloadSerial))
                .P("downloadMessage", AppState.DownloadMessage).P("downloadOk", AppState.DownloadOk);
            return w.End().ToString();
        }

        static string Meta(Dataset ds)
        {
            var s = AppState.S;
            var w = new JW().Obj();
            w.P("status", AppState.Status).P("version", Version);
            // koordináták: az adatokból, ha nincs, a Beállítások › Helyszín tartalékértéke (latSource jelzi)
            bool dataLL = !double.IsNaN(ds.Lat) && !double.IsNaN(ds.Lon);
            w.P("station", ds.Station).P("lat", dataLL ? ds.Lat : s.FallbackLat, 6).P("lon", dataLL ? ds.Lon : s.FallbackLon, 6)
             .P("latSource", dataLL ? "adat" : "beállítás").P("dir", ds.Dir).P("baseDir", AppState.L.Base);
            w.P("place", s.PlaceName.Length > 0 ? s.PlaceName : ds.Neighborhood.Length > 0 ? ds.Neighborhood : ds.Station).P("neighborhood", ds.Neighborhood);
            w.P("n", ds.N).Ms("first", ds.T[0]).Ms("last", ds.T[ds.N - 1]);
            if (ds.CalibRes != null) w.Key("calib").Obj().P("active", ds.CalibRes.Active).P("factor", ds.CalibRes.Global, 3).P("mode", ds.CalibRes.Cfg.mode).P("period", ds.CalibRes.ByDay != null).End();
            w.P("fromCache", ds.FromCache).P("loadMs", ds.LoadMs).P("newRows", ds.NewRows).P("parsedRows", ds.ParsedRows).P("duplicates", ds.DuplicateRows);
            w.P("loadedAt", ds.LoadedAt.ToString("yyyy-MM-dd HH:mm:ss", Inv));
            w.Key("files").Arr(); foreach (var f in ds.Files) w.Str(f); w.EndArr();
            w.Key("warnings").Arr(); foreach (var x in ds.Warnings) w.Str(x); w.EndArr();
            w.P("flaggedRows", ds.FlaggedRows);
            w.Key("flagCounts").Obj();
            for (int b = 0; b < Flag.Bits.Length; b++) w.P(Flag.Codes[b], ds.FlagCounts[b]);
            w.End();
            w.Key("flagTexts").Obj();
            for (int b = 0; b < Flag.Bits.Length; b++) w.P(Flag.Bits[b].ToString(Inv), Flag.Texts[b]);
            w.End();
            w.Key("gaps").Arr();
            foreach (var g in ds.Gaps) w.Arr().Long(g.From * 1000).Long(g.To * 1000).EndArr();
            w.EndArr();
            w.Key("settings").Obj()
                .P("hotDay", s.HotDay).P("tropicalNight", s.TropicalNight).P("frostDay", s.FrostDay).P("iceDay", s.IceDay)
                .P("gapMinutes", s.GapMinutes).P("windUnit", s.WindUnit).P("excludeSuspect", s.ExcludeSuspect)
                .P("tz", Tz.Iana).P("heatWarn1", s.HeatWarn1).P("heatWarn3", s.HeatWarn3).P("currency", s.Currency)
                .Key("rainNormal").Arr();
            foreach (var x in s.RainNormal) w.Num(x, 1);
            w.EndArr().End();
            return w.End().ToString();
        }

        // ------------------------------------------------------------------ napi adatok (naptár, heti összesítő)

        // oszlopos formában: minden mező egy tömb, a napok sorrendjében
        static string Days(Dataset ds, bool clean)
        {
            var days = clean ? ds.Days : ds.DaysRaw;
            var w = new JW().Obj().P("clean", clean).P("count", days.Count);
            Action<string, Func<DayStat, double>, int> num = (k, f, dec) => { w.Key(k).Arr(); foreach (var d in days) w.Num(f(d), dec); w.EndArr(); };
            Action<string, Func<DayStat, long>> ms = (k, f) => { w.Key(k).Arr(); foreach (var d in days) { long t = f(d); if (t == 0) w.Null(); else w.Long(t * 1000); } w.EndArr(); };
            w.Key("date").Arr(); foreach (var d in days) w.Str(d.Date.ToString("yyyy-MM-dd", Inv)); w.EndArr();
            ms("start", d => d.Start); ms("end", d => d.End);
            num("cov", d => d.Coverage, 3); num("n", d => d.Count, 0);
            num("tmin", d => d.Tmin, 1); num("tmax", d => d.Tmax, 1); num("tavg", d => d.Tavg, 1);
            ms("tminT", d => d.TminT); ms("tmaxT", d => d.TmaxT);
            num("range", d => d.Tmax - d.Tmin, 1); num("dew", d => d.DewAvg, 1);
            num("humMin", d => d.HumMin, 0); num("humMax", d => d.HumMax, 0); num("hum", d => d.HumAvg, 0);
            num("presMin", d => d.PresMin, 1); num("presMax", d => d.PresMax, 1); num("pres", d => d.PresAvg, 1);
            num("presChg", d => d.PresLast - d.PresFirst, 1);
            num("wind", d => d.WindAvg, 1); num("windMax", d => d.WindMax, 1); num("gust", d => d.GustMax, 1); ms("gustT", d => d.GustMaxT);
            num("dir", d => d.WindDir, 0);
            num("rain", d => d.Rain, 2); num("rate", d => d.RainRateMax, 1);
            num("sun", d => d.SunHours, 1); num("energy", d => d.Energy, 2); num("solar", d => d.SolarMax, 0); num("uv", d => d.UvMax, 0);
            num("vpd", d => d.VpdMax, 2); num("vpdDay", d => d.VpdDay, 2);
            return w.End().ToString();
        }

        // ------------------------------------------------------------------ szárazság, locsolás

        static string DroughtJson(Dataset ds)
        {
            var cfg = DroughtStore.Get();
            var r = Drought.Compute(ds, cfg);
            int n = r.Dates.Count;
            var w = new JW().Obj();
            w.Key("config").Raw(DroughtStore.Serialize(cfg));
            w.Key("defaults").Raw(DroughtStore.Serialize(DConfig.Defaults()));
            w.Key("presets").Raw(DroughtStore.Serialize(DroughtStore.Presets()));
            if (DroughtStore.RepairNote != null) w.P("repairNote", DroughtStore.RepairNote);
            w.P("lat", ds.Lat, 4).P("n", n);
            w.Key("date").Arr(); foreach (var s in r.Dates) w.Str(s); w.EndArr();
            Action<string, double[], int> arr = (k, a, dec) => { w.Key(k).Arr(); if (a != null) foreach (var x in a) w.Num(x, dec); w.EndArr(); };
            arr("et0", r.Et0, 2); arr("rain", r.Rain, 2); arr("ro", r.Ro, 2); arr("rs", r.Rs, 2); arr("tmax", r.Tmax, 1); arr("tmin", r.Tmin, 1); arr("cn", r.CnUsed, 1);
            w.Key("est").Arr(); if (r.Est != null) foreach (var b in r.Est) w.Bool(b); w.EndArr();
            arr("ffmc", r.Ffmc, 1); arr("dmc", r.Dmc, 1); arr("dc", r.Dc, 1); arr("isi", r.Isi, 1); arr("bui", r.Bui, 1); arr("fwi", r.Fwi, 1);
            arr("fwiT", r.FwiT, 1); arr("fwiH", r.FwiH, 0); arr("fwiW", r.FwiW, 1); arr("fwiRo", r.FwiRo, 2);
            w.Key("fwiEst").Arr(); if (r.FwiEst != null) foreach (var b in r.FwiEst) w.Bool(b); w.EndArr();
            arr("gdd", r.Gdd, 1); arr("gddCum", r.GddCum, 0); arr("gddPheno", r.GddPheno, 0);
            w.Key("pheno").Obj();
            foreach (var kv in r.Pheno) w.Key(kv.Key.ToString(Inv)).Arr().Str(kv.Value[0].ToString("yyyy-MM-dd", Inv)).Str(kv.Value[1].ToString("yyyy-MM-dd", Inv)).EndArr();
            w.End();
            w.Key("cal").Arr();
            for (int m = 1; m <= 12; m++) w.Obj().P("month", m).P("ratio", r.CalRatio[m], 3).P("factor", r.CalFactor[m], 2).End();
            w.EndArr();
            w.P("suggestedFactor", r.SuggestedFactor, 2);
            w.Key("profiles").Arr();
            foreach (var p in r.Profiles)
            {
                w.Obj().P("id", p.P.id).P("area", p.Area, 3)
                 .P("stress30", p.Stress30, 3).P("stress90", p.Stress90, 3).P("drMonthAgo", p.DrMonthAgo, 1)
                 .P("reserveL", p.ReserveL, 0).P("reserveDays", p.ReserveDays, 1).P("litersToday", p.LitersToday, 2);
                arr("dr", p.Dr, 1); arr("depl", p.Depl, 3); arr("ks", p.Ks, 2); arr("etc", p.Etc, 2); arr("irr", p.Irr, 1); arr("kc", p.Kc, 2); arr("taw", p.Taw, 1); arr("raw", p.Raw, 1);
                w.End();
            }
            w.EndArr();
            return w.End().ToString();
        }

        // ------------------------------------------------------------------ "Ma" kártya

        static int Nearest(Dataset ds, long t, long tolSec)
        {
            int i = ds.LowerBound(t);
            int best = -1; long bd = long.MaxValue;
            for (int k = Math.Max(0, i - 1); k <= Math.Min(ds.N - 1, i); k++)
            {
                long d = Math.Abs(ds.T[k] - t);
                if (d < bd) { bd = d; best = k; }
            }
            return bd <= tolSec ? best : -1;
        }

        static double PresAt(Dataset ds, int i)
        {
            if (i < 0 || (ds.Flags[i] & Flag.Pres) != 0) return double.NaN;
            return (ds.C[Col.PMAX][i] + ds.C[Col.PMIN][i]) / 2.0;
        }

        static string Today(Dataset ds, Settings s)
        {
            var w = new JW().Obj();
            var now = ds.Now;
            int last = ds.N - 1;
            long nowEpoch = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

            w.Key("now").Obj();
            if (now != null)
            {
                double feels = now.Temp;
                if (!double.IsNaN(now.Temp) && now.Temp >= 27 && !double.IsNaN(now.HeatIndex)) feels = now.HeatIndex;
                else if (!double.IsNaN(now.Temp) && now.Temp <= 10 && !double.IsNaN(now.WindChill)) feels = now.WindChill;
                w.Ms("t", now.T).P("source", now.Source).P("ageMin", (nowEpoch - now.T) / 60)
                 .P("temp", now.Temp, 1).P("feels", feels, 1).P("dew", now.Dew, 1).P("hum", now.Hum, 0)
                 .P("wind", now.Wind, 1).P("gust", now.Gust, 1).P("dir", now.Dir, 0).P("pres", now.Pres, 1)
                 .P("rainRate", now.PrecipRate, 2).P("rainTotal", now.PrecipTotal, 2).P("solar", now.Solar, 0).P("uv", now.Uv, 1);
            }
            // nyomásváltozás 3 óra alatt és a tegnapi érték ugyanekkor
            double p0 = PresAt(ds, last), p3 = PresAt(ds, Nearest(ds, ds.T[last] - 3 * 3600, 900));
            w.P("pres3h", p0 - p3, 1);
            int t3 = Nearest(ds, ds.T[last] - 3 * 3600, 900);
            w.P("temp3h", t3 >= 0 && (ds.Flags[t3] & Flag.Temp) == 0 ? ds.C[Col.TA][t3] : double.NaN, 1);
            double g1 = double.NaN, r1 = double.NaN;
            for (int i = ds.LowerBound(ds.T[last] - 3600); i <= last; i++)
            {
                if ((ds.Flags[i] & Flag.Wind) == 0 && !float.IsNaN(ds.C[Col.WGH][i]) && (double.IsNaN(g1) || ds.C[Col.WGH][i] > g1)) g1 = ds.C[Col.WGH][i];
                if ((ds.Flags[i] & Flag.RainRate) == 0 && !float.IsNaN(ds.C[Col.PRATE][i]) && (double.IsNaN(r1) || ds.C[Col.PRATE][i] > r1)) r1 = ds.C[Col.PRATE][i];
            }
            w.P("gust1h", g1, 1).P("rate1h", r1, 1);
            // a hőség-figyelőhöz: az előző napok középhőmérséklete csak egymást követő, teljes napokból (11.7) …
            var prevDays = new List<double>();
            for (int dn = ds.Day[last] - 1; prevDays.Count < 3; dn--)
            {
                DayStat pd;
                if (!ds.DayMap.TryGetValue(dn, out pd) || pd.Coverage < DayStat.FullCoverage || double.IsNaN(pd.Tavg)) break;
                prevDays.Add(pd.Tavg);
            }
            w.Key("tavgPrev").Arr(); foreach (var x in prevDays) w.Num(x, 1); w.EndArr();
            // … és a mai, még részleges nap helyett az utolsó 24 óra átlaga (reggel nem csak az éjszakából számol)
            double s24 = 0; int n24 = 0;
            for (int i = ds.LowerBound(ds.T[last] - 86400 + 1); i <= last; i++)
                if ((ds.Flags[i] & Flag.Temp) == 0 && !float.IsNaN(ds.C[Col.TA][i])) { s24 += ds.C[Col.TA][i]; n24++; }
            w.P("tavg24", n24 >= 200 ? s24 / n24 : double.NaN, 1);
            int y = Nearest(ds, ds.T[last] - 86400, 900);
            w.P("tempYesterday", y >= 0 && (ds.Flags[y] & Flag.Temp) == 0 ? ds.C[Col.TA][y] : double.NaN, 1);
            w.End();

            DayStat today;
            ds.DayMap.TryGetValue(ds.Day[last], out today);
            if (today != null)
            {
                w.Key("today").Obj();
                w.P("date", today.Date.ToString("yyyy-MM-dd", Inv)).P("count", today.Count).P("coverage", today.Coverage, 3);
                w.P("tmin", today.Tmin, 1).Ms("tminT", today.TminT).P("tmax", today.Tmax, 1).Ms("tmaxT", today.TmaxT).P("tavg", today.Tavg, 1);
                w.P("humMin", today.HumMin, 0).P("humMax", today.HumMax, 0).P("presMin", today.PresMin, 1).P("presMax", today.PresMax, 1);
                w.P("rain", today.Rain, 2).P("rainRateMax", today.RainRateMax, 1).P("gustMax", today.GustMax, 1).Ms("gustMaxT", today.GustMaxT);
                w.P("windAvg", today.WindAvg, 1).P("windDir", today.WindDir, 0).P("energy", today.Energy, 2).P("uvMax", today.UvMax, 1).P("solarMax", today.SolarMax, 0);
                w.End();

                // a mai nap az eddigi évhez képest (tiszta adatból, legalább 80%-ban lefedett napokkal)
                int year = today.Date.Year;
                var yAll = ds.Days.Where(d => d.Date.Year == year && d.Day != today.Day).ToList();
                var yd = yAll.Where(d => d.Coverage >= DayStat.FullCoverage).ToList();   // a rangsorok és darabszámok a teljes napokból (8.6)
                w.Key("year").Obj();
                w.P("year", year).P("days", yd.Count + 1);
                if (!double.IsNaN(today.Tmax))
                    w.P("warmRank", 1 + yd.Count(d => d.Tmax > today.Tmax));
                if (!double.IsNaN(today.Tmin))
                    w.P("coldRank", 1 + yd.Count(d => d.Tmin < today.Tmin));
                var md = yd.Where(d => d.Date.Month == today.Date.Month && !double.IsNaN(d.Tmax)).ToList();
                if (md.Count >= 3)
                {
                    w.P("monthDays", md.Count).P("monthTmaxAvg", md.Average(d => d.Tmax), 1).P("monthTminAvg", md.Where(d => !double.IsNaN(d.Tmin)).Average(d => d.Tmin), 1);
                }
                var all = yd.Concat(new[] { today }).ToList();
                // a csapadékösszeg minden napból (a hiányos nap esője is esett)
                w.P("rain", yAll.Concat(new[] { today }).Where(d => !double.IsNaN(d.Rain)).Sum(d => d.Rain), 1);
                w.P("hotDays", all.Count(d => d.Tmax >= s.HotDay)).P("frostDays", all.Count(d => d.Tmin < s.FrostDay))
                 .P("tropicalNights", all.Count(d => d.Tmin >= s.TropicalNight)).P("rainDays", yAll.Concat(new[] { today }).Count(d => d.Rain >= 1.0));
                var hot = all.Where(d => !double.IsNaN(d.Tmax)).OrderByDescending(d => d.Tmax).FirstOrDefault();
                var cold = all.Where(d => !double.IsNaN(d.Tmin)).OrderBy(d => d.Tmin).FirstOrDefault();
                if (hot != null) w.P("maxT", hot.Tmax, 1).Ms("maxTt", hot.TmaxT);
                if (cold != null) w.P("minT", cold.Tmin, 1).Ms("minTt", cold.TminT);
                w.End();
            }
            return w.End().ToString();
        }

        // ------------------------------------------------------------------ idősorok (LTTB-ritkítással)

        static long QLong(NameValueCollection q, string k, long def)
        {
            long v;
            return q[k] != null && long.TryParse(q[k], NumberStyles.Integer, Inv, out v) ? v : def;
        }

        static string Series(Dataset ds, Settings s, NameValueCollection q)
        {
            long fromMs = QLong(q, "from", ds.T[0] * 1000), toMs = QLong(q, "to", ds.T[ds.N - 1] * 1000);
            int n = (int)Math.Max(50, Math.Min(10000, QLong(q, "n", 1500)));
            bool clean = q["clean"] == "1";
            int i0 = ds.LowerBound(fromMs / 1000), i1 = ds.LowerBound(toMs / 1000 + 1);
            if (i0 > 0) i0--;               // egy pont a tartományon kívül is, hogy a vonal a széléig érjen
            if (i1 < ds.N) i1++;
            if (i1 <= i0) i1 = Math.Min(ds.N, i0 + 1);

            long gapSec = s.GapMinutes * 60L;
            var segs = new List<int[]>();
            int st = i0;
            for (int i = i0 + 1; i < i1; i++)
                if (ds.T[i] - ds.T[i - 1] > gapSec) { segs.Add(new[] { st, i }); st = i; }
            segs.Add(new[] { st, i1 });
            int total = i1 - i0;
            bool raw = total <= n;

            var w = new JW().Obj();
            w.P("from", fromMs).P("to", toMs).P("count", total).P("raw", raw).P("clean", clean);

            Func<int, int, Getter> G = (col, mask) => clean
                ? (Getter)(i => (ds.Flags[i] & mask) != 0 ? double.NaN : ds.C[col][i])
                : (i => ds.C[col][i]);
            Getter pres = clean
                ? (Getter)(i => (ds.Flags[i] & Flag.Pres) != 0 ? double.NaN : (ds.C[Col.PMAX][i] + ds.C[Col.PMIN][i]) / 2.0)
                : (i => (ds.C[Col.PMAX][i] + ds.C[Col.PMIN][i]) / 2.0);

            w.Key("groups").Obj();
            Group(w, ds, segs, total, n, raw, "temp", G(Col.TA, Flag.Temp), 1,
                new[] { "lo", "hi", "dew" }, new[] { G(Col.TL, Flag.Temp), G(Col.TH, Flag.Temp), G(Col.DA, Flag.Dew) }, new[] { 1, 2, 0 });
            Group(w, ds, segs, total, n, raw, "hum", G(Col.HA, Flag.Hum), 0, new string[0], new Getter[0], new int[0]);
            Group(w, ds, segs, total, n, raw, "pres", pres, 1, new string[0], new Getter[0], new int[0]);
            Group(w, ds, segs, total, n, raw, "wind", G(Col.WSA, Flag.Wind), 1,
                new[] { "gust", "dir" }, new[] { G(Col.WGH, Flag.Wind), G(Col.WDIR, Flag.Wind) }, new[] { 2, 3 });
            Group(w, ds, segs, total, n, raw, "rain", G(Col.PRATE, Flag.RainRate), 2,
                new[] { "tot" }, new[] { G(Col.PTOT, Flag.Rain) }, new[] { 2 });
            Group(w, ds, segs, total, n, raw, "sun", G(Col.SOL, Flag.Solar), 0,
                new[] { "uv" }, new[] { G(Col.UV, Flag.Uv) }, new[] { 2 });
            w.End();

            // gyanús pontok jelölése (csak ha nincsenek elrejtve)
            w.Key("flags").Obj();
            if (!clean)
            {
                FlagPoints(w, ds, i0, i1, "temp", Flag.Temp, i => ds.C[Col.TH][i]);
                FlagPoints(w, ds, i0, i1, "hum", Flag.Hum, i => ds.C[Col.HA][i]);
                FlagPoints(w, ds, i0, i1, "pres", Flag.Pres, i => (ds.C[Col.PMAX][i] + ds.C[Col.PMIN][i]) / 2.0);
                FlagPoints(w, ds, i0, i1, "wind", Flag.Wind, i => ds.C[Col.WGH][i]);
                FlagPoints(w, ds, i0, i1, "rain", Flag.Rain, i => ds.C[Col.PRATE][i]);
                FlagPoints(w, ds, i0, i1, "sun", Flag.Solar, i => ds.C[Col.SOL][i]);
                FlagPoints(w, ds, i0, i1, "uv", Flag.Uv & ~Flag.SOLAR, i => ds.C[Col.UV][i]);
            }
            w.End();

            // napi csapadék oszlopok a tartomány napjaira
            var days = clean ? ds.Days : ds.DaysRaw;
            w.Key("daily").Obj();
            var dsel = days.Where(d => d.End * 1000 > fromMs && d.Start * 1000 < toMs).ToList();
            w.Key("t").Arr(); foreach (var d in dsel) w.Long((d.Start + d.End) / 2 * 1000); w.EndArr();
            w.Key("rain").Arr(); foreach (var d in dsel) w.Num(d.Rain, 2); w.EndArr();
            w.Key("energy").Arr(); foreach (var d in dsel) w.Num(d.Energy, 2); w.EndArr();
            w.End();
            return w.End().ToString();
        }

        // agg: 0 = átlag, 1 = minimum, 2 = maximum, 3 = a kiválasztott pont értéke
        static void Group(JW w, Dataset ds, List<int[]> segs, int total, int n, bool raw, string name,
                          Getter primary, int dec, string[] names, Getter[] comp, int[] agg)
        {
            var t = new List<long>();
            var p = new List<double>();
            var cv = new List<double>[comp.Length];
            for (int c = 0; c < comp.Length; c++) cv[c] = new List<double>();
            var sel = new List<int>(); var bs = new List<int>(); var be = new List<int>();

            for (int si = 0; si < segs.Count; si++)
            {
                int a = segs[si][0], b = segs[si][1];
                if (si > 0)
                {
                    // hiány: null pont a két szakasz között, így a vonal megszakad
                    t.Add((ds.T[a - 1] + ds.T[a]) / 2 * 1000);
                    p.Add(double.NaN);
                    for (int c = 0; c < comp.Length; c++) cv[c].Add(double.NaN);
                }
                int len = b - a;
                int segN = raw ? len : Math.Max(3, (int)Math.Round((double)n * len / total));
                sel.Clear(); bs.Clear(); be.Clear();
                Lttb(ds.T, primary, a, b, segN, sel, bs, be);
                for (int k = 0; k < sel.Count; k++)
                {
                    int i = sel[k];
                    t.Add(ds.T[i] * 1000);
                    p.Add(primary(i));
                    for (int c = 0; c < comp.Length; c++)
                    {
                        double v = double.NaN, sum = 0; int cnt = 0;
                        if (agg[c] == 3) v = comp[c](i);
                        else
                        {
                            for (int j = bs[k]; j < be[k]; j++)
                            {
                                double x = comp[c](j);
                                if (double.IsNaN(x)) continue;
                                if (agg[c] == 0) { sum += x; cnt++; }
                                else if (double.IsNaN(v) || (agg[c] == 1 ? x < v : x > v)) v = x;
                            }
                            if (agg[c] == 0 && cnt > 0) v = sum / cnt;
                        }
                        cv[c].Add(v);
                    }
                }
            }

            w.Key(name).Obj();
            w.Key("t").Arr(); foreach (var x in t) w.Long(x); w.EndArr();
            w.Key("v").Arr(); foreach (var x in p) w.Num(x, dec); w.EndArr();
            for (int c = 0; c < comp.Length; c++)
            {
                w.Key(names[c]).Arr(); foreach (var x in cv[c]) w.Num(x, dec); w.EndArr();
            }
            w.End();
        }

        // Largest-Triangle-Three-Buckets ritkítás; a vödrök határait is visszaadja a kísérő értékek összesítéséhez
        static void Lttb(long[] T, Getter Y, int s, int e, int n, List<int> sel, List<int> bs, List<int> be)
        {
            int len = e - s;
            if (len <= 0) return;
            if (n >= len || n < 3)
            {
                for (int i = s; i < e; i++) { sel.Add(i); bs.Add(i); be.Add(i + 1); }
                return;
            }
            double every = (double)(len - 2) / (n - 2);
            int a = s;
            sel.Add(a); bs.Add(s); be.Add(s + 1);
            for (int i = 0; i < n - 2; i++)
            {
                int avgStart = s + 1 + (int)Math.Floor((i + 1) * every);
                int avgEnd = Math.Min(e, s + 1 + (int)Math.Floor((i + 2) * every));
                if (avgStart >= e) avgStart = e - 1;
                if (avgEnd <= avgStart) avgEnd = avgStart + 1;
                double ax = 0, ay = 0; int cnt = 0;
                for (int j = avgStart; j < avgEnd; j++)
                {
                    double y = Y(j);
                    if (double.IsNaN(y)) continue;
                    ax += T[j]; ay += y; cnt++;
                }
                if (cnt > 0) { ax /= cnt; ay /= cnt; } else { ax = T[avgStart]; ay = 0; }

                int rs = s + 1 + (int)Math.Floor(i * every);
                int re = Math.Min(e - 1, s + 1 + (int)Math.Floor((i + 1) * every));
                if (re <= rs) re = rs + 1;
                double px = T[a], py = Y(a);
                if (double.IsNaN(py)) py = ay;
                double maxArea = -1; int pick = rs;
                for (int j = rs; j < re; j++)
                {
                    double y = Y(j);
                    if (double.IsNaN(y)) continue;
                    double area = Math.Abs((px - ax) * (y - py) - (px - T[j]) * (ay - py));
                    if (area > maxArea) { maxArea = area; pick = j; }
                }
                sel.Add(pick); bs.Add(rs); be.Add(re);
                a = pick;
            }
            sel.Add(e - 1); bs.Add(e - 1); be.Add(e);
        }

        static void FlagPoints(JW w, Dataset ds, int i0, int i1, string name, int mask, Getter val)
        {
            var idx = new List<int>();
            for (int i = i0; i < i1; i++) if ((ds.Flags[i] & mask) != 0) idx.Add(i);
            const int cap = 400;
            w.Key(name).Arr();
            int step = Math.Max(1, (int)Math.Ceiling(idx.Count / (double)cap));
            for (int k = 0; k < idx.Count; k += step)
            {
                int i = idx[k];
                double v = val(i);
                if (double.IsNaN(v)) continue;
                w.Arr().Long(ds.T[i] * 1000).Num(v, 2).Long(ds.Flags[i] & mask).EndArr();
            }
            w.EndArr();
        }
    }
}
