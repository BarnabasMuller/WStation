using WStation.Launcher;
using Xunit;

namespace WStation.Tests;

// Az indítók (PLAN-3.0 9.4, 1.8) parancssor-kezelése: a hívó kapcsolói betű szerint jutnak át a programhoz, és ha a hívó nem adott
// meg mappát, az indító a telepített mappa gyökerét adja át (különben a program az app\ almappát tekintené a telepítésnek).
public class InditoTests
{
    const string Root = @"C:\Próba mappa\WStation\";
    const string Exe = @"C:\Próba mappa\WStation\app\WStation.Downloader.exe";

    [Theory]
    [InlineData("\"C:\\Próba mappa\\WStation\\Letöltő.exe\" --nowait", "--nowait")]
    [InlineData("\"C:\\Próba mappa\\WStation\\Letöltő.exe\"", "")]
    [InlineData("\"C:\\Próba mappa\\WStation\\Letöltő.exe\"--nowait", "--nowait")]
    [InlineData("Letöltő.exe   --mentes --most", "--mentes --most")]
    [InlineData("Letöltő.exe\t--nowait", "--nowait")]
    [InlineData("Letöltő.exe", "")]
    [InlineData("\"C:\\a b\\Letöltő.exe\" --mappa \"D:\\x y\\\\\" --nowait", "--mappa \"D:\\x y\\\\\" --nowait")]
    [InlineData("", "")]
    public void ArgsTail_a_programnev_utani_resz_betu_szerint(string commandLine, string expected)
    {
        Assert.Equal(expected, Parancssor.ArgsTail(commandLine));
    }

    [Fact]
    public void Quote_a_zaro_visszaperjelet_megduplazza()
    {
        Assert.Equal("\"C:\\WStation\\\\\"", Parancssor.Quote(@"C:\WStation\"));
        Assert.Equal("\"C:\\\\\"", Parancssor.Quote(@"C:\"));
        Assert.Equal("\"C:\\a b\\x.exe\"", Parancssor.Quote(@"C:\a b\x.exe"));
    }

    [Fact]
    public void Build_mappa_nelkul_a_gyokeret_adja_at()
    {
        string cmd = Parancssor.Build(Exe, "\"" + Root + "Letöltő.exe\" --nowait", new[] { Root + "Letöltő.exe", "--nowait" }, Root);
        Assert.Equal("\"" + Exe + "\" --nowait --mappa \"C:\\Próba mappa\\WStation\\\\\"", cmd);
    }

    [Fact]
    public void Build_kapcsolok_nelkul_is_atadja_a_mappat()
    {
        string cmd = Parancssor.Build(Exe, "Letöltő.exe", new[] { "Letöltő.exe" }, Root);
        Assert.Equal("\"" + Exe + "\" --mappa \"C:\\Próba mappa\\WStation\\\\\"", cmd);
    }

    [Fact]
    public void Build_a_hivo_mappajat_nem_irja_felul()
    {
        string line = "Letöltő.exe --MAPPA \"D:\\Adat\" --nowait";
        string cmd = Parancssor.Build(Exe, line, new[] { "Letöltő.exe", "--MAPPA", @"D:\Adat", "--nowait" }, Root);
        Assert.Equal("\"" + Exe + "\" --MAPPA \"D:\\Adat\" --nowait", cmd);
    }

    [Fact]
    public void HasMappa_a_programnevet_nem_nezi()
    {
        Assert.False(Parancssor.HasMappa(new[] { "--mappa" }));
        Assert.True(Parancssor.HasMappa(new[] { "x.exe", "--Mappa", "D:\\" }));
        Assert.False(Parancssor.HasMappa(new[] { "x.exe", "--mappak" }));
    }
}
