using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Idojaras
{
    // megjelenito.ini – küszöbértékek és megjelenítési beállítások
    class Settings
    {
        public double HotDay = 30;          // hőségnap: max >= ennyi
        public double TropicalNight = 20;   // trópusi éjszaka: min >= ennyi
        public double FrostDay = 0;         // fagyos nap: min < ennyi
        public double IceDay = 0;           // téli nap: max < ennyi
        public int GapMinutes = 15;         // ennél hosszabb szünet = adathiány
        public double UvMax = 15;
        public double TempMin = -35, TempMax = 45;
        public double PresMin = 950, PresMax = 1060;
        public double RainRateMax = 300;
        public double GustMax = 200;
        public double SolarMax = 1500;
        public double SpikeDeg = 4;
        public double SunTemp = 38, SunSolar = 400, SunWind = 5;
        public string WindUnit = "kmh";
        public bool ExcludeSuspect = true;
        public bool DevTools = false;
        public string StationId = "";       // a beallitasok.ini-ből (letöltő)
        public int RainGapMin = 30;         // esőesemény vége: ennyi perc eső nélkül
        public double CloudburstRate = 30;  // felhőszakadás: legalább ennyi mm/h
        public double FrontTempDrop = 4, FrontGust = 40, FrontTurn = 45;          // frontátvonulás
        public double StormRate = 20, StormGust = 45, StormTempDrop = 3;          // zivatar
        public int AfternoonFrom = 12, AfternoonTo = 18;                           // "délután" a szélstatisztikában
        public double WindyKmh = 20;        // 3 Bft fölött = legalább 4 Bft = 20 km/h
        // gyanús mérések: melyik szabály jelöljön
        public bool FlagQc0 = true, FlagQcNeg = true, FlagRange = true, FlagSpike = true, FlagSun = true, FlagRestart = true;
        public int RestartMin = 60;         // ennél hosszabb kiesés utáni első mérés gyanús
        public bool FlagRainJump = true, FlagFrozenWind = true;
        public double RainJumpRate = 200;   // csapadékugrás: két mérés között ennél nagyobb (mm/h) növekedés
        public double MeltTemp = 2, MeltMm = 5;   // olvadás: legalább ennyi mm egy lépésben, legfeljebb ennyi °C-on
        public double FrozenHours = 3, FrozenTemp = 1;   // befagyott szélmérő: legalább ennyi órán át 0 szél és lökés, ennyi °C alatt
        // helyszín (Beállítások › Helyszín)
        public string PlaceName = "";       // üres: az állomás WU-helyneve (pl. Budaörs)
        public string TimeZone = "Europe/Budapest";
        public double FallbackLat = 47.46, FallbackLon = 18.96;   // csak ha az adatokban nincs koordináta
        public double HeatWarn1 = 25, HeatWarn3 = 27;              // hőség-figyelő: napi középhőmérséklet (I., ill. III. fokozat)
        public double PlausibleTmax = 41.9;                        // e fölötti hőmérséklet-csúcs „ellenőrizetlen rekord” (alapból az országos rekord)
        public string Currency = "Ft";
        // felhőzet-becslés (ƒx ablak)
        public double CloudKrs = 1.0;       // a mért sugárzás szorzója (érzékelő / 5 perces csúcsérték korrekciója)
        public double CloudA = 0.75, CloudB = 3.4;   // Kasten–Czeplak állandók
        public double CloudClearK = 0.7;    // e fölötti (sima) tisztaégbolt-index derültnek számít
        public double CloudHmin = 20;       // ennél alacsonyabb napnál nappal nem becsül (°)
        public int CloudWindowMin = 30;     // az átlag és a szórás ablaka (perc)
        public double CloudCuStd = 0.15;    // a k szórása e fölött: gomolyfelhős
        public double CloudNightClear = 0.3, CloudNightPart = 0.1;   // éjjeli lehűlés (°C/óra): derült / részben felhős határa
        public double CloudNightWind = 5;   // éjjel e fölötti szélnél (km/h) nem becsül

        public List<string> Warnings = new List<string>();

        const string Template =
@"# =====================================================================
#  WStation – Megjelenítő – beállítások
#  A '#' kezdetű sorok megjegyzések. Formátum: Kulcs=Érték
#  Tizedesjelnek pontot vagy vesszőt is írhatsz.
# =====================================================================

# --- Helyszín (a Beállítások › Helyszín részben is állítható) ---
# A hely neve a felületen és a jelentésekben; üresen az állomás WU-helyneve
HelyNev=
# Időzóna (IANA-név, pl. Europe/Budapest, Europe/Vienna, Europe/London)
Idozona=Europe/Budapest
# Tartalék koordináták – csak ha az adatokban nincs lat/lon (napkelte, hold, párolgás)
TartalekSzelesseg=47.46
TartalekHosszusag=18.96
# Hőség-figyelő: napi középhőmérséklet (°C) – I. fokozat 1 napig, II. 3 napig, III. 3 napig
HosegFigyelo1=25
HosegFigyelo3=27
# Hihetőségi határ (°C): e fölötti csúcs a rekordoknál „ellenőrizetlen” jelzést kap (alapból az országos rekord, 41,9 °C)
HihetoMaxHomerseklet=41.9
# Pénznem a vízdíjhoz
Penznem=Ft

# --- Felhőzet becslése (a ƒx ablakban is állítható) ---
# A mért napsugárzás szorzója (érzékelő-eltérés; a METAR-ellenőrzés javasolhat értéket)
FelhoKRs=1.0
# Kasten–Czeplak állandók: k = 1 - A * (N/8)^B
FelhoA=0.75
FelhoB=3.4
# Derült-küszöb: a tisztaégbolt-index e fölött derült (a burkoló a legtisztább napokat adja)
FelhoDerultK=0.7
# Nappal csak ennél magasabb napnál becsül (fok)
FelhoNapMin=20
# Az átlag és a szórás ablaka (perc)
FelhoAblakPerc=30
# A tisztaégbolt-index szórása e fölött: gomolyfelhős (hol süt, hol nem)
FelhoGomolySzoras=0.15
# Éjjel: lehűlés °C/óra szélcsendben – e fölött derült, e fölött részben felhős, alatta felhős
FelhoEjDerult=0.3
FelhoEjReszben=0.1
# Éjjel ennél erősebb szélnél (km/h) nem becsül
FelhoEjSzelMax=5

# --- Statisztikai küszöbök (°C) ---
# Hőségnap: a napi maximum legalább ennyi
HosegNap=30
# Trópusi éjszaka: a napi minimum legalább ennyi
TropusiEjszaka=20
# Fagyos nap: a napi minimum ennél kisebb
FagyosNap=0
# Téli nap: a napi maximum ennél kisebb
TeliNap=0

# --- Adathiány ---
# Ha két mérés között ennél több perc telik el, a grafikon megszakad
HianyPerc=15

# --- Gyanús értékek jelölése (semmit nem töröl, csak megjelöl) ---
UvMax=15
HomersekletMin=-35
HomersekletMax=45
LegnyomasMin=950
LegnyomasMax=1060
CsapadekIntenzitasMax=300
SzellokesMax=200
NapsugarzasMax=1500
# Hirtelen ugrás: ennyi fokot ugrik 5 perc alatt, majd vissza
UgrasFok=4
# Napsütés miatti túlmelegedés gyanúja: legalább ennyi fok,
# legalább ennyi W/m² napsugárzás és legfeljebb ennyi km/h szél mellett
NapsutesHomerseklet=38
NapsutesSugarzas=400
NapsutesSzel=5
# A rekordoknál alapból zárja-e ki a gyanús adatokat (true/false)
GyanusKizarasa=true

# --- Megjelenítés ---
# Szél mértékegysége induláskor: kmh, csomo, beaufort
SzelEgyseg=kmh

# --- Esőesemények ---
# Egy zápor vége: ennyi perc eső nélkül
EsoesemenySzunetPerc=30
# Felhőszakadás jelölése: legalább ennyi mm/h
FelhoszakadasMmH=30

# --- Front- és zivatardetektor ---
# Frontátvonulás: legalább ennyi °C lehűlés 1 órán belül, és lökés vagy szélfordulás
FrontHomersekletEses=4
FrontLokes=40
FrontSzelfordulas=45
# Zivatar: csapadékintenzitás (mm/h), lökés (km/h) és lehűlés (°C)
ZivatarIntenzitas=20
ZivatarLokes=45
ZivatarLehules=3

# --- Gyanús mérések: melyik szabály jelöljön (true/false) ---
# A WU minőségellenőrzése: qcStatus = 0 (bizonytalan) és -1 (hibás)
JelolWuBizonytalan=true
JelolWuHibas=true
# A program saját szabályai
JelolLehetetlen=true
JelolUgras=true
JelolNapsutes=true
JelolKieses=true
# Kiesés: ennél több perc szünet utáni első mérés gyanús
KiesesPerc=60
# Csapadékugrás: két mérés között ennél nagyobb növekedés (mm/h), vagy fagypont körül egyszerre sok (olvadó hó);
# a napi összeg megmarad, csak az intenzitásrekordból és a felhőszakadásokból marad ki
JelolCsapadekUgras=true
CsapadekUgrasMmH=200
OlvadasHomerseklet=2
OlvadasMm=5
# Befagyott szélmérő: legalább ennyi órán át pontosan 0 szél és lökés, legfeljebb ennyi °C-on
JelolBefagyottSzel=true
BefagyottOra=3
BefagyottHomerseklet=1

# --- Szél-statisztika ---
# A „szeles délután” időablaka (óra) és határa: 20 km/h = 4 Bft (3 Bft fölött)
DelutanTol=12
DelutanIg=18
SzelesDelutanKmh=20
";

        public static Settings Load(Layout L)
        {
            var s = new Settings();
            string path = L.Settings;
            try
            {
                Guard.CreateDirectory(L.App);
                if (!File.Exists(path))
                    Guard.WriteAllText(path, Template.Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(true));
                else
                    Kozos.IniMerge.Merge(path, Template); // frissítés után az új kulcsok bekerülnek
            }
            catch (Exception ex) { s.Warnings.Add("A megjelenito.ini nem hozható létre: " + ex.Message); }

            var res = FromDict(ReadIni(path), L, s);
            Tz.Set(res.TimeZone);   // minden napváltás, dátum és óra ebben az időzónában
            return res;
        }

        // a beállítások egy kulcs–érték listából (a fájlból, vagy az előnézethez módosított másolatból)
        public static Settings FromDict(Dictionary<string, string> d, Layout L, Settings s)
        {
            string v;
            s.HotDay = Num(d, "HosegNap", s.HotDay, s);
            s.TropicalNight = Num(d, "TropusiEjszaka", s.TropicalNight, s);
            s.FrostDay = Num(d, "FagyosNap", s.FrostDay, s);
            s.IceDay = Num(d, "TeliNap", s.IceDay, s);
            s.GapMinutes = (int)Num(d, "HianyPerc", s.GapMinutes, s);
            if (s.GapMinutes < 6) s.GapMinutes = 6;
            s.UvMax = Num(d, "UvMax", s.UvMax, s);
            s.TempMin = Num(d, "HomersekletMin", s.TempMin, s);
            s.TempMax = Num(d, "HomersekletMax", s.TempMax, s);
            s.PresMin = Num(d, "LegnyomasMin", s.PresMin, s);
            s.PresMax = Num(d, "LegnyomasMax", s.PresMax, s);
            s.RainRateMax = Num(d, "CsapadekIntenzitasMax", s.RainRateMax, s);
            s.GustMax = Num(d, "SzellokesMax", s.GustMax, s);
            s.SolarMax = Num(d, "NapsugarzasMax", s.SolarMax, s);
            s.SpikeDeg = Num(d, "UgrasFok", s.SpikeDeg, s);
            s.SunTemp = Num(d, "NapsutesHomerseklet", s.SunTemp, s);
            s.SunSolar = Num(d, "NapsutesSugarzas", s.SunSolar, s);
            s.SunWind = Num(d, "NapsutesSzel", s.SunWind, s);
            if (d.TryGetValue("GyanusKizarasa", out v)) s.ExcludeSuspect = !(v.ToLowerInvariant() == "false" || v == "0" || v.ToLowerInvariant() == "nem");
            if (d.TryGetValue("SzelEgyseg", out v))
            {
                v = v.ToLowerInvariant();
                if (v == "kmh" || v == "csomo" || v == "beaufort") s.WindUnit = v;
                else s.Warnings.Add("Ismeretlen SzelEgyseg: " + v + " (kmh, csomo vagy beaufort lehet)");
            }
            if (d.TryGetValue("FejlesztoiEszkozok", out v)) s.DevTools = v.ToLowerInvariant() == "true";
            s.RainGapMin = (int)Num(d, "EsoesemenySzunetPerc", s.RainGapMin, s);
            s.CloudburstRate = Num(d, "FelhoszakadasMmH", s.CloudburstRate, s);
            s.FrontTempDrop = Num(d, "FrontHomersekletEses", s.FrontTempDrop, s);
            s.FrontGust = Num(d, "FrontLokes", s.FrontGust, s);
            s.FrontTurn = Num(d, "FrontSzelfordulas", s.FrontTurn, s);
            s.StormRate = Num(d, "ZivatarIntenzitas", s.StormRate, s);
            s.StormGust = Num(d, "ZivatarLokes", s.StormGust, s);
            s.StormTempDrop = Num(d, "ZivatarLehules", s.StormTempDrop, s);
            s.AfternoonFrom = (int)Num(d, "DelutanTol", s.AfternoonFrom, s);
            s.AfternoonTo = (int)Num(d, "DelutanIg", s.AfternoonTo, s);
            s.WindyKmh = Num(d, "SzelesDelutanKmh", s.WindyKmh, s);
            s.FlagQc0 = Bool(d, "JelolWuBizonytalan", true); s.FlagQcNeg = Bool(d, "JelolWuHibas", true);
            s.FlagRange = Bool(d, "JelolLehetetlen", true); s.FlagSpike = Bool(d, "JelolUgras", true);
            s.FlagSun = Bool(d, "JelolNapsutes", true); s.FlagRestart = Bool(d, "JelolKieses", true);
            s.RestartMin = Math.Max(10, Math.Min(1440, (int)Num(d, "KiesesPerc", s.RestartMin, s)));
            s.FlagRainJump = Bool(d, "JelolCsapadekUgras", true); s.FlagFrozenWind = Bool(d, "JelolBefagyottSzel", true);
            s.RainJumpRate = Num(d, "CsapadekUgrasMmH", s.RainJumpRate, s);
            s.MeltTemp = Num(d, "OlvadasHomerseklet", s.MeltTemp, s); s.MeltMm = Num(d, "OlvadasMm", s.MeltMm, s);
            s.FrozenHours = Num(d, "BefagyottOra", s.FrozenHours, s); s.FrozenTemp = Num(d, "BefagyottHomerseklet", s.FrozenTemp, s);
            // helyszín
            if (d.TryGetValue("HelyNev", out v)) s.PlaceName = v.Trim().Length > 40 ? v.Trim().Substring(0, 40) : v.Trim();
            if (d.TryGetValue("Idozona", out v) && v.Trim().Length > 0)
            {
                if (Tz.WindowsId(v.Trim()) != null) s.TimeZone = v.Trim();
                else s.Warnings.Add("Ismeretlen időzóna: " + v.Trim() + " – marad: " + s.TimeZone);
            }
            s.FallbackLat = Math.Max(-90, Math.Min(90, Num(d, "TartalekSzelesseg", s.FallbackLat, s)));
            s.FallbackLon = Math.Max(-180, Math.Min(180, Num(d, "TartalekHosszusag", s.FallbackLon, s)));
            s.HeatWarn1 = Num(d, "HosegFigyelo1", s.HeatWarn1, s);
            s.HeatWarn3 = Num(d, "HosegFigyelo3", s.HeatWarn3, s);
            s.PlausibleTmax = Num(d, "HihetoMaxHomerseklet", s.PlausibleTmax, s);
            if (d.TryGetValue("Penznem", out v) && v.Trim().Length > 0) s.Currency = v.Trim().Length > 6 ? v.Trim().Substring(0, 6) : v.Trim();
            // felhőzet
            s.CloudKrs = Math.Max(0.3, Math.Min(2, Num(d, "FelhoKRs", s.CloudKrs, s)));
            s.CloudA = Math.Max(0.3, Math.Min(1, Num(d, "FelhoA", s.CloudA, s)));
            s.CloudB = Math.Max(1, Math.Min(6, Num(d, "FelhoB", s.CloudB, s)));
            s.CloudClearK = Math.Max(0.5, Math.Min(1.1, Num(d, "FelhoDerultK", s.CloudClearK, s)));
            s.CloudHmin = Math.Max(5, Math.Min(40, Num(d, "FelhoNapMin", s.CloudHmin, s)));
            s.CloudWindowMin = (int)Math.Max(10, Math.Min(120, Num(d, "FelhoAblakPerc", s.CloudWindowMin, s)));
            s.CloudCuStd = Math.Max(0.03, Math.Min(0.5, Num(d, "FelhoGomolySzoras", s.CloudCuStd, s)));
            s.CloudNightClear = Num(d, "FelhoEjDerult", s.CloudNightClear, s);
            s.CloudNightPart = Num(d, "FelhoEjReszben", s.CloudNightPart, s);
            s.CloudNightWind = Num(d, "FelhoEjSzelMax", s.CloudNightWind, s);

            // a letöltő beállításából az állomás
            var dl = ReadIni(L.DlIni);
            if (dl.TryGetValue("StationId", out v)) s.StationId = v.Trim().ToUpperInvariant();
            return s;
        }

        static bool Bool(Dictionary<string, string> d, string key, bool def)
        {
            string v;
            if (!d.TryGetValue(key, out v)) return def;
            v = v.Trim().ToLowerInvariant();
            return !(v == "false" || v == "0" || v == "nem" || v == "no");
        }

        // a Megjelenítőből szerkeszthető kulcsok: [név] = (min, max, egész?, logikai?)
        public static readonly Dictionary<string, double[]> Editable = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "HosegNap", new double[] { 20, 45, 0, 0 } }, { "TropusiEjszaka", new double[] { 10, 30, 0, 0 } }, { "FagyosNap", new double[] { -10, 5, 0, 0 } }, { "TeliNap", new double[] { -10, 5, 0, 0 } },
            { "HianyPerc", new double[] { 6, 240, 1, 0 } },
            { "UvMax", new double[] { 10, 30, 0, 0 } }, { "HomersekletMin", new double[] { -60, 0, 0, 0 } }, { "HomersekletMax", new double[] { 30, 60, 0, 0 } },
            { "LegnyomasMin", new double[] { 850, 1000, 0, 0 } }, { "LegnyomasMax", new double[] { 1020, 1100, 0, 0 } }, { "CsapadekIntenzitasMax", new double[] { 50, 1000, 0, 0 } },
            { "SzellokesMax", new double[] { 80, 400, 0, 0 } }, { "NapsugarzasMax", new double[] { 1000, 2000, 0, 0 } }, { "UgrasFok", new double[] { 1, 20, 0, 0 } },
            { "NapsutesHomerseklet", new double[] { 25, 50, 0, 0 } }, { "NapsutesSugarzas", new double[] { 100, 1200, 0, 0 } }, { "NapsutesSzel", new double[] { 0, 30, 0, 0 } },
            { "EsoesemenySzunetPerc", new double[] { 5, 360, 1, 0 } }, { "FelhoszakadasMmH", new double[] { 5, 200, 0, 0 } },
            { "FrontHomersekletEses", new double[] { 1, 15, 0, 0 } }, { "FrontLokes", new double[] { 15, 150, 0, 0 } }, { "FrontSzelfordulas", new double[] { 10, 180, 0, 0 } },
            { "ZivatarIntenzitas", new double[] { 2, 200, 0, 0 } }, { "ZivatarLokes", new double[] { 15, 150, 0, 0 } }, { "ZivatarLehules", new double[] { 0.5, 15, 0, 0 } },
            { "DelutanTol", new double[] { 0, 23, 1, 0 } }, { "DelutanIg", new double[] { 1, 24, 1, 0 } }, { "SzelesDelutanKmh", new double[] { 5, 100, 0, 0 } },
            { "JelolWuBizonytalan", new double[] { 0, 1, 0, 1 } }, { "JelolWuHibas", new double[] { 0, 1, 0, 1 } }, { "JelolLehetetlen", new double[] { 0, 1, 0, 1 } },
            { "JelolUgras", new double[] { 0, 1, 0, 1 } }, { "JelolNapsutes", new double[] { 0, 1, 0, 1 } }, { "JelolKieses", new double[] { 0, 1, 0, 1 } },
            { "KiesesPerc", new double[] { 10, 1440, 1, 0 } }, { "GyanusKizarasa", new double[] { 0, 1, 0, 1 } },
            { "JelolCsapadekUgras", new double[] { 0, 1, 0, 1 } }, { "CsapadekUgrasMmH", new double[] { 50, 1000, 0, 0 } },
            { "OlvadasHomerseklet", new double[] { -5, 5, 0, 0 } }, { "OlvadasMm", new double[] { 1, 50, 0, 0 } },
            { "JelolBefagyottSzel", new double[] { 0, 1, 0, 1 } }, { "BefagyottOra", new double[] { 1, 24, 0, 0 } }, { "BefagyottHomerseklet", new double[] { -10, 5, 0, 0 } },
            { "TartalekSzelesseg", new double[] { -90, 90, 0, 0 } }, { "TartalekHosszusag", new double[] { -180, 180, 0, 0 } },
            { "HosegFigyelo1", new double[] { 15, 40, 0, 0 } }, { "HosegFigyelo3", new double[] { 15, 40, 0, 0 } }, { "HihetoMaxHomerseklet", new double[] { 25, 60, 0, 0 } },
            { "FelhoKRs", new double[] { 0.3, 2, 0, 0 } }, { "FelhoA", new double[] { 0.3, 1, 0, 0 } }, { "FelhoB", new double[] { 1, 6, 0, 0 } }, { "FelhoDerultK", new double[] { 0.5, 1.1, 0, 0 } },
            { "FelhoNapMin", new double[] { 5, 40, 0, 0 } }, { "FelhoAblakPerc", new double[] { 10, 120, 1, 0 } }, { "FelhoGomolySzoras", new double[] { 0.03, 0.5, 0, 0 } },
            { "FelhoEjDerult", new double[] { 0.2, 4, 0, 0 } }, { "FelhoEjReszben", new double[] { 0, 2, 0, 0 } }, { "FelhoEjSzelMax", new double[] { 1, 30, 0, 0 } }
        };
        // szöveges, szerkeszthető kulcsok (a helyszínhez)
        public static readonly HashSet<string> TextKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "HelyNev", "Idozona", "Penznem" };

        // az alapértékek a sablonból
        public static Dictionary<string, string> Defaults()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in Template.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return d;
        }

        // értékek érvényesítése; hiba esetén üzenet, egyébként null és a normalizált érték
        public static string Check(string key, string val, out string norm)
        {
            norm = null;
            if (TextKeys.Contains(key))
            {
                string t = (val ?? "").Trim();
                if (t.IndexOfAny(new[] { '\r', '\n', '=', '#' }) >= 0) return key + ": nem tartalmazhat sortörést, „=” vagy „#” jelet";
                if (key.Equals("HelyNev", StringComparison.OrdinalIgnoreCase) && t.Length > 40) return "A hely neve legfeljebb 40 karakter.";
                if (key.Equals("Penznem", StringComparison.OrdinalIgnoreCase) && (t.Length < 1 || t.Length > 6)) return "A pénznem 1–6 karakter (pl. Ft, €, EUR).";
                if (key.Equals("Idozona", StringComparison.OrdinalIgnoreCase) && Tz.WindowsId(t) == null) return "Ismeretlen időzóna: " + t;
                norm = t;
                return null;
            }
            double[] r;
            if (!Editable.TryGetValue(key, out r)) return "Nem szerkeszthető kulcs: " + key;
            val = (val ?? "").Trim();
            if (r[3] == 1)
            {
                string v = val.ToLowerInvariant();
                if (v == "true" || v == "1") { norm = "true"; return null; }
                if (v == "false" || v == "0") { norm = "false"; return null; }
                return key + ": igaz/hamis érték kell";
            }
            double x;
            if (!double.TryParse(val.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out x) || double.IsNaN(x) || double.IsInfinity(x)) return key + ": számot adj meg";
            if (r[2] == 1 && x != Math.Floor(x)) return key + ": egész szám kell";
            if (x < r[0] || x > r[1]) return key + ": " + r[0].ToString(CultureInfo.InvariantCulture) + " és " + r[1].ToString(CultureInfo.InvariantCulture) + " között";
            norm = x.ToString(CultureInfo.InvariantCulture);
            return null;
        }

        public static Dictionary<string, string> ReadIni(string path)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return d;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                        int eq = line.IndexOf('=');
                        if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                }
            }
            catch { }
            return d;
        }

        static double Num(Dictionary<string, string> d, string k, double def, Settings s)
        {
            string v;
            if (!d.TryGetValue(k, out v) || v.Length == 0) return def;
            double r;
            if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out r)) return r;
            s.Warnings.Add("Érvénytelen érték a megjelenito.ini-ben: " + k + "=" + v + " (alapérték: " + def + ")");
            return def;
        }
    }
}
