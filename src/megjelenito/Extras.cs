using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Idojaras
{
    // A felület beállításai (téma, fülek, heti kártyák stb.) – appdata_megjelenítő\felulet.json
    static class UiSettings
    {
        static readonly object Lock = new object();
        static string FilePath { get { return Path.Combine(AppState.L.App, "felulet.json"); } }

        public static string Handle(string method, string body, out int status)
        {
            status = 200;
            lock (Lock)
            {
                if (method == "POST")
                {
                    try { new JavaScriptSerializer().DeserializeObject(body ?? ""); }
                    catch { status = 400; return "{\"error\":\"érvénytelen JSON\"}"; }
                    Guard.CreateDirectory(AppState.L.App);
                    string tmp = FilePath + ".tmp";
                    Guard.WriteAllText(tmp, body, new UTF8Encoding(false));
                    if (File.Exists(FilePath)) Guard.Replace(tmp, FilePath, null); else Guard.Move(tmp, FilePath);
                    return "{\"ok\":true}";
                }
                try
                {
                    if (File.Exists(FilePath))
                    {
                        string t = File.ReadAllText(FilePath, Encoding.UTF8);
                        new JavaScriptSerializer().DeserializeObject(t);
                        return t;
                    }
                }
                catch (Exception ex) { Program.Log("A felulet.json sérült, alapértékekkel indulok: " + ex.Message); }
                return "{}";
            }
        }

        // a felület nyelve (az exporthoz)
        public static string Lang()
        {
            try
            {
                int st;
                var d = new JavaScriptSerializer().DeserializeObject(Handle("GET", null, out st)) as System.Collections.Generic.Dictionary<string, object>;
                object v;
                if (d != null && d.TryGetValue("lang", out v) && v != null && Langs.Exists(Convert.ToString(v))) return Convert.ToString(v);
            }
            catch { }
            return "hu";
        }

        // a választott mértékegységek (units.temp/rain/wind/pres) – az exporthoz
        public static string Unit(string key, string def)
        {
            try
            {
                int st;
                var d = new JavaScriptSerializer().DeserializeObject(Handle("GET", null, out st)) as System.Collections.Generic.Dictionary<string, object>;
                object u;
                if (d != null && d.TryGetValue("units", out u) && u is System.Collections.Generic.Dictionary<string, object>)
                {
                    object v;
                    if (((System.Collections.Generic.Dictionary<string, object>)u).TryGetValue(key, out v) && v != null) return Convert.ToString(v);
                }
                if (key == "wind" && d != null && d.ContainsKey("windUnit")) return Convert.ToString(d["windUnit"]);
            }
            catch { }
            return def;
        }
    }

    // Egyszerű Excel-export: napi összesítő CSV a felület nyelvén és számformátumával (9.9)
    static class Export
    {
        static readonly CultureInfo Hu = CultureInfo.GetCultureInfo("hu-HU");

        public static string FileName()
        {
            return "idojaras_napi_osszesito_" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv";
        }

        static CultureInfo Ci = Hu;
        static string[] Dirs = { "É", "ÉÉK", "ÉK", "KÉK", "K", "KDK", "DK", "DDK", "D", "DDNy", "DNy", "NyDNy", "Ny", "NyÉNy", "ÉNy", "ÉÉNy" };

        static string N(double v, int dec)
        {
            return double.IsNaN(v) ? "" : Math.Round(v, dec).ToString(dec > 0 ? "0." + new string('0', dec) : "0", Ci);
        }

        static string Dir(double deg)
        {
            if (double.IsNaN(deg)) return "";
            return Dirs[(int)Math.Round(((deg % 360) + 360) % 360 / 22.5) % 16];
        }

        // a nyelv számformátuma és égtájai; tizedesvesszőnél „;”, tizedespontnál „,” az elválasztó (ahogy az adott nyelvű Excel várja)
        static string SetLang(string lang)
        {
            Ci = Hu;
            Dirs = new[] { "É", "ÉÉK", "ÉK", "KÉK", "K", "KDK", "DK", "DDK", "D", "DDNy", "DNy", "NyDNy", "Ny", "NyÉNy", "ÉNy", "ÉÉNy" };
            var m = Langs.Meta(lang);
            if (m != null)
            {
                object l, dirs;
                if (m.TryGetValue("locale", out l)) { try { Ci = CultureInfo.GetCultureInfo(Convert.ToString(l)); } catch { Ci = Hu; } }
                if (m.TryGetValue("dirs", out dirs) && dirs is object[] && ((object[])dirs).Length == 16) Dirs = ((object[])dirs).Select(x => Convert.ToString(x)).ToArray();
            }
            return Ci.NumberFormat.NumberDecimalSeparator == "," ? ";" : ",";
        }

        public static string DailyCsv(Dataset ds, bool raw = false)
        {
            var sb = new StringBuilder();
            if (ds == null) return "";
            string lang = UiSettings.Lang(), sep = SetLang(lang);
            Func<string, string> L = h => Langs.T(h, lang);
            // a felületen választott mértékegységekben (belül minden metrikus)
            bool f = UiSettings.Unit("temp", "C") == "F", inch = UiSettings.Unit("rain", "mm") == "in", inHg = UiSettings.Unit("pres", "hPa") == "inHg";
            string wu = UiSettings.Unit("wind", "kmh");
            string tl = f ? "°F" : "°C", rl = inch ? "in" : "mm", pl = inHg ? "inHg" : "hPa";
            string wl = wu == "kn" ? L("csomó") : wu == "ms" ? "m/s" : wu == "mph" ? "mph" : wu == "bft" ? "Bft" : "km/h";
            Func<double, double> T = c => f ? c * 9 / 5 + 32 : c;
            Func<double, double> TD = c => f ? c * 9 / 5 : c;
            Func<double, double> R = mm => inch ? mm / 25.4 : mm;
            Func<double, double> P = h => inHg ? h * 0.02953 : h;
            Func<double, double> W = k => double.IsNaN(k) ? k : wu == "kn" ? k / 1.852 : wu == "ms" ? k / 3.6 : wu == "mph" ? k / 1.609344 : wu == "bft" ? Math.Min(12, Math.Pow(Math.Max(0, k / 3.6) / 0.836, 2.0 / 3)) : k;
            int rd = inch ? 1 : 0, pd = inHg ? 2 : 1, wd = wu == "bft" ? 2 : 1;
            string[] head =
            {
                "Dátum", "Hét napja", "Lefedettség (%)", "Mérések", "Min. hőmérséklet (" + tl + ")", "Min. időpont", "Max. hőmérséklet (" + tl + ")", "Max. időpont",
                "Átlaghőmérséklet (" + tl + ")", "Hőingás (" + tl + ")", "Átl. harmatpont (" + tl + ")", "Min. pára (%)", "Max. pára (%)", "Átl. pára (%)",
                "Min. légnyomás (" + pl + ")", "Max. légnyomás (" + pl + ")", "Átl. légnyomás (" + pl + ")", "Átlagszél (" + wl + ")", "Max. szél (" + wl + ")", "Max. lökés (" + wl + ")",
                "Lökés időpontja", "Uralkodó szélirány", "Szélirány (°)", "Csapadék (" + rl + ")", "Max. intenzitás (" + rl + "/h)", "Napsütéses órák (h)",
                "Napenergia (kWh/m²)", "Max. napsugárzás (W/m²)", "Max. UV"
            };
            sb.Append(string.Join(sep, head.Select(h => L(h)))).Append("\r\n");
            Func<long, string> time = t => t == 0 ? "" : Tz.Local(t).ToString("HH:mm");
            // alapból a tisztított adat (a gyanús mérések nélkül), kérésre a nyers
            foreach (var d in raw ? ds.DaysRaw : ds.Days)
            {
                var row = new[]
                {
                    d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Ci.DateTimeFormat.GetDayName(d.Date.DayOfWeek), N(d.Coverage * 100, 0), d.Count.ToString(),
                    N(T(d.Tmin), 1), time(d.TminT), N(T(d.Tmax), 1), time(d.TmaxT), N(T(d.Tavg), 1), N(TD(d.Tmax - d.Tmin), 1), N(T(d.DewAvg), 1),
                    N(d.HumMin, 0), N(d.HumMax, 0), N(d.HumAvg, 0), N(P(d.PresMin), pd), N(P(d.PresMax), pd), N(P(d.PresAvg), pd),
                    N(W(d.WindAvg), wd), N(W(d.WindMax), 1), N(W(d.GustMax), 1), time(d.GustMaxT), Dir(d.WindDir), N(d.WindDir, 0),
                    N(R(d.Rain), 2 + rd), N(R(d.RainRateMax), 1 + rd), N(d.SunHours, 1), N(d.Energy, 2), N(d.SolarMax, 0), N(d.UvMax, 0)
                };
                sb.Append(string.Join(sep, row)).Append("\r\n");
            }
            return sb.ToString();
        }

        // ablakos módban mentés-párbeszédablakkal; böngészős módban a felület letöltésként kéri le
        public static string SaveWithDialog(Dataset ds, bool raw = false)
        {
            var win = Program.Window;
            if (win == null || win.IsDisposed) return "{\"download\":true}";
            string csv = DailyCsv(ds, raw);
            string saved = null, error = null;
            win.Invoke((Action)(() =>
            {
                using (var d = new SaveFileDialog
                {
                    Title = "Napi összesítő mentése (Excelhez)",
                    Filter = "CSV (Excel) (*.csv)|*.csv",
                    FileName = FileName(),
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                })
                {
                    if (d.ShowDialog(win) != DialogResult.OK) return;
                    try { File.WriteAllText(d.FileName, csv, new UTF8Encoding(true)); saved = d.FileName; } // kivétel: a felhasználó maga választotta a helyet
                    catch (Exception ex) { error = ex.Message; }
                }
            }));
            var w = new JW().Obj();
            if (saved != null) w.P("saved", true).P("path", saved).P("rows", ds.Days.Count);
            else if (error != null) w.P("saved", false).P("error", "A fájl nem menthető: " + error);
            else w.P("saved", false).P("cancelled", true);
            return w.End().ToString();
        }
    }
}
