using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using WStation.Core;
using Xunit;

namespace WStation.Tests
{
    /// <summary>
    /// PLAN-3.0 14.5 / 6.2 / 17/4: a magban (<c>WStation.Core</c>) nincs írható statikus mező a megengedett listán kívül. Írható = nem
    /// <c>const</c> és nem <c>readonly</c> (az automatikus tulajdonságok rejtett mezőivel együtt). A munkaterülethez kötött állapot a
    /// <see cref="WorkspaceContext"/>-ben van; ide csak az kerülhet, ami tényleg folyamatszintű – indoklással.
    /// </summary>
    public class StatikusMezoTests
    {
        /// <summary>A megengedett írható statikus mezők (típus teljes neve + mező / tulajdonság neve) és az indoklás.</summary>
        static readonly Dictionary<string, string> Megengedett = new()
        {
            // a gazda (S2 megjegyzés, 2026-11-18): egy gépnek egy ablaka, egy Feladatütemezője
            { "WStation.Core.Common.Host.Desktop", "folyamatszintű: az asztali gazda (egy ablak egy folyamatban) – PLAN-3.0 16. S2 megjegyzés" },
            { "WStation.Core.Common.Host.Scheduler", "folyamatszintű: a gép Feladatütemezője – PLAN-3.0 16. S2 megjegyzés" },
            // nyelvi fájlok (6.2 leltár: Langs.Dir, Cache – folyamatszintű; a Cache readonly, ezért nincs a listán)
            { "WStation.Core.Common.Langs.Dir", "folyamatszintű: a nyelvi fájlok fejlesztői mappája (--web) – PLAN-3.0 6.2 leltár" },
            { "WStation.Core.Common.Langs.Current", "a Letöltő (és a Telepítő) nyelve; a Letöltő a 2.1-ben nem változik (külön folyamat, S2 megjegyzés), " +
                "a Megjelenítő magja nem olvassa – a WorkspaceContext-be a DlConfig-gal együtt kerül (S3, 8.1)" },
            // írásőr (6.2 leltár: folyamatszintű, több gyökérrel – 2.2)
            { "WStation.Core.Common.Guard.root", "folyamatszintű írásőr; több gyökér és munkaterületenkénti őr: 2.2 (PLAN-3.0 6.3)" },
            // önteszt (--teszt): egy külön folyamat egyszeri futása, nem munkaterület-állapot
            { "WStation.Core.SelfTest.ok", "önteszt-számláló: a --teszt külön folyamatban, egyszer fut" },
            { "WStation.Core.SelfTest.fail", "önteszt-számláló: a --teszt külön folyamatban, egyszer fut" },
            { "WStation.Core.SelfTest.skip", "önteszt-számláló: a --teszt külön folyamatban, egyszer fut" },
        };

        static List<string> IrhatoStatikusMezok()
        {
            var asm = typeof(WorkspaceContext).Assembly;
            var res = new List<string>();
            foreach (var t in asm.GetTypes())
            {
                // a fordító saját típusai (lambdák gyorsítótára, állapotgépek) nem a mi állapotunk
                if (t.IsDefined(typeof(CompilerGeneratedAttribute), false) || t.FullName!.Contains('<')) continue;
                foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.IsLiteral || f.IsInitOnly) continue;
                    string nev = f.Name;
                    // automatikus tulajdonság rejtett mezője: <Név>k__BackingField → Név
                    if (nev.StartsWith("<", StringComparison.Ordinal) && nev.Contains(">k__BackingField", StringComparison.Ordinal))
                        nev = nev.Substring(1, nev.IndexOf('>') - 1);
                    res.Add(t.FullName + "." + nev);
                }
            }
            res.Sort(StringComparer.Ordinal);
            return res;
        }

        [Fact]
        public void A_magban_nincs_irhato_statikus_mezo_a_megengedett_listan_kivul()
        {
            var tiltott = IrhatoStatikusMezok().Where(m => !Megengedett.ContainsKey(m)).ToList();
            Assert.True(tiltott.Count == 0,
                "Írható statikus mező a magban (PLAN-3.0 6.2, 17/4) – tedd a WorkspaceContext-be, vagy (ha tényleg folyamatszintű) indoklással a listára:\n"
                + string.Join("\n", tiltott));
        }

        [Fact]
        public void A_megengedett_lista_nem_tartalmaz_elavult_bejegyzest()
        {
            // ha egy megengedett mező megszűnik vagy readonly lesz, a lista is szűküljön (ne maradjon kiskapu)
            var van = new HashSet<string>(IrhatoStatikusMezok());
            var elavult = Megengedett.Keys.Where(k => !van.Contains(k)).ToList();
            Assert.True(elavult.Count == 0, "A listán szerepel, de már nem írható statikus mező: " + string.Join(", ", elavult));
        }

        [Fact]
        public void A_munkateruleti_allapot_tulajdonsagkent_a_kornyezetbol_olvas()
        {
            // a 6.2 leltár „WorkspaceContext” sorai: ezek ma tulajdonságok (nem mezők) a régi helyükön
            var asm = typeof(WorkspaceContext).Assembly;
            (string Tipus, string[] Nevek)[] leltar =
            {
                ("WStation.Core.AppState", new[] { "L", "S", "Data", "Status", "Message", "Progress", "DownloadMessage", "DownloadOk", "backupChecked", "cancelDownload" }),
                ("WStation.Core.Tz", new[] { "Zone", "Iana" }),
                ("WStation.Core.Loader", new[] { "gapSec" }),
                ("WStation.Core.Cloud", new[] { "envFor", "env" }),
                ("WStation.Core.DroughtStore", new[] { "cached", "RepairNote" }),
            };
            const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var (tipus, nevek) in leltar)
            {
                var t = asm.GetType(tipus, throwOnError: true)!;
                foreach (var n in nevek)
                {
                    Assert.Null(t.GetField(n, S));
                    Assert.NotNull(t.GetProperty(n, S));
                }
            }
            // a ref-fel átadott mezők a környezet mezői
            Assert.NotNull(typeof(WorkspaceContext).GetField("busy"));
            Assert.NotNull(typeof(WorkspaceContext).GetField("DownloadSerial"));
            // a kivitel nyelve a kérés paramétere (nincs Export.Ci / Export.Dirs mező)
            var export = asm.GetType("WStation.Core.Export", throwOnError: true)!;
            Assert.Null(export.GetField("Ci", S));
            Assert.Null(export.GetField("Dirs", S));
        }
    }
}
