using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Kozos
{
    // Frissítéskor a beállításfájlba beírja a sablon azon kulcsait, amik még hiányoznak (a megjegyzésükkel együtt).
    // A felhasználó által átírt értékekhez nem nyúl.
    static class IniMerge
    {
        public static List<string> Merge(string path, string template)
        {
            if (!File.Exists(path)) return new List<string>();
            return Guard.Locked(path, () => MergeUnlocked(path, template));   // a másik program se írjon bele közben (15.3)
        }
        static List<string> MergeUnlocked(string path, string template)
        {
            var added = new List<string>();
            if (!File.Exists(path)) return added;
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                text = sr.ReadToEnd();
            foreach (var raw in text.Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l[0] == '#' || l[0] == ';') continue;
                int eq = l.IndexOf('=');
                if (eq > 0) existing.Add(l.Substring(0, eq).Trim());
            }

            var sb = new StringBuilder();
            var comments = new List<string>();
            foreach (var raw in template.Replace("\r\n", "\n").Split('\n'))
            {
                string l = raw.TrimEnd();
                if (l.Trim().Length == 0) { comments.Clear(); continue; }
                if (l.TrimStart().StartsWith("#")) { comments.Add(l); continue; }
                int eq = l.IndexOf('=');
                if (eq <= 0) { comments.Clear(); continue; }
                string key = l.Substring(0, eq).Trim();
                if (!existing.Contains(key))
                {
                    if (comments.Count > 0 || sb.Length == 0) sb.Append("\r\n");
                    foreach (var c in comments) sb.Append(c).Append("\r\n");
                    sb.Append(l).Append("\r\n");
                    added.Add(key);
                    existing.Add(key);
                }
                comments.Clear();
            }
            if (added.Count > 0)
            {
                string prefix = text.EndsWith("\n") ? "" : "\r\n";
                Guard.AppendAllText(path, prefix + "\r\n# --- Új beállítások (" + DateTime.Now.ToString("yyyy-MM-dd") + ") ---" + sb, new UTF8Encoding(false));
            }
            return added;
        }
    }
}
