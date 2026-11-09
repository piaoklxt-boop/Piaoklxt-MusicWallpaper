using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace QQMusicWallpaper;

internal enum OperationKind { Session, ControlPanel, SettingsChange, SettingsApply, DesktopInteraction }

// Local metadata only: callers pass control identifiers, enum results and numeric
// settings. Never pass song text, window titles, paths or exception messages.
internal static class OperationLog
{
    private static OperationJournal journal;
    public static void Start()
    {
        journal = new OperationJournal(Path.Combine(AppPaths.DataDirectory, "operations.log"));
        Record(OperationKind.Session, "start", "version=" + AppInfo.Version);
    }
    public static void Record(OperationKind kind, string result, string details = "")
    {
        var current = journal;
        if (current != null) current.Record(kind, result, details);
    }
    public static void Stop()
    {
        var current = journal;
        if (current == null) return;
        Record(OperationKind.Session, "stop");
        journal = null;
        current.Flush(1000);
    }
}

internal sealed class OperationJournal
{
    private readonly object sync = new object();
    private readonly Queue<string> pending = new Queue<string>();
    private readonly ManualResetEvent idle = new ManualResetEvent(true);
    private readonly string path;
    private readonly long maximumBytes;
    private readonly Action<string> sink;
    private bool writing;
    private int dropped;
    internal OperationJournal(string path, long maximumBytes = 2 * 1024 * 1024, Action<string> sink = null)
    { this.path = path; this.maximumBytes = maximumBytes; this.sink = sink ?? Append; }

    internal void Record(OperationKind kind, string result, string details)
    {
        string line = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture) + "\t" + kind +
            "\t" + Field(result) + "\t" + Field(details) + Environment.NewLine;
        bool start = false;
        lock (sync)
        {
            if (pending.Count == 512) { dropped++; return; }
            pending.Enqueue(line);
            idle.Reset();
            if (!writing) { writing = true; start = true; }
        }
        if (start) ThreadPool.QueueUserWorkItem(_ => Drain());
    }
    private static string Field(string value)
    {
        value = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return value.Length <= 1024 ? value : value.Substring(0, 1024);
    }
    private void Drain()
    {
        while (true)
        {
            string line;
            lock (sync)
            {
                if (pending.Count == 0) { writing = false; idle.Set(); return; }
                line = pending.Dequeue();
                if (dropped != 0) { line += "Dropped\tqueue-full\tcount=" + dropped + Environment.NewLine; dropped = 0; }
            }
            try { sink(line); }
            catch { /* Logging must never break input or settings application. */ }
        }
    }
    private void Append(string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        long incoming = Encoding.UTF8.GetByteCount(line);
        if (File.Exists(path) && new FileInfo(path).Length + incoming > maximumBytes)
        {
            if (File.Exists(path + ".2")) File.Delete(path + ".2");
            if (File.Exists(path + ".1")) File.Move(path + ".1", path + ".2");
            File.Move(path, path + ".1");
        }
        File.AppendAllText(path, line, new UTF8Encoding(false));
    }
    internal bool Flush(int milliseconds) { return idle.WaitOne(milliseconds); }
}
