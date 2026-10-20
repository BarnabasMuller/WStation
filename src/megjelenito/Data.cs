using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Idojaras
{
    // A részletes mérések oszlopai (a WU_Letolto idojaras_adatok.csv-je szerint)
    static class Col
    {
        public static readonly string[] Names =
        {
            "tempHigh", "tempLow", "tempAvg", "dewptHigh", "dewptLow", "dewptAvg",
            "humidityHigh", "humidityLow", "humidityAvg",
            "windspeedHigh", "windspeedLow", "windspeedAvg", "windgustHigh", "windgustLow", "windgustAvg",
            "windchillHigh", "windchillLow", "windchillAvg", "heatindexHigh", "heatindexLow", "heatindexAvg",
            "pressureMax", "pressureMin", "pressureTrend", "precipRate", "precipTotal",
            "solarRadiationHigh", "uvHigh", "winddirAvg"
        };
        public const int TH = 0, TL = 1, TA = 2, DH = 3, DL = 4, DA = 5, HH = 6, HL = 7, HA = 8,
            WSH = 9, WSL = 10, WSA = 11, WGH = 12, WGL = 13, WGA = 14, WCH = 15, WCL = 16, WCA = 17,
            HIH = 18, HIL = 19, HIA = 20, PMAX = 21, PMIN = 22, PTR = 23, PRATE = 24, PTOT = 25,
            SOL = 26, UV = 27, WDIR = 28;
        public const int Count = 29;
    }

    // Minőségjelzők (bitmaszk). Semmit nem törlünk, csak jelölünk.
    static class Flag
    {
        public const int QC0 = 1, QCNEG = 2, UV = 4, TEMP = 8, HUM = 16, PRES = 32, RAIN = 64, WIND = 128,
                         SPIKE = 256, SUN = 512, RESTART = 1024, SOLAR = 2048, RAINJUMP = 4096, WINDFROZEN = 8192;
        public const int Common = QC0 | QCNEG | RESTART;
        // mely jelzők miatt hagyjuk ki az adott mennyiséget "tiszta" számításnál
        public const int Temp = Common | TEMP | SPIKE | SUN;
        public const int Dew = Common | TEMP | SPIKE | HUM;
        public const int Hum = Common | HUM;
        public const int Pres = Common | PRES;
        public const int Wind = Common | WIND | WINDFROZEN;
        // a csapadékösszeg (halmozott érték) a csapadékugrásnál is megmarad; csak az intenzitás (rekord, felhőszakadás) esik ki
        public const int Rain = Common | RAIN;
        public const int RainRate = Common | RAIN | RAINJUMP;
        public const int Solar = Common | SOLAR;
        public const int Uv = Common | UV | SOLAR;

        public static readonly int[] Bits = { QC0, QCNEG, UV, TEMP, HUM, PRES, RAIN, WIND, SPIKE, SUN, RESTART, SOLAR, RAINJUMP, WINDFROZEN };
        public static readonly string[] Texts =
        {
            "A WU minőségellenőrzése bizonytalannak jelölte (qcStatus = 0)",
            "A WU minőségellenőrzése hibásnak jelölte (qcStatus = -1)",
            "Lehetetlen UV-érték",
            "Lehetetlen hőmérséklet",
            "Lehetetlen páratartalom",
            "Lehetetlen légnyomás",
            "Lehetetlen csapadékérték",
            "Lehetetlen szélérték",
            "Hirtelen hőmérséklet-ugrás (tüske)",
            "Napsütés miatti túlmelegedés gyanúja",
            "Első mérés egy hosszabb kiesés után",
            "Lehetetlen napsugárzás",
            "Csapadékugrás (túl sok eső pár perc alatt, vagy egyszerre elolvadt hó)",
            "Befagyott szélmérő gyanúja (órákig pontosan 0 szél fagyban)"
        };
        public static readonly string[] Codes = { "qc0", "qc-1", "uv", "temp", "hum", "pres", "rain", "wind", "spike", "sun", "restart", "solar", "rainjump", "windfrozen" };

        public static string Describe(int f)
        {
            var parts = new List<string>();
            for (int i = 0; i < Bits.Length; i++) if ((f & Bits[i]) != 0) parts.Add(Texts[i]);
            return string.Join("; ", parts);
        }
    }

    class Gap
    {
        public long From, To; // az utolsó mérés előtte, az első utána (epoch mp)
        public int Index;      // az első mérés indexe a hiány után
    }

    class DayStat
    {
        public const double FullCoverage = 0.8;   // ennyi lefedettség fölött „teljes” a nap: rangsorok, darabszámok, átlagok (8.6)
        public int Day;                 // napszám (2000-01-01 = 0), helyi idő szerint
        public DateTime Date;
        public long Start, End;         // a helyi nap határai UTC epochban
        public int Count, First, Last;  // mérések és indexek [First, Last]
        public double Coverage;         // 0..1
        public double Tmin = double.NaN, Tmax = double.NaN, Tavg = double.NaN;
        public long TminT, TmaxT;
        public double DewAvg = double.NaN, HumMin = double.NaN, HumMax = double.NaN, HumAvg = double.NaN;
        public double PresMin = double.NaN, PresMax = double.NaN, PresAvg = double.NaN;
        public double PresFirst = double.NaN, PresLast = double.NaN;   // a nap első és utolsó érvényes nyomásértéke
        public double WindAvg = double.NaN, WindMax = double.NaN, GustMax = double.NaN, WindDir = double.NaN;
        public long GustMaxT;
        public double Rain = double.NaN, RainRateMax = double.NaN;
        public long RainRateMaxT;
        public double SolarMax = double.NaN, Energy = double.NaN, UvMax = double.NaN;
        public double SunHours = double.NaN;  // WMO: azok az órák, amikor a napsugárzás >= 120 W/m²
        public double VpdMax = double.NaN, VpdDay = double.NaN;  // páranyomás-hiány (kPa): napi max, nappali (8–18 h) átlag
    }

    class MonthStat
    {
        public int Year, Month, Days;
        public double Tavg = double.NaN, Tmin = double.NaN, Tmax = double.NaN, TmaxAvg = double.NaN, TminAvg = double.NaN;
        public double Rain, Energy, GustMax = double.NaN;
        public int HotDays, FrostDays, TropicalNights, IceDays, RainDays;
    }

    class HourStat
    {
        public long T; // az óra kezdete (epoch mp)
        public int Count;
        public double Tavg = double.NaN, Tmin = double.NaN, Tmax = double.NaN, Hum = double.NaN, Pres = double.NaN,
                      Wind = double.NaN, Gust = double.NaN, Rain = 0, Solar = double.NaN;
    }

    class Current
    {
        public long T;
        public string Source = "";
        public double Temp = double.NaN, HeatIndex = double.NaN, WindChill = double.NaN, Dew = double.NaN, Hum = double.NaN,
                      Wind = double.NaN, Gust = double.NaN, Dir = double.NaN, Pres = double.NaN, PrecipRate = double.NaN,
                      PrecipTotal = double.NaN, Solar = double.NaN, Uv = double.NaN;
    }

    class Dataset
    {
        public string Dir = "", Station = "";
        public string Neighborhood = "";   // a WU szerinti hely (a pillanatnyi állapotból), a hely nevének alapértéke
        public double Lat = double.NaN, Lon = double.NaN;
        public int N;
        public long[] T;
        public float[][] C;
        public sbyte[] Qc;
        public int[] Flags;
        public int[] Day;
        public byte[] Hour;
        public List<Gap> Gaps = new List<Gap>();
        public List<DayStat> Days = new List<DayStat>(), DaysRaw = new List<DayStat>();
        public Dictionary<int, DayStat> DayMap = new Dictionary<int, DayStat>(), DayMapRaw = new Dictionary<int, DayStat>();
        public List<MonthStat> Months = new List<MonthStat>();
        public List<HourStat> Hours = new List<HourStat>();
        public Current Now;
        public List<string> Files = new List<string>();
        public List<string> Warnings = new List<string>();
        public bool FromCache;
        public int ParsedRows, NewRows, DuplicateRows;
        public long LoadMs;
        public DateTime LoadedAt = DateTime.Now;
        public int[] FlagCounts = new int[Flag.Bits.Length];
        public int FlaggedRows;
        public float[] RawPtot, RawPrate;   // nyers (kalibrálatlan) csapadék
        public Calib.Result CalibRes;

        public double V(int col, int i) { return C[col][i]; }

        // az első index, ahol T >= t
        public int LowerBound(long t)
        {
            int lo = 0, hi = N;
            while (lo < hi) { int m = (lo + hi) >> 1; if (T[m] < t) lo = m + 1; else hi = m; }
            return lo;
        }
    }

    static class Tz
    {
        // a helyi időzóna (Beállítások › Helyszín, megjelenito.ini Idozona; alapból Europe/Budapest)
        public static TimeZoneInfo Zone = Find("Central Europe Standard Time");
        public static string Iana = "Europe/Budapest";
        public static readonly DateTime Base = new DateTime(2000, 1, 1);
        static readonly DateTime Unix = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // a választható időzónák: IANA (a felületnek) ↔ Windows-azonosító (a .NET-nek)
        public static readonly string[][] Zones =
        {
            new[] { "Europe/Budapest", "Central Europe Standard Time" }, new[] { "Europe/Vienna", "W. Europe Standard Time" },
            new[] { "Europe/Berlin", "W. Europe Standard Time" }, new[] { "Europe/Rome", "W. Europe Standard Time" },
            new[] { "Europe/Zurich", "W. Europe Standard Time" }, new[] { "Europe/Amsterdam", "W. Europe Standard Time" },
            new[] { "Europe/Prague", "Central Europe Standard Time" }, new[] { "Europe/Bratislava", "Central Europe Standard Time" },
            new[] { "Europe/Belgrade", "Central Europe Standard Time" }, new[] { "Europe/Ljubljana", "Central Europe Standard Time" },
            new[] { "Europe/Warsaw", "Central European Standard Time" }, new[] { "Europe/Zagreb", "Central European Standard Time" },
            new[] { "Europe/Paris", "Romance Standard Time" }, new[] { "Europe/Madrid", "Romance Standard Time" },
            new[] { "Europe/Brussels", "Romance Standard Time" }, new[] { "Europe/London", "GMT Standard Time" },
            new[] { "Europe/Lisbon", "GMT Standard Time" }, new[] { "Atlantic/Canary", "GMT Standard Time" },
            new[] { "Europe/Bucharest", "GTB Standard Time" }, new[] { "Europe/Athens", "GTB Standard Time" },
            new[] { "Europe/Helsinki", "FLE Standard Time" }, new[] { "Europe/Kiev", "FLE Standard Time" },
            new[] { "Europe/Istanbul", "Turkey Standard Time" }, new[] { "Europe/Moscow", "Russian Standard Time" },
            new[] { "America/New_York", "Eastern Standard Time" }, new[] { "America/Toronto", "Eastern Standard Time" },
            new[] { "America/Chicago", "Central Standard Time" }, new[] { "America/Mexico_City", "Central Standard Time (Mexico)" },
            new[] { "America/Denver", "Mountain Standard Time" }, new[] { "America/Phoenix", "US Mountain Standard Time" },
            new[] { "America/Los_Angeles", "Pacific Standard Time" }, new[] { "America/Anchorage", "Alaskan Standard Time" },
            new[] { "Pacific/Honolulu", "Hawaiian Standard Time" }, new[] { "America/Sao_Paulo", "E. South America Standard Time" },
            new[] { "America/Argentina/Buenos_Aires", "Argentina Standard Time" }, new[] { "Africa/Johannesburg", "South Africa Standard Time" },
            new[] { "Asia/Dubai", "Arabian Standard Time" }, new[] { "Asia/Kolkata", "India Standard Time" },
            new[] { "Asia/Shanghai", "China Standard Time" }, new[] { "Asia/Tokyo", "Tokyo Standard Time" },
            new[] { "Australia/Sydney", "AUS Eastern Standard Time" }, new[] { "Pacific/Auckland", "New Zealand Standard Time" },
            new[] { "UTC", "UTC" }
        };

        public static string WindowsId(string iana)
        {
            foreach (var z in Zones) if (string.Equals(z[0], iana, StringComparison.OrdinalIgnoreCase)) return z[1];
            return null;
        }

        // false, ha ismeretlen az időzóna (ilyenkor marad az előző)
        public static bool Set(string iana)
        {
            string win = WindowsId(iana);
            if (win == null) return false;
            try
            {
                Zone = TimeZoneInfo.FindSystemTimeZoneById(win);
                foreach (var z in Zones) if (string.Equals(z[0], iana, StringComparison.OrdinalIgnoreCase)) Iana = z[0];
                return true;
            }
            catch { return false; }
        }

        static TimeZoneInfo Find(string id)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { return TimeZoneInfo.Local; }
        }

        public static DateTime Local(long epoch) { return TimeZoneInfo.ConvertTimeFromUtc(Unix.AddSeconds(epoch), Zone); }
        public static int DayNumber(DateTime local) { return (int)(local.Date - Base).TotalDays; }
        public static DateTime DateOf(int day) { return Base.AddDays(day); }

        // a helyi nap éjfélje UTC epochban
        public static long LocalMidnightEpoch(DateTime localDate)
        {
            var d = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);
            return (long)(TimeZoneInfo.ConvertTimeToUtc(d, Zone) - Unix).TotalSeconds;
        }
    }

    // ------------------------------------------------------------------ betöltés + cache

    static class Loader
    {
        const int CacheVersion = 4;   // 4: soronkénti forrásfájl, állomás-koordináták
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        class RawRow
        {
            public long T;
            public sbyte Qc;
            public float[] V;
            public int Prio;
            public short Src;   // a forrásfájl indexe a manifesztben
            public string St;
        }

        class FileInfoRec
        {
            public string Name;
            public long Length;
            public long Ticks;
        }

        public static Dataset Load(Layout L, Settings s, Action<string> progress)
        {
            var sw = Stopwatch.StartNew();
            string dir = L.Data;
            var ds = new Dataset { Dir = dir };
            if (!Directory.Exists(dir)) return null;
            ds.Warnings.AddRange(s.Warnings);

            var obsFiles = new List<string>();
            var curFiles = new List<string>();
            foreach (var f in Directory.GetFiles(dir, "*.csv", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                string kind = Classify(f);
                if (kind == "obs") obsFiles.Add(f);
                else if (kind == "cur") curFiles.Add(f);
            }
            if (obsFiles.Count == 0) return null;

            var manifest = obsFiles.Select(f =>
            {
                var fi = new FileInfo(f);
                return new FileInfoRec { Name = fi.Name, Length = fi.Length, Ticks = fi.LastWriteTimeUtc.Ticks };
            }).ToList();
            ds.Files = manifest.Select(m => m.Name).ToList();

            string cacheDir = L.Cache;
            string cachePath = Path.Combine(cacheDir, "meresek.bin");
            List<FileInfoRec> cachedManifest;
            string cachedStation;
            double cachedLat, cachedLon;
            RawRow[] cached = ReadCache(cachePath, out cachedManifest, out cachedStation, out cachedLat, out cachedLon, ds);

            string wantStation = s.StationId;
            bool stationOk = cached != null && (wantStation.Length == 0 || string.Equals(cachedStation, wantStation, StringComparison.OrdinalIgnoreCase));
            List<RawRow> rows;
            string station = cachedStation;
            // azonos időpontnál az újabb fájl nyer: prioritás = a fájl helye módosítási idő szerint
            var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            { int k = 1; foreach (var m in manifest.OrderBy(x => x.Ticks)) rank[m.Name] = k++; }
            int cachedUniq = 0;

            if (stationOk && SameManifest(cachedManifest, manifest))
            {
                rows = cached.ToList();
                foreach (var r in rows) r.Prio = rank[manifest[r.Src].Name];
                ds.FromCache = true;
            }
            else
            {
                // csak a megváltozott / új fájlokat dolgozzuk fel; a cache-ből csak a változatlan fájlok sorai maradnak meg
                // (így egy CSV-ből kitörölt sor a cache-ből is eltűnik); ha fájl tűnt el, teljes újraépítés
                bool anyRemoved = cachedManifest != null && cachedManifest.Any(c => !manifest.Any(m => m.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase)));
                bool incremental = stationOk && !anyRemoved;
                Func<FileInfoRec, bool> unchanged = m => cachedManifest != null && cachedManifest.Any(c => c.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase) && c.Length == m.Length && c.Ticks == m.Ticks);
                var toParse = manifest.Where(m => !incremental || !unchanged(m)).ToList();

                var parsed = new List<RawRow>();
                var stationCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in toParse.OrderBy(x => x.Ticks))
                {
                    if (progress != null) progress("Beolvasás: " + m.Name);
                    int from = parsed.Count;
                    ParseObsFile(Path.Combine(dir, m.Name), rank[m.Name], parsed, stationCount, ds);
                    short src = (short)manifest.IndexOf(m);
                    for (int i = from; i < parsed.Count; i++) parsed[i].Src = src;
                }
                ds.ParsedRows = parsed.Count;

                // állomás: a letöltő beállítása, különben a leggyakoribb
                if (!incremental || string.IsNullOrEmpty(station))
                    station = wantStation.Length > 0 ? wantStation
                        : stationCount.Count > 0 ? stationCount.OrderByDescending(kv => kv.Value).First().Key : "";
                if (stationCount.Count > 1)
                    ds.Warnings.Add("A CSV-kben több állomás adata is van, csak ezt mutatom: " + station);

                rows = new List<RawRow>();
                if (stationOk) cachedUniq = cached.Select(r => r.T).Distinct().Count();
                if (incremental)
                {
                    // a cache sorainak forrásindexe a régi manifeszt szerint van: átszámozzuk az újra
                    var keepIdx = new Dictionary<int, short>();
                    for (int i = 0; i < cachedManifest.Count; i++)
                    {
                        var m = manifest.FirstOrDefault(x => x.Name.Equals(cachedManifest[i].Name, StringComparison.OrdinalIgnoreCase));
                        if (m != null && unchanged(m)) keepIdx[i] = (short)manifest.IndexOf(m);
                    }
                    foreach (var r in cached)
                    {
                        short ni;
                        if (!keepIdx.TryGetValue(r.Src, out ni)) continue;
                        r.Src = ni; r.Prio = rank[manifest[ni].Name];
                        rows.Add(r);
                    }
                    if (double.IsNaN(ds.Lat)) { ds.Lat = cachedLat; ds.Lon = cachedLon; }
                }
                foreach (var r in parsed)
                    if (station.Length == 0 || string.Equals(r.St, station, StringComparison.OrdinalIgnoreCase)) rows.Add(r);
                if (rows.Count == 0 && parsed.Count > 0)
                    ds.Warnings.Add("A beallitasok.ini állomása (" + station + ") nem szerepel a CSV-kben.");
            }
            if (ds.FromCache) { ds.Lat = cachedLat; ds.Lon = cachedLon; }

            ds.Station = station ?? "";

            // rendezés + duplikátumszűrés epoch szerint: azonos epochnál a nagyobb prioritás (újabb fájl) nyer
            rows.Sort((a, b) => a.T != b.T ? a.T.CompareTo(b.T) : b.Prio.CompareTo(a.Prio));
            var uniq = new List<RawRow>(rows.Count);
            int dup = 0;
            foreach (var r in rows)
            {
                if (uniq.Count > 0 && uniq[uniq.Count - 1].T == r.T) { dup++; continue; }
                uniq.Add(r);
            }
            ds.DuplicateRows = dup;
            if (!ds.FromCache) ds.NewRows = Math.Max(0, uniq.Count - cachedUniq);

            ds.N = uniq.Count;
            ds.T = new long[ds.N];
            ds.Qc = new sbyte[ds.N];
            ds.C = new float[Col.Count][];
            for (int c = 0; c < Col.Count; c++) ds.C[c] = new float[ds.N];
            for (int i = 0; i < ds.N; i++)
            {
                var r = uniq[i];
                ds.T[i] = r.T;
                ds.Qc[i] = r.Qc;
                for (int c = 0; c < Col.Count; c++) ds.C[c][i] = r.V[c];
                r.Prio = 0;
            }

            if (!ds.FromCache)
            {
                try
                {
                    Guard.CreateDirectory(cacheDir);
                    WriteCache(cachePath, manifest, ds.Station, ds.Lat, ds.Lon, rows);   // duplikátumszűrés előtt: fájlonként cserélhető
                }
                catch (Exception ex) { ds.Warnings.Add("A cache nem menthető (" + ex.Message + "), a program enélkül is működik."); }
            }

            if (progress != null) progress("Feldolgozás...");
            Process(ds, s);
            ds.Now = BuildCurrent(ds, curFiles);
            ds.LoadMs = sw.ElapsedMilliseconds;
            ds.LoadedAt = DateTime.Now;
            return ds;
        }

        // fejléc alapján: "obs" részletes mérés, "cur" pillanatnyi állapot, null egyéb
        public static string Classify(string path)
        {
            try
            {
                string header;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                    header = sr.ReadLine();
                if (header == null) return null;
                header = header.TrimStart('﻿');
                char d = Delim(header);
                var cols = new HashSet<string>(header.Split(d).Select(x => x.Trim().Trim('"')), StringComparer.OrdinalIgnoreCase);
                if (cols.Contains("datum")) return null; // napi összesítő
                if (cols.Contains("letoltve") && cols.Contains("temp") && cols.Contains("epoch")) return "cur";
                if (cols.Contains("stationID") && cols.Contains("epoch") && cols.Contains("obsTimeLocal") && cols.Contains("tempAvg")) return "obs";
            }
            catch { }
            return null;
        }

        static char Delim(string header)
        {
            int sc = header.Count(c => c == ';'), cc = header.Count(c => c == ','), tc = header.Count(c => c == '\t');
            if (tc > sc && tc > cc) return '\t';
            return sc >= cc ? ';' : ',';
        }

        static string ReadAllShared(string path)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                        return sr.ReadToEnd();
                }
                catch (IOException)
                {
                    if (attempt >= 3) throw;
                    System.Threading.Thread.Sleep(1000);
                }
            }
        }

        static void ParseObsFile(string path, int prio, List<RawRow> outRows, Dictionary<string, int> stationCount, Dataset ds)
        {
            string text;
            try { text = ReadAllShared(path); }
            catch (Exception ex) { ds.Warnings.Add(Path.GetFileName(path) + " nem olvasható: " + ex.Message); return; }
            text = text.TrimStart('﻿');
            int nl = text.IndexOf('\n');
            if (nl < 0) return;
            string header = text.Substring(0, nl).TrimEnd('\r');
            char d = Delim(header);
            bool decComma = d != ',';
            var hcols = SplitLine(header, d);
            var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < hcols.Count; i++) idx[hcols[i].Trim()] = i;
            int iEpoch = idx["epoch"], iSt = idx.ContainsKey("stationID") ? idx["stationID"] : -1;
            int iQc = idx.ContainsKey("qcStatus") ? idx["qcStatus"] : -1;
            int iLat = idx.ContainsKey("lat") ? idx["lat"] : -1, iLon = idx.ContainsKey("lon") ? idx["lon"] : -1;
            var map = new int[Col.Count];
            for (int c = 0; c < Col.Count; c++) { int k; map[c] = idx.TryGetValue(Col.Names[c], out k) ? k : -1; }

            string want = ds.Station; // üres: még nem tudjuk
            int bad = 0, pos = nl + 1;
            while (pos < text.Length)
            {
                int end = text.IndexOf('\n', pos);
                if (end < 0) end = text.Length;
                string line = text.Substring(pos, end - pos).TrimEnd('\r');
                pos = end + 1;
                if (line.Length == 0) continue;
                var f = line.IndexOf('"') >= 0 ? SplitLine(line, d) : new List<string>(line.Split(d));
                long t;
                if (f.Count <= iEpoch || !long.TryParse(f[iEpoch].Trim(), NumberStyles.Integer, Inv, out t)) { bad++; continue; }
                string st = iSt >= 0 && iSt < f.Count ? f[iSt].Trim() : "";
                int cnt;
                stationCount.TryGetValue(st, out cnt);
                stationCount[st] = cnt + 1;
                var r = new RawRow { T = t, Prio = prio, V = new float[Col.Count], Qc = 1 };
                for (int c = 0; c < Col.Count; c++)
                {
                    int k = map[c];
                    r.V[c] = k >= 0 && k < f.Count ? ParseF(f[k], decComma) : float.NaN;
                }
                if (iQc >= 0 && iQc < f.Count)
                {
                    int q;
                    if (int.TryParse(f[iQc].Trim(), NumberStyles.Integer, Inv, out q)) r.Qc = (sbyte)Math.Max(-1, Math.Min(1, q));
                }
                if (double.IsNaN(ds.Lat) && iLat >= 0 && iLat < f.Count) { ds.Lat = ParseF(f[iLat], decComma); ds.Lon = ParseF(f[iLon], decComma); }
                r.St = st;
                outRows.Add(r);
            }
            if (bad > 0) ds.Warnings.Add(Path.GetFileName(path) + ": " + bad + " értelmezhetetlen sor kihagyva.");
        }

        static float ParseF(string s, bool decComma)
        {
            s = s.Trim();
            if (s.Length == 0) return float.NaN;
            if (decComma) s = s.Replace(',', '.');
            double v;
            return double.TryParse(s, NumberStyles.Float, Inv, out v) ? (float)v : float.NaN;
        }

        static List<string> SplitLine(string line, char d)
        {
            var res = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q)
                {
                    if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; }
                    else sb.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == d) { res.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            res.Add(sb.ToString());
            return res;
        }

        static bool SameManifest(List<FileInfoRec> a, List<FileInfoRec> b)
        {
            if (a == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                var x = b.FirstOrDefault(m => m.Name.Equals(a[i].Name, StringComparison.OrdinalIgnoreCase));
                if (x == null || x.Length != a[i].Length || x.Ticks != a[i].Ticks) return false;
            }
            return true;
        }

        static RawRow[] ReadCache(string path, out List<FileInfoRec> manifest, out string station, out double lat, out double lon, Dataset ds)
        {
            manifest = null; station = null; lat = double.NaN; lon = double.NaN;
            if (!File.Exists(path)) return null;
            try
            {
                using (var br = new BinaryReader(File.OpenRead(path), Encoding.UTF8))
                {
                    if (br.ReadInt32() != 0x4A4F4449 || br.ReadInt32() != CacheVersion) return null;
                    station = br.ReadString();
                    lat = br.ReadDouble(); lon = br.ReadDouble();
                    int mc = br.ReadInt32();
                    manifest = new List<FileInfoRec>();
                    for (int i = 0; i < mc; i++) manifest.Add(new FileInfoRec { Name = br.ReadString(), Length = br.ReadInt64(), Ticks = br.ReadInt64() });
                    int n = br.ReadInt32(), cols = br.ReadInt32();
                    if (cols != Col.Count) return null;
                    var T = ReadLongs(br, n);
                    var qc = br.ReadBytes(n);
                    var srcb = br.ReadBytes(n * 2);
                    if (srcb.Length != n * 2) throw new EndOfStreamException();
                    var src = new short[n]; Buffer.BlockCopy(srcb, 0, src, 0, srcb.Length);
                    var data = new float[cols][];
                    for (int c = 0; c < cols; c++) data[c] = ReadFloats(br, n);
                    var rows = new RawRow[n];
                    for (int i = 0; i < n; i++)
                    {
                        var v = new float[cols];
                        for (int c = 0; c < cols; c++) v[c] = data[c][i];
                        rows[i] = new RawRow { T = T[i], Qc = (sbyte)qc[i], V = v, Prio = 0, Src = src[i] };
                        if (src[i] < 0 || src[i] >= mc) return null;
                    }
                    return rows;
                }
            }
            catch (Exception ex)
            {
                ds.Warnings.Add("A cache sérült, újraépítem (" + ex.Message + ").");
                manifest = null; station = null;
                return null;
            }
        }

        static void WriteCache(string path, List<FileInfoRec> manifest, string station, double lat, double lon, List<RawRow> rows)
        {
            string tmp = path + ".tmp";
            using (var bw = new BinaryWriter(Guard.Create(tmp), Encoding.UTF8))
            {
                bw.Write(0x4A4F4449); bw.Write(CacheVersion);
                bw.Write(station ?? "");
                bw.Write(lat); bw.Write(lon);
                bw.Write(manifest.Count);
                foreach (var m in manifest) { bw.Write(m.Name); bw.Write(m.Length); bw.Write(m.Ticks); }
                int n = rows.Count;
                bw.Write(n); bw.Write(Col.Count);
                var T = new long[n];
                var qc = new byte[n];
                for (int i = 0; i < n; i++) { T[i] = rows[i].T; qc[i] = (byte)rows[i].Qc; }
                var tb = new byte[n * 8]; Buffer.BlockCopy(T, 0, tb, 0, tb.Length); bw.Write(tb);
                bw.Write(qc);
                var src = new short[n];
                for (int i = 0; i < n; i++) src[i] = rows[i].Src;
                var sb2 = new byte[n * 2]; Buffer.BlockCopy(src, 0, sb2, 0, sb2.Length); bw.Write(sb2);
                var col = new float[n];
                var cb = new byte[n * 4];
                for (int c = 0; c < Col.Count; c++)
                {
                    for (int i = 0; i < n; i++) col[i] = rows[i].V[c];
                    Buffer.BlockCopy(col, 0, cb, 0, cb.Length);
                    bw.Write(cb);
                }
            }
            if (File.Exists(path)) Guard.Replace(tmp, path, null); else Guard.Move(tmp, path);
        }

        static long[] ReadLongs(BinaryReader br, int n)
        {
            var b = br.ReadBytes(n * 8);
            if (b.Length != n * 8) throw new EndOfStreamException();
            var r = new long[n]; Buffer.BlockCopy(b, 0, r, 0, b.Length); return r;
        }

        static float[] ReadFloats(BinaryReader br, int n)
        {
            var b = br.ReadBytes(n * 4);
            if (b.Length != n * 4) throw new EndOfStreamException();
            var r = new float[n]; Buffer.BlockCopy(b, 0, r, 0, b.Length); return r;
        }

        // ------------------------------------------------------------------ feldolgozás

        // a napi lefedettségben ennél hosszabb szünet hiány (a „HianyPerc” beállítás, 13.5 – korábban fixen 15 perc)
        static long gapSec = 900;

        public static void Process(Dataset ds, Settings s)
        {
            gapSec = Math.Max(300, s.GapMinutes * 60L);
            int n = ds.N;
            ds.Day = new int[n];
            ds.Hour = new byte[n];
            ds.Flags = new int[n];
            for (int i = 0; i < n; i++)
            {
                var loc = Tz.Local(ds.T[i]);
                ds.Day[i] = Tz.DayNumber(loc);
                ds.Hour[i] = (byte)loc.Hour;
            }
            Calib.Apply(ds, s);   // csapadékmérő-kalibráció (a nyers érték megmarad)
            ComputeFlags(ds, s);
            ComputeGaps(ds, s);
            ds.DaysRaw = ComputeDays(ds, false);
            ds.Days = ComputeDays(ds, true);
            ds.DayMap = ds.Days.ToDictionary(d => d.Day);
            ds.DayMapRaw = ds.DaysRaw.ToDictionary(d => d.Day);
            ds.Months = ComputeMonths(ds, s);
            ds.Hours = ComputeHours(ds);
        }

        static bool Bad(float v, double min, double max) { return !float.IsNaN(v) && (v < min || v > max); }

        // 30 perces átlagszél minden mérés körül (a napsütés miatti túlmelegedés szabályához: a pillanatnyi szél félrevezet)
        static double[] WindAvg30(Dataset ds)
        {
            var res = new double[ds.N];
            int a = 0, z = 0; double sum = 0; int cnt = 0;
            for (int i = 0; i < ds.N; i++)
            {
                while (z < ds.N && ds.T[z] <= ds.T[i] + 900) { float w = ds.C[Col.WSA][z]; if (!float.IsNaN(w)) { sum += w; cnt++; } z++; }
                while (a < z && ds.T[a] < ds.T[i] - 900) { float w = ds.C[Col.WSA][a]; if (!float.IsNaN(w)) { sum -= w; cnt--; } a++; }
                res[i] = cnt > 0 ? sum / cnt : double.NaN;
            }
            return res;
        }

        // sorozatos szabályok (nem egy sorból dönthetők el): csapadékugrás és befagyott szélmérő
        static int[] RunFlags(Dataset ds, Settings s)
        {
            var f = new int[ds.N];
            var C = ds.C;
            if (s.FlagRainJump)
                for (int i = 1; i < ds.N; i++)
                {
                    if (ds.Day[i] != ds.Day[i - 1]) continue;
                    long dt = ds.T[i] - ds.T[i - 1];
                    float p0 = C[Col.PTOT][i - 1], p1 = C[Col.PTOT][i];
                    if (dt <= 0 || dt > 900 || float.IsNaN(p0) || float.IsNaN(p1) || p1 <= p0) continue;
                    double inc = p1 - p0, rate = inc / (dt / 3600.0);
                    float t = C[Col.TA][i];
                    bool melt = !float.IsNaN(t) && t <= s.MeltTemp && inc >= s.MeltMm;   // hó / jég olvadt el egyszerre a mérőben
                    if (rate > s.RainJumpRate || melt)
                    {
                        f[i] |= Flag.RAINJUMP;
                        // a követő percekben az állomás még a „bennragadt” intenzitást küldi, bár már nem esik: azok is gyanúsak
                        for (int k = i + 1; k < ds.N && ds.T[k] - ds.T[i] <= 1200 && !(C[Col.PTOT][k] > C[Col.PTOT][k - 1] + 0.01f); k++)
                            if (C[Col.PRATE][k] > 0) f[k] |= Flag.RAINJUMP;
                    }
                }
            if (s.FlagFrozenWind)
            {
                int i = 0;
                while (i < ds.N)
                {
                    bool zero = C[Col.WSA][i] == 0 && C[Col.WGH][i] == 0;
                    if (!zero) { i++; continue; }
                    int j = i; double tmin = double.MaxValue;
                    while (j < ds.N && C[Col.WSA][j] == 0 && C[Col.WGH][j] == 0 && (j == i || ds.T[j] - ds.T[j - 1] <= 1800))
                    { float t = C[Col.TA][j]; if (!float.IsNaN(t) && t < tmin) tmin = t; j++; }
                    if (ds.T[j - 1] - ds.T[i] >= s.FrozenHours * 3600 && tmin <= s.FrozenTemp)
                        for (int k = i; k < j; k++) f[k] |= Flag.WINDFROZEN;
                    i = j;
                }
            }
            return f;
        }

        // egy sor jelzői a beállított (be/ki kapcsolható) szabályok szerint
        static int FlagOf(Dataset ds, Settings s, int i, double[] wind30)
        {
            var C = ds.C;
            long gapSec = s.RestartMin * 60L;
            int f = 0;
            if (ds.Qc[i] == 0) { if (s.FlagQc0) f |= Flag.QC0; }
            else if (ds.Qc[i] < 0) { if (s.FlagQcNeg) f |= Flag.QCNEG; }
            if (s.FlagRange)
            {
                if (Bad(C[Col.UV][i], 0, s.UvMax)) f |= Flag.UV;
                if (Bad(C[Col.TH][i], s.TempMin, s.TempMax) || Bad(C[Col.TL][i], s.TempMin, s.TempMax) || Bad(C[Col.TA][i], s.TempMin, s.TempMax)) f |= Flag.TEMP;
                if (Bad(C[Col.HA][i], 0, 100) || Bad(C[Col.HH][i], 0, 100) || Bad(C[Col.HL][i], 0, 100)) f |= Flag.HUM;
                if (Bad(C[Col.PMIN][i], s.PresMin, s.PresMax) || Bad(C[Col.PMAX][i], s.PresMin, s.PresMax)) f |= Flag.PRES;
                if (Bad(C[Col.PRATE][i], 0, s.RainRateMax) || Bad(C[Col.PTOT][i], 0, 1000)) f |= Flag.RAIN;
                if (Bad(C[Col.WGH][i], 0, s.GustMax) || Bad(C[Col.WSH][i], 0, s.GustMax) || Bad(C[Col.WSA][i], 0, s.GustMax)) f |= Flag.WIND;
                if (Bad(C[Col.SOL][i], 0, s.SolarMax)) f |= Flag.SOLAR;
            }
            if (s.FlagRestart && i > 0 && ds.T[i] - ds.T[i - 1] > gapSec) f |= Flag.RESTART;
            // tüske: mindkét szomszédtól ugyanabba az irányba tér el nagyon (rövid időn belül)
            if (s.FlagSpike && i > 0 && i < ds.N - 1 && ds.T[i] - ds.T[i - 1] <= 900 && ds.T[i + 1] - ds.T[i] <= 900)
            {
                double d1 = C[Col.TA][i] - C[Col.TA][i - 1], d2 = C[Col.TA][i] - C[Col.TA][i + 1];
                if (Math.Abs(d1) > s.SpikeDeg && Math.Abs(d2) > s.SpikeDeg && Math.Sign(d1) == Math.Sign(d2)) f |= Flag.SPIKE;
            }
            // túlmelegedés: gyenge a szél pillanatnyilag VAGY a 30 perces átlagban (8.5 – egy-egy szellő ne mentse fel)
            float th = C[Col.TH][i], sol = C[Col.SOL][i], ws = C[Col.WSA][i];
            double w30 = wind30 != null ? wind30[i] : double.NaN;
            bool calm = float.IsNaN(ws) || ws <= s.SunWind || (!double.IsNaN(w30) && w30 <= s.SunWind);
            if (s.FlagSun && !float.IsNaN(th) && th >= s.SunTemp && !float.IsNaN(sol) && sol >= s.SunSolar && calm) f |= Flag.SUN;
            return f;
        }

        // előnézet mentés nélkül: hány sor lenne jelölve ezekkel a beállításokkal (összesen és szabályonként)
        public static int[] FlagPreview(Dataset ds, Settings s, out int rows)
        {
            var counts = new int[Flag.Bits.Length]; rows = 0;
            var w30 = WindAvg30(ds); var run = RunFlags(ds, s);
            for (int i = 0; i < ds.N; i++)
            {
                int f = FlagOf(ds, s, i, w30) | run[i];
                if (f == 0) continue;
                rows++;
                for (int b = 0; b < Flag.Bits.Length; b++) if ((f & Flag.Bits[b]) != 0) counts[b]++;
            }
            return counts;
        }

        static void ComputeFlags(Dataset ds, Settings s)
        {
            var w30 = WindAvg30(ds); var run = RunFlags(ds, s);
            for (int i = 0; i < ds.N; i++) ds.Flags[i] = FlagOf(ds, s, i, w30) | run[i];
            ds.FlagCounts = new int[Flag.Bits.Length];
            ds.FlaggedRows = 0;
            for (int i = 0; i < ds.N; i++)
            {
                if (ds.Flags[i] == 0) continue;
                ds.FlaggedRows++;
                for (int b = 0; b < Flag.Bits.Length; b++) if ((ds.Flags[i] & Flag.Bits[b]) != 0) ds.FlagCounts[b]++;
            }
        }

        static void ComputeGaps(Dataset ds, Settings s)
        {
            ds.Gaps = new List<Gap>();
            long lim = s.GapMinutes * 60L;
            for (int i = 1; i < ds.N; i++)
                if (ds.T[i] - ds.T[i - 1] > lim) ds.Gaps.Add(new Gap { From = ds.T[i - 1], To = ds.T[i], Index = i });
        }

        // a sor értéke, vagy NaN ha tiszta módban a jelző miatt ki kell hagyni
        static double Val(Dataset ds, int col, int i, bool clean, int mask)
        {
            if (clean && (ds.Flags[i] & mask) != 0) return double.NaN;
            return ds.C[col][i];
        }

        public static List<DayStat> ComputeDays(Dataset ds, bool clean)
        {
            var res = new List<DayStat>();
            int i = 0;
            while (i < ds.N)
            {
                int day = ds.Day[i], j = i;
                while (j < ds.N && ds.Day[j] == day) j++;
                res.Add(ComputeDay(ds, day, i, j, clean));
                i = j;
            }
            return res;
        }

        public static DayStat ComputeDay(Dataset ds, int day, int a, int b, bool clean)
        {
            var d = new DayStat { Day = day, Date = Tz.DateOf(day), First = a, Last = b - 1, Count = b - a };
            d.Start = Tz.LocalMidnightEpoch(d.Date);
            d.End = Tz.LocalMidnightEpoch(d.Date.AddDays(1));
            double tSum = 0, dewSum = 0, humSum = 0, presSum = 0, wSum = 0, vx = 0, vy = 0, energy = 0;
            int tN = 0, dewN = 0, humN = 0, presN = 0, wN = 0, solN = 0;
            double rainTotal = 0, segMax = double.NaN, prevTot = double.NaN, sunSec = 0, vpdSum = 0;
            int vpdN = 0;
            long covered = 0;
            for (int i = a; i < b; i++)
            {
                long dt = i > a ? Math.Min(ds.T[i] - ds.T[i - 1], gapSec) : Math.Min(ds.T[i] - d.Start, 300);
                if (dt < 0) dt = 0;
                covered += dt;

                double th = Val(ds, Col.TH, i, clean, Flag.Temp), tl = Val(ds, Col.TL, i, clean, Flag.Temp), ta = Val(ds, Col.TA, i, clean, Flag.Temp);
                if (!double.IsNaN(th) && (double.IsNaN(d.Tmax) || th > d.Tmax)) { d.Tmax = th; d.TmaxT = ds.T[i]; }
                if (!double.IsNaN(tl) && (double.IsNaN(d.Tmin) || tl < d.Tmin)) { d.Tmin = tl; d.TminT = ds.T[i]; }
                if (!double.IsNaN(ta)) { tSum += ta; tN++; }

                double vt = Val(ds, Col.TA, i, clean, Flag.Temp), vh = Val(ds, Col.HA, i, clean, Flag.Hum);
                if (!double.IsNaN(vt) && !double.IsNaN(vh))
                {
                    double vpd = 0.6108 * Math.Exp(17.27 * vt / (vt + 237.3)) * (1 - vh / 100.0);
                    if (double.IsNaN(d.VpdMax) || vpd > d.VpdMax) d.VpdMax = vpd;
                    if (ds.Hour[i] >= 8 && ds.Hour[i] < 18) { vpdSum += vpd; vpdN++; }
                }
                double dew = Val(ds, Col.DA, i, clean, Flag.Dew);
                if (!double.IsNaN(dew)) { dewSum += dew; dewN++; }

                double hh = Val(ds, Col.HH, i, clean, Flag.Hum), hl = Val(ds, Col.HL, i, clean, Flag.Hum), ha = Val(ds, Col.HA, i, clean, Flag.Hum);
                if (!double.IsNaN(hh) && (double.IsNaN(d.HumMax) || hh > d.HumMax)) d.HumMax = hh;
                if (!double.IsNaN(hl) && (double.IsNaN(d.HumMin) || hl < d.HumMin)) d.HumMin = hl;
                if (!double.IsNaN(ha)) { humSum += ha; humN++; }

                double pmax = Val(ds, Col.PMAX, i, clean, Flag.Pres), pmin = Val(ds, Col.PMIN, i, clean, Flag.Pres);
                if (!double.IsNaN(pmax) && (double.IsNaN(d.PresMax) || pmax > d.PresMax)) d.PresMax = pmax;
                if (!double.IsNaN(pmin) && (double.IsNaN(d.PresMin) || pmin < d.PresMin)) d.PresMin = pmin;
                if (!double.IsNaN(pmax) && !double.IsNaN(pmin))
                {
                    presSum += (pmax + pmin) / 2; presN++;
                    if (double.IsNaN(d.PresFirst)) d.PresFirst = (pmax + pmin) / 2;
                    d.PresLast = (pmax + pmin) / 2;
                }

                double ws = Val(ds, Col.WSA, i, clean, Flag.Wind), wsh = Val(ds, Col.WSH, i, clean, Flag.Wind), wg = Val(ds, Col.WGH, i, clean, Flag.Wind);
                double dir = ds.C[Col.WDIR][i];
                if (!double.IsNaN(ws))
                {
                    wSum += ws; wN++;
                    if (!double.IsNaN(dir)) { vx += ws * Math.Sin(dir * Math.PI / 180); vy += ws * Math.Cos(dir * Math.PI / 180); }
                }
                if (!double.IsNaN(wsh) && (double.IsNaN(d.WindMax) || wsh > d.WindMax)) d.WindMax = wsh;
                if (!double.IsNaN(wg) && (double.IsNaN(d.GustMax) || wg > d.GustMax)) { d.GustMax = wg; d.GustMaxT = ds.T[i]; }

                // csapadék: a precipTotal napi halmozott; a nap maximuma, nap közbeni nullázásnál szakaszonként összeadva
                double tot = Val(ds, Col.PTOT, i, clean, Flag.Rain);
                if (!double.IsNaN(tot))
                {
                    if (!double.IsNaN(prevTot) && tot < prevTot - 0.25) { rainTotal += double.IsNaN(segMax) ? 0 : segMax; segMax = double.NaN; }
                    if (double.IsNaN(segMax) || tot > segMax) segMax = tot;
                    prevTot = tot;
                }
                double rate = Val(ds, Col.PRATE, i, clean, Flag.RainRate);
                if (!double.IsNaN(rate) && (double.IsNaN(d.RainRateMax) || rate > d.RainRateMax)) { d.RainRateMax = rate; d.RainRateMaxT = ds.T[i]; }

                double sol = Val(ds, Col.SOL, i, clean, Flag.Solar);
                if (!double.IsNaN(sol))
                {
                    if (double.IsNaN(d.SolarMax) || sol > d.SolarMax) d.SolarMax = sol;
                    energy += sol * dt; solN++;
                    if (sol >= 120) sunSec += dt;
                }
                double uv = Val(ds, Col.UV, i, clean, Flag.Uv);
                if (!double.IsNaN(uv) && (double.IsNaN(d.UvMax) || uv > d.UvMax)) d.UvMax = uv;
            }
            if (tN > 0) d.Tavg = tSum / tN;
            if (vpdN > 0) d.VpdDay = vpdSum / vpdN;
            if (dewN > 0) d.DewAvg = dewSum / dewN;
            if (humN > 0) d.HumAvg = humSum / humN;
            if (presN > 0) d.PresAvg = presSum / presN;
            if (wN > 0)
            {
                d.WindAvg = wSum / wN;
                if (vx != 0 || vy != 0) { d.WindDir = Math.Atan2(vx, vy) * 180 / Math.PI; if (d.WindDir < 0) d.WindDir += 360; }
            }
            if (!double.IsNaN(segMax) || rainTotal > 0) d.Rain = rainTotal + (double.IsNaN(segMax) ? 0 : segMax);
            if (solN > 0) { d.Energy = energy / 3600.0 / 1000.0; d.SunHours = sunSec / 3600.0; } // W/m² * s -> kWh/m²
            d.Coverage = Math.Min(1.0, covered / (double)(d.End - d.Start));
            return d;
        }

        static List<MonthStat> ComputeMonths(Dataset ds, Settings s)
        {
            var res = new List<MonthStat>();
            foreach (var g in ds.Days.GroupBy(d => d.Date.Year * 100 + d.Date.Month))
            {
                var m = new MonthStat { Year = g.Key / 100, Month = g.Key % 100 };
                var good = g.Where(d => d.Coverage >= DayStat.FullCoverage).ToList();   // közös küszöb (8.6)
                m.Days = good.Count;
                var ta = good.Where(d => !double.IsNaN(d.Tavg)).Select(d => d.Tavg).ToList();
                if (ta.Count > 0) m.Tavg = ta.Average();
                var tx = good.Where(d => !double.IsNaN(d.Tmax)).Select(d => d.Tmax).ToList();
                var tn = good.Where(d => !double.IsNaN(d.Tmin)).Select(d => d.Tmin).ToList();
                if (tx.Count > 0) { m.Tmax = tx.Max(); m.TmaxAvg = tx.Average(); }
                if (tn.Count > 0) { m.Tmin = tn.Min(); m.TminAvg = tn.Average(); }
                m.Rain = g.Where(d => !double.IsNaN(d.Rain)).Sum(d => d.Rain);
                m.Energy = g.Where(d => !double.IsNaN(d.Energy)).Sum(d => d.Energy);
                var gu = g.Where(d => !double.IsNaN(d.GustMax)).Select(d => d.GustMax).ToList();
                if (gu.Count > 0) m.GustMax = gu.Max();
                m.HotDays = good.Count(d => d.Tmax >= s.HotDay);
                m.TropicalNights = good.Count(d => d.Tmin >= s.TropicalNight);
                m.FrostDays = good.Count(d => d.Tmin < s.FrostDay);
                m.IceDays = good.Count(d => d.Tmax < s.IceDay);
                m.RainDays = g.Count(d => d.Rain >= 1.0);
                res.Add(m);
            }
            return res;
        }

        static List<HourStat> ComputeHours(Dataset ds)
        {
            var res = new List<HourStat>();
            int i = 0;
            while (i < ds.N)
            {
                long h = ds.T[i] / 3600;
                var hs = new HourStat { T = h * 3600 };
                double tS = 0, huS = 0, pS = 0, wS = 0, sS = 0; int tN = 0, huN = 0, pN = 0, wN = 0, sN = 0;
                int j = i;
                for (; j < ds.N && ds.T[j] / 3600 == h; j++)
                {
                    double ta = Val(ds, Col.TA, j, true, Flag.Temp);
                    if (!double.IsNaN(ta)) { tS += ta; tN++; }
                    double th = Val(ds, Col.TH, j, true, Flag.Temp), tl = Val(ds, Col.TL, j, true, Flag.Temp);
                    if (!double.IsNaN(th) && (double.IsNaN(hs.Tmax) || th > hs.Tmax)) hs.Tmax = th;
                    if (!double.IsNaN(tl) && (double.IsNaN(hs.Tmin) || tl < hs.Tmin)) hs.Tmin = tl;
                    double hu = Val(ds, Col.HA, j, true, Flag.Hum); if (!double.IsNaN(hu)) { huS += hu; huN++; }
                    double p1 = Val(ds, Col.PMAX, j, true, Flag.Pres), p2 = Val(ds, Col.PMIN, j, true, Flag.Pres);
                    if (!double.IsNaN(p1) && !double.IsNaN(p2)) { pS += (p1 + p2) / 2; pN++; }
                    double w = Val(ds, Col.WSA, j, true, Flag.Wind); if (!double.IsNaN(w)) { wS += w; wN++; }
                    double g = Val(ds, Col.WGH, j, true, Flag.Wind); if (!double.IsNaN(g) && (double.IsNaN(hs.Gust) || g > hs.Gust)) hs.Gust = g;
                    double so = Val(ds, Col.SOL, j, true, Flag.Solar); if (!double.IsNaN(so)) { sS += so; sN++; }
                    // óránkénti csapadék = a napi halmozott érték növekménye
                    double tot = Val(ds, Col.PTOT, j, true, Flag.Rain);
                    if (!double.IsNaN(tot) && j > 0)
                    {
                        double prev = Val(ds, Col.PTOT, j - 1, true, Flag.Rain);
                        if (ds.Day[j] != ds.Day[j - 1] || double.IsNaN(prev)) hs.Rain += ds.Day[j] != ds.Day[j - 1] ? tot : 0;
                        else if (tot >= prev) hs.Rain += tot - prev;
                        else hs.Rain += tot; // nap közbeni nullázás
                    }
                }
                hs.Count = j - i;
                if (tN > 0) hs.Tavg = tS / tN;
                if (huN > 0) hs.Hum = huS / huN;
                if (pN > 0) hs.Pres = pS / pN;
                if (wN > 0) hs.Wind = wS / wN;
                if (sN > 0) hs.Solar = sS / sN;
                res.Add(hs);
                i = j;
            }
            return res;
        }

        // ------------------------------------------------------------------ pillanatnyi állapot

        static Current BuildCurrent(Dataset ds, List<string> curFiles)
        {
            Current best = null;
            if (ds.N > 0)
            {
                int i = ds.N - 1;
                best = new Current
                {
                    T = ds.T[i], Source = "részletes mérés",
                    Temp = ds.C[Col.TA][i], HeatIndex = ds.C[Col.HIA][i], WindChill = ds.C[Col.WCA][i], Dew = ds.C[Col.DA][i],
                    Hum = ds.C[Col.HA][i], Wind = ds.C[Col.WSA][i], Gust = ds.C[Col.WGH][i], Dir = ds.C[Col.WDIR][i],
                    Pres = (ds.C[Col.PMAX][i] + ds.C[Col.PMIN][i]) / 2, PrecipRate = ds.C[Col.PRATE][i], PrecipTotal = ds.C[Col.PTOT][i],
                    Solar = ds.C[Col.SOL][i], Uv = ds.C[Col.UV][i]
                };
            }
            foreach (var f in curFiles)
            {
                try
                {
                    string text = ReadAllShared(f).TrimStart('﻿');
                    var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
                    if (lines.Count < 2) continue;
                    char d = Delim(lines[0]);
                    var h = SplitLine(lines[0], d);
                    var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int k = 0; k < h.Count; k++) idx[h[k].Trim()] = k;
                    bool dc = d != ',';
                    for (int li = 1; li < lines.Count; li++)
                    {
                        var r = SplitLine(lines[li], d);
                        Func<string, double> g = name => { int k; return idx.TryGetValue(name, out k) && k < r.Count ? ParseF(r[k], dc) : double.NaN; };
                        long t;
                        int ie;
                        if (!idx.TryGetValue("epoch", out ie) || ie >= r.Count || !long.TryParse(r[ie].Trim(), out t)) continue;
                        if (ds.Station.Length > 0 && idx.ContainsKey("stationID") && !string.Equals(r[idx["stationID"]].Trim(), ds.Station, StringComparison.OrdinalIgnoreCase)) continue;
                        int inb;
                        if (idx.TryGetValue("neighborhood", out inb) && inb < r.Count && r[inb].Trim().Length > 0) ds.Neighborhood = r[inb].Trim();
                        if (best != null && t <= best.T) continue;
                        best = new Current
                        {
                            T = t, Source = "pillanatnyi állapot",
                            Temp = g("temp"), HeatIndex = g("heatIndex"), WindChill = g("windChill"), Dew = g("dewpt"), Hum = g("humidity"),
                            Wind = g("windSpeed"), Gust = g("windGust"), Dir = g("winddir"), Pres = g("pressure"), PrecipRate = g("precipRate"),
                            PrecipTotal = g("precipTotal"), Solar = g("solarRadiation"), Uv = g("uv")
                        };
                    }
                }
                catch (Exception ex) { ds.Warnings.Add(Path.GetFileName(f) + " nem olvasható: " + ex.Message); }
            }
            return best;
        }
    }
}
