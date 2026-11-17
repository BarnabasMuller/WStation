// A 2.2.8 beállításfájljainak alakjai – a JSON-réteg oda-vissza tesztjeihez (PLAN-3.0 7.2, 14.2/4).
// A mezők a src/megjelenito/Drought.cs (DConfig és társai), a Calib.cs (Cfg, Row) és a src/kozos/Utemezes.cs (Spec)
// 2.2.8-as deklarációinak változatlan másolatai: a sorrendjük és az alapértékeik határozzák meg a fájlok tartalmát.
// Az 1.5 csomag (a mag áthelyezése) után a tesztek a valódi osztályokra állíthatók át, és ez a fájl törölhető.
#nullable disable
using System.Collections.Generic;

namespace WStation.Tests.Alakok228
{
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
    }

    // Calib.cs
    public class Row { public string from = "", to = "", note = ""; public double manual; }
    public class Cfg { public bool enabled = true; public string mode = "global"; public double factor = 0; public List<Row> rows = new List<Row>(); }

    // Utemezes.cs
    public class Spec
    {
        public string kind = "off";
        public int minutes;               // interval: percben (15 … 1440)
        public string time = "06:00";     // daily / weekly: HH:MM
        public List<string> days = new List<string>();   // weekly: MON … SUN
        public string exe;                // a feladat által futtatott program
    }
}
