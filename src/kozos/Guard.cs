using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Fájlművelet-őr: a programok csak a saját mappájukon belül írhatnak, törölhetnek, nevezhetnek át.
// Minden író művelet ezen keresztül megy; ami a mappán kívülre mutatna, azt megtagadja (és a hívó naplózza).
// Kivétel csak az, amit a felhasználó maga választ ki (pl. az Excel-export mentés ablaka).
static class Guard
{
    static string root;
    public static string Root { get { return root; } }

    public static void SetRoot(string dir)
    {
        root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
    }

    public static string P(string path)
    {
        if (root == null) throw new InvalidOperationException("Guard: nincs beállítva a programmappa.");
        string full = Path.GetFullPath(path);
        if (!(full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("Biztonsági őr: a program csak a saját mappájába írhat (" + root + "), ide nem: " + full);
        return full;
    }

    public static bool Inside(string path)
    {
        try { P(path); return true; } catch { return false; }
    }

    public static void WriteAllText(string path, string text, Encoding enc) { File.WriteAllText(P(path), text, enc); }
    public static void WriteAllText(string path, string text) { File.WriteAllText(P(path), text); }
    public static void WriteAllLines(string path, IEnumerable<string> lines, Encoding enc) { File.WriteAllLines(P(path), lines, enc); }
    public static void AppendAllText(string path, string text, Encoding enc) { File.AppendAllText(P(path), text, enc); }
    public static void AppendAllText(string path, string text) { File.AppendAllText(P(path), text); }
    public static void Move(string from, string to) { File.Move(P(from), P(to)); }
    public static void Replace(string src, string dst, string backup) { File.Replace(P(src), P(dst), backup == null ? null : P(backup)); }
    public static void Copy(string from, string to) { File.Copy(from, P(to)); }
    public static void Copy(string from, string to, bool overwrite) { File.Copy(from, P(to), overwrite); }
    public static void Delete(string path) { File.Delete(P(path)); }
    public static FileStream Create(string path) { return File.Create(P(path)); }
    public static FileStream OpenWrite(string path, FileMode mode, FileAccess access, FileShare share) { return new FileStream(P(path), mode, access, share); }
    public static DirectoryInfo CreateDirectory(string path) { return Directory.CreateDirectory(P(path)); }
    public static void DeleteDirectory(string path, bool recursive) { Directory.Delete(P(path), recursive); }

    // Ugyanazt a fájlt (pl. beallitasok.ini) a Megjelenítő és az ütemezett Letöltő is írhatja: rendszerszintű,
    // a fájl útvonalához kötött zár, hogy az egyik módosítása ne vesszen el (15.3). Legfeljebb 10 másodpercig vár.
    public static T Locked<T>(string path, Func<T> f)
    {
        uint h = 2166136261;
        foreach (char c in Path.GetFullPath(path).ToLowerInvariant()) { h ^= c; h *= 16777619; }
        using (var m = new System.Threading.Mutex(false, "WStation_file_" + h.ToString("x8")))
        {
            bool own = false;
            try { own = m.WaitOne(10000); }
            catch (System.Threading.AbandonedMutexException) { own = true; }
            try { return f(); }
            finally { if (own) m.ReleaseMutex(); }
        }
    }
    public static void Locked(string path, Action a) { Locked<object>(path, () => { a(); return null; }); }

    // atomi csere: előbb .tmp, aztán a régi helyére – egy félbeszakadt írás nem hagy csonka fájlt
    public static void WriteAllLinesAtomic(string path, IEnumerable<string> lines, Encoding enc)
    {
        string tmp = path + ".tmp";
        File.WriteAllLines(P(tmp), lines, enc);
        if (File.Exists(P(path))) File.Replace(P(tmp), P(path), null); else File.Move(P(tmp), P(path));
    }
}
