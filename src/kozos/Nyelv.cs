using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

// Többnyelvűség a C# programokban (Megjelenítő, Letöltő, Telepítő) – ugyanazokból a fájlokból, mint a felület:
// web/lang/<kód>.json (beágyazott erőforrásként „web/lang/<kód>.json” néven). A szöveg forrása mindig a magyar;
// új nyelvhez elég egy új JSON-fájl (docs/FORDITAS.md), a programkódot nem kell módosítani.
static class Langs
{
    public static string Dir;            // fejlesztői mód: a web\lang mappa (ha meg van adva, innen olvas)
    public static string Current = "hu"; // a Letöltő és a Telepítő ezt használja a T()-hez
    static readonly Dictionary<string, Dictionary<string, object>> Cache = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
    static readonly Regex CodeRe = new Regex(@"^[a-z]{2,3}(-[A-Za-z]{2})?$");

    public static IEnumerable<string> Codes()
    {
        var res = new List<string>();
        if (Dir != null && Directory.Exists(Dir))
            res.AddRange(Directory.GetFiles(Dir, "*.json").Select(Path.GetFileNameWithoutExtension));
        else
            foreach (var n in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                if (n.StartsWith("web/lang/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    res.Add(n.Substring(9, n.Length - 14));
        return res.Where(c => !c.StartsWith("_") && CodeRe.IsMatch(c)).Distinct().OrderBy(c => c);
    }

    public static bool Exists(string code) { return code != null && CodeRe.IsMatch(code) && Codes().Contains(code); }

    static Dictionary<string, object> Raw(string code)
    {
        if (code == null || code == "hu" || !CodeRe.IsMatch(code)) return null;
        lock (Cache)
        {
            Dictionary<string, object> d;
            if (Cache.TryGetValue(code, out d)) return d;
            string text = null;
            try
            {
                if (Dir != null) { string f = Path.Combine(Dir, code + ".json"); if (File.Exists(f)) text = File.ReadAllText(f, Encoding.UTF8); }
                else
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("web/lang/" + code + ".json"))
                        if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
                d = text == null ? null : new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text) as Dictionary<string, object>;
            }
            catch { d = null; }
            Cache[code] = d;
            return d;
        }
    }

    public static Dictionary<string, object> Meta(string code)
    {
        var d = Raw(code);
        object m;
        return d != null && d.TryGetValue("_meta", out m) ? m as Dictionary<string, object> : null;
    }

    // a programfájlok neve az adott nyelven (a telepítő nyelve szerint): _meta.files.viewer / .downloader
    public static readonly Dictionary<string, string> HuFiles = new Dictionary<string, string> { { "viewer", "Megjelenítő.exe" }, { "downloader", "Letöltő.exe" } };
    public static string FileName(string kind, string code)
    {
        if (code != null && code != "hu")
        {
            var m = Meta(code);
            object f, v;
            if (m != null && m.TryGetValue("files", out f) && f is Dictionary<string, object> && ((Dictionary<string, object>)f).TryGetValue(kind, out v))
            {
                string s = Convert.ToString(v).Trim();
                // csak egyszerű fájlnév (mappa, tiltott karakter nélkül), .exe végződéssel
                if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && s.Length > 4 && s.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0) return s;
            }
        }
        return HuFiles[kind];
    }
    // minden ismert név (minden nyelv), a régi telepítések és a nyelvváltás felismeréséhez
    public static List<string> AllFileNames(string kind)
    {
        var res = new List<string> { HuFiles[kind] };
        foreach (var c in Codes()) { string n = FileName(kind, c); if (!res.Contains(n, StringComparer.OrdinalIgnoreCase)) res.Add(n); }
        return res;
    }

    // [{code, name, locale}] – a magyar mindig az első
    public static string ListJson()
    {
        var js = new JavaScriptSerializer();
        var list = new List<Dictionary<string, object>> { new Dictionary<string, object> { { "code", "hu" }, { "name", "Magyar" }, { "locale", "hu-HU" } } };
        foreach (var c in Codes())
        {
            var m = Meta(c);
            if (m == null) continue;
            object n, l;
            m.TryGetValue("name", out n); m.TryGetValue("locale", out l);
            list.Add(new Dictionary<string, object> { { "code", c }, { "name", Convert.ToString(n ?? c) }, { "locale", Convert.ToString(l ?? c) } });
        }
        return js.Serialize(list);
    }

    // fordítás a beállított nyelvre: teljes egyezés, különben a benne lévő ismert kifejezések cseréje
    public static string T(string hu) { return T(hu, Current); }
    public static string T(string hu, string code)
    {
        if (string.IsNullOrEmpty(hu) || code == "hu") return hu;
        var d = Raw(code);
        if (d == null) return hu;
        object v;
        string key = hu.Trim();
        if (d.TryGetValue(key, out v) && v is string && ((string)v).Length > 0) return hu.Replace(key, (string)v);
        string res = hu;
        foreach (var kv in d.Where(x => x.Key != "_meta" && x.Value is string && ((string)x.Value).Length > 0 && x.Key.Length >= 3).OrderByDescending(x => x.Key.Length))
            if (res.IndexOf(kv.Key, StringComparison.Ordinal) >= 0) res = res.Replace(kv.Key, (string)kv.Value);
        return res;
    }
    public static string F(string hu, params object[] args) { return string.Format(T(hu), args); }

    // a Windows nyelvéből ajánlott kód (a telepítőhöz); ha nincs ilyen nyelvi fájl, magyar
    public static string FromSystem()
    {
        string c = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return c == "hu" || !Exists(c) ? "hu" : c;
    }
}
