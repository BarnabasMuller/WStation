using System;
using System.Threading;

namespace WStation.Core.Common;

/// <summary>
/// A mag egyetlen órája (PLAN-3.0 14.3, 4.3, 17/4): a magban közvetlen <c>DateTime.Now/UtcNow/Today</c> nem szerepelhet.
/// Alapértelmezésben a rendszeróra; tesztben <see cref="Freeze"/> rögzíti, <see cref="Advance"/> lépteti.
/// A felülírás <see cref="AsyncLocal{T}"/>-ban él (nem írható statikus mező): a párhuzamosan futó xUnit-tesztek nem látják
/// egymás óráját, a tesztből indított szálak/feladatok viszont örökölik. Az 1.9-ben az óra még nem munkaterület-függő;
/// a 6.2 szerinti <c>Now(ws)</c> alakot az S2 egy túlterheléssel bővítheti, a hívók cseréje nélkül.
/// </summary>
public static class Clock
{
    sealed class Frozen
    {
        readonly object _gate = new();
        DateTime _utc;
        public Frozen(DateTime utc) { _utc = utc; }
        public DateTime Utc { get { lock (_gate) return _utc; } }
        public void Advance(TimeSpan d) { lock (_gate) _utc += d; }
    }

    static readonly AsyncLocal<Frozen?> Override = new();

    /// <summary>A pillanatnyi idő (UTC, <see cref="DateTimeKind.Utc"/>) – megegyezik a <c>DateTime.UtcNow</c>-val.</summary>
    public static DateTime UtcNow => Override.Value?.Utc ?? DateTime.UtcNow;

    /// <summary>A pillanatnyi helyi idő (a gép időzónájában, <see cref="DateTimeKind.Local"/>) – megegyezik a <c>DateTime.Now</c>-val.</summary>
    public static DateTime Now => Override.Value is { } f ? f.Utc.ToLocalTime() : DateTime.Now;

    /// <summary>A mai nap helyi dátuma – megegyezik a <c>DateTime.Today</c>-jel.</summary>
    public static DateTime Today => Now.Date;

    /// <summary>A pillanatnyi idő <see cref="DateTimeOffset"/>-ként (helyi eltolással) – megegyezik a <c>DateTimeOffset.Now</c>-val.</summary>
    public static DateTimeOffset OffsetNow => Override.Value is { } f ? new DateTimeOffset(f.Utc).ToLocalTime() : DateTimeOffset.Now;

    /// <summary>A pillanatnyi idő <see cref="DateTimeOffset"/>-ként, UTC – megegyezik a <c>DateTimeOffset.UtcNow</c>-val.</summary>
    public static DateTimeOffset OffsetUtcNow => Override.Value is { } f ? new DateTimeOffset(f.Utc) : DateTimeOffset.UtcNow;

    /// <summary>Rögzíti az órát az adott UTC pillanatra, a visszaadott érték eldobásáig (a korábbi állapotot állítja vissza). Csak az aktuális
    /// (aszinkron) környezetre hat; beágyazható.</summary>
    public static IDisposable Freeze(DateTime utc)
    {
        if (utc.Kind == DateTimeKind.Local) utc = utc.ToUniversalTime();
        else utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var prev = Override.Value;
        Override.Value = new Frozen(utc);
        return new Restore(prev);
    }

    /// <summary>Előreléptet egy rögzített órát. Rögzítés nélkül hibát jelez.</summary>
    public static void Advance(TimeSpan d)
    {
        var f = Override.Value ?? throw new InvalidOperationException("Az óra nincs rögzítve (Clock.Freeze).");
        f.Advance(d);
    }

    sealed class Restore : IDisposable
    {
        readonly Frozen? _prev; bool _done;
        public Restore(Frozen? prev) { _prev = prev; }
        public void Dispose() { if (_done) return; _done = true; Override.Value = _prev; }
    }
}
