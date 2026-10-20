using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Idojaras
{
    // ===================================================================== beállítások (appdata_megjelenítő\szarazsag.json)
    // A mezőnevek a JSON-ban és a felületen is ugyanezek. Az alapértékek a szabványos (FAO-56, USDA) értékek.

    public class DGlobal
    {
        public double solarFactor = 1.10;   // napsugárzás-korrekció (a mintaadat tiszta nyári napjai alapján)
        public bool autoCal = false;        // havi automatikus kalibrálás a legtisztább napokból
        public double windHeight = 6.0;     // a szélmérő magassága (m)
        public double roofFactor = 1.0;     // tetőhelyzet-szorzó a 2 m-es szélre
        public double albedo = 0.23;        // FAO-56 referencia-gyep
        public double elevation = 164;      // tengerszint feletti magasság (m)
        public bool startFull = true;       // az első napon telített talaj (téli feltöltődés)
        public bool pAdjust = true;         // p korrekciója a párolgás szerint (FAO-56, 22. táblázat lábjegyzete)
        public string springStart = "04-01", fullLeaf = "06-01", fallStart = "09-15", dormant = "11-01";
    }

    public class DSoil
    {
        public string name = "Saját kert: 15 cm termőföld agyagon";
        public double topDepth = 0.15;      // a felső réteg vastagsága (m)
        public double topFC = 0.27, topWP = 0.115;   // felső réteg: szántóföldi vízkapacitás, hervadáspont (m³/m³)
        public double subFC = 0.36, subWP = 0.22;    // alsó réteg
        public double rew = 9;              // könnyen párolgó víz a felszínen (mm)
        public double cn = 77;              // SCS görbeszám normál nedvességnél (AMC II)
    }

    public class DRunoff
    {
        public bool enabled = true;
        public string method = "merleg";    // "merleg": a felső réteg telítettségéből; "5nap": 5 napos előzetes csapadék
        public double ia = 0.2;             // kezdeti veszteség aránya (Ia = ia·S)
        public double dry = 13, wet = 28;   // AMC határok nyugalmi időszakban (mm / 5 nap)
        public double dryGrow = 36, wetGrow = 53; // AMC határok vegetációs időben
    }

    public class DProfile
    {
        public string id = "", name = "", type = "gyep";   // felszin | gyep | bokor | cserep | fa
        public bool enabled = true;
        public string fajta = "";            // fáknál: barack | meggy | altalanos | egyedi
        public double kc = 1.0;              // állandó Kc (felszín, cserép)
        public double kcIni = 0.55, kcMid = 0.90, kcEnd = 0.65, kcWinter = 0.30;
        public double zr = 0.3;              // gyökérzóna mélysége (m)
        public double p = 0.5;               // könnyen felvehető víz aránya
        // gyep
        public double sprinklerRate = 38;    // szórófej kijuttatási intenzitás (mm/h)
        public double maxSession = 12;       // egy menetben kijuttatható víz agyagos talajon (mm) – felette elfolyik
        public double lawnArea = 100;        // m² (literes bejegyzésekhez)
        // bokor
        public double area = 1.0;            // bokronkénti nedvesített/lombkorona terület (m²)
        public double count = 1;             // bokrok száma
        public double dripCount = 2, dripLph = 4;  // csepegtetők bokronként (fáknál fánként) és l/h
        // cserép
        public double potLiters = 10, potDiam = 28, availFrac = 0.35;
        public string place = "szabad";      // szabad | felig | fedett
        // fa
        public double canopyD = 4, height = 3.5, trunkCm = 60, age = 10;
        public string cover = "csupasz";     // csupasz | fu | mulcs
        public double formFactor = 0.45, sapFrac = 0.7, waterFrac = 0.25;
        // automata öntözés
        public bool autoIrr = false;
        public string autoDays = "1,4";      // a hét napjai (1 = hétfő … 7 = vasárnap)
        public double autoMinutes = 10;
        public string autoFrom = "05-01", autoTo = "09-30";
    }

    public class DFwi
    {
        public double ffmc0 = 85, dmc0 = 6, dc0 = 15;   // szabványos tavaszi kezdőértékek (Van Wagner 1987)
        public string seasonStart = "03-01";           // a tűzszezon kezdete: ekkor indul újra a számítás
        public double windFactor = 1.0;                // a szél átszámítása a szabványos 10 m-re (a te mérőd ~6 m)
        public double noonHourUtc = 11;                // déli észlelés: 12:00 helyi téli idő = 11:00 UTC
    }

    public class DMilestone { public string name = ""; public double gdd; }

    public class DGdd
    {
        public double baseT = 10, cap = 30;           // bázis- és felső hőmérséklet (°C)
        public string start = "01-01";                // az összegzés kezdete évente
        public bool kcByGdd = false;                  // a vegetációs szakaszok a hőösszegből (nem fix dátumból)
        public double phenoBase = 5;                  // a szakaszokhoz használt bázis (°C)
        public double springGdd = 120, fullLeafGdd = 450;   // rügyfakadás és teljes lomb (fokcelsiusnap, phenoBase felett)
        public List<DMilestone> milestones = new List<DMilestone>
        {
            new DMilestone { name = "Kajszi/barack virágzás (becslés)", gdd = 60 },
            new DMilestone { name = "Meggy virágzás (becslés)", gdd = 110 },
            new DMilestone { name = "Szőlő virágzás (becslés)", gdd = 400 }
        };
        public double milestoneBase = 5;
    }

    public class DVpd { public double low = 0.4, idealLo = 0.8, idealHi = 1.2, high = 1.6, severe = 2.5; }

    public class DIrrigation
    {
        public string date = "", zone = "", unit = "mm", note = "";
        public string diaryId;   // ha a kerti naplóból jött: a naplóbejegyzés azonosítója (a kettő együtt törlődik)
        public double amount;
    }

    // mentett öntözési program: melyik zóna, mely napokon, mennyit (egy menetben)
    public class DProgram
    {
        public string id = "", name = "", zone = "", days = "1,4", unit = "perc";
        public int every;                    // 0: a hét napjai (days); N > 0: N naponta (az időszak első napjától)
        public double amount = 10;
    }
    // egy program érvényessége: tól–ig (yyyy-MM-dd), vagy évente ismétlődve (HH-NN)
    public class DSchedule
    {
        public string id = "", program = "", from = "", to = "";
        public bool yearly;
    }

    public class DConfig
    {
        public int version = 2;
        public List<DProgram> programs = new List<DProgram>();
        public List<DSchedule> schedules = new List<DSchedule>();
        public DGlobal global = new DGlobal();
        public DSoil soil = new DSoil();
        public DRunoff runoff = new DRunoff();
        public List<DProfile> profiles = new List<DProfile>();
        public List<DIrrigation> log = new List<DIrrigation>();
        public DFwi fwi = new DFwi();
        public DGdd gdd = new DGdd();
        public DVpd vpd = new DVpd();

        public static DConfig Defaults()
        {
            var c = new DConfig();
            c.profiles.Add(new DProfile { id = "felszin", name = "Talajfelszín (felső 10 cm)", type = "felszin", kc = 1.0, zr = 0.10 });
            c.profiles.Add(new DProfile { id = "gyep", name = "Gyep (szórófejes)", type = "gyep", kcMid = 0.90, kcWinter = 0.55, zr = 0.30, p = 0.5 });
            c.profiles.Add(new DProfile { id = "bokor", name = "Bokrok (csepegtetett)", type = "bokor", kcMid = 0.80, kcWinter = 0.30, zr = 0.60, p = 0.5, area = 1.0, count = 1 });
            c.profiles.Add(new DProfile { id = "cserep1", name = "Közepes cserép", type = "cserep", kc = 1.0, p = 0.35, potLiters = 10, potDiam = 28, place = "szabad" });
            c.profiles.Add(new DProfile { id = "barack", name = "Barackfa", type = "fa", fajta = "barack", kcIni = 0.55, kcMid = 0.90, kcEnd = 0.65, kcWinter = 0.15, zr = 1.5, p = 0.5, canopyD = 4, height = 3.5, trunkCm = 60, age = 10 });
            c.profiles.Add(new DProfile { id = "meggy", name = "Meggyfa", type = "fa", fajta = "meggy", kcIni = 0.45, kcMid = 0.95, kcEnd = 0.70, kcWinter = 0.15, zr = 1.5, p = 0.5, canopyD = 4, height = 4, trunkCm = 70, age = 12 });
            c.profiles.Add(new DProfile { id = "lombos", name = "Általános lombos fa", type = "fa", fajta = "altalanos", enabled = false, kcIni = 0.50, kcMid = 0.95, kcEnd = 0.70, kcWinter = 0.15, zr = 1.5, p = 0.5, canopyD = 6, height = 8, trunkCm = 120, age = 30 });
            return c;
        }
    }

    // ===================================================================== tárolás

    static class DroughtStore
    {
        static readonly object Lock = new object();
        static DConfig cached;
        public static string RepairNote;
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        static string FilePath { get { return Path.Combine(AppState.L.App, "szarazsag.json"); } }
        static string PresetDir { get { return Path.Combine(AppState.L.App, "presetek"); } }

        public static DConfig Get()
        {
            lock (Lock)
            {
                if (cached != null) return cached;
                try
                {
                    if (File.Exists(FilePath))
                    {
                        cached = Js.Deserialize<DConfig>(File.ReadAllText(FilePath, Encoding.UTF8));
                        Normalize(cached);
                        var rep = Repair(cached);
                        if (rep.Count > 0)
                        {
                            Program.Log("A szarazsag.json hibás értékeket tartalmazott, ezek az alapértékre álltak: " + string.Join("; ", rep));
                            RepairNote = "Hibás értékek az alapértékre álltak: " + string.Join("; ", rep);
                            try { Guard.Copy(FilePath, FilePath + ".hibas_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")); } catch { }
                        }
                        return cached;
                    }
                }
                catch (Exception ex)
                {
                    Program.Log("A szarazsag.json sérült, félreteszem és alapértékekkel indulok: " + ex.Message);
                    try { Guard.Move(FilePath, FilePath + ".hibas_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")); } catch { }
                }
                cached = DConfig.Defaults();
                return cached;
            }
        }

        static void Normalize(DConfig c)
        {
            if (c.global == null) c.global = new DGlobal();
            if (c.soil == null) c.soil = new DSoil();
            if (c.runoff == null) c.runoff = new DRunoff();
            if (c.profiles == null || c.profiles.Count == 0) c.profiles = DConfig.Defaults().profiles;
            if (c.log == null) c.log = new List<DIrrigation>();
            if (c.fwi == null) c.fwi = new DFwi();
            if (c.gdd == null) c.gdd = new DGdd();
            if (c.gdd.milestones == null) c.gdd.milestones = new List<DMilestone>();
            if (c.vpd == null) c.vpd = new DVpd();
            if (c.programs == null) c.programs = new List<DProgram>();
            if (c.schedules == null) c.schedules = new List<DSchedule>();
            int k = 0;
            foreach (var p in c.profiles) if (string.IsNullOrEmpty(p.id)) p.id = "p" + (++k) + "_" + DateTime.Now.Ticks % 100000;
            // 1.x–2.0: a profil „automata öntözés” mezői → mentett program + évente ismétlődő időszak
            if (c.version < 2)
            {
                foreach (var p in c.profiles.Where(x => x.autoIrr))
                {
                    var pg = new DProgram { id = "pg_" + p.id, name = "Automata – " + p.name, zone = p.id, days = p.autoDays ?? "1,4", amount = p.autoMinutes, unit = "perc" };
                    c.programs.Add(pg);
                    c.schedules.Add(new DSchedule { id = "sc_" + p.id, program = pg.id, from = p.autoFrom ?? "05-01", to = p.autoTo ?? "09-30", yearly = true });
                    p.autoIrr = false;
                }
                c.version = 2;
            }
        }

        static bool ValidDate(string s) { DateTime d; return DateTime.TryParseExact(s ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d); }
        static void VPrograms(DConfig c)
        {
            foreach (var pg in c.programs) VProgram(c, pg);
            foreach (var sc in c.schedules) VSchedule(c, sc);
        }
        static void VProgram(DConfig c, DProgram pg)
        {
            {
                string n = "program „" + pg.name + "”: ";
                if (string.IsNullOrWhiteSpace(pg.name) || pg.name.Length > 60) throw new Exception(n + "a név 1–60 karakter");
                if (!c.profiles.Any(p => p.id == pg.zone)) throw new Exception(n + "ismeretlen zóna");
                if (pg.unit != "mm" && pg.unit != "l" && pg.unit != "perc") throw new Exception(n + "az egység mm, l vagy perc");
                R(n + "mennyiség", pg.amount, 0, 100000);
                if (pg.every < 0 || pg.every > 60) throw new Exception(n + "x naponta: 0–60");
                if (pg.every == 0 && !System.Text.RegularExpressions.Regex.IsMatch(pg.days ?? "", @"^\s*[1-7](\s*,\s*[1-7])*\s*$")) throw new Exception(n + "napok: 1–7 vesszővel");
            }
        }
        static void VSchedule(DConfig c, DSchedule sc)
        {
            {
                if (!c.programs.Any(p => p.id == sc.program)) throw new Exception("időszak: ismeretlen program");
                if (sc.yearly) { Md("időszak kezdete", sc.from); Md("időszak vége", sc.to); }
                else
                {
                    if (!ValidDate(sc.from) || !ValidDate(sc.to)) throw new Exception("időszak: dátum ÉÉÉÉ-HH-NN formában");
                    if (string.CompareOrdinal(sc.from, sc.to) > 0) throw new Exception("időszak: a „tól” későbbi, mint az „ig”");
                }
            }
        }

        static int MdNum(string s) { int m, d; return s != null && s.Length == 5 && int.TryParse(s.Substring(0, 2), out m) && int.TryParse(s.Substring(3, 2), out d) ? m * 100 + d : 0; }

        // az adott napon a program szerint locsol-e
        public static bool ProgramOn(DProgram pg, DSchedule sc, DateTime d)
        {
            DateTime a;
            if (sc.yearly)
            {
                int md = d.Month * 100 + d.Day, from = MdNum(sc.from), to = MdNum(sc.to);
                bool inside = from <= to ? md >= from && md <= to : md >= from || md <= to;
                if (!inside) return false;
                // febr. 29. kezdet nem szökőévben: febr. 28. (különben a DateTime kivételt dob, és leáll a mérleg)
                Func<int, DateTime> startIn = y => new DateTime(y, from / 100, Math.Min(from % 100, DateTime.DaysInMonth(y, from / 100)));
                a = startIn(d.Year);
                if (a > d) a = startIn(d.Year - 1);
            }
            else
            {
                string ds = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (string.CompareOrdinal(ds, sc.from) < 0 || string.CompareOrdinal(ds, sc.to) > 0) return false;
                a = DateTime.ParseExact(sc.from, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            if (pg.every > 0) return ((int)(d - a).TotalDays) % pg.every == 0;
            int dow = ((int)d.DayOfWeek + 6) % 7 + 1;
            return (pg.days ?? "").Split(',', ';', ' ').Any(x => x.Trim() == dow.ToString(CultureInfo.InvariantCulture));
        }

        public static DConfig Parse(string json)
        {
            var c = Js.Deserialize<DConfig>(json);
            if (c == null) throw new Exception("üres beállítás");
            Normalize(c);
            Validate(c);
            return c;
        }

        // A felület is ellenőriz, de a szerver sem fogad el lehetetlen értéket (pl. kézzel szerkesztett json esetén)
        static void R(string what, double v, double lo, double hi)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v < lo || v > hi)
                throw new Exception(what + " = " + v.ToString(CultureInfo.InvariantCulture) + " (megengedett: " + lo.ToString(CultureInfo.InvariantCulture) + " … " + hi.ToString(CultureInfo.InvariantCulture) + ")");
        }
        static void Md(string what, string s)
        {
            int m, d;
            if (s == null || s.Length != 5 || s[2] != '-' || !int.TryParse(s.Substring(0, 2), out m) || !int.TryParse(s.Substring(3, 2), out d) || m < 1 || m > 12 || d < 1 || d > DateTime.DaysInMonth(2024, m))
                throw new Exception(what + " = „" + s + "” (HH-NN formában kell, pl. 04-01)");
        }

        public static void Validate(DConfig c)
        {
            VGlobal(c); VSoil(c); VFwi(c); VVpd(c); VGdd(c);
            foreach (var p in c.profiles) VProfile(p);
            foreach (var l in c.log) R("öntözés mennyisége (" + l.date + ")", l.amount, 0, 100000);
            VPrograms(c);
        }

        // Sérült / régi, hibás értékeket tartalmazó beállítás javítása betöltéskor: a hibás rész az alapértékre áll
        public static List<string> Repair(DConfig c)
        {
            var fixedParts = new List<string>();
            var d = DConfig.Defaults();
            Action<string, Action, Action> fix = (name, check, reset) => { try { check(); } catch (Exception ex) { reset(); fixedParts.Add(name + " (" + ex.Message + ")"); } };
            fix("közös beállítások", () => VGlobal(c), () => c.global = d.global);
            fix("talaj és lefolyás", () => VSoil(c), () => { c.soil = d.soil; c.runoff = d.runoff; });
            fix("FWI", () => VFwi(c), () => c.fwi = d.fwi);
            fix("VPD", () => VVpd(c), () => c.vpd = d.vpd);
            fix("GDD", () => VGdd(c), () => c.gdd = d.gdd);
            for (int i = 0; i < c.profiles.Count; i++)
            {
                var p = c.profiles[i];
                try { VProfile(p); }
                catch (Exception ex)
                {
                    var def = d.profiles.FirstOrDefault(x => x.type == p.type && (p.type != "fa" || x.fajta == p.fajta)) ?? d.profiles.First(x => x.type == p.type || x.type == "gyep");
                    var js = Serialize(def);
                    var np = Js.Deserialize<DProfile>(js);
                    np.id = p.id; np.name = string.IsNullOrWhiteSpace(p.name) ? def.name : (p.name.Length > 60 ? p.name.Substring(0, 60) : p.name); np.enabled = p.enabled; np.type = p.type; np.fajta = p.fajta;
                    c.profiles[i] = np;
                    fixedParts.Add("profil „" + np.name + "” (" + ex.Message + ")");
                }
            }
            c.log.RemoveAll(l => double.IsNaN(l.amount) || l.amount < 0 || l.amount > 100000);
            // öntözési programok és időszakok: csak a hibás elem esik ki, a többi megmarad
            foreach (var pg in c.programs.ToList())
                try { VProgram(c, pg); } catch (Exception ex) { c.programs.Remove(pg); fixedParts.Add(ex.Message + " – a program kimaradt"); }
            foreach (var sc in c.schedules.ToList())
                try { VSchedule(c, sc); } catch (Exception ex) { c.schedules.Remove(sc); fixedParts.Add(ex.Message + " – az időszak kimaradt"); }
            return fixedParts;
        }

        static void VGlobal(DConfig c)
        {
            var g = c.global;
            R("napsugárzás-szorzó", g.solarFactor, 0.5, 2); R("szélmérő magassága", g.windHeight, 1, 100); R("tetőhelyzet-szorzó", g.roofFactor, 0.3, 2);
            R("albedó", g.albedo, 0.05, 0.5); R("tengerszint feletti magasság", g.elevation, -500, 9000);
            Md("vegetáció kezdete", g.springStart); Md("teljes lomb", g.fullLeaf); Md("lombhullás", g.fallStart); Md("nyugalmi időszak", g.dormant);
        }

        static void VSoil(DConfig c)
        {
            var s = c.soil;
            R("felső réteg vastagsága", s.topDepth, 0.01, 1); R("θFC (felső)", s.topFC, 0.02, 0.6); R("θWP (felső)", s.topWP, 0.005, 0.5);
            R("θFC (alsó)", s.subFC, 0.02, 0.6); R("θWP (alsó)", s.subWP, 0.005, 0.5); R("REW", s.rew, 1, 30); R("CN", s.cn, 30, 99);
            if (s.topWP >= s.topFC || s.subWP >= s.subFC) throw new Exception("a hervadáspont nem lehet nagyobb a szántóföldi vízkapacitásnál");
            var ro = c.runoff;
            R("Ia/S", ro.ia, 0, 0.5); R("AMC I", ro.dry, 0, 300); R("AMC III", ro.wet, 0, 300); R("AMC I (vegetáció)", ro.dryGrow, 0, 300); R("AMC III (vegetáció)", ro.wetGrow, 0, 300);
        }

        static void VFwi(DConfig c)
        {
            var f = c.fwi;
            R("FFMC kezdőérték", f.ffmc0, 0, 101); R("DMC kezdőérték", f.dmc0, 0, 500); R("DC kezdőérték", f.dc0, 0, 1500);
            R("szél-szorzó", f.windFactor, 0.3, 3); R("déli óra", f.noonHourUtc, 0, 23); Md("tűzszezon kezdete", f.seasonStart);
            if (f.noonHourUtc != Math.Floor(f.noonHourUtc)) throw new Exception("a déli óra egész szám legyen");
        }

        static void VVpd(DConfig c)
        {
            var v = c.vpd;
            R("VPD határ", v.low, 0, 10); R("VPD határ", v.severe, 0, 10);
            if (!(v.low < v.idealLo && v.idealLo < v.idealHi && v.idealHi < v.high && v.high < v.severe)) throw new Exception("a VPD-határok növekvő sorrendben legyenek");
        }

        static void VGdd(DConfig c)
        {
            var gd = c.gdd;
            R("bázishőmérséklet", gd.baseT, -10, 30); R("felső korlát", gd.cap, 10, 50); R("fenológiai bázis", gd.phenoBase, -10, 30);
            R("rügyfakadás", gd.springGdd, 0, 5000); R("teljes lomb", gd.fullLeafGdd, 0, 10000); Md("GDD kezdete", gd.start);
            if (gd.cap <= gd.baseT || gd.fullLeafGdd <= gd.springGdd) throw new Exception("a GDD-határok sorrendje hibás");
            foreach (var m in gd.milestones) R("mérföldkő (" + m.name + ")", m.gdd, 0, 10000);
        }

        static void VProfile(DProfile p)
        {
            {
                string n = "„" + p.name + "”: ";
                if (string.IsNullOrWhiteSpace(p.name) || p.name.Length > 60) throw new Exception("a profil neve 1–60 karakter legyen");
                R(n + "Kc", p.kc, 0.1, 2); R(n + "Kc kezdeti", p.kcIni, 0, 2); R(n + "Kc közép", p.kcMid, 0, 2); R(n + "Kc vég", p.kcEnd, 0, 2); R(n + "Kc tél", p.kcWinter, 0, 2);
                R(n + "Zr", p.zr, 0.05, 5); R(n + "p", p.p, 0.05, 0.95);
                R(n + "szórófej", p.sprinklerRate, 1, 100); R(n + "menetenként", p.maxSession, 1, 100); R(n + "gyepterület", p.lawnArea, 0, 100000);
                R(n + "terület", p.area, 0.01, 1000); R(n + "darab", p.count, 1, 10000); R(n + "csepegtetők", p.dripCount, 0, 100); R(n + "l/h", p.dripLph, 0.1, 100);
                R(n + "cserép térfogat", p.potLiters, 0.1, 2000); R(n + "cserép átmérő", p.potDiam, 5, 300); R(n + "víztartalom", p.availFrac, 0.05, 0.8);
                R(n + "lombkorona", p.canopyD, 0.2, 40); R(n + "magasság", p.height, 0.2, 60); R(n + "törzskerület", p.trunkCm, 1, 1000); R(n + "életkor", p.age, 0, 500);
                R(n + "alaktényező", p.formFactor, 0.1, 1); R(n + "szijács", p.sapFrac, 0.05, 1); R(n + "kivonható víz", p.waterFrac, 0.01, 0.6); R(n + "öntözés perc", p.autoMinutes, 0, 600);
                Md(n + "öntözés kezdete", p.autoFrom); Md(n + "öntözés vége", p.autoTo);
            }
        }

        public static void Save(DConfig c)
        {
            lock (Lock)
            {
                Guard.CreateDirectory(AppState.L.App);
                string tmp = FilePath + ".tmp";
                Guard.WriteAllText(tmp, Js.Serialize(c), new UTF8Encoding(false));
                if (File.Exists(FilePath)) Guard.Replace(tmp, FilePath, null); else Guard.Move(tmp, FilePath);
                cached = c;
            }
        }

        public static string Serialize(object o) { return Js.Serialize(o); }

        // presetek: presetek\<fajta>__<név>.json  ({kind, name, saved, data})
        static string SafeName(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s.Trim()) sb.Append(Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch);
            return sb.ToString();
        }

        public static void SavePreset(string kind, string name, string dataJson)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new Exception("Adj nevet a presetnek.");
            Js.DeserializeObject(dataJson); // érvényes JSON?
            Guard.CreateDirectory(PresetDir);
            var o = new Dictionary<string, object> { { "kind", kind }, { "name", name.Trim() }, { "saved", DateTime.Now.ToString("yyyy-MM-dd HH:mm") }, { "data", Js.DeserializeObject(dataJson) } };
            Guard.WriteAllText(Path.Combine(PresetDir, SafeName(kind) + "__" + SafeName(name) + ".json"), Js.Serialize(o), new UTF8Encoding(false));
        }

        public static void DeletePreset(string kind, string name)
        {
            string f = Path.Combine(PresetDir, SafeName(kind) + "__" + SafeName(name) + ".json");
            if (File.Exists(f)) Guard.Delete(f);
        }

        public static List<Dictionary<string, object>> Presets()
        {
            var res = new List<Dictionary<string, object>>();
            if (!Directory.Exists(PresetDir)) return res;
            foreach (var f in Directory.GetFiles(PresetDir, "*.json").OrderBy(x => x))
            {
                try
                {
                    var o = (Dictionary<string, object>)Js.DeserializeObject(File.ReadAllText(f, Encoding.UTF8));
                    if (o.ContainsKey("kind") && o.ContainsKey("name") && o.ContainsKey("data")) res.Add(o);
                }
                catch { }
            }
            return res;
        }
    }

    // ===================================================================== számítás

    class DProfileResult
    {
        public DProfile P;
        public double[] Dr, Depl, Ks, Etc, Irr, Kc, Taw, Raw;
        public double Area;       // m² (a literes átváltáshoz): gyep: lawnArea, bokor: area, cserép: felület, fa: lombkorona
        public double Stress30, Stress90, DrMonthAgo = double.NaN, ReserveL = double.NaN, ReserveDays = double.NaN, LitersToday = double.NaN;
    }

    class DResult
    {
        public List<string> Dates = new List<string>();
        public double[] Et0, Rain, Ro, Rs, Tmax, Tmin, CnUsed;
        public bool[] Est;
        public List<DProfileResult> Profiles = new List<DProfileResult>();
        public double[] CalRatio = new double[13], CalFactor = new double[13];  // havi kalibráció (1..12)
        public double[] Ffmc, Dmc, Dc, Isi, Bui, Fwi, Gdd, GddCum, GddPheno, Vpd;
        public double[] FwiT, FwiH, FwiW, FwiRo;   // az FWI napi (déli) bemenetei, a magyarázathoz
        public bool[] FwiEst;                       // a déli mérés hiányzott: a bemenet pótolt (13.6)
        public Dictionary<int, DateTime[]> Pheno = new Dictionary<int, DateTime[]>();   // év -> [rügyfakadás, teljes lomb] a GDD-ből
        public double SuggestedFactor = double.NaN;
    }

    static class Drought
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const double Sigma = 4.903e-9;

        public static double Es(double t) { return 0.6108 * Math.Exp(17.27 * t / (t + 237.3)); }

        // extraterresztriális sugárzás (MJ/m²/nap), FAO-56 21. egyenlet
        public static double Ra(double latDeg, int doy)
        {
            double lat = latDeg * Math.PI / 180;
            double dr = 1 + 0.033 * Math.Cos(2 * Math.PI * doy / 365);
            double dec = 0.409 * Math.Sin(2 * Math.PI * doy / 365 - 1.39);
            double ws = Math.Acos(Math.Max(-1, Math.Min(1, -Math.Tan(lat) * Math.Tan(dec))));
            return 24 * 60 / Math.PI * 0.0820 * dr * (ws * Math.Sin(lat) * Math.Sin(dec) + Math.Cos(lat) * Math.Cos(dec) * Math.Sin(ws));
        }

        public static double U2(double uz, DGlobal g)
        {
            double z = Math.Max(2.0, g.windHeight);
            return uz * 4.87 / Math.Log(67.8 * z - 5.42) * g.roofFactor;
        }

        // FAO-56 Penman–Monteith napi referencia-párolgás (mm/nap)
        public static double Et0Pm(double tmax, double tmin, double rhmax, double rhmin, double ea, double u2, double rs, double ra, DGlobal g)
        {
            double t = (tmax + tmin) / 2;
            double P = 101.3 * Math.Pow((293 - 0.0065 * g.elevation) / 293, 5.26);
            double gamma = 0.000665 * P;
            double delta = 4098 * Es(t) / Math.Pow(t + 237.3, 2);
            double es = (Es(tmax) + Es(tmin)) / 2;
            if (double.IsNaN(ea)) ea = (Es(tmin) * rhmax / 100 + Es(tmax) * rhmin / 100) / 2;
            double rso = (0.75 + 2e-5 * g.elevation) * ra;
            double rns = (1 - g.albedo) * rs;
            double rel = rso > 0 ? Math.Min(1.0, rs / rso) : 0.5;
            double rnl = Sigma * (Math.Pow(tmax + 273.16, 4) + Math.Pow(tmin + 273.16, 4)) / 2 * (0.34 - 0.14 * Math.Sqrt(Math.Max(0, ea))) * (1.35 * rel - 0.35);
            double rn = rns - rnl;
            double et0 = (0.408 * delta * rn + gamma * 900 / (t + 273) * u2 * (es - ea)) / (delta + gamma * (1 + 0.34 * u2));
            return Math.Max(0, et0);
        }

        public static double Et0Hargreaves(double tmax, double tmin, double ra)
        {
            return Math.Max(0, 0.0023 * ((tmax + tmin) / 2 + 17.8) * Math.Sqrt(Math.Max(0, tmax - tmin)) * ra * 0.408);
        }

        static int Md(string s) // "MM-NN" -> 101..1231
        {
            try { return int.Parse(s.Substring(0, 2), Inv) * 100 + int.Parse(s.Substring(3, 2), Inv); } catch { return 0; }
        }

        static DateTime On(DateTime d, string md)
        {
            int v = Md(md);
            int m = Math.Max(1, Math.Min(12, v / 100)), day = Math.Max(1, Math.Min(DateTime.DaysInMonth(d.Year, m), v % 100));
            return new DateTime(d.Year, m, day);
        }

        // a növényi szorzó adott napon (fáknál szezonális görbe, gyepnél/bokornál aktív/nyugalmi)
        public static double Kc(DProfile p, DateTime d, DGlobal g) { return Kc(p, d, g, null); }

        public static double Kc(DProfile p, DateTime d, DGlobal g, DateTime[] pheno)
        {
            if (p.type == "felszin" || p.type == "cserep") return p.kc;
            DateTime s = On(d, g.springStart), f = On(d, g.fullLeaf), a = On(d, g.fallStart), e = On(d, g.dormant);
            if (pheno != null) { s = pheno[0]; f = pheno[1] > pheno[0] ? pheno[1] : pheno[0].AddDays(30); }
            if (p.type == "gyep" || p.type == "bokor")
            {
                if (d < s || d >= e) return p.kcWinter;
                if (d < f) return p.kcWinter + (p.kcMid - p.kcWinter) * Frac(s, f, d);
                if (d < a) return p.kcMid;
                return p.kcMid + (p.kcWinter - p.kcMid) * Frac(a, e, d);
            }
            // fa: tél -> kezdeti (rügyfakadás) -> felfutás -> közép -> lombhullás -> vég -> tél
            if (d < s || d >= e) return p.kcWinter;
            if (d < f) return p.kcIni + (p.kcMid - p.kcIni) * Frac(s, f, d);
            if (d < a) return p.kcMid;
            return p.kcMid + (p.kcEnd - p.kcMid) * Frac(a, e, d);
        }

        static double Frac(DateTime a, DateTime b, DateTime d)
        {
            double tot = (b - a).TotalDays;
            return tot <= 0 ? 1 : Math.Max(0, Math.Min(1, (d - a).TotalDays / tot));
        }

        static bool Growing(DateTime d, DGlobal g) { return d >= On(d, g.springStart) && d < On(d, g.dormant); }

        public static double CnI(double cn) { return cn / (2.281 - 0.01281 * cn); }
        public static double CnIII(double cn) { return cn / (0.427 + 0.00573 * cn); }

        public static double Runoff(double p, double cn, double iaRatio)
        {
            if (p <= 0 || cn <= 0) return 0;
            double S = 25400 / Math.Min(99.9, cn) - 254;
            double ia = iaRatio * S;
            return p > ia ? (p - ia) * (p - ia) / (p - ia + S) : 0;
        }

        public static double Taw(DProfile p, DSoil s)
        {
            if (p.type == "felszin")
                return 1000 * (s.topFC - 0.5 * s.topWP) * p.zr;   // TEW
            if (p.type == "cserep")
                return p.potLiters * p.availFrac / Math.Max(1e-4, PotArea(p));
            double zr = p.zr;
            if (p.type == "fa" && p.age > 0 && p.age < 5) zr *= Math.Max(0.3, p.age / 5.0);
            double top = Math.Min(zr, s.topDepth), sub = Math.Max(0, zr - s.topDepth);
            return 1000 * ((s.topFC - s.topWP) * top + (s.subFC - s.subWP) * sub);
        }

        public static double PotArea(DProfile p) { return Math.PI * Math.Pow(p.potDiam / 200.0, 2); }
        public static double CanopyArea(DProfile p) { return Math.PI * Math.Pow(p.canopyD / 2.0, 2); }

        public static double AreaOf(DProfile p)
        {
            switch (p.type)
            {
                case "gyep": return p.lawnArea;
                case "bokor": return p.area;
                case "cserep": return PotArea(p);
                case "fa": return CanopyArea(p);
                default: return 1;
            }
        }

        // öntözési bejegyzés átváltása mm-re az adott profilnál
        public static double ToMm(DProfile p, double amount, string unit)
        {
            double area = Math.Max(1e-4, AreaOf(p));
            if (unit == "mm") return amount;
            if (unit == "perc")
            {
                if (p.type == "gyep") return amount / 60 * p.sprinklerRate;
                if (p.type == "bokor" || p.type == "fa") return p.dripCount * p.dripLph * amount / 60 / area;
                return 0;
            }
            return amount / area; // liter
        }

        static bool AutoOn(DProfile p, DateTime d)
        {
            if (!p.autoIrr) return false;
            int md = d.Month * 100 + d.Day;
            int from = Md(p.autoFrom), to = Md(p.autoTo);
            if (from > 0 && to > 0 && (md < from || md > to)) return false;
            int dow = ((int)d.DayOfWeek + 6) % 7 + 1;
            return (p.autoDays ?? "").Split(',', ';', ' ').Any(x => x.Trim() == dow.ToString(Inv));
        }

        // ------------------------------------------------------------------ FWI (Van Wagner 1987, a cffdrs megvalósítás szerint)
        static readonly double[] DmcLe = { 6.5, 7.5, 9.0, 12.8, 13.9, 13.9, 12.4, 10.9, 9.4, 8.0, 7.0, 6.0 };
        static readonly double[] DcLf = { -1.6, -1.6, -1.6, 0.9, 3.8, 5.8, 6.4, 5.0, 2.4, 0.4, -1.6, -1.6 };

        public static double FfmcStep(double T, double H, double W, double ro, double Fo)
        {
            double mo = 147.2 * (101 - Fo) / (59.5 + Fo);
            if (ro > 0.5)
            {
                double rf = ro - 0.5;
                double add = 42.5 * rf * Math.Exp(-100 / (251 - mo)) * (1 - Math.Exp(-6.93 / rf));
                if (mo > 150) add += 0.0015 * Math.Pow(mo - 150, 2) * Math.Sqrt(rf);
                mo = Math.Min(250, mo + add);
            }
            double ed = 0.942 * Math.Pow(H, 0.679) + 11 * Math.Exp((H - 100) / 10) + 0.18 * (21.1 - T) * (1 - Math.Exp(-0.115 * H));
            double m;
            if (mo > ed)
            {
                double ko = 0.424 * (1 - Math.Pow(H / 100, 1.7)) + 0.0694 * Math.Sqrt(W) * (1 - Math.Pow(H / 100, 8));
                double kd = ko * 0.581 * Math.Exp(0.0365 * T);
                m = ed + (mo - ed) * Math.Pow(10, -kd);
            }
            else
            {
                double ew = 0.618 * Math.Pow(H, 0.753) + 10 * Math.Exp((H - 100) / 10) + 0.18 * (21.1 - T) * (1 - Math.Exp(-0.115 * H));
                if (mo < ew)
                {
                    double k1 = 0.424 * (1 - Math.Pow((100 - H) / 100, 1.7)) + 0.0694 * Math.Sqrt(W) * (1 - Math.Pow((100 - H) / 100, 8));
                    double kw = k1 * 0.581 * Math.Exp(0.0365 * T);
                    m = ew - (ew - mo) * Math.Pow(10, -kw);
                }
                else m = mo;
            }
            return Math.Max(0, Math.Min(101, 59.5 * (250 - m) / (147.2 + m)));
        }

        public static double DmcStep(double T, double H, double ro, double Po, int month)
        {
            if (T < -1.1) T = -1.1;
            double rk = 1.894 * (T + 1.1) * (100 - H) * DmcLe[month - 1] * 1e-4;
            double pr = Po;
            if (ro > 1.5)
            {
                double re = 0.92 * ro - 1.27;
                double mo = 20 + Math.Exp(5.6348 - Po / 43.43);
                double b = Po <= 33 ? 100 / (0.5 + 0.3 * Po) : Po <= 65 ? 14 - 1.3 * Math.Log(Po) : 6.2 * Math.Log(Po) - 17.2;
                double mr = mo + 1000 * re / (48.77 + b * re);
                pr = Math.Max(0, 244.72 - 43.43 * Math.Log(mr - 20));
            }
            return Math.Max(0, pr + Math.Max(0, rk));
        }

        public static double DcStep(double T, double ro, double Do, int month)
        {
            if (T < -2.8) T = -2.8;
            double pe = Math.Max(0, (0.36 * (T + 2.8) + DcLf[month - 1]) / 2);
            double dr = Do;
            if (ro > 2.8)
            {
                double rd = 0.83 * ro - 1.27;
                double qo = 800 * Math.Exp(-Do / 400);
                double qr = qo + 3.937 * rd;
                dr = Math.Max(0, 400 * Math.Log(800 / qr));
            }
            return dr + pe;
        }

        public static double IsiCalc(double ffmc, double W)
        {
            double fm = 147.2 * (101 - ffmc) / (59.5 + ffmc);
            double fW = Math.Exp(0.05039 * W);
            double fF = 91.9 * Math.Exp(-0.1386 * fm) * (1 + Math.Pow(fm, 5.31) / 4.93e7);
            return 0.208 * fW * fF;
        }

        public static double BuiCalc(double P, double D)
        {
            double u = P <= 0.4 * D ? 0.8 * P * D / (P + 0.4 * D) : P - (1 - 0.8 * D / (P + 0.4 * D)) * (0.92 + Math.Pow(0.0114 * P, 1.7));
            return double.IsNaN(u) || u < 0 ? 0 : u;
        }

        public static double FwiCalc(double isi, double bui)
        {
            double fD = bui <= 80 ? 0.626 * Math.Pow(bui, 0.809) + 2 : 1000 / (25 + 108.64 * Math.Exp(-0.023 * bui));
            double B = 0.1 * isi * fD;
            return B > 1 ? Math.Exp(2.72 * Math.Pow(0.434 * Math.Log(B), 0.647)) : B;
        }

        static void ComputeFwi(Dataset ds, DConfig cfg, DResult r, DateTime[] dates)
        {
            int n = dates.Length;
            r.Ffmc = new double[n]; r.Dmc = new double[n]; r.Dc = new double[n]; r.Isi = new double[n]; r.Bui = new double[n]; r.Fwi = new double[n];
            r.FwiT = new double[n]; r.FwiH = new double[n]; r.FwiW = new double[n]; r.FwiRo = new double[n]; r.FwiEst = new bool[n];
            var fw = cfg.fwi;
            double F = fw.ffmc0, P = fw.dmc0, D = fw.dc0;
            int season = Md(fw.seasonStart);
            double prevNoonTot = double.NaN, prevDayRain = 0;
            for (int i = 0; i < n; i++)
            {
                var dt = dates[i];
                if (dt.Month * 100 + dt.Day == season) { F = fw.ffmc0; P = fw.dmc0; D = fw.dc0; }
                // a déli (11:00 UTC) észlelés: ±90 percen belül a legközelebbi
                long midnight = Tz.LocalMidnightEpoch(dt);
                long noon = (long)(new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Utc) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds + (long)(fw.noonHourUtc * 3600);
                int k = ds.LowerBound(noon);
                int best = -1; long bd = long.MaxValue;
                for (int j = Math.Max(0, k - 1); j <= Math.Min(ds.N - 1, k); j++) { long dd = Math.Abs(ds.T[j] - noon); if (dd < bd) { bd = dd; best = j; } }
                bool ok = best >= 0 && bd <= 5400 && (ds.Flags[best] & (Flag.Temp | Flag.Hum)) == 0;
                double T, H, W;
                if (ok) { T = ds.C[Col.TA][best]; H = ds.C[Col.HA][best]; W = ds.C[Col.WSA][best] * fw.windFactor; }
                else
                {
                    // nincs déli mérés (13.6): a legközelebbi jó mérés ±4 órán belül, különben a nap értékei
                    // (dél körül a hőmérséklet a maximum, a pára a minimum közelében jár) – kitalált 50 % helyett
                    r.FwiEst[i] = true;
                    int near = -1; long nd = long.MaxValue;
                    for (int j = ds.LowerBound(noon - 4 * 3600); j < ds.N && ds.T[j] <= noon + 4 * 3600; j++)
                        if ((ds.Flags[j] & (Flag.Temp | Flag.Hum)) == 0 && !float.IsNaN(ds.C[Col.TA][j]) && !float.IsNaN(ds.C[Col.HA][j]) && Math.Abs(ds.T[j] - noon) < nd) { nd = Math.Abs(ds.T[j] - noon); near = j; }
                    DayStat dsd; ds.DayMap.TryGetValue(Tz.DayNumber(dt), out dsd);
                    T = near >= 0 ? ds.C[Col.TA][near] : r.Tmax[i];
                    H = near >= 0 ? ds.C[Col.HA][near] : dsd != null && !double.IsNaN(dsd.HumMin) ? dsd.HumMin : double.NaN;
                    W = (near >= 0 && !float.IsNaN(ds.C[Col.WSA][near]) ? ds.C[Col.WSA][near] : dsd != null ? dsd.WindAvg : double.NaN) * fw.windFactor;
                }
                // ha semmi nincs: a tegnapi bemenet (ennek a hatása a legkisebb), első napon semleges érték
                if (double.IsNaN(T)) T = i > 0 ? r.FwiT[i - 1] : 15; if (double.IsNaN(H)) H = i > 0 ? r.FwiH[i - 1] : 60; if (double.IsNaN(W)) W = i > 0 ? r.FwiW[i - 1] : 10;
                H = Math.Max(0, Math.Min(100, H));
                // 24 órás csapadék délig: tegnap dél utáni + ma délelőtti
                double noonTot = ok && !float.IsNaN(ds.C[Col.PTOT][best]) ? ds.C[Col.PTOT][best] : double.NaN;
                double todayBeforeNoon = double.IsNaN(noonTot) ? r.Rain[i] / 2 : Math.Min(noonTot, r.Rain[i]);
                double yesterdayAfterNoon = i == 0 ? 0 : (double.IsNaN(prevNoonTot) ? prevDayRain / 2 : Math.Max(0, prevDayRain - prevNoonTot));
                double ro = Math.Max(0, todayBeforeNoon + yesterdayAfterNoon);
                prevNoonTot = noonTot; prevDayRain = r.Rain[i];
                r.FwiT[i] = T; r.FwiH[i] = H; r.FwiW[i] = W; r.FwiRo[i] = ro;
                F = FfmcStep(T, H, W, ro, F);
                P = DmcStep(T, H, ro, P, dt.Month);
                D = DcStep(T, ro, D, dt.Month);
                double isi = IsiCalc(F, W), bui = BuiCalc(P, D);
                r.Ffmc[i] = F; r.Dmc[i] = P; r.Dc[i] = D; r.Isi[i] = isi; r.Bui[i] = bui; r.Fwi[i] = FwiCalc(isi, bui);
            }
        }

        public static DResult Compute(Dataset ds, DConfig cfg)
        {
            var r = new DResult();
            var g = cfg.global;
            if (ds == null || ds.Days.Count == 0) return r;
            double lat = double.IsNaN(ds.Lat) ? (AppState.S != null ? AppState.S.FallbackLat : 47.46) : ds.Lat;   // tartalék: Beállítások › Helyszín

            // --- napsugárzás-kalibráció havonta: a 3 legtisztább nap Rs/Rso aránya
            var byMonth = new Dictionary<int, List<double>>();
            foreach (var d in ds.Days)
            {
                if (d.Coverage < 0.9 || double.IsNaN(d.Energy)) continue;
                double rso = (0.75 + 2e-5 * g.elevation) * Ra(lat, d.Date.DayOfYear);
                if (rso <= 0) continue;
                List<double> l;
                if (!byMonth.TryGetValue(d.Date.Month, out l)) byMonth[d.Date.Month] = l = new List<double>();
                l.Add(d.Energy * 3.6 / rso);
            }
            var summer = new List<double>();
            for (int m = 1; m <= 12; m++)
            {
                List<double> l;
                if (byMonth.TryGetValue(m, out l) && l.Count >= 5)
                {
                    double top = l.OrderByDescending(x => x).Take(3).Average();
                    r.CalRatio[m] = top;
                    r.CalFactor[m] = Math.Round(Math.Max(0.8, Math.Min(2.0, 0.97 / top)), 2);
                    if (m >= 5 && m <= 8) summer.Add(top);
                }
                else { r.CalRatio[m] = double.NaN; r.CalFactor[m] = double.NaN; }
            }
            if (summer.Count > 0) r.SuggestedFactor = Math.Round(0.97 / summer.Average(), 2);

            // --- napok: az első és az utolsó adatnap között minden naptári nap
            DateTime first = ds.Days[0].Date, last = ds.Days[ds.Days.Count - 1].Date;
            int n = (int)(last - first).TotalDays + 1;
            r.Et0 = new double[n]; r.Rain = new double[n]; r.Ro = new double[n]; r.Rs = new double[n]; r.Tmax = new double[n]; r.Tmin = new double[n];
            r.CnUsed = new double[n]; r.Est = new bool[n];
            var dates = new DateTime[n];
            var recent = new Queue<double>();
            for (int i = 0; i < n; i++)
            {
                var date = first.AddDays(i);
                dates[i] = date;
                r.Dates.Add(date.ToString("yyyy-MM-dd", Inv));
                DayStat d;
                ds.DayMap.TryGetValue(Tz.DayNumber(date), out d);
                double ra = Ra(lat, date.DayOfYear);
                double factor = g.autoCal && !double.IsNaN(r.CalFactor[date.Month]) ? r.CalFactor[date.Month] : g.solarFactor;
                bool ok = d != null && d.Coverage >= 0.5 && !double.IsNaN(d.Tmax) && !double.IsNaN(d.Tmin);
                double et0;
                if (ok)
                {
                    double rs = double.IsNaN(d.Energy) ? double.NaN : d.Energy * 3.6 * factor / Math.Max(0.5, d.Coverage);
                    double uz = double.IsNaN(d.WindAvg) ? 2.0 * 3.6 : d.WindAvg;   // km/h; FAO-alapérték 2 m/s, ha nincs
                    double u2 = U2(uz / 3.6, g);
                    double ea = double.IsNaN(d.HumMax) || double.IsNaN(d.HumMin) ? (double.IsNaN(d.DewAvg) ? double.NaN : Es(d.DewAvg)) : double.NaN;
                    if (double.IsNaN(rs))
                    {
                        et0 = Et0Hargreaves(d.Tmax, d.Tmin, ra);
                        r.Est[i] = true;
                    }
                    else
                    {
                        if (double.IsNaN(ea) && (double.IsNaN(d.HumMax) || double.IsNaN(d.HumMin))) ea = Es(d.Tmin); // FAO-56: Tdew ≈ Tmin
                        et0 = Et0Pm(d.Tmax, d.Tmin, d.HumMax, d.HumMin, ea, u2, rs, ra, g);
                    }
                    r.Rs[i] = rs; r.Tmax[i] = d.Tmax; r.Tmin[i] = d.Tmin;
                    if (d.Coverage < 0.8) r.Est[i] = true;
                }
                else
                {
                    // hiányzó nap: Hargreaves, ha van hőmérséklet, különben az előző napok átlaga
                    r.Est[i] = true;
                    if (d != null && !double.IsNaN(d.Tmax) && !double.IsNaN(d.Tmin)) et0 = Et0Hargreaves(d.Tmax, d.Tmin, ra);
                    else et0 = recent.Count > 0 ? recent.Average() : 1.0;
                    r.Rs[i] = double.NaN;
                    r.Tmax[i] = d != null ? d.Tmax : double.NaN; r.Tmin[i] = d != null ? d.Tmin : double.NaN;
                }
                r.Et0[i] = et0;
                recent.Enqueue(et0); if (recent.Count > 7) recent.Dequeue();
                r.Rain[i] = d != null && !double.IsNaN(d.Rain) ? d.Rain : 0;
            }

            // --- lefolyás: a felső réteg telítettsége (rejtett "lefolyás-vödör") vagy 5 napos előzetes csapadék
            var s = cfg.soil;
            double tawTop = Math.Max(1, 1000 * (s.topFC - s.topWP) * s.topDepth);
            double drTop = g.startFull ? 0 : tawTop * 0.5;
            for (int i = 0; i < n; i++)
            {
                double P = r.Rain[i], cn = s.cn;
                if (cfg.runoff.method == "5nap")
                {
                    double p5 = 0;
                    for (int k = Math.Max(0, i - 5); k < i; k++) p5 += r.Rain[k];
                    bool grow = Growing(dates[i], g);
                    double lo = grow ? cfg.runoff.dryGrow : cfg.runoff.dry, hi = grow ? cfg.runoff.wetGrow : cfg.runoff.wet;
                    cn = p5 < lo ? CnI(s.cn) : p5 > hi ? CnIII(s.cn) : s.cn;
                }
                else
                {
                    double w = 1 - drTop / tawTop;   // 0 = kiszáradt, 1 = telített
                    cn = w < 0.5 ? CnI(s.cn) + (s.cn - CnI(s.cn)) * (w / 0.5) : s.cn + (CnIII(s.cn) - s.cn) * ((w - 0.5) / 0.5);
                }
                r.CnUsed[i] = cn;
                r.Ro[i] = cfg.runoff.enabled ? Runoff(P, cn, cfg.runoff.ia) : 0;
                drTop = Math.Max(0, Math.Min(tawTop, drTop - (P - r.Ro[i]) + 0.9 * r.Et0[i] * (drTop > 0.5 * tawTop ? (tawTop - drTop) / (0.5 * tawTop) : 1)));
            }

            // --- hőösszeg (GDD) és a GDD-ből számolt vegetációs szakaszok
            var gd = cfg.gdd;
            r.Gdd = new double[n]; r.GddCum = new double[n]; r.GddPheno = new double[n];
            double cum = 0, cumP = 0, cumM = 0;
            int startMd = Md(gd.start);
            var springAt = new Dictionary<int, DateTime>(); var leafAt = new Dictionary<int, DateTime>();
            for (int i = 0; i < n; i++)
            {
                var dt = dates[i];
                if (dt.Month * 100 + dt.Day == startMd || i == 0) { cum = 0; cumP = 0; cumM = 0; }
                double tx = r.Tmax[i], tn = r.Tmin[i];
                double g1 = 0, gp = 0;
                if (!double.IsNaN(tx) && !double.IsNaN(tn))
                {
                    double txc = Math.Min(tx, gd.cap), tnc = Math.Min(tn, gd.cap);
                    g1 = Math.Max(0, (txc + Math.Max(tnc, gd.baseT)) / 2 - gd.baseT);
                    gp = Math.Max(0, (txc + Math.Max(tnc, gd.phenoBase)) / 2 - gd.phenoBase);
                }
                cum += g1; cumP += gp;
                r.Gdd[i] = g1; r.GddCum[i] = cum; r.GddPheno[i] = cumP;
                if (!springAt.ContainsKey(dt.Year) && cumP >= gd.springGdd) springAt[dt.Year] = dt;
                if (!leafAt.ContainsKey(dt.Year) && cumP >= gd.fullLeafGdd) leafAt[dt.Year] = dt;
            }
            foreach (var y in springAt.Keys)
                r.Pheno[y] = new[] { springAt[y], leafAt.ContainsKey(y) ? leafAt[y] : springAt[y].AddDays(45) };

            // --- kanadai erdőtűz-veszélyességi index (Van Wagner 1987) a déli észlelésekből
            ComputeFwi(ds, cfg, r, dates);

            // --- talajvíz-mérleg profilonként (FAO-56, 85. egyenlet)
            foreach (var p in cfg.profiles)
            {
                var pr = new DProfileResult { P = p, Dr = new double[n], Depl = new double[n], Ks = new double[n], Etc = new double[n], Irr = new double[n], Kc = new double[n], Taw = new double[n], Raw = new double[n] };
                pr.Area = AreaOf(p);
                double taw = Math.Max(0.5, Taw(p, s));
                double rainF = p.type == "cserep" ? (p.place == "fedett" ? 0 : p.place == "felig" ? 0.5 : 1) : 1;
                double etF = p.type == "cserep" ? (p.place == "fedett" ? 0.6 : p.place == "felig" ? 0.8 : 1) : 1;
                double dr = g.startFull ? 0 : taw * 0.5;
                var logs = cfg.log.Where(l => l.zone == p.id).ToList();
                var progs = (cfg.schedules ?? new List<DSchedule>()).Select(sc => new { sc, pg = (cfg.programs ?? new List<DProgram>()).FirstOrDefault(x => x.id == sc.program) }).Where(x => x.pg != null && x.pg.zone == p.id).ToList();
                for (int i = 0; i < n; i++)
                {
                    DateTime[] ph = null;
                    if (gd.kcByGdd) r.Pheno.TryGetValue(dates[i].Year, out ph);
                    double kc = Kc(p, dates[i], g, ph);
                    double pFrac;
                    if (p.type == "felszin") pFrac = Math.Min(0.95, s.rew / taw);
                    else
                    {
                        pFrac = p.p;
                        if (g.pAdjust) pFrac = Math.Max(0.1, Math.Min(0.8, p.p + 0.04 * (5 - kc * r.Et0[i])));
                    }
                    double raw = pFrac * taw;
                    double ks = dr > raw ? Math.Max(0, (taw - dr) / ((1 - pFrac) * taw)) : 1;
                    double etc = ks * kc * r.Et0[i] * etF;
                    double irr = 0;
                    if (AutoOn(p, dates[i]))
                        irr += p.type == "gyep" ? ToMm(p, p.autoMinutes, "perc") : (p.type == "bokor" || p.type == "fa") ? ToMm(p, p.autoMinutes, "perc") : 0;
                    foreach (var l in logs) if (l.date == r.Dates[i]) irr += ToMm(p, l.amount, l.unit);
                    foreach (var x in progs) if (DroughtStore.ProgramOn(x.pg, x.sc, dates[i])) irr += ToMm(p, x.pg.amount, x.pg.unit);
                    double peff = (r.Rain[i] - (p.type == "cserep" ? 0 : r.Ro[i])) * rainF;
                    dr = dr - peff - irr + etc;
                    if (dr < 0) dr = 0;              // a többlet mélybe szivárog
                    if (dr > taw) dr = taw;
                    pr.Dr[i] = dr; pr.Depl[i] = dr / taw; pr.Ks[i] = ks; pr.Etc[i] = etc; pr.Irr[i] = irr; pr.Kc[i] = kc; pr.Taw[i] = taw; pr.Raw[i] = raw;
                }
                // tartós vízhiány és tartalék
                pr.Stress30 = Enumerable.Range(Math.Max(0, n - 30), Math.Min(30, n)).Average(i => 1 - pr.Ks[i]);
                pr.Stress90 = Enumerable.Range(Math.Max(0, n - 90), Math.Min(90, n)).Average(i => 1 - pr.Ks[i]);
                if (n > 30) pr.DrMonthAgo = pr.Dr[n - 31];
                if (p.type == "fa")
                {
                    double dbh = p.trunkCm / Math.PI / 100.0;
                    double v = Math.PI * Math.Pow(dbh / 2, 2) * p.height * p.formFactor * p.sapFrac;
                    pr.ReserveL = v * 1000 * p.waterFrac;
                    double potL = pr.Kc[n - 1] * r.Et0[n - 1] * pr.Area;
                    pr.ReserveDays = potL > 0.05 ? pr.ReserveL / potL : double.NaN;
                }
                pr.LitersToday = pr.Etc[n - 1] * pr.Area;
                r.Profiles.Add(pr);
            }
            return r;
        }
    }
}
