using System;
using System.Collections.Generic;
using System.Globalization;

namespace WStation.Core.Common
{
    /// <summary>
    /// Számformázás nyelv szerint, nyelvi adatbázis (ICU) nélkül (PLAN-3.0 7.3). A NAS-on nincs ICU, a .NET invariáns módban fut
    /// (<c>InvariantGlobalization</c>), ezért a <c>CultureInfo.GetCultureInfo("hu-HU")</c> nem használható: a tizedesjelet,
    /// az ezres elválasztót és a hét napjainak nevét ez az osztály adja.
    /// </summary>
    /// <remarks>
    /// Forrás: a nyelvi fájl <c>_meta</c> része (<c>"dec"</c>, <c>"grp"</c>, <c>"days"</c> – hétfőtől vasárnapig), ha megvan;
    /// különben a <c>_meta.locale</c> szerinti beépített táblázat, amely a 2.2.8 által Windowson használt
    /// <c>CultureInfo</c> értékeit adja (hu-HU, de-DE, en-GB, en-US, es-ES). Ismeretlen nyelv vagy területi beállítás: magyar
    /// (a 2.2.8 is magyarra esett vissza, ha a területi beállítás nem volt érvényes).
    /// </remarks>
    public sealed class NumFmt
    {
        /// <summary>Tizedesjel, pl. „,”.</summary>
        public string Dec { get; }

        /// <summary>Ezres elválasztó, pl. nem törhető szóköz.</summary>
        public string Grp { get; }

        readonly string[] days;   // a DayOfWeek sorrendjében: vasárnap = 0
        readonly NumberFormatInfo nfi;

        NumFmt(string dec, string grp, string[] daysFromSunday)
        {
            Dec = dec;
            Grp = grp;
            days = daysFromSunday;
            var n = (NumberFormatInfo)NumberFormatInfo.InvariantInfo.Clone();
            n.NumberDecimalSeparator = n.PercentDecimalSeparator = n.CurrencyDecimalSeparator = dec;
            n.NumberGroupSeparator = n.PercentGroupSeparator = n.CurrencyGroupSeparator = grp;
            nfi = NumberFormatInfo.ReadOnly(n);
        }

        // A 2.2.8 által Windowson (.NET Framework 4.8) használt értékek; a magyar ezres elválasztó ott nem törhető szóköz.
        static readonly Dictionary<string, NumFmt> Known = new Dictionary<string, NumFmt>(StringComparer.OrdinalIgnoreCase)
        {
            { "hu-HU", new NumFmt(",", " ", new[] { "vasárnap", "hétfő", "kedd", "szerda", "csütörtök", "péntek", "szombat" }) },
            { "de-DE", new NumFmt(",", ".", new[] { "Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag" }) },
            { "en-GB", new NumFmt(".", ",", new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" }) },
            { "en-US", new NumFmt(".", ",", new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" }) },
            { "es-ES", new NumFmt(",", ".", new[] { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" }) },
        };

        /// <summary>A magyar formátum (a program alapnyelve).</summary>
        public static NumFmt Hu => Known["hu-HU"];

        /// <summary>Az adott nyelv (nyelvi kód, pl. „de”) formátuma; ismeretlen nyelvre a magyar.</summary>
        public static NumFmt For(string? lang)
        {
            if (lang == null || lang == "hu") return Hu;
            return FromMeta(Langs.Meta(lang));
        }

        /// <summary>A formátum egy nyelvi fájl <c>_meta</c> részéből (lásd az osztály leírását).</summary>
        internal static NumFmt FromMeta(IDictionary<string, object>? meta)
        {
            if (meta == null) return Hu;
            NumFmt? baseFmt = null;
            if (meta.TryGetValue("locale", out object? l) && l != null) Known.TryGetValue(Convert.ToString(l, CultureInfo.InvariantCulture) ?? "", out baseFmt);
            baseFmt ??= Hu;
            string dec = Str(meta, "dec") ?? baseFmt.Dec, grp = Str(meta, "grp") ?? baseFmt.Grp;
            string[] d = baseFmt.days;
            if (meta.TryGetValue("days", out object? dl) && dl is object[] arr && arr.Length == 7)
            {
                // a fájlban hétfőtől vasárnapig (ahogy a felület is mutatja), itt vasárnaptól
                d = new string[7];
                for (int i = 0; i < 7; i++) d[(i + 1) % 7] = Convert.ToString(arr[i], CultureInfo.InvariantCulture) ?? "";
            }
            return dec == baseFmt.Dec && grp == baseFmt.Grp && ReferenceEquals(d, baseFmt.days) ? baseFmt : new NumFmt(dec, grp, d);
        }

        static string? Str(IDictionary<string, object> meta, string key)
        {
            return meta.TryGetValue(key, out object? v) && v is string s && s.Length > 0 ? s : null;
        }

        /// <summary>Mint a <c>v.ToString(format, culture)</c>: .NET számformátum, ennek a nyelvnek a jeleivel.</summary>
        public string Format(double v, string format) => v.ToString(format, nfi);

        /// <summary>Mint a <c>culture.DateTimeFormat.GetDayName(d)</c>.</summary>
        public string DayName(DayOfWeek d) => days[(int)d];
    }
}
