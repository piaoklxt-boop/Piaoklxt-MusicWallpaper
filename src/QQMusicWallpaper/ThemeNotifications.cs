using System;
using System.Threading;

namespace QQMusicWallpaper;

internal sealed class ThemeNotifications
{
    private readonly Action deliver;
    private readonly object gate = new object();
    private bool running, pending;
    internal ThemeNotifications(Action deliver) { this.deliver = deliver; }
    internal void Request()
    {
        lock (gate)
        {
            pending = true;
            if (running) return;
            running = true;
        }
        ThreadPool.QueueUserWorkItem(_ => Drain());
    }
    private void Drain()
    {
        while (true)
        {
            lock (gate)
            {
                if (!pending) { running = false; return; }
                pending = false;
            }
            try { deliver(); }
            catch (Exception error) { RuntimeLog.Write("Theme notification failed: " + error.GetType().Name); }
        }
    }
}
