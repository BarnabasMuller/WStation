using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WStation.Core.Common;
using Xunit;

namespace WStation.Tests
{
    public class ClockTests
    {
        static readonly DateTime T0 = new(2026, 7, 1, 10, 30, 0, DateTimeKind.Utc);

        [Fact]
        public void Rogzites_nelkul_a_rendszerorat_adja()
        {
            var a = DateTime.UtcNow; var c = Clock.UtcNow; var b = DateTime.UtcNow;
            Assert.InRange(c, a, b);
            Assert.Equal(DateTimeKind.Utc, c.Kind);
            Assert.Equal(DateTimeKind.Local, Clock.Now.Kind);
        }

        [Fact]
        public void Rogzitett_ora_allo_es_a_vegen_visszaall()
        {
            using (Clock.Freeze(T0))
            {
                Assert.Equal(T0, Clock.UtcNow);
                Assert.Equal(T0.ToLocalTime(), Clock.Now);
                Assert.Equal(T0.ToLocalTime().Date, Clock.Today);
                Assert.Equal(new DateTimeOffset(T0), Clock.OffsetUtcNow);
                Assert.Equal(T0.ToLocalTime(), Clock.OffsetNow.LocalDateTime);
                Assert.Equal(DateTimeKind.Utc, Clock.UtcNow.Kind);
                Assert.Equal(DateTimeKind.Local, Clock.Now.Kind);
            }
            Assert.NotEqual(T0, Clock.UtcNow);
        }

        [Fact]
        public void Leptetes_es_beagyazas()
        {
            using (Clock.Freeze(T0))
            {
                Clock.Advance(TimeSpan.FromMinutes(5));
                Assert.Equal(T0.AddMinutes(5), Clock.UtcNow);
                using (Clock.Freeze(T0.AddDays(1)))
                    Assert.Equal(T0.AddDays(1), Clock.UtcNow);
                Assert.Equal(T0.AddMinutes(5), Clock.UtcNow);
            }
        }

        [Fact]
        public void Leptetes_rogzites_nelkul_hiba()
        {
            Assert.Throws<InvalidOperationException>(() => Clock.Advance(TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public async Task A_rogzites_a_leszarmazott_feladatokra_is_hat_de_a_parhuzamos_tesztekre_nem()
        {
            using (Clock.Freeze(T0))
            {
                Assert.Equal(T0, await Task.Run(() => Clock.UtcNow));
            }
            var masik = await Task.Run(() => { using (Clock.Freeze(T0.AddYears(1))) return Clock.UtcNow; });
            Assert.Equal(T0.AddYears(1), masik);
            Assert.NotEqual(T0.AddYears(1), Clock.UtcNow); // a szál saját környezete nem módosult
        }

        // PLAN-3.0 17/4: a magban nincs közvetlen órahívás (a Clock.cs kivételével)
        [Fact]
        public void A_magban_nincs_kozvetlen_orahivas()
        {
            string dir = AppContext.BaseDirectory;
            string? gyoker = null;
            for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "src", "WStation.Core"))) { gyoker = Path.Combine(d.FullName, "src", "WStation.Core"); break; }
            Assert.NotNull(gyoker);
            var re = new Regex(@"\bDateTime(Offset)?\s*\.\s*(Now|UtcNow|Today)\b");
            var talalat = Directory.EnumerateFiles(gyoker!, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                         && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                         && !f.EndsWith(Path.Combine("Common", "Clock.cs")))
                .SelectMany(f => File.ReadAllLines(f).Select((l, i) => (f, i, l)).Where(x => re.IsMatch(x.l)))
                .Select(x => Path.GetFileName(x.f) + ":" + (x.i + 1) + " " + x.l.Trim()).ToList();
            Assert.True(talalat.Count == 0, "Közvetlen órahívás a magban (használd a Clock-ot):\n" + string.Join("\n", talalat));
        }
    }
}
