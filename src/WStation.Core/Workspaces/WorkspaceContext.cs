#nullable disable
using System;
using System.Threading;

namespace WStation.Core
{
    /// <summary>
    /// Egy munkaterület minden futás közbeni állapota (PLAN-3.0 6.2, „Leltár – a mai írható statikus állapot sorsa”).
    /// A korábbi statikus mezők (<c>AppState</c>, <c>Tz</c>, <c>Loader.gapSec</c>, <c>Cloud</c>, <c>DroughtStore</c>) ma tulajdonságok, amelyek
    /// a <see cref="Ctx.Current"/>-ből olvasnak; a hívási helyek nem változtak. A mezők neve és kezdőértéke a régi statikus mezőé.
    /// A <c>ref</c>-fel átadott mezők (<see cref="busy"/>, <see cref="DownloadSerial"/>) mezők maradnak – tulajdonságra nem lehet <c>ref</c>-et venni –,
    /// ezeknél a hívási hely <c>Ctx.Current.&lt;mező&gt;</c>.
    /// </summary>
    sealed class WorkspaceContext
    {
        public WorkspaceContext(Layout l)
        {
            L = l ?? throw new ArgumentNullException(nameof(l));
        }

        // ---- AppState (Api.cs)
        /// <summary>A munkaterület mappái. Írható: az önteszt a saját ideiglenes mappájára állítja (a 2.x-ben is így volt).</summary>
        public Layout L;
        public Settings S = new Settings();
        public volatile Dataset Data;
        public volatile string Status = "loading"; // loading | downloading | ready | nodata | error
        public volatile string Message = "";
        public volatile string Progress = "";
        public volatile string DownloadMessage = "";
        public volatile bool DownloadOk = true;
        public long DownloadSerial;
        public int busy;
        public bool backupChecked;
        public volatile bool cancelDownload;

        // ---- Tz (Data.cs): a helyi időzóna (Beállítások › Helyszín, megjelenito.ini Idozona; alapból Europe/Budapest)
        public TimeZoneInfo Zone = Tz.Find("Central Europe Standard Time");
        public string Iana = "Europe/Budapest";

        // ---- Loader (Data.cs): a napi lefedettségben ennél hosszabb szünet hiány (a „HianyPerc” beállításból)
        public long GapSec = 900;

        // ---- Cloud: az állomás „tiszta égbolti” burkológörbéjének gyorsítótára (melyik adatsorhoz készült, és maga a görbe)
        public Dataset CloudEnvFor;
        public double[,] CloudEnv;

        // ---- DroughtStore: a szarazsag.json gyorsítótára és a javítási megjegyzés
        public DConfig DroughtCached;
        public string DroughtRepairNote;
    }

    /// <summary>
    /// A futó kód munkaterület-környezete (PLAN-3.0 6.2): <see cref="AsyncLocal{T}"/>, így a szálkészletbe adott munkák és az új szálak is
    /// öröklik. Minden API-kérés és háttérmunka a legelején belép: <c>using (Ctx.Enter(ws)) { … }</c>.
    /// Belépés nélkül a <see cref="Current"/> kivételt dob – nincs alapértelmezés, így egy elfelejtett belépés nem ír csendben rossz mappába.
    /// </summary>
    static class Ctx
    {
        static readonly AsyncLocal<WorkspaceContext> current = new AsyncLocal<WorkspaceContext>();

        /// <summary>A belépett munkaterület; belépés nélkül <see cref="InvalidOperationException"/>.</summary>
        public static WorkspaceContext Current
        {
            get
            {
                return current.Value ?? throw new InvalidOperationException(
                    "Nincs munkaterület-környezet: a kód Ctx.Enter(...) nélkül fut (PLAN-3.0 6.2).");
            }
        }

        /// <summary>Van-e belépett munkaterület (kivétel nélkül – naplózáshoz, ellenőrzéshez).</summary>
        public static bool HasCurrent { get { return current.Value != null; } }

        /// <summary>Belépés a munkaterületbe a visszaadott érték eldobásáig; utána az előző környezet áll vissza. Beágyazható.</summary>
        public static IDisposable Enter(WorkspaceContext ws)
        {
            if (ws == null) throw new ArgumentNullException(nameof(ws));
            var prev = current.Value;
            current.Value = ws;
            return new Restore(prev);
        }

        sealed class Restore : IDisposable
        {
            readonly WorkspaceContext prev;
            bool done;
            public Restore(WorkspaceContext prev) { this.prev = prev; }
            public void Dispose()
            {
                if (done) return;
                done = true;
                current.Value = prev;
            }
        }
    }
}
