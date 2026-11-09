using System;
using System.IO;
using System.Threading;
using QQMusicWallpaper;

namespace ControllerRegression.Tests;

internal static class OperationLogRegression
{
    internal static void Register(Action<string, Action> run)
    {
        run("operation log writes asynchronously and bounds its pending queue", NonBlockingQueue);
        run("operation log rotates and preserves structured action outcomes", Rotation);
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void NonBlockingQueue()
    {
        using (var entered = new ManualResetEvent(false))
        using (var release = new ManualResetEvent(false))
        {
            int writes = 0;
            var journal = new OperationJournal("unused", 4096, line => { entered.Set(); release.WaitOne(); Interlocked.Increment(ref writes); });
            journal.Record(OperationKind.ControlPanel, "requested", "");
            Assert(entered.WaitOne(2000), "Writer never started.");
            var producer = new Thread(() => { for (int i = 0; i < 1200; i++) journal.Record(OperationKind.DesktopInteraction, "Delivered", "click=" + i); });
            producer.IsBackground = true;
            try { producer.Start(); Assert(producer.Join(2000), "Record waited for blocked disk output."); }
            finally { release.Set(); producer.Join(2000); journal.Flush(3000); }
            Assert(writes <= 513, "Pending operation queue was not bounded.");
        }
    }
    private static void Rotation()
    {
        string root = Path.Combine(Path.GetTempPath(), "MusicWallpaper-operation-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "operations.log");
        try
        {
            var journal = new OperationJournal(path, 400);
            for (int i = 0; i < 24; i++) journal.Record(OperationKind.SettingsApply, "completed", "elapsedMs=" + i);
            Assert(journal.Flush(5000), "Operation log did not flush.");
            Assert(Directory.GetFiles(root).Length == 3, "Rotation did not keep exactly current and two previous files.");
            foreach (string file in Directory.GetFiles(root)) Assert(new FileInfo(file).Length <= 400, "Rotation exceeded its byte budget.");
            string text = File.ReadAllText(path);
            Assert(text.Contains("SettingsApply\tcompleted\telapsedMs=23"), "Latest operation result was lost.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
