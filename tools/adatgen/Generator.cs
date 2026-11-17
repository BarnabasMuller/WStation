using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WStation.AdatGen;

/// <summary>
/// Determinisztikus mintaadat: a WU-letöltő CSV-jével azonos oszlopok és formátum (UTF-8 BOM, CRLF, ';' és ',').
/// Nem valódi mérés. Nincs rendszeróra, területi beállítás vagy időzóna-adatbázis: az időt és az óraátállítást
/// kézzel számoljuk, a véletlen egy saját, állapotmentes hash-függvényből jön.
/// </summary>
public static class Generator
{
    public const string StationId = "ITESZT1";
    public const string ApiKey = "TESZT-KULCS-ADATGEN";
    public const long Seed = 20260101;
    const double Lat = 47.5, Lon = 19.0;
    const string TzName = "Europe/Budapest";
    const int StepSec = 300;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly DateTime Epoch2000 = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
    static readonly DateTime Start = new(2025, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);       // első helyi nap
    static readonly DateTime EndExcl = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Unspecified);    // az utolsó helyi nap után

    // A WU letöltő által kiírt oszlopok sorrendje (kulcsoszlopok elöl, majd a WU mezői laposítva, a végén a mértékegység).
    public static readonly string[] ObsColumns =
    {
        "stationID", "obsTimeLocal", "obsTimeUtc", "epoch", "tz", "lat", "lon", "solarRadiationHigh", "uvHigh", "winddirAvg",
        "humidityHigh", "humidityLow", "humidityAvg", "qcStatus",
        "tempHigh", "tempLow", "tempAvg", "windspeedHigh", "windspeedLow", "windspeedAvg", "windgustHigh", "windgustLow", "windgustAvg",
        "dewptHigh", "dewptLow", "dewptAvg", "windchillHigh", "windchillLow", "windchillAvg", "heatindexHigh", "heatindexLow", "heatindexAvg",
        "pressureMax", "pressureMin", "pressureTrend", "precipRate", "precipTotal", "units"
    };

    // ---------------------------------------------------------------- véletlen: állapotmentes

    static ulong Mix(ulong x)
    {
        unchecked
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }

    /// <summary>Egyenletes szám [0,1) a (csatorna, index) párhoz.</summary>
    static double U(int ch, long k)
    {
        unchecked
        {
            return (Mix(Mix((ulong)Seed ^ ((ulong)ch * 0x100000001B3UL)) + (ulong)k) >> 11) * (1.0 / 9007199254740992.0);
        }
    }

    /// <summary>Folytonos zaj [-1,1] a csatornához: a csomópontok (egész x) közt koszinuszos átmenettel.</summary>
    static double Noise(int ch, double x)
    {
        double f = Math.Floor(x);
        double a = U(ch, (long)f) * 2 - 1, b = U(ch, (long)f + 1) * 2 - 1;
        double t = (1 - Math.Cos((x - f) * Math.PI)) / 2;
        return a + (b - a) * t;
    }

    // ---------------------------------------------------------------- idő: helyi idő kézzel (EU nyári időszámítás)

    static int DayIndex(DateTime d) => (int)(d.Date - Epoch2000).TotalDays;

    static DateTime LastSunday(int year, int month)
    {
        var d = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        return d.AddDays(-(int)d.DayOfWeek);
    }

    /// <summary>Helyi (közép-európai) eltolás percben az UTC pillanatra: március utolsó vasárnapja 01:00 UTC-től október utolsó vasárnapja 01:00 UTC-ig +120, egyébként +60.</summary>
    static int OffsetMin(DateTime utc)
    {
        var begin = LastSunday(utc.Year, 3).AddHours(1);
        var end = LastSunday(utc.Year, 10).AddHours(1);
        return utc >= begin && utc < end ? 120 : 60;
    }

    static DateTime UtcOfLocal(DateTime local)
    {
        var u = local.AddMinutes(-120);
        return OffsetMin(u) == 120 ? u : local.AddMinutes(-60);
    }

    // ---------------------------------------------------------------- események (rögzített, helyi időben)

    static readonly DateTime MissingDay = new(2025, 12, 14);                                                                                      // egész nap hiányzik
    static readonly (DateTime Begin, DateTime End) Restart = (new DateTime(2026, 2, 3, 9, 10, 0), new DateTime(2026, 2, 3, 11, 45, 0));          // állomás-újraindulás
    static readonly (DateTime Begin, DateTime End) NetOutage = (new DateTime(2026, 6, 20, 22, 0, 0), new DateTime(2026, 6, 21, 5, 30, 0));       // éjszakai kiesés
    static readonly (DateTime Begin, DateTime End) FrozenWind = (new DateTime(2026, 1, 10, 0, 0, 0), new DateTime(2026, 1, 10, 14, 0, 0));      // befagyott szélmérő
    static readonly DateTime ColdFrom = new(2026, 1, 8), ColdTo = new(2026, 1, 14);       // hidegbetörés (szélsőséges minimum)
    static readonly DateTime HeatFrom = new(2026, 7, 20), HeatTo = new(2026, 7, 24);      // hőhullám (szélsőséges maximum)
    static readonly DateTime StormDay = new(2026, 8, 14);                                  // vihar: nagy széllökés és felhőszakadás
    static readonly DateTime RainJumpTime = new(2026, 5, 9, 14, 25, 0);                    // csapadékugrás (hibás érték)

    static bool In(DateTime t, (DateTime Begin, DateTime End) r) => t >= r.Begin && t < r.End;

    // ---------------------------------------------------------------- időjárás-modell

    sealed record DayInfo(bool Rain, double RainStartH, double RainDurH, double RainPeak, double Cloud, double WindBase, double WindDir, bool Storm);

    static DayInfo DayParams(DateTime date)
    {
        int k = DayIndex(date);
        bool storm = date == StormDay;
        bool rain = storm || U(11, k) < 0.24;
        double cloud = rain ? 0.7 + 0.3 * U(12, k) : Math.Pow(U(12, k), 1.6) * 0.8;
        double peak = storm ? 62 : 0.5 + 9 * Math.Pow(U(13, k), 2.2);
        double start = storm ? 17.0 : 1 + 20 * U(14, k);
        double dur = storm ? 1.2 : 1 + 7 * U(15, k);
        double wind = 4 + 9 * Math.Pow(U(16, k), 1.7);
        double dir = 360 * U(17, k);
        return new DayInfo(rain, start, dur, peak, cloud, wind, dir, storm);
    }

    static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    sealed class Sample
    {
        public double Temp, Hum, Dew, Wind, Gust, Dir, Pres, Rate, Solar, Uv;
    }

    static Sample Model(DateTime local, DateTime utc, DayInfo day)
    {
        double doy = local.DayOfYear;
        double h = local.Hour + local.Minute / 60.0;
        double minutes = (utc - Start).TotalMinutes;
        double season = 10.5 + 11.5 * Math.Sin(2 * Math.PI * (doy - 110) / 365.0);
        double anomaly = 4.5 * Noise(21, (utc - Epoch2000).TotalDays / 3.0);
        if (local.Date >= ColdFrom && local.Date < ColdTo) anomaly -= 12;
        if (local.Date >= HeatFrom && local.Date < HeatTo) anomaly += 10;
        double amp = 3.5 + 3.5 * (1 - Math.Abs(doy - 200) / 165.0) * (1 - 0.6 * day.Cloud);
        double diurnal = amp * Math.Sin(2 * Math.PI * (h - 9.5) / 24.0);
        double fine = 0.25 * Noise(22, minutes / 20.0);

        // napsugárzás: a nap magassága (47,5° É), felhőzettel gyengítve; a napidő az UTC-ből és a hosszúságból
        double decl = 23.44 * Math.PI / 180 * Math.Sin(2 * Math.PI * (doy - 80) / 365.0);
        double solarHour = utc.Hour + utc.Minute / 60.0 + Lon / 15.0 - 12;
        double hourAngle = solarHour * 15 * Math.PI / 180;
        double sinElev = Math.Sin(Lat * Math.PI / 180) * Math.Sin(decl) + Math.Cos(Lat * Math.PI / 180) * Math.Cos(decl) * Math.Cos(hourAngle);
        double clear = sinElev > 0 ? 1000 * Math.Pow(sinElev, 1.15) : 0;
        double cloudNow = Clamp(day.Cloud + 0.25 * Noise(23, minutes / 45.0), 0, 1);
        double solar = Math.Max(0, clear * (1 - 0.78 * Math.Pow(cloudNow, 2.2)));
        double uv = solar > 0 ? Math.Min(11, solar / 95.0) : 0;

        double temp = season + anomaly + diurnal + fine - 2.0 * (day.Cloud - 0.4);

        double rate = 0;
        if (day.Rain)
        {
            double x = (h - day.RainStartH) / day.RainDurH;
            if (x >= 0 && x <= 1)
            {
                double shape = Math.Sin(Math.PI * x);
                double burst = 0.55 + 0.9 * (Noise(24, minutes / 10.0) + 1) / 2;
                rate = day.RainPeak * shape * burst;
                temp -= 1.5 * shape;
            }
        }

        double hum = Clamp(78 - 2.2 * diurnal + 10 * (day.Cloud - 0.4) + 4 * Noise(25, minutes / 60.0), 28, 99);
        if (rate > 0) hum = Math.Max(hum, 93);
        double dew = temp - (100 - hum) / 5.0;

        double wind = Math.Max(0, day.WindBase * (0.6 + 0.8 * (1 + Noise(26, minutes / 120.0)) / 2) * (1 + 0.3 * (solar / 1000.0)) + 2 * Noise(27, minutes / 8.0));
        double gust = wind * (1.35 + 0.5 * (Noise(28, minutes / 6.0) + 1) / 2);
        if (day.Storm)
        {
            double s = Math.Sin(Math.PI * Clamp((h - 16.0) / 3.0, 0, 1));
            wind += 38 * s;
            gust = Math.Max(gust, wind * 1.5 + 18 * s);
            if (local.Hour == 17 && local.Minute == 35) gust = 112;     // a nap legnagyobb széllökése (≈ 31 m/s)
        }
        if (In(local, FrozenWind)) { wind = 0; gust = 0; }
        double dir = (day.WindDir + 60 * Noise(29, minutes / 90.0) + 360) % 360;

        double pres = 1015 + 9 * Noise(30, (utc - Epoch2000).TotalDays / 2.5) - (rate > 0 ? 3 : 0) - (day.Storm ? 9 : 0);

        return new Sample { Temp = temp, Hum = hum, Dew = dew, Wind = wind, Gust = gust, Dir = dir, Pres = pres, Rate = rate, Solar = solar, Uv = uv };
    }

    static double WindChill(double t, double w) => t <= 10 && w > 4.8 ? 13.12 + 0.6215 * t - 11.37 * Math.Pow(w, 0.16) + 0.3965 * t * Math.Pow(w, 0.16) : t;

    static double HeatIndex(double t, double rh)
    {
        if (t < 26.7) return t;
        double f = t * 9 / 5 + 32;
        double hi = -42.379 + 2.04901523 * f + 10.14333127 * rh - 0.22475541 * f * rh - 0.00683783 * f * f - 0.05481717 * rh * rh
                    + 0.00122874 * f * f * rh + 0.00085282 * f * rh * rh - 0.00000199 * f * f * rh * rh;
        return Math.Max(t, (hi - 32) * 5 / 9);
    }

    // ---------------------------------------------------------------- CSV

    /// <summary>Szám szövege: tizedesvessző, a kerekítés félről fölfelé, kultúrafüggetlen.</summary>
    static string N(double v, int dec) => Math.Round(v, dec, MidpointRounding.AwayFromZero).ToString("F" + dec, Inv).Replace('.', ',');

    static string Join(IEnumerable<string> fields) => string.Join(';', fields);

    /// <summary>Az egész adatot memóriában előállítja: részletes mérések, napi összesítő, pillanatnyi állapot (CSV-szöveg, BOM nélkül).</summary>
    public static (string Obs, string Daily, string Current) Build()
    {
        var obs = new StringBuilder();
        var daily = new StringBuilder();
        obs.Append(Join(ObsColumns)).Append("\r\n");
        daily.Append("datum;").Append(Join(ObsColumns)).Append("\r\n");

        double precipTotal = 0;
        DateTime curDay = DateTime.MinValue;
        string? dayFirstLine = null;
        double dTmax = 0, dTmin = 0, dTsum = 0, dHmax = 0, dHmin = 0, dHsum = 0, dWmax = 0, dWsum = 0, dGmax = 0;
        double dPmax = 0, dPmin = 0, dSolMax = 0, dUvMax = 0, dDsum = 0, dDmax = 0, dDmin = 0, lastDir = 0;
        int dN = 0;
        string[]? last = null;
        DateTime lastUtc = default;
        var day = DayParams(Start);

        void ResetDay()
        {
            dTmax = double.MinValue; dTmin = double.MaxValue; dTsum = 0; dHmax = 0; dHmin = 100; dHsum = 0; dWmax = 0; dWsum = 0; dGmax = 0;
            dPmax = double.MinValue; dPmin = double.MaxValue; dSolMax = 0; dUvMax = 0; dDsum = 0; dDmax = double.MinValue; dDmin = double.MaxValue;
            dN = 0; dayFirstLine = null; precipTotal = 0;
        }

        void FlushDay()
        {
            if (curDay == DateTime.MinValue || dN == 0 || dayFirstLine == null) return;
            var r = new List<string> { curDay.ToString("yyyy-MM-dd", Inv) };
            r.AddRange(dayFirstLine.Split(';'));
            // a napi értékek az első sor megfelelő oszlopait váltják le
            void Set(string col, string v) => r[1 + Array.IndexOf(ObsColumns, col)] = v;
            Set("solarRadiationHigh", N(dSolMax, 1)); Set("uvHigh", N(dUvMax, 1)); Set("winddirAvg", N(lastDir, 0));
            Set("humidityHigh", N(dHmax, 0)); Set("humidityLow", N(dHmin, 0)); Set("humidityAvg", N(dHsum / dN, 0));
            Set("tempHigh", N(dTmax, 1)); Set("tempLow", N(dTmin, 1)); Set("tempAvg", N(dTsum / dN, 1));
            Set("windspeedHigh", N(dWmax, 1)); Set("windspeedLow", N(0, 1)); Set("windspeedAvg", N(dWsum / dN, 1));
            Set("windgustHigh", N(dGmax, 1)); Set("windgustLow", N(0, 1)); Set("windgustAvg", N(dWsum / dN * 1.5, 1));
            Set("dewptHigh", N(dDmax, 1)); Set("dewptLow", N(dDmin, 1)); Set("dewptAvg", N(dDsum / dN, 1));
            Set("pressureMax", N(dPmax, 2)); Set("pressureMin", N(dPmin, 2));
            Set("precipTotal", N(precipTotal, 2));
            daily.Append(Join(r)).Append("\r\n");
        }

        ResetDay();
        for (var utc = UtcOfLocal(Start); ; utc = utc.AddSeconds(StepSec))
        {
            var local = utc.AddMinutes(OffsetMin(utc));
            if (local >= EndExcl) break;

            if (local.Date != curDay)
            {
                FlushDay();
                curDay = local.Date;
                day = DayParams(curDay);
                ResetDay();
            }

            long k = (long)(utc - Epoch2000).TotalSeconds / StepSec;
            // hiányok: egész nap, újraindulás, kiesés, véletlenszerű egy-egy sor
            if (local.Date == MissingDay || In(local, Restart) || In(local, NetOutage) || U(41, k) < 1.0 / 400) continue;

            var s = Model(local, utc, day);
            precipTotal += s.Rate * StepSec / 3600.0;

            double temp = s.Temp, hum = s.Hum, pres = s.Pres, uv = s.Uv, rate = s.Rate;
            int qc = 1;
            // hibás értékek (a Megjelenítő jelzői: tüske, lehetetlen páratartalom / nyomás / UV, qcStatus, csapadékugrás)
            if (U(42, k) < 1.0 / 3000) temp += 30;
            if (U(43, k) < 1.0 / 5000) hum = 120;
            if (U(44, k) < 1.0 / 6000) pres = 0;
            if (U(45, k) < 1.0 / 7000) uv = 40;
            if (U(46, k) < 1.0 / 2500) qc = -1; else if (U(47, k) < 1.0 / 1500) qc = 0;
            if (local == RainJumpTime) { precipTotal += 80; rate = 400; }
            double tot = precipTotal;

            double tH = temp + 0.15, tL = temp - 0.15;
            double wA = s.Wind, wH = s.Wind * 1.15, wL = s.Wind * 0.6;
            if (In(local, FrozenWind)) wH = wL = 0;
            double gA = s.Gust * 0.85, gH = s.Gust, gL = s.Gust * 0.6;
            double dA = s.Dew, dH = s.Dew + 0.1, dL = s.Dew - 0.1;
            double hH = Math.Min(hum + 1, 100), hL = Math.Max(hum - 1, 0);
            if (hum > 100) { hH = hum; hL = hum; }
            double pMax = pres == 0 ? 0 : pres + 0.05, pMin = pres == 0 ? 0 : pres - 0.05;

            var f = new[]
            {
                StationId, local.ToString("yyyy-MM-dd HH:mm:ss", Inv), utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv),
                ((long)(utc - DateTime.UnixEpoch).TotalSeconds).ToString(Inv), TzName,
                N(Lat, 6), N(Lon, 6), N(s.Solar, 1), N(uv, 1), N(s.Dir, 0),
                N(hH, 0), N(hL, 0), N(Math.Min(hum, 120), 0), qc.ToString(Inv),
                N(tH, 1), N(tL, 1), N(temp, 1), N(wH, 1), N(wL, 1), N(wA, 1), N(gH, 1), N(gL, 1), N(gA, 1),
                N(dH, 1), N(dL, 1), N(dA, 1), N(WindChill(tH, wH), 1), N(WindChill(tL, wL), 1), N(WindChill(temp, wA), 1),
                N(HeatIndex(tH, hH), 1), N(HeatIndex(tL, hL), 1), N(HeatIndex(temp, Math.Min(hum, 100)), 1),
                N(pMax, 2), N(pMin, 2), "0,00", N(rate, 2), N(tot, 2), "m"
            };
            string line = Join(f);
            obs.Append(line).Append("\r\n");
            dayFirstLine ??= line;

            // napi összesítő: csak a hibátlan sorokból
            if (qc == 1 && temp < 60 && hum <= 100 && pres > 0 && uv <= 20)
            {
                dN++; dTmax = Math.Max(dTmax, tH); dTmin = Math.Min(dTmin, tL); dTsum += temp;
                dHmax = Math.Max(dHmax, hH); dHmin = Math.Min(dHmin, hL); dHsum += hum;
                dWmax = Math.Max(dWmax, wH); dWsum += wA; dGmax = Math.Max(dGmax, gH);
                dPmax = Math.Max(dPmax, pres); dPmin = Math.Min(dPmin, pres);
                dSolMax = Math.Max(dSolMax, s.Solar); dUvMax = Math.Max(dUvMax, uv); lastDir = s.Dir;
                dDsum += dA; dDmax = Math.Max(dDmax, dH); dDmin = Math.Min(dDmin, dL);
            }
            last = f; lastUtc = utc;
        }
        FlushDay();

        // pillanatnyi állapot: az utolsó mérésből (a WU "observations/current" mezőnevei)
        var cur = new StringBuilder();
        cur.Append("letoltve;stationID;obsTimeLocal;obsTimeUtc;epoch;lat;lon;neighborhood;country;temp;heatIndex;windChill;dewpt;humidity;windSpeed;windGust;winddir;pressure;precipRate;precipTotal;solarRadiation;uv;qcStatus;units\r\n");
        if (last is { } row)
        {
            string C(string col) => row[Array.IndexOf(ObsColumns, col)];
            cur.Append(Join(new[]
            {
                lastUtc.AddMinutes(1).ToString("yyyy-MM-dd HH:mm:ss", Inv), StationId, C("obsTimeLocal"), C("obsTimeUtc"), C("epoch"), C("lat"), C("lon"),
                "Tesztfalu", "HU", C("tempAvg"), C("heatindexAvg"), C("windchillAvg"), C("dewptAvg"), C("humidityAvg"), C("windspeedAvg"), C("windgustHigh"),
                C("winddirAvg"), C("pressureMax"), C("precipRate"), C("precipTotal"), C("solarRadiationHigh"), C("uvHigh"), C("qcStatus"), "m"
            })).Append("\r\n");
        }
        return (obs.ToString(), daily.ToString(), cur.ToString());
    }

    /// <summary>A Megjelenítő által várt szerkezet: &lt;gyökér&gt;\data\*.csv és &lt;gyökér&gt;\appdata_letöltő\beallitasok.ini (kitalált kulccsal).</summary>
    public static void Write(string root)
    {
        var (obs, daily, cur) = Build();
        string data = Path.Combine(root, "data"), dl = Path.Combine(root, "appdata_letöltő");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(dl);
        var bom = new UTF8Encoding(true);
        File.WriteAllText(Path.Combine(data, "idojaras_adatok.csv"), obs, bom);
        File.WriteAllText(Path.Combine(data, "napi_osszesito.csv"), daily, bom);
        File.WriteAllText(Path.Combine(data, "pillanatnyi_allapot.csv"), cur, bom);
        File.WriteAllText(Path.Combine(dl, "beallitasok.ini"),
            "# Az adatgen által generált kitalált beállítás (nem valódi kulcs, nem valódi állomás).\r\n" +
            "ApiKey=" + ApiKey + "\r\nStationId=" + StationId + "\r\nUnits=m\r\nCsvDelimiter=;\r\nDecimalSeparator=,\r\n", bom);
    }
}
