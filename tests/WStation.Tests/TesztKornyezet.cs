using System;
using System.IO;
using System.Threading;
using WStation.Core;
using WStation.Core.Common;
using Xunit;

namespace WStation.Tests
{
    /// <summary>
    /// A magot betöltő tesztek közös környezete (2.1). A <see cref="Guard"/> gyökere folyamatszintű (egy gyökér; több gyökér a 2.2-ben jön),
    /// ezért minden ilyen teszt mappája egyetlen ideiglenes gyökér alatt van, és a gyökeret csak egyszer állítjuk be – így a párhuzamosan
    /// futó tesztosztályok (golden, munkaterület-tesztek) nem veszik el egymás elől az írási jogot.
    /// </summary>
    static class TesztKornyezet
    {
        static readonly Lazy<string> gyoker = new(() =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "wstation-teszt-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            Guard.SetRoot(dir);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } };
            return dir;
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>A közös ideiglenes gyökér (egyben a <see cref="Guard"/> gyökere).</summary>
        public static string Gyoker => gyoker.Value;

        /// <summary>Egy új (még nem létező) mappa a gyökér alatt.</summary>
        public static string UjMappa(string nev) => Path.Combine(Gyoker, nev + "-" + Path.GetRandomFileName());

        /// <summary>Munkaterület a mappán, betöltve – ahogy a gazda teszi: létrehozás, belépés, mappák, háttérbetöltés, várakozás.</summary>
        public static WorkspaceContext Betolt(string dir)
        {
            _ = Gyoker;   // a Guard gyökere beállítva
            var L = Layout.For(dir);
            var ws = new WorkspaceContext(L);
            using (Ctx.Enter(ws))
            {
                L.Ensure();
                AppState.RefreshAsync(false);
                for (int i = 0; i < 1200 && AppState.Status == "loading"; i++) Thread.Sleep(100);
                Assert.True(AppState.Status == "ready", "A mintaadat nem töltődött be: " + AppState.Status + " – " + AppState.Message);
            }
            return ws;
        }
    }
}
