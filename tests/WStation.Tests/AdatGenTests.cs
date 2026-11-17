using System.IO;
using System.Linq;
using WStation.AdatGen;
using Xunit;

namespace WStation.Tests;

public class AdatGenTests
{
    [Fact]
    public void Build_ketszer_azonos_szoveget_ad()
    {
        var a = Generator.Build();
        var b = Generator.Build();
        Assert.Equal(a.Obs, b.Obs);
        Assert.Equal(a.Daily, b.Daily);
        Assert.Equal(a.Current, b.Current);
    }

    [Fact]
    public void Write_ketszer_bajtra_azonos_fajlokat_ir()
    {
        string root = Path.Combine(Path.GetTempPath(), "wstation-adatgen-" + Path.GetRandomFileName());
        try
        {
            string r1 = Path.Combine(root, "egy"), r2 = Path.Combine(root, "ketto");
            Generator.Write(r1);
            Generator.Write(r2);
            foreach (var f in Directory.GetFiles(r1, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(r1, f);
                Assert.Equal(File.ReadAllBytes(f), File.ReadAllBytes(Path.Combine(r2, rel)));
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void A_fejlec_es_a_sorok_oszlopszama_egyezik_es_a_formatum_a_letoltoe()
    {
        var (obs, daily, cur) = Generator.Build();
        Assert.DoesNotContain("\n", obs.Replace("\r\n", ""));          // minden sorvég CRLF
        var lines = obs.Split("\r\n", System.StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(string.Join(';', Generator.ObsColumns), lines[0]);
        Assert.All(lines, l => Assert.Equal(Generator.ObsColumns.Length, l.Split(';').Length));
        Assert.True(lines.Length > 100_000);                            // 14 hónap, 5 percenként
        Assert.All(lines.Skip(1).Take(50), l => Assert.StartsWith(Generator.StationId + ";", l));
        Assert.DoesNotContain(".", lines[1].Split(';')[16]);            // tizedesvessző
        Assert.True(daily.Split("\r\n", System.StringSplitOptions.RemoveEmptyEntries).Length > 400);
        Assert.Contains("Tesztfalu", cur);
    }

    [Fact]
    public void Az_epoch_novekvo_es_van_hiany_valamint_ket_oraatallitas()
    {
        var rows = Generator.Build().Obs.Split("\r\n", System.StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.Split(';')).ToList();
        var epochs = rows.Select(r => long.Parse(r[3])).ToList();
        for (int i = 1; i < epochs.Count; i++) Assert.True(epochs[i] > epochs[i - 1]);
        Assert.Contains(Enumerable.Range(1, epochs.Count - 1), i => epochs[i] - epochs[i - 1] > 3600);   // hiány
        // a helyi–UTC eltolás kétféle: +1 és +2 óra (óraátállítás mindkét irányban)
        var offsets = rows.Select(r => (System.DateTime.Parse(r[1], System.Globalization.CultureInfo.InvariantCulture) - System.DateTime.Parse(r[2].TrimEnd('Z'), System.Globalization.CultureInfo.InvariantCulture)).TotalMinutes).Distinct().OrderBy(x => x).ToList();
        Assert.Equal(new[] { 60.0, 120.0 }, offsets);
    }

    [Fact]
    public void Nincs_valodi_adat_a_beallitasokban()
    {
        Assert.StartsWith("TESZT-KULCS-", Generator.ApiKey);
        Assert.Equal("ITESZT1", Generator.StationId);
    }
}
