// WStation – az indítók parancssor-kezelése (PLAN-3.0 9.4). Külön fájlban, mert az egységtesztek is ezt fordítják
// (tests/WStation.Tests/InditoTests.cs). C# 7.3, .NET Framework 4.8 és .NET 10 alatt is fordul.
using System;

namespace WStation.Launcher
{
    static class Parancssor
    {
        // a parancssor a programnév után, betű szerint (az idézőjelek és szóközök úgy maradnak, ahogy a hívó megadta).
        // A programnév a Windows szabálya szerint: idézőjelben a következő idézőjelig, különben az első szóközig / tabulátorig.
        public static string ArgsTail(string commandLine)
        {
            string s = commandLine ?? "";
            int i = 0;
            if (s.Length > 0 && s[0] == '"')
            {
                int end = s.IndexOf('"', 1);
                i = end < 0 ? s.Length : end + 1;
            }
            else while (i < s.Length && s[i] != ' ' && s[i] != '\t') i++;
            return s.Substring(i).Trim(' ', '\t');
        }

        // megadta-e a hívó a --mappa kapcsolót (az argv[0] a program neve)
        public static bool HasMappa(string[] argv)
        {
            for (int i = 1; i < argv.Length; i++) if (string.Equals(argv[i], "--mappa", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // idézőjelbe tett útvonal; a záró „\”-ek megduplázva, hogy ne a záró idézőjelet védjék („C:\WStation\” → "C:\WStation\\")
        public static string Quote(string path)
        {
            int n = 0;
            while (n < path.Length && path[path.Length - 1 - n] == '\\') n++;
            return "\"" + path + new string('\\', n) + "\"";
        }

        // a gyerek teljes parancssora: a program idézőjelben, a hívó kapcsolói betű szerint, és ha nem adta meg, a --mappa
        public static string Build(string exe, string commandLine, string[] argv, string root)
        {
            string args = ArgsTail(commandLine);
            if (!HasMappa(argv)) args = (args.Length > 0 ? args + " " : "") + "--mappa " + Quote(root);
            return Quote(exe) + (args.Length > 0 ? " " + args : "");
        }
    }
}
