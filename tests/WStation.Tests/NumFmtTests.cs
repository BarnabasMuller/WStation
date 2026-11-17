using System;
using System.Collections.Generic;
using WStation.Core.Common;
using Xunit;

namespace WStation.Tests
{
    /// <summary>NumFmt (PLAN-3.0 7.3): a 2.2.8 által Windowson használt CultureInfo-értékek, ICU nélkül.</summary>
    public class NumFmtTests
    {
        [Theory]
        [InlineData("hu", "0.0", 1.25, "1,3")]
        [InlineData("hu", "0.00", -0.5, "-0,50")]
        [InlineData("de", "0.0", 12.34, "12,3")]
        [InlineData("en", "0.0", 12.34, "12.3")]
        [InlineData("es", "0", 7.6, "8")]
        [InlineData("hu", "#,##0.0", 12345.6, "12 345,6")]
        [InlineData("de", "#,##0.0", 12345.6, "12.345,6")]
        [InlineData("en", "#,##0.0", 12345.6, "12,345.6")]
        public void Format_a_nyelv_jeleivel(string lang, string format, double v, string vart)
        {
            Assert.Equal(vart, NumFmt.For(lang).Format(v, format));
        }

        [Theory]
        [InlineData("hu", DayOfWeek.Monday, "hétfő")]
        [InlineData("hu", DayOfWeek.Sunday, "vasárnap")]
        [InlineData("de", DayOfWeek.Thursday, "Donnerstag")]
        [InlineData("en", DayOfWeek.Saturday, "Saturday")]
        [InlineData("es", DayOfWeek.Wednesday, "miércoles")]
        public void DayName_a_nyelven(string lang, DayOfWeek d, string vart)
        {
            Assert.Equal(vart, NumFmt.For(lang).DayName(d));
        }

        [Fact]
        public void Ismeretlen_nyelv_vagy_teruleti_beallitas_magyar()
        {
            Assert.Same(NumFmt.Hu, NumFmt.For(null));
            Assert.Same(NumFmt.Hu, NumFmt.For("xx"));
            Assert.Same(NumFmt.Hu, NumFmt.FromMeta(new Dictionary<string, object> { { "locale", "fr-FR" } }));
        }

        [Fact]
        public void A_meta_dec_grp_days_felulirja_a_tablazatot()
        {
            var f = NumFmt.FromMeta(new Dictionary<string, object>
            {
                { "locale", "en-GB" }, { "dec", "," }, { "grp", "'" },
                { "days", new object[] { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" } },
            });
            Assert.Equal("1'234,5", f.Format(1234.5, "#,##0.0"));
            Assert.Equal("Mo", f.DayName(DayOfWeek.Monday));
            Assert.Equal("So", f.DayName(DayOfWeek.Sunday));
        }

        [Fact]
        public void Elvalaszto_valasztas_mint_az_exportban()
        {
            // az Excel-export: tizedesvesszőnél „;”, tizedespontnál „,” (Extras.cs SetLang)
            Assert.Equal(",", NumFmt.For("hu").Dec);
            Assert.Equal(".", NumFmt.For("en").Dec);
        }
    }
}
