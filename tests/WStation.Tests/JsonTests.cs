using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using WStation.Core.Common;
using WStation.Tests.Alakok228;
using Xunit;

namespace WStation.Tests
{
    // A JSON-réteg tesztjei (PLAN-3.0 7.2). Ahol a megjegyzés „mérve” szót ír, ott az elvárt érték a valódi
    // JavaScriptSerializer-ből (.NET Framework 4.8) származik: egy eldobható segédprogram írta ki, amely nincs a repóban.
    public class JsonTests
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------ segédek és mintatípusok

        public enum Szin { Piros, Zold = 5 }

        public class Belso { public string n = "x"; public double v; }

        public class Minta
        {
            public int b = 1;
            public string a = "s";
            public double D { get; set; }
            public string? nullS;
            public List<Belso> lista = new List<Belso>();
            public Belso belso = new Belso();
            int privat = 3;
            public static int stat = 9;
            public readonly int ro = 4;
            public int CsakGet { get { return 7; } }
            public string? C { get; set; }
            public int? nint;
            public Szin szin = Szin.Zold;
            public double[]? tomb;
            public Dictionary<string, object>? szotar;
            public bool flag;
            public long L;
            public decimal M;
            public float F;
            public int Privat() { return privat; }
        }

        public class Os { public int osMezo = 1; public int OsProp { get; set; } }
        public class Leszarmazott : Os { public int sajatMezo = 2; public int SajatProp { get; set; } }
        public class Kor { public Kor? en; public int x; }

        public class Cel
        {
            public double d = 5.5;
            public int i = 3;
            public string? s = "alap";
            public bool f = true;
            public List<string>? lista = new List<string> { "a" };
            public Belso? belso = new Belso();
            public long nagy = 1;
            public int? ni = 2;
            public double[]? da;
            public Dictionary<string, object>? szotar;
            public object? o;
            public List<Belso?>? bl = new List<Belso?>();
            public Szin szin;
            public decimal m;
            public float fl;
            public string? P { get; set; }
            public Dictionary<string, double>? sd;
        }

        public class Ketertelmu { public int x; public int X; }
        public class Sorrend { public int a = 1; public int b = 2; public int z = 3; public int P { get; set; } public int Q { get; set; } }

        static string Minta228(string nev)
        {
            using (Stream? st = typeof(JsonTests).Assembly.GetManifestResourceStream("Json228/" + nev))
            {
                Assert.NotNull(st);
                using (var r = new StreamReader(st!, new UTF8Encoding(false)))
                    return r.ReadToEnd();
            }
        }

        static Dictionary<string, object?> Szotar(object? o)
        {
            return Assert.IsType<Dictionary<string, object?>>(o);
        }

        static string Ismetel(string s, int n)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(s);
            return sb.ToString();
        }

        // ================================================================== olvasás: számok

        [Theory]   // mind mérve
        [InlineData("0", typeof(int), "0")]
        [InlineData("-0", typeof(int), "0")]
        [InlineData("-1", typeof(int), "-1")]
        [InlineData("2147483647", typeof(int), "2147483647")]
        [InlineData("-2147483648", typeof(int), "-2147483648")]
        [InlineData("2147483648", typeof(long), "2147483648")]
        [InlineData("-2147483649", typeof(long), "-2147483649")]
        [InlineData("1792000000000", typeof(long), "1792000000000")]
        [InlineData("9223372036854775807", typeof(long), "9223372036854775807")]
        [InlineData("-9223372036854775808", typeof(long), "-9223372036854775808")]
        [InlineData("9223372036854775808", typeof(decimal), "9223372036854775808")]
        [InlineData("100000000000000000000", typeof(decimal), "100000000000000000000")]
        [InlineData("79228162514264337593543950335", typeof(decimal), "79228162514264337593543950335")]
        [InlineData("1.0", typeof(decimal), "1.0")]
        [InlineData("1.5", typeof(decimal), "1.5")]
        [InlineData("0.1", typeof(decimal), "0.1")]
        [InlineData("1.10", typeof(decimal), "1.10")]
        [InlineData("2.50", typeof(decimal), "2.50")]
        [InlineData("0.0", typeof(decimal), "0.0")]
        [InlineData("-0.0", typeof(decimal), "0.0")]
        [InlineData("0.30000000000000004", typeof(decimal), "0.30000000000000004")]
        [InlineData("3.141592653589793", typeof(decimal), "3.141592653589793")]
        [InlineData("0.1234567890123456789012345678901234567890", typeof(decimal), "0.1234567890123456789012345679")]
        public void Szam_egesz_int_vagy_long_tizedes_decimal(string json, Type tipus, string ertek)
        {
            object? v = Json.DeserializeObject(json);
            Assert.NotNull(v);
            Assert.Equal(tipus, v!.GetType());
            Assert.Equal(ertek, ((IFormattable)v).ToString(null, Inv));
        }

        [Theory]   // mind mérve: a kitevős alak és ami a decimal-ba sem fér, double
        [InlineData("1e5", 100000.0)]
        [InlineData("1E5", 100000.0)]
        [InlineData("1e+5", 100000.0)]
        [InlineData("1.5e3", 1500.0)]
        [InlineData("1e-5", 1e-5)]
        [InlineData("1.5e-7", 1.5e-7)]
        [InlineData("1e-400", 0.0)]
        [InlineData("1e28", 1e28)]
        [InlineData("1.7976931348623157E+308", double.MaxValue)]
        [InlineData("79228162514264337593543950336", 7.9228162514264338E+28)]
        [InlineData("123456789012345678901234567890.5", 1.2345678901234568E+29)]
        public void Szam_kitevos_alak_double(string json, double ertek)
        {
            object? v = Json.DeserializeObject(json);
            Assert.Equal(ertek, Assert.IsType<double>(v));
        }

        [Theory]   // mérve: a régi is hibát jelzett
        [InlineData("1e400")]
        [InlineData("-1e400")]
        [InlineData("[1e400]")]
        public void Szam_tul_nagy_kivetel(string json)
        {
            Assert.ThrowsAny<ArgumentException>(() => Json.DeserializeObject(json));
        }

        [Fact]
        public void A_beolvasott_szam_Convert_ToDouble_lal_olvashato()
        {
            // a 2.x kód így olvassa a számokat, bármelyik típus jön
            var d = Szotar(Json.DeserializeObject("{\"a\":1,\"b\":0.23,\"c\":3000000000,\"d\":1e-7}"));
            Assert.Equal(1.0, Convert.ToDouble(d["a"], Inv));
            Assert.Equal(0.23, Convert.ToDouble(d["b"], Inv));
            Assert.Equal(3e9, Convert.ToDouble(d["c"], Inv));
            Assert.Equal(1e-7, Convert.ToDouble(d["d"], Inv));
        }

        // ================================================================== olvasás: alakok

        [Fact]
        public void Tomb_object_tomb_nem_lista()
        {
            object? v = Json.DeserializeObject("[1,\"a\",true,null,1.5,{},[]]");
            Assert.Equal(typeof(object[]), v!.GetType());
            var a = (object?[])v;
            Assert.Equal(7, a.Length);
            Assert.Equal(1, Assert.IsType<int>(a[0]));
            Assert.Equal("a", Assert.IsType<string>(a[1]));
            Assert.True(Assert.IsType<bool>(a[2]));
            Assert.Null(a[3]);
            Assert.Equal(1.5m, Assert.IsType<decimal>(a[4]));
            Assert.Empty(Szotar(a[5]));
            Assert.Equal(typeof(object[]), a[6]!.GetType());
            Assert.Empty((object?[])a[6]!);
        }

        [Fact]
        public void Objektum_szotar_a_kulcsok_sorrendje_megmarad()
        {
            var d = Szotar(Json.DeserializeObject("{\"b\":1,\"a\":2,\"c\":3,\"Z\":4,\"0\":5,\"á\":6}"));
            Assert.Equal(new[] { "b", "a", "c", "Z", "0", "á" }, d.Keys.ToArray());
            // a régi alakra épülő kód így veszi át: (Dictionary<string, object>) – ugyanaz a futásidejű típus
            Assert.Equal(typeof(Dictionary<string, object>), d.GetType());
        }

        [Fact]
        public void Objektum_ismetlodo_kulcsnal_az_utolso_ertek_az_elso_helyen()
        {
            // mérve: {"a":1,"a":2} → {a: 2}
            var d = Szotar(Json.DeserializeObject("{\"a\":1,\"b\":7,\"a\":2}"));
            Assert.Equal(new[] { "a", "b" }, d.Keys.ToArray());
            Assert.Equal(2, d["a"]);
        }

        [Fact]
        public void Objektum_kulcsa_kis_nagybetu_erzekeny_es_lehet_ures()
        {
            var d = Szotar(Json.DeserializeObject("{\"a\":1,\"A\":2,\"\":3}"));   // mérve
            Assert.Equal(1, d["a"]);
            Assert.Equal(2, d["A"]);
            Assert.Equal(3, d[""]);
        }

        [Fact]
        public void Egymasba_agyazott_szerkezet()
        {
            var d = Szotar(Json.DeserializeObject(" {\"a\" : {\"b\" : {\"c\" : [ 1 , {\"d\" : null } ] } } } "));
            var c = Assert.IsType<object?[]>(Szotar(Szotar(d["a"])["b"])["c"]);
            Assert.Equal(1, c[0]);
            Assert.Null(Szotar(c[1])["d"]);
        }

        [Fact]
        public void Logikai_es_null()
        {
            Assert.True(Assert.IsType<bool>(Json.DeserializeObject("true")));
            Assert.False(Assert.IsType<bool>(Json.DeserializeObject("false")));
            Assert.Null(Json.DeserializeObject("null"));
        }

        [Fact]
        public void Ures_szoveg_null_a_null_bemenet_kivetel()
        {
            // mérve: "" és " " → null; null → ArgumentNullException
            Assert.Null(Json.DeserializeObject(""));
            Assert.Null(Json.DeserializeObject(" \t\r\n "));
            Assert.Throws<ArgumentNullException>(() => Json.DeserializeObject(null!));
        }

        [Fact]
        public void Szoveg_escape_ek_es_nem_ascii_karakterek()
        {
            // mérve
            Assert.Equal("a\nb\tc\r\b\f/\\\"", Json.DeserializeObject("\"a\\nb\\tc\\r\\b\\f\\/\\\\\\\"\""));
            Assert.Equal("Aé<", Json.DeserializeObject("\"\\u0041\\u00e9\\u003c\""));
            Assert.Equal("\ud83d\ude00", Json.DeserializeObject("\"\\ud83d\\ude00\""));
            Assert.Equal("árvíztűrő tükörfúrógép \ud83d\ude00 €", Json.DeserializeObject("\"árvíztűrő tükörfúrógép \ud83d\ude00 €\""));
            Assert.Equal("\0", Json.DeserializeObject("\"\\u0000\""));
            Assert.Equal("", Json.DeserializeObject("\"\""));
        }

        [Theory]   // mérve: a régi is hibát jelzett mindegyikre
        [InlineData("{")]
        [InlineData("[")]
        [InlineData("]")]
        [InlineData("}")]
        [InlineData("[1")]
        [InlineData("[1,]")]
        [InlineData("[,]")]
        [InlineData("[1,,2]")]
        [InlineData("[1 2]")]
        [InlineData("{\"a\":1,}")]
        [InlineData("{\"a\"}")]
        [InlineData("{\"a\":}")]
        [InlineData("{\"a\" 1}")]
        [InlineData("{\"a\":1 \"b\":2}")]
        [InlineData("{\"a\":1")]
        [InlineData("{\"a\":1}}")]
        [InlineData("{} x")]
        [InlineData("[] []")]
        [InlineData("1 2")]
        [InlineData("null null")]
        [InlineData("True")]
        [InlineData("undefined")]
        [InlineData("nul")]
        [InlineData("truex")]
        [InlineData("{\"k\":undefined}")]
        [InlineData("\"abc")]
        [InlineData("\"\\q\"")]
        [InlineData("\"\\x41\"")]
        [InlineData("\"\\u12\"")]
        [InlineData("\"\\uZZZZ\"")]
        [InlineData("0x10")]
        [InlineData("1e")]
        [InlineData("-")]
        [InlineData("1.5.5")]
        [InlineData("/*c*/1")]
        [InlineData("1//c")]
        [InlineData("\ufeff1")]
        public void Hibas_bemenet_ArgumentException(string json)
        {
            Assert.ThrowsAny<ArgumentException>(() => Json.DeserializeObject(json));
        }

        [Theory]
        // ISMERT ELTÉRÉS: ezeket a régi (mérve) elfogadta, az új réteg – szigorú JSON-olvasó – hibát jelez.
        // A program által írt fájlokban és a böngésző kéréseiben ilyen alak nem fordul elő.
        [InlineData("'abc'")]                 // aposztrófos szöveg
        [InlineData("{a:1}")]                 // idézőjel nélküli kulcs
        [InlineData("{'a':1}")]
        [InlineData("{1:2}")]
        [InlineData("01")]                    // vezető nulla
        [InlineData("+1")]
        [InlineData(".5")]
        [InlineData("1.")]
        [InlineData("NaN")]
        [InlineData("[Infinity]")]
        [InlineData("\"a\tb\"")]              // nyers vezérlőkarakter a szövegben
        [InlineData("\"a\nb\"")]
        [InlineData("\"\\'\"")]               // \' escape
        [InlineData("\"\\ud83d\"")]           // párosítatlan helyettesítő (a régi U+FFFD-t adott)
        [InlineData("\u00a01")]               // nem törhető szóköz mint térköz
        public void Ismert_elteres_a_regi_engedekenysegei_nincsenek_meg(string json)
        {
            Assert.ThrowsAny<ArgumentException>(() => Json.DeserializeObject(json));
        }

        [Fact]
        public void Ismert_elteres_a_Date_alaku_szoveg_szoveg_marad()
        {
            // a régi (mérve) az ilyen szövegből DateTime-ot csinált; dátum típus nincs használatban (7.2)
            Assert.Equal("/Date(1234567890000)/", Json.DeserializeObject("\"\\/Date(1234567890000)\\/\""));
            Assert.Equal("/Date(1)/", Szotar(Json.DeserializeObject("{\"d\":\"\\/Date(1)\\/\"}"))["d"]);
        }

        [Fact]
        public void Ismert_elteres_a_type_kulcs_kozonseges_kulcs()
        {
            // a régi (mérve) a "__type" kulcsra kivételt dobott (típusfeloldó nélkül)
            var d = Szotar(Json.DeserializeObject("{\"__type\":\"X\",\"a\":1}"));
            Assert.Equal("X", d["__type"]);
        }

        // ================================================================== korlátok

        [Fact]
        public void Alapertelmezett_korlatok_64_MB_es_200_szint()
        {
            Assert.Equal(64 * 1024 * 1024, Json.DefaultMaxLength);
            Assert.Equal(200, Json.DefaultMaxDepth);
        }

        [Theory]   // mérve RecursionLimit = 3-mal: minden érték (az egyszerű is) egy szint
        [InlineData("1", true)]
        [InlineData("[1]", true)]
        [InlineData("[[1]]", true)]
        [InlineData("[[[1]]]", false)]
        [InlineData("[[]]", true)]
        [InlineData("[[[]]]", true)]
        [InlineData("[[[[]]]]", false)]
        [InlineData("{\"a\":{\"a\":1}}", true)]
        [InlineData("{\"a\":{\"a\":{\"a\":1}}}", false)]
        [InlineData("{\"a\":{\"a\":{\"a\":{}}}}", false)]
        [InlineData("[[{\"a\":1}]]", false)]
        [InlineData("[[[\"a\"]]]", false)]
        [InlineData("[[null]]", true)]
        public void Melysegkorlat_olvasaskor(string json, bool belefer)
        {
            if (belefer) Json.DeserializeObject(json, Json.DefaultMaxLength, 3);
            else Assert.ThrowsAny<ArgumentException>(() => Json.DeserializeObject(json, Json.DefaultMaxLength, 3));
        }

        [Fact]
        public void Melysegkorlat_olvasaskor_alapbol_200_szint()
        {
            Json.DeserializeObject(Ismetel("[", 200) + Ismetel("]", 200));
            Json.DeserializeObject(Ismetel("[", 199) + "1" + Ismetel("]", 199));
            Json.DeserializeObject(Ismetel("{\"a\":", 199) + "1" + Ismetel("}", 199));
            Assert.ThrowsAny<ArgumentException>(() => Json.DeserializeObject(Ismetel("[", 201) + Ismetel("]", 201)));
            Assert.ThrowsAny<ArgumentException>(() => Json.DeserializeObject(Ismetel("[", 200) + "1" + Ismetel("]", 200)));
            Assert.ThrowsAny<ArgumentException>(() => Json.DeserializeObject(Ismetel("{\"a\":", 200) + "1" + Ismetel("}", 200)));
            Assert.ThrowsAny<ArgumentException>(() => Json.Deserialize<object>(Ismetel("[", 201) + Ismetel("]", 201)));
        }

        [Theory]   // mérve RecursionLimit = 3-mal
        [InlineData("1", true)]
        [InlineData("[1]", true)]
        [InlineData("[[1]]", true)]
        [InlineData("[[[1]]]", false)]
        [InlineData("[[[]]]", true)]
        [InlineData("[[[[]]]]", false)]
        [InlineData("{\"a\":{\"a\":1}}", true)]
        [InlineData("{\"a\":{\"a\":{\"a\":1}}}", false)]
        [InlineData("{\"a\":{\"a\":{}}}", true)]
        public void Melysegkorlat_iraskor(string json, bool belefer)
        {
            object? o = Json.DeserializeObject(json);
            if (belefer) Assert.Equal(json, Json.Serialize(o, Json.DefaultMaxLength, 3));
            else Assert.Throws<ArgumentException>(() => Json.Serialize(o, Json.DefaultMaxLength, 3));
        }

        [Fact]
        public void Melysegkorlat_iraskor_alapbol_200_szint()
        {
            object ok = new object[0], sok;
            for (int i = 0; i < 199; i++) ok = new object[] { ok };   // 200 egymásba ágyazott tömb
            sok = new object[] { ok };
            Assert.Equal(Ismetel("[", 200) + Ismetel("]", 200), Json.Serialize(ok));
            Assert.Throws<ArgumentException>(() => Json.Serialize(sok));

            var lanc = new Kor();   // mérve (100-as korláttal): N objektum lánca N + 1 szint, mert a mezők értéke is szint
            Kor veg = lanc;
            for (int i = 0; i < 198; i++) { veg.en = new Kor(); veg = veg.en; }
            Json.Serialize(lanc);
            veg.en = new Kor();
            Assert.Throws<ArgumentException>(() => Json.Serialize(lanc));
        }

        [Fact]
        public void Meretkorlat_olvasaskor_karakterben_szamol()
        {
            // mérve MaxJsonLength = 10-zel: a korlát karakterben értendő, nem bájtban
            Assert.Equal("12345678", Json.DeserializeObject("\"12345678\"", 10, 200));
            Assert.Equal("éééééééé", Json.DeserializeObject("\"éééééééé\"", 10, 200));
            Assert.ThrowsAny<ArgumentException>(() => Json.DeserializeObject("\"123456789\"", 10, 200));
            Assert.ThrowsAny<ArgumentException>(() => Json.DeserializeObject("[1,2,3,4,5]", 10, 200));
            Assert.ThrowsAny<ArgumentException>(() => Json.Deserialize<object>("[1,2,3,4,5]", 10, 200));
        }

        [Fact]
        public void Meretkorlat_olvasaskor_alapbol_64_MB()
        {
            string nagy = new string(' ', Json.DefaultMaxLength + 1);
            Assert.ThrowsAny<ArgumentException>(() => Json.DeserializeObject(nagy));
        }

        [Fact]
        public void Meretkorlat_iraskor()
        {
            // mérve: íráskor InvalidOperationException
            Assert.Equal("\"12345678\"", Json.Serialize("12345678", 10, 200));
            Assert.Equal("\"éééééééé\"", Json.Serialize("éééééééé", 10, 200));
            Assert.Throws<InvalidOperationException>(() => Json.Serialize("123456789", 10, 200));
            Assert.Throws<InvalidOperationException>(() => Json.Serialize(new[] { 1, 2, 3, 4, 5 }, 10, 200));
        }

        [Fact]
        public void Ervenytelen_korlat_kivetel()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Json.Serialize(1, -1, 200));
            Assert.Throws<ArgumentOutOfRangeException>(() => Json.DeserializeObject("1", 10, 0));
        }

        // ================================================================== írás: karakterkódolás

        [Fact]
        public void Iras_az_ascii_tartomany_escape_elese_bajtra_a_regi()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 0x80; i++) sb.Append((char)i);
            // mérve (a 0x7f változatlanul megy ki)
            const string regi = "\"\\u0000\\u0001\\u0002\\u0003\\u0004\\u0005\\u0006\\u0007\\b\\t\\n\\u000b\\f\\r\\u000e\\u000f"
                + "\\u0010\\u0011\\u0012\\u0013\\u0014\\u0015\\u0016\\u0017\\u0018\\u0019\\u001a\\u001b\\u001c\\u001d\\u001e\\u001f"
                + " !\\\"#$%\\u0026\\u0027()*+,-./0123456789:;\\u003c=\\u003e?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\\\]^_`abcdefghijklmnopqrstuvwxyz{|}~\u007f\"";
            Assert.Equal(regi, Json.Serialize(sb.ToString()));
        }

        [Fact]
        public void Iras_kacsacsor_es_et_jel_u_alakban_kisbetus_hexaval()
        {
            Assert.Equal("\"\\u003cb\\u003e \\u0026 \\u0027x\\u0027 a/b\"", Json.Serialize("<b> & 'x' a/b"));
            Assert.Equal("\"\\u003c\"", Json.Serialize('<'));
        }

        [Fact]
        public void Iras_nem_ascii_karakter_valtozatlanul()
        {
            // mérve
            const string s = "árvíztűrő tükörfúrógép ÁŰŐ € °C m³ – …";
            Assert.Equal("\"" + s + "\"", Json.Serialize(s));
            Assert.Equal("\"\ud83d\ude00\"", Json.Serialize("\ud83d\ude00"));
            Assert.Equal("\"\u007f\u0080\\u0085\u009f\u00a0\u00ad\"", Json.Serialize("\u007f\u0080\u0085\u009f\u00a0\u00ad"));
            Assert.Equal("\"\\u2028\\u2029\ufeff\"", Json.Serialize("\u2028\u2029\ufeff"));
        }

        [Fact]
        public void Iras_a_kulcs_is_escape_elve()
        {
            // mérve
            Assert.Equal("{\"\\u003ca\\u0026b\\u003e\\u0027\\\"é\\n\":1}", Json.Serialize(new Dictionary<string, object> { { "<a&b>'\"é\n", 1 } }));
        }

        [Fact]
        public void Iras_utf8_ba_irva_a_bajtok_a_regivel_egyeznek()
        {
            // a 2.x a szöveget UTF-8-ként, BOM nélkül írja fájlba
            byte[] b = new UTF8Encoding(false).GetBytes(Json.Serialize(new Dictionary<string, object> { { "n", "é<" } }));
            Assert.Equal(new byte[] { 0x7b, 0x22, 0x6e, 0x22, 0x3a, 0x22, 0xc3, 0xa9, 0x5c, 0x75, 0x30, 0x30, 0x33, 0x63, 0x22, 0x7d }, b);
        }

        // ================================================================== írás: számok és egyszerű értékek

        [Theory]   // mind mérve – a .NET Framework "R" alakja (15, szükség esetén 17 értékes jegy)
        [InlineData(0.0, "0")]
        [InlineData(1.0, "1")]
        [InlineData(1.5, "1.5")]
        [InlineData(0.1, "0.1")]
        [InlineData(1.10, "1.1")]
        [InlineData(100.0, "100")]
        [InlineData(164.0, "164")]
        [InlineData(0.115, "0.115")]
        [InlineData(0.23, "0.23")]
        [InlineData(4.35, "4.35")]
        [InlineData(0.07, "0.07")]
        [InlineData(1234567.8, "1234567.8")]
        [InlineData(0.000123, "0.000123")]
        [InlineData(0.001, "0.001")]
        [InlineData(1e-4, "0.0001")]
        [InlineData(1e-5, "1E-05")]
        [InlineData(1.5e-7, "1.5E-07")]
        [InlineData(1e15, "1E+15")]
        [InlineData(1e16, "1E+16")]
        [InlineData(1e17, "1E+17")]
        [InlineData(1.5e20, "1.5E+20")]
        [InlineData(1e21, "1E+21")]
        [InlineData(1792000000000.0, "1792000000000")]
        [InlineData(0.30000000000000004, "0.30000000000000004")]
        [InlineData(1.0 / 3, "0.33333333333333331")]
        [InlineData(2.0 / 3, "0.66666666666666663")]
        [InlineData(3.141592653589793, "3.1415926535897931")]
        [InlineData(123456789.123456789, "123456789.12345679")]
        [InlineData(12345678901234567890.0, "1.2345678901234567E+19")]
        [InlineData(123456789012345680.0, "1.2345678901234568E+17")]
        [InlineData(double.MaxValue, "1.7976931348623157E+308")]
        [InlineData(double.MinValue, "-1.7976931348623157E+308")]
        [InlineData(double.Epsilon, "4.94065645841247E-324")]
        public void Iras_double_a_regi_alakban(double d, string regi)
        {
            Assert.Equal(regi, Json.Serialize(d));
        }

        [Fact]
        public void Iras_negativ_nulla_elojel_nelkul()
        {
            // mérve: a régi a -0.0-t is "0"-nak írta (a modern .NET magától "-0"-t írna)
            Assert.Equal("0", Json.Serialize(-0.0));
            Assert.Equal("0", Json.Serialize(-0.0f));
        }

        [Theory]   // mérve
        [InlineData(0.1f, "0.1")]
        [InlineData(1f, "1")]
        [InlineData(1.5f, "1.5")]
        [InlineData(0.3f, "0.3")]
        [InlineData(float.MaxValue, "3.40282347E+38")]
        [InlineData(1e-10f, "1E-10")]
        [InlineData(16777217f, "16777216")]
        public void Iras_float_a_regi_alakban(float f, string regi)
        {
            Assert.Equal(regi, Json.Serialize(f));
        }

        [Fact]
        public void Iras_decimal_a_tizedesjegyek_megmaradnak()
        {
            // mérve
            Assert.Equal("1.10", Json.Serialize(1.10m));
            Assert.Equal("1", Json.Serialize(1m));
            Assert.Equal("1.000", Json.Serialize(1.000m));
            Assert.Equal("0.0000001", Json.Serialize(0.0000001m));
            Assert.Equal("79228162514264337593543950335", Json.Serialize(decimal.MaxValue));
            Assert.Equal("0.0", Json.Serialize(-0.0m));
        }

        [Fact]
        public void Iras_egeszek_logikai_felsorolas_karakter()
        {
            // mérve
            Assert.Equal("5", Json.Serialize(5));
            Assert.Equal("-2147483648", Json.Serialize(int.MinValue));
            Assert.Equal("9223372036854775807", Json.Serialize(long.MaxValue));
            Assert.Equal("18446744073709551615", Json.Serialize(ulong.MaxValue));
            Assert.Equal("4294967295", Json.Serialize(uint.MaxValue));
            Assert.Equal("255", Json.Serialize((byte)255));
            Assert.Equal("-1", Json.Serialize((sbyte)-1));
            Assert.Equal("-5", Json.Serialize((short)-5));
            Assert.Equal("true", Json.Serialize(true));
            Assert.Equal("false", Json.Serialize(false));
            Assert.Equal("5", Json.Serialize(Szin.Zold));
            Assert.Equal("42", Json.Serialize((Szin)42));
            Assert.Equal("5", Json.Serialize((int?)5));
            Assert.Equal("null", Json.Serialize((int?)null));
            Assert.Equal("\"x\"", Json.Serialize('x'));
            Assert.Equal("null", Json.Serialize('\0'));
            Assert.Equal("\"\"", Json.Serialize(""));
            Assert.Equal("null", Json.Serialize(null));
            Assert.Equal("null", Json.Serialize(DBNull.Value));
            Assert.Equal("\"11111111-2222-3333-4444-555555555555\"", Json.Serialize(new Guid("11111111-2222-3333-4444-555555555555")));
        }

        [Fact]
        public void Iras_NaN_es_vegtelen_null()
        {
            // 7.2: null. (A régi – mérve – NaN / Infinity szót írt ki, ami nem érvényes JSON; a kód eddig is kerülte.)
            Assert.Equal("null", Json.Serialize(double.NaN));
            Assert.Equal("null", Json.Serialize(double.PositiveInfinity));
            Assert.Equal("null", Json.Serialize(double.NegativeInfinity));
            Assert.Equal("null", Json.Serialize(float.NaN));
            Assert.Equal("{\"n\":\"x\",\"v\":null}", Json.Serialize(new Belso { v = double.NaN }));
            Assert.Equal("[1,null]", Json.Serialize(new[] { 1.0, double.NaN }));
        }

        [Fact]
        public void Olvasas_null_ertektipusu_mezobe_az_alapertek_marad()
        {
            // 7.2 – a NaN null-ként íródik ki, visszaolvasva a mező az alapértékén marad.
            // ELTÉRÉS: a régi (mérve) itt InvalidOperationException-t dobott.
            string json = Json.Serialize(new Cel { d = double.NaN });
            Assert.Contains("\"d\":null", json);
            Cel c = Json.Deserialize<Cel>(json)!;
            Assert.Equal(5.5, c.d);

            c = Json.Deserialize<Cel>("{\"d\":null,\"i\":null,\"f\":null,\"szin\":null,\"m\":null,\"nagy\":null}")!;
            Assert.Equal(5.5, c.d);
            Assert.Equal(3, c.i);
            Assert.True(c.f);
            Assert.Equal(Szin.Piros, c.szin);
            Assert.Equal(1, c.nagy);
        }

        [Fact]
        public void Iras_datum_tipus_kivetel()
        {
            // 7.2: ne keletkezzen "\/Date(…)\/" alak (a régi – mérve – azt írt)
            var dt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            Assert.Throws<InvalidOperationException>(() => Json.Serialize(dt));
            Assert.Throws<InvalidOperationException>(() => Json.Serialize(new DateTimeOffset(dt)));
            Assert.Throws<InvalidOperationException>(() => Json.Serialize(new Dictionary<string, object> { { "d", dt } }));
            Assert.Throws<InvalidOperationException>(() => Json.Serialize(new object[] { 1, dt }));
            Assert.Throws<InvalidOperationException>(() => Json.Serialize(new { mikor = (DateTime?)dt }));
        }

        [Fact]
        public void Olvasas_datum_tipusu_celba_kivetel()
        {
            // ELTÉRÉS: a régi (mérve) a szöveget dátumként értelmezte
            Assert.Throws<InvalidOperationException>(() => Json.Deserialize<DateTime>("\"2026-01-02\""));
        }

        // ================================================================== írás: gyűjtemények és osztályok

        [Fact]
        public void Iras_szotar_a_kulcsok_a_bejaras_sorrendjeben_a_null_is_kiirva()
        {
            var d = new Dictionary<string, object?>
            {
                { "b", 1 }, { "a", "x" }, { "n", null }, { "d", 1.5 }, { "l", new List<object?> { 1, "a", null } },
                { "t", new object[] { true } }, { "e", new Dictionary<string, object>() }, { "u", new object[0] }
            };
            // mérve
            Assert.Equal("{\"b\":1,\"a\":\"x\",\"n\":null,\"d\":1.5,\"l\":[1,\"a\",null],\"t\":[true],\"e\":{},\"u\":[]}", Json.Serialize(d));
            Assert.Equal("{\"a\":\"b\"}", Json.Serialize(new Dictionary<string, string> { { "a", "b" } }));
            Assert.Equal("{\"a\":1.5}", Json.Serialize(new Dictionary<string, double> { { "a", 1.5 } }));
            Assert.Equal("{\"a\":1}", Json.Serialize(new Dictionary<object, object> { { "a", 1 } }));
            Assert.Equal("{\"a\":2,\"b\":1}", Json.Serialize(new SortedDictionary<string, object> { { "b", 1 }, { "a", 2 } }));
        }

        [Fact]
        public void Iras_szotar_nem_szoveg_kulccsal_kivetel()
        {
            // mérve: ArgumentException
            Assert.Throws<ArgumentException>(() => Json.Serialize(new Dictionary<int, double> { { 1, 1.5 } }));
            Assert.Equal("{}", Json.Serialize(new Dictionary<int, double>()));   // mérve: üresen nem hiba
            Assert.Throws<ArgumentException>(() => Json.Serialize(new Dictionary<object, object> { { 1, 1 } }));
            Assert.Throws<ArgumentException>(() => Json.Serialize(new Dictionary<Szin, int> { { Szin.Zold, 1 } }));
        }

        [Fact]
        public void Iras_listak_es_tombok()
        {
            // mérve
            Assert.Equal("[1,2]", Json.Serialize(new List<int> { 1, 2 }));
            Assert.Equal("[1,2]", Json.Serialize(new[] { 1, 2 }));
            Assert.Equal("[]", Json.Serialize(new List<int>()));
            Assert.Equal("[2,4]", Json.Serialize(new[] { 1, 2 }.Select(x => x * 2)));
            Assert.Equal("[[1],[2,3]]", Json.Serialize(new[] { new[] { 1 }, new[] { 2, 3 } }));
            Assert.Equal("[1,2,255]", Json.Serialize(new byte[] { 1, 2, 255 }));
            Assert.Equal("[\"a\",\"b\"]", Json.Serialize(new[] { 'a', 'b' }));
            Assert.Equal("[{\"code\":\"hu\",\"name\":\"Magyar\"}]",
                Json.Serialize(new List<Dictionary<string, object>> { new Dictionary<string, object> { { "code", "hu" }, { "name", "Magyar" } } }));
        }

        [Fact]
        public void Iras_osztaly_mezok_aztan_tulajdonsagok_a_deklaracio_sorrendjeben()
        {
            // mérve: a privát és a statikus tag kimarad, a csak olvasható mező és tulajdonság kiíródik, a null is
            Assert.Equal("{\"b\":1,\"a\":\"s\",\"nullS\":null,\"lista\":[],\"belso\":{\"n\":\"x\",\"v\":0},\"ro\":4,\"nint\":null,\"szin\":5,"
                + "\"tomb\":null,\"szotar\":null,\"flag\":false,\"L\":0,\"M\":0,\"F\":0,\"D\":0,\"CsakGet\":7,\"C\":null}", Json.Serialize(new Minta()));
        }

        [Fact]
        public void Iras_a_tagok_sorrendje_nem_fugg_a_korabbi_olvasastol()
        {
            // A régi (mérve) a tagokat abban a sorrendben írta ki, ahogy a futtatókörnyezet visszaadta, és ez egy korábbi,
            // név szerinti keresés (típusos beolvasás) után megváltozhatott: a keresett tag előre került.
            // Az új réteg mindig a deklaráció sorrendjében ír (7.2).
            Json.Deserialize<Sorrend>("{\"z\":9,\"Q\":1,\"b\":5}");
            Assert.Equal("{\"a\":1,\"b\":2,\"z\":3,\"P\":0,\"Q\":0}", Json.Serialize(new Sorrend()));
        }

        [Fact]
        public void Iras_leszarmazott_elobb_a_sajat_tagok()
        {
            // mérve
            Assert.Equal("{\"sajatMezo\":2,\"osMezo\":1,\"SajatProp\":0,\"OsProp\":0}", Json.Serialize(new Leszarmazott()));
        }

        [Fact]
        public void Iras_nevtelen_tipus()
        {
            // mérve
            Assert.Equal("{\"z\":1,\"a\":\"x\",\"m\":{\"k\":1.5}}", Json.Serialize(new { z = 1, a = "x", m = new { k = 1.5 } }));
            Assert.Equal("{}", Json.Serialize(new object()));
        }

        [Fact]
        public void Iras_korkoros_hivatkozas_kivetel()
        {
            var k = new Kor();
            k.en = k;
            Assert.Throws<InvalidOperationException>(() => Json.Serialize(k));   // mérve
            // ugyanaz az objektum kétszer, de nem körben: rendben
            var b = new Belso();
            Assert.Equal("[{\"n\":\"x\",\"v\":0},{\"n\":\"x\",\"v\":0}]", Json.Serialize(new[] { b, b }));
        }

        // ================================================================== típusos olvasás

        [Fact]
        public void Tipusos_hianyzo_kulcs_alapertek_ismeretlen_kulcs_kimarad()
        {
            Cel c = Json.Deserialize<Cel>("{}")!;
            Assert.Equal(5.5, c.d);
            Assert.Equal(3, c.i);
            Assert.Equal("alap", c.s);
            Assert.True(c.f);
            Assert.Equal(new[] { "a" }, c.lista);
            Assert.Equal("x", c.belso!.n);
            Assert.Equal(2, c.ni);

            c = Json.Deserialize<Cel>("{\"ismeretlen\":1,\"s\":\"ok\",\"masik\":{\"x\":[1,2]}}")!;   // mérve
            Assert.Equal("ok", c.s);
            Assert.Equal(5.5, c.d);
        }

        [Fact]
        public void Tipusos_kis_nagybetu_nem_szamit_az_utolso_kulcs_nyer()
        {
            // mérve
            Assert.Equal(2.0, Json.Deserialize<Cel>("{\"D\":2}")!.d);
            Assert.Equal("nagy", Json.Deserialize<Cel>("{\"S\":\"nagy\"}")!.s);
            Assert.Equal("kis", Json.Deserialize<Cel>("{\"p\":\"kis\"}")!.P);
            Assert.Equal(2.0, Json.Deserialize<Cel>("{\"d\":1,\"D\":2}")!.d);
            Assert.Equal(1.0, Json.Deserialize<Cel>("{\"D\":2,\"d\":1}")!.d);
            Belso b = Json.Deserialize<Cel>("{\"belso\":{\"N\":\"nagy\",\"V\":2}}")!.belso!;
            Assert.Equal("nagy", b.n);
            Assert.Equal(2.0, b.v);
        }

        [Fact]
        public void Tipusos_ket_csak_betumeretben_kulonbozo_tag_kivetel()
        {
            // mérve: a régi is AmbiguousMatchException-t dobott
            Assert.Throws<AmbiguousMatchException>(() => Json.Deserialize<Ketertelmu>("{\"x\":1}"));
        }

        [Fact]
        public void Tipusos_null_es_ures_bemenet_null()
        {
            // mérve
            Assert.Null(Json.Deserialize<Cel>("null"));
            Assert.Null(Json.Deserialize<Cel>(""));
            Assert.Null(Json.Deserialize<Dictionary<string, object>>("null"));
            Assert.Null(Json.Deserialize<string>("null"));
            Assert.Throws<InvalidOperationException>(() => Json.Deserialize<int>("null"));
        }

        [Fact]
        public void Tipusos_null_hivatkozas_tipusu_mezobe_null()
        {
            // mérve
            Cel c = Json.Deserialize<Cel>("{\"s\":null,\"belso\":null,\"ni\":null,\"lista\":null,\"da\":null}")!;
            Assert.Null(c.s);
            Assert.Null(c.belso);
            Assert.Null(c.ni);
            Assert.Null(c.lista);
            Assert.Null(c.da);
        }

        [Fact]
        public void Tipusos_egyszeru_ertekek_atalakitasa()
        {
            // mind mérve
            Assert.Equal(1.0, Json.Deserialize<Cel>("{\"d\":1}")!.d);
            Assert.Equal(1.25, Json.Deserialize<Cel>("{\"d\":1.25}")!.d);
            Assert.Equal(1.25, Json.Deserialize<Cel>("{\"d\":\"1.25\"}")!.d);
            Assert.Equal(1e30, Json.Deserialize<Cel>("{\"d\":1e30}")!.d);
            Assert.Equal(1.2345678901234567E+19, Json.Deserialize<Cel>("{\"d\":12345678901234567890}")!.d);
            Assert.Equal(0.0, Json.Deserialize<Cel>("{\"d\":{}}")!.d);
            Assert.Equal(7, Json.Deserialize<Cel>("{\"i\":\"7\"}")!.i);
            Assert.Equal(100, Json.Deserialize<Cel>("{\"i\":1e2}")!.i);
            Assert.Equal(long.MaxValue, Json.Deserialize<Cel>("{\"nagy\":9223372036854775807}")!.nagy);
            Assert.Equal("1", Json.Deserialize<Cel>("{\"s\":1}")!.s);
            Assert.Equal("1.50", Json.Deserialize<Cel>("{\"s\":1.50}")!.s);
            Assert.Equal("True", Json.Deserialize<Cel>("{\"s\":true}")!.s);
            Assert.Equal("100000", Json.Deserialize<Cel>("{\"s\":1e5}")!.s);
            Assert.Equal("é<", Json.Deserialize<Cel>("{\"s\":\"é\\u003c\"}")!.s);
            Assert.False(Json.Deserialize<Cel>("{\"f\":false}")!.f);
            Assert.False(Json.Deserialize<Cel>("{\"f\":\"false\"}")!.f);
            Assert.True(Json.Deserialize<Cel>("{\"f\":\"True\"}")!.f);
            Assert.Equal(5, Json.Deserialize<Cel>("{\"ni\":5}")!.ni);
            Assert.Equal(5, Json.Deserialize<Cel>("{\"ni\":\"5\"}")!.ni);
            Assert.Equal(1.10m, Json.Deserialize<Cel>("{\"m\":1.10}")!.m);
            Assert.Equal("1.10", Json.Deserialize<Cel>("{\"m\":\"1.10\"}")!.m.ToString(Inv));
            Assert.Equal(100m, Json.Deserialize<Cel>("{\"m\":1e2}")!.m);
            Assert.Equal(0.1f, Json.Deserialize<Cel>("{\"fl\":0.1}")!.fl);
            Assert.Equal("p", Json.Deserialize<Cel>("{\"P\":\"p\"}")!.P);
        }

        [Fact]
        public void Tipusos_felsorolas()
        {
            // mérve
            Assert.Equal(Szin.Zold, Json.Deserialize<Cel>("{\"szin\":5}")!.szin);
            Assert.Equal(Szin.Zold, Json.Deserialize<Cel>("{\"szin\":\"Zold\"}")!.szin);
            Assert.Equal(Szin.Zold, Json.Deserialize<Cel>("{\"szin\":\"zold\"}")!.szin);
            Assert.Equal(Szin.Zold, Json.Deserialize<Cel>("{\"szin\":\"5\"}")!.szin);
            Assert.Equal((Szin)42, Json.Deserialize<Cel>("{\"szin\":42}")!.szin);
        }

        [Theory]   // mind mérve: a régi is hibát jelzett (ott Exception / FormatException, itt egységesen FormatException)
        [InlineData("{\"d\":\"1,25\"}")]
        [InlineData("{\"d\":\"abc\"}")]
        [InlineData("{\"d\":\"\"}")]
        [InlineData("{\"d\":true}")]
        [InlineData("{\"i\":1.5}")]
        [InlineData("{\"i\":2.5}")]
        [InlineData("{\"i\":1.0}")]
        [InlineData("{\"i\":-1.9}")]
        [InlineData("{\"i\":\"7.0\"}")]
        [InlineData("{\"i\":3000000000}")]
        [InlineData("{\"i\":true}")]
        [InlineData("{\"i\":\"\"}")]
        [InlineData("{\"f\":1}")]
        [InlineData("{\"f\":0}")]
        [InlineData("{\"f\":\"igen\"}")]
        [InlineData("{\"f\":\"1\"}")]
        [InlineData("{\"f\":\"\"}")]
        [InlineData("{\"ni\":1.5}")]
        [InlineData("{\"szin\":\"x\"}")]
        [InlineData("{\"szin\":1.0}")]
        [InlineData("{\"fl\":1e39}")]
        [InlineData("{\"da\":[\"x\"]}")]
        public void Tipusos_nem_atalakithato_ertek_FormatException(string json)
        {
            Assert.Throws<FormatException>(() => Json.Deserialize<Cel>(json));
        }

        [Theory]   // mind mérve: InvalidOperationException
        [InlineData("[]")]
        [InlineData("1")]
        [InlineData("\"x\"")]
        [InlineData("{\"d\":[1]}")]
        [InlineData("{\"s\":[1]}")]
        [InlineData("{\"belso\":[]}")]
        [InlineData("{\"belso\":1}")]
        [InlineData("{\"belso\":\"x\"}")]
        [InlineData("{\"bl\":[1]}")]
        [InlineData("{\"da\":[null]}")]
        [InlineData("{\"sd\":{\"a\":null}}")]
        public void Tipusos_nem_illo_alak_InvalidOperationException(string json)
        {
            Assert.Throws<InvalidOperationException>(() => Json.Deserialize<Cel>(json));
        }

        [Fact]
        public void Tipusos_hibas_json_ArgumentException()
        {
            Assert.ThrowsAny<ArgumentException>(() => Json.Deserialize<Cel>("{\"d\":"));
            Assert.ThrowsAny<ArgumentException>(() => Json.Deserialize<Cel>("{\"d\":1e400}"));   // mérve
            Assert.Throws<ArgumentNullException>(() => Json.Deserialize<Cel>(null!));
        }

        [Fact]
        public void Tipusos_beagyazott_osztaly_lista_tomb_szotar()
        {
            // mind mérve
            Cel c = Json.Deserialize<Cel>("{\"belso\":{\"n\":\"y\"},\"bl\":[{\"n\":\"a\",\"v\":1},{},null],\"da\":[1,2.5,\"3\"],"
                + "\"lista\":[\"x\",\"y\"],\"sd\":{\"a\":1,\"b\":\"2.5\"},\"szotar\":{\"a\":1,\"b\":[1,{\"c\":2}]},\"o\":{\"a\":1.5}}")!;
            Assert.Equal("y", c.belso!.n);
            Assert.Equal(0.0, c.belso.v);
            Assert.Equal(3, c.bl!.Count);
            Assert.Equal("a", c.bl[0]!.n);
            Assert.Equal(1.0, c.bl[0]!.v);
            Assert.Equal("x", c.bl[1]!.n);
            Assert.Null(c.bl[2]);
            Assert.Equal(new[] { 1.0, 2.5, 3.0 }, c.da);
            Assert.Equal(new[] { "x", "y" }, c.lista);
            Assert.Equal(new[] { "a", "b" }, c.sd!.Keys.ToArray());
            Assert.Equal(2.5, c.sd["b"]);
            // object és Dictionary<string, object> célba az általános alak megy változatlanul
            Assert.Equal(1, Assert.IsType<int>(c.szotar!["a"]));
            Assert.Equal(2, Szotar(Assert.IsType<object?[]>(c.szotar["b"])[1])["c"]);
            Assert.Equal(1.5m, Szotar(c.o)["a"]);
            Assert.IsType<object?[]>(Json.Deserialize<Cel>("{\"o\":[1]}")!.o);
            Assert.Equal("x", Json.Deserialize<Cel>("{\"o\":\"x\"}")!.o);
        }

        [Fact]
        public void Tipusos_orokolt_tagok_es_nem_irhato_tagok()
        {
            // mérve
            Leszarmazott l = Json.Deserialize<Leszarmazott>("{\"osMezo\":5,\"OsProp\":6,\"sajatMezo\":7,\"SajatProp\":8}")!;
            Assert.Equal(5, l.osMezo);
            Assert.Equal(6, l.OsProp);
            Assert.Equal(7, l.sajatMezo);
            Assert.Equal(8, l.SajatProp);

            int elotte = Minta.stat;
            Minta m = Json.Deserialize<Minta>("{\"CsakGet\":9,\"privat\":9,\"stat\":" + (elotte + 1).ToString(Inv) + ",\"b\":2}")!;
            Assert.Equal(2, m.b);
            Assert.Equal(7, m.CsakGet);
            Assert.Equal(3, m.Privat());
            Assert.Equal(elotte, Minta.stat);
        }

        [Fact]
        public void Tipusos_legfelso_szint_mas_tipusokkal()
        {
            // mind mérve
            Assert.Equal(5, Json.Deserialize<int>("5"));
            Assert.Equal(5, Json.Deserialize<int>("\"5\""));
            Assert.Equal(1.5, Json.Deserialize<double>("1.5"));
            Assert.True(Json.Deserialize<bool>("true"));
            Assert.Equal("x", Json.Deserialize<string>("\"x\""));
            Assert.Equal("5", Json.Deserialize<string>("5"));
            Assert.Equal(new List<int> { 1, 2 }, Json.Deserialize<List<int>>("[1,2]"));
            Assert.Equal(new[] { 1, 2 }, Json.Deserialize<int[]>("[1,2]"));
            Assert.Equal(new[] { 1.0, 2.0 }, Json.Deserialize<double[]>("[1,2]"));
            Assert.Equal(new object[] { 1, "a" }, Json.Deserialize<object[]>("[1,\"a\"]"));
            Assert.Equal("q", Json.Deserialize<List<Belso>>("[{\"n\":\"q\"}]")![0].n);
            Assert.IsType<Dictionary<string, object?>>(Json.Deserialize<object>("{\"a\":1}"));
            Assert.IsType<object?[]>(Json.Deserialize<object>("[1]"));
            Assert.Equal(1, Json.Deserialize<Dictionary<string, object>>("{\"a\":1,\"b\":[1]}")!["a"]);
            Assert.Equal(1.0, Json.Deserialize<Dictionary<string, double>>("{\"a\":1}")!["a"]);
            Assert.Equal("1", Json.Deserialize<Dictionary<string, string>>("{\"a\":1}")!["a"]);
            Assert.Equal(1, Szotar(Json.Deserialize<List<Dictionary<string, object>>>("[{\"a\":1}]")![0])["a"]);
            Assert.Throws<FormatException>(() => Json.Deserialize<List<int>>("[1.5]"));
            Assert.Throws<InvalidOperationException>(() => Json.Deserialize<Dictionary<string, object>>("[1]"));
            Assert.Throws<InvalidOperationException>(() => Json.Deserialize<Dictionary<int, string>>("{\"1\":\"a\"}"));
            Assert.Throws<InvalidOperationException>(() => Json.Deserialize<Belso>("[{\"n\":1}]"));
        }

        // ================================================================== oda-vissza: a 2.2.8 fájlformátumai
        // A Json228 mappa fájljait a valódi JavaScriptSerializer írta (a 2.2.8 osztályaiból és hívásaiból), kitalált
        // tartalommal: az új réteggel beolvasva és visszaírva bájtra ugyanazt kell kapni.

        [Theory]
        [InlineData("szarazsag.json")]         // DroughtStore: Serialize(DConfig)
        [InlineData("szarazsag_alap.json")]    // DConfig.Defaults()
        [InlineData("kalibracio.json")]        // Calib: Serialize(Cfg)
        [InlineData("kalibracio_alap.json")]
        [InlineData("valtozasnaplo.json")]     // ChangeLog: Serialize(List<Dictionary<string, object>>)
        [InlineData("naplo.json")]             // JsonStore: a böngésző JSON-ja beolvasva és visszaírva
        [InlineData("futas.json")]
        [InlineData("felulet.json")]           // a telepítő olvassa be és írja vissza
        [InlineData("riasztasok.json")]        // Letöltő, Alerts
        [InlineData("preset.json")]            // DroughtStore.SavePreset
        [InlineData("nyelvek.json")]           // Langs.ListJson
        public void Fajl_228_altalanos_alakban_oda_vissza_bajtra_egyezik(string nev)
        {
            string regi = Minta228(nev);
            Assert.Equal(regi, Json.Serialize(Json.DeserializeObject(regi)));
        }

        [Theory]
        [InlineData("szarazsag.json")]
        [InlineData("szarazsag_alap.json")]
        public void Fajl_228_szarazsag_tipusosan_oda_vissza_bajtra_egyezik(string nev)
        {
            string regi = Minta228(nev);
            DConfig c = Json.Deserialize<DConfig>(regi)!;
            Assert.Equal(regi, Json.Serialize(c));
        }

        [Fact]
        public void Fajl_228_szarazsag_tartalma_helyesen_olvasodik()
        {
            DConfig c = Json.Deserialize<DConfig>(Minta228("szarazsag.json"))!;
            Assert.Equal(2, c.version);
            Assert.Equal(1.0 / 3, c.global.solarFactor);
            Assert.Equal(0.1 + 0.2, c.fwi.windFactor);
            Assert.Equal("Próbakert: homok & agyag <15 cm> 'felső'", c.soil.name);
            Assert.Equal(7, c.profiles.Count);
            Assert.Equal("Gyep (szórófejes)", c.profiles[1].name);
            Assert.Equal(1e-5, c.profiles[1].lawnArea);
            Assert.Equal(1e16, c.profiles[2].count);
            Assert.False(c.profiles[6].enabled);
            Assert.Equal("Bokrok \"csepegtető\"", c.programs[1].name);
            Assert.Equal(3, c.programs[1].every);
            Assert.True(c.schedules[0].yearly);
            Assert.Equal("kézi\töntözés\nkét sor", c.log[0].note);
            Assert.Null(c.log[0].diaryId);
            Assert.Equal("d-0001", c.log[1].diaryId);
            Assert.Equal(22.25, c.log[1].amount);
            Assert.Equal(3, c.gdd.milestones.Count);
            Assert.Equal(400.0, c.gdd.milestones[2].gdd);
        }

        [Fact]
        public void Fajl_228_szarazsag_regebbi_hianyos_fajl_az_alapertekekkel_toltodik()
        {
            // egy korábbi változat fájljában még nincs minden mező: ami hiányzik, alapértéken marad (a 2.x erre épít)
            DConfig c = Json.Deserialize<DConfig>("{\"version\":1,\"global\":{\"albedo\":0.25},\"profiles\":[{\"id\":\"gyep\",\"kcMid\":0.85}],\"regiMezo\":true}")!;
            Assert.Equal(1, c.version);
            Assert.Equal(0.25, c.global.albedo);
            Assert.Equal(1.10, c.global.solarFactor);
            Assert.Equal(0.85, c.profiles[0].kcMid);
            Assert.Equal(0.55, c.profiles[0].kcIni);
            Assert.Equal("1,4", c.profiles[0].autoDays);
            Assert.Empty(c.programs);
            Assert.Equal(0.36, c.soil.subFC);
            Assert.Equal(3, c.gdd.milestones.Count);
        }

        [Theory]
        [InlineData("kalibracio.json")]
        [InlineData("kalibracio_alap.json")]
        public void Fajl_228_kalibracio_tipusosan_oda_vissza_bajtra_egyezik(string nev)
        {
            string regi = Minta228(nev);
            Cfg c = Json.Deserialize<Cfg>(regi)!;
            Assert.Equal(regi, Json.Serialize(c));
        }

        [Fact]
        public void Fajl_228_kalibracio_tartalma_helyesen_olvasodik()
        {
            Cfg c = Json.Deserialize<Cfg>(Minta228("kalibracio.json"))!;
            Assert.True(c.enabled);
            Assert.Equal("period", c.mode);
            Assert.Equal(1.07, c.factor);
            Assert.Equal(2, c.rows.Count);
            Assert.Equal("esőmérő henger – próba", c.rows[0].note);
            Assert.Equal(23.4, c.rows[0].manual);
        }

        // a Letöltő tördelője (Letolto.cs, PrettyJson) – változatlan másolat, csak a teszthez
        static string PrettyJson(string json)
        {
            var sb = new StringBuilder();
            int ind = 0;
            bool q = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (q)
                {
                    sb.Append(c);
                    if (c == '\\') { sb.Append(json[++i]); }
                    else if (c == '"') q = false;
                    continue;
                }
                switch (c)
                {
                    case '"': q = true; sb.Append(c); break;
                    case '{':
                    case '[': sb.Append(c).Append('\n').Append(' ', ++ind * 2); break;
                    case '}':
                    case ']': sb.Append('\n').Append(' ', --ind * 2).Append(c); break;
                    case ',': sb.Append(c).Append('\n').Append(' ', ind * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        [Fact]
        public void Fajl_228_lekerdezes_allapot_oda_vissza_bajtra_egyezik()
        {
            string regi = Minta228("lekerdezes_allapot.json");   // tördelt alak
            Assert.Equal(regi, PrettyJson(Json.Serialize(Json.DeserializeObject(regi))));
        }

        [Fact]
        public void Fajl_228_lekerdezes_allapot_ugy_olvashato_ahogy_a_Letolto_olvassa()
        {
            // State.Load (Letolto.cs) lépései
            var root = (Dictionary<string, object>)Json.DeserializeObject(Minta228("lekerdezes_allapot.json"))!;
            Assert.Equal("2026-09-30", Convert.ToString(root["apiCallDay"], Inv));
            Assert.Equal(37, Convert.ToInt32(root["apiCallCount"], Inv));
            var runs = root["runs"] as object[];
            Assert.NotNull(runs);
            Assert.Equal(2, runs!.Length);
            Assert.Equal("hiba: a kiszolgáló válasza: \"401\" {érvénytelen kulcs}, [próbáld újra]", ((Dictionary<string, object>)runs[1])["result"]);
            var stations = root["stations"] as Dictionary<string, object>;
            Assert.NotNull(stations);
            Assert.Equal(new[] { "ITESZT1", "ITESZT2" }, stations!.Keys.ToArray());
            var s = (Dictionary<string, object>)stations["ITESZT1"];
            var days = s["days"] as Dictionary<string, object>;
            Assert.Equal(new[] { "2026-09-28", "2026-09-29", "2026-09-30" }, days!.Keys.ToArray());
            Assert.Equal("reszleges", Convert.ToString(days["2026-09-30"], Inv));
            Assert.Equal("2026-09-30T10:15:02.1234567Z", s["lastSuccessUtc"] as string);   // szöveg marad, nem dátum
            Assert.Null(s["lastDailyUtc"]);
            Assert.Equal(120, Convert.ToInt32(s["tzOffsetMin"], Inv));
            Assert.Equal(-60, Convert.ToInt32(((Dictionary<string, object>)stations["ITESZT2"])["tzOffsetMin"], Inv));
        }

        [Fact]
        public void Fajl_228_felulet_a_telepito_modositasa_utan_a_sorrend_megmarad()
        {
            // Telepito.cs: beolvas, d["lang"] = kód, visszaír
            string regi = Minta228("felulet.json");
            var d = (Dictionary<string, object>)Json.DeserializeObject(regi)!;
            d["lang"] = "de";
            Assert.Equal(regi.Replace("\"lang\":\"hu\"", "\"lang\":\"de\""), Json.Serialize(d));
            // mérve: a böngésző 1e-7 és 1e21 alakját a régi 1E-07 és 1E+21 alakban írta vissza
            Assert.Contains("\"tiny\":1E-07,\"big\":1E+21,\"egesz\":4294967296", regi);
            var units = (Dictionary<string, object>)d["units"];
            Assert.Equal("kmh", Convert.ToString(units["wind"], Inv));
        }

        [Fact]
        public void Fajl_228_bongeszo_irta_naplo_a_regi_alakban_irodik_vissza()
        {
            // a SelfTest.cs próbaadata: a felület JSON.stringify-jal írt listája
            const string bongeszo = "[{\"id\":\"d1\",\"date\":\"2026-10-10\",\"text\":\"metszés\",\"amount\":2},"
                + "{\"id\":2,\"t\":1792000000000,\"intensity\":\"<b>\",\"km\":7.50}]";
            var a = Json.DeserializeObject(bongeszo) as object[];
            Assert.NotNull(a);
            var elso = (Dictionary<string, object>)a![0];
            var masodik = (Dictionary<string, object>)a[1];
            Assert.Equal("metszés", elso["text"]);
            Assert.IsType<int>(elso["amount"]);
            Assert.IsType<long>(masodik["t"]);
            Assert.Equal("<b>", masodik["intensity"]);
            // mérve: a < és > \u003c / \u003e alakban, a 7.50 tizedesjegyei megmaradnak
            Assert.Equal("[{\"id\":\"d1\",\"date\":\"2026-10-10\",\"text\":\"metszés\",\"amount\":2},"
                + "{\"id\":2,\"t\":1792000000000,\"intensity\":\"\\u003cb\\u003e\",\"km\":7.50}]", Json.Serialize(a));
        }

        [Fact]
        public void Fajl_228_valtozasnaplo_a_beagyazott_json_szoveg_ep_marad()
        {
            // ChangeLog.Read: object[] → Dictionary-k; a "from" / "to" maga is JSON-szöveg (Calib)
            var a = Json.DeserializeObject(Minta228("valtozasnaplo.json")) as object[];
            Assert.Equal(3, a!.Length);
            var e = (Dictionary<string, object>)a[0];
            Cfg c = Json.Deserialize<Cfg>((string)e["to"])!;
            Assert.Equal(1.07, c.factor);
            Assert.Equal("calib", ((Dictionary<string, object>)e["revert"])["type"]);
            Assert.False(((Dictionary<string, object>)a[1]).ContainsKey("revert"));
            Assert.Null(((Dictionary<string, object>)a[2])["from"]);
        }

        [Fact]
        public void Utemezes_Spec_a_bongeszo_keresebol()
        {
            // Api.cs: Deserialize<Kozos.Utemezes.Spec>(body)
            Spec s = Json.Deserialize<Spec>("{\"kind\":\"weekly\",\"minutes\":0,\"time\":\"06:30\",\"days\":[\"MON\",\"THU\"]}")!;
            Assert.Equal("weekly", s.kind);
            Assert.Equal(0, s.minutes);
            Assert.Equal("06:30", s.time);
            Assert.Equal(new[] { "MON", "THU" }, s.days);
            Assert.Null(s.exe);
            // mérve
            Assert.Equal("{\"kind\":\"off\",\"minutes\":0,\"time\":\"06:00\",\"days\":[],\"exe\":null}", Json.Serialize(new Spec()));
        }
    }
}
