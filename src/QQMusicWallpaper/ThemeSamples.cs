using System;
using System.Drawing;
using System.Threading;

namespace QQMusicWallpaper;

internal sealed class ThemeSample
{
    internal readonly Color Color;
    internal readonly double Brightness;
    internal ThemeSample(Color color, double brightness) { Color = color; Brightness = brightness; }
}

// Keep one property read outstanding, outside the appearance UI thread.
// Accept a result under the session lock, then persist outside that lock.
internal sealed class ThemeSamples : IDisposable
{
    private readonly Func<ThemeSample> capture;
    private readonly Func<ThemeSample, Action> accept;
    private readonly object gate = new object();
    private ThemeSample latest;
    private DateTime captured, requested;
    private bool busy, disposed, slowReported;
    private int generation;
    internal ThemeSamples(Func<ThemeSample> capture, Func<ThemeSample, Action> accept = null)
    { this.capture = capture; this.accept = accept; }
    internal ThemeSample Read()
    {
        lock (gate)
        {
            if (disposed) return null;
            if (!busy && DateTime.UtcNow - requested > TimeSpan.FromMilliseconds(1000))
            {
                busy = true; slowReported = false; requested = DateTime.UtcNow;
                int epoch = generation;
                ThreadPool.QueueUserWorkItem(_ => Capture(epoch));
            }
            else if (busy && !slowReported && DateTime.UtcNow - requested > TimeSpan.FromSeconds(3))
            { slowReported = true; RuntimeLog.Write("Theme property read delayed; retaining last color without blocking settings."); }
            return DateTime.UtcNow - captured < TimeSpan.FromSeconds(5) ? latest : null;
        }
    }
    private void Capture(int epoch)
    {
        ThemeSample result = null;
        try { result = capture(); }
        catch (Exception error) { RuntimeLog.Write("Theme property read failed: " + error.GetType().Name); }
        try
        {
            Action persist = null;
            lock (gate)
            {
                if (!disposed && epoch == generation)
                {
                    latest = result; captured = result == null ? DateTime.MinValue : DateTime.UtcNow;
                    if (result != null && accept != null) persist = accept(result);
                }
            }
            if (persist != null) persist();
        }
        catch (Exception error) { RuntimeLog.Write("Theme property publication failed: " + error.GetType().Name); }
        finally { lock (gate) { busy = false; } }
    }
    internal void Clear() { lock (gate) { generation++; latest = null; captured = DateTime.MinValue; } }
    public void Dispose() { lock (gate) { disposed = true; latest = null; } }
}
