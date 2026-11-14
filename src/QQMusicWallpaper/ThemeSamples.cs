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

// PrintWindow can wait inside an external renderer. Keep at most one capture
// outstanding, outside the appearance UI thread and its preferences lock.
internal sealed class ThemeSamples : IDisposable
{
    private readonly Func<ThemeSample> capture;
    private readonly object gate = new object();
    private ThemeSample latest;
    private DateTime captured, requested;
    private bool busy, disposed, slowReported;
    private int generation;
    internal ThemeSamples(Func<ThemeSample> capture) { this.capture = capture; }
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
            { slowReported = true; RuntimeLog.Write("Theme capture delayed; retaining last color without blocking settings."); }
            return DateTime.UtcNow - captured < TimeSpan.FromSeconds(5) ? latest : null;
        }
    }
    private void Capture(int epoch)
    {
        ThemeSample result = null;
        try { result = capture(); }
        catch (Exception error) { RuntimeLog.Write("Theme capture failed: " + error.GetType().Name); }
        finally
        {
            lock (gate)
            {
                if (!disposed && epoch == generation) { latest = result; captured = result == null ? DateTime.MinValue : DateTime.UtcNow; }
                busy = false;
            }
        }
    }
    internal void Clear() { lock (gate) { generation++; latest = null; captured = DateTime.MinValue; } }
    public void Dispose() { lock (gate) { disposed = true; latest = null; } }
}
