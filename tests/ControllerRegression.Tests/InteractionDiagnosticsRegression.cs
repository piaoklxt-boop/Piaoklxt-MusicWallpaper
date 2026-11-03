using System;
using System.Linq;
using System.Threading;
using QQMusicWallpaper;

namespace ControllerRegression.Tests;

internal static class InteractionDiagnosticsRegression
{
    internal static void Register(Action<string, Action> run)
    {
        run("diagnostics retain only the newest 32 interactions", BoundedExport);
        run("a single failure and cancellation remain silent", QuietCancellation);
        run("three consecutive failures notify once with a cooldown", ConsecutiveFailures);
        run("success breaks the failure streak and suppresses an obsolete notice", SuccessResets);
        run("late results cannot evict recent interactions or trigger a stale warning", LateResult);
        run("diagnostic recording accepts no private text fields", AllowlistedSchema);
        run("diagnostic export remains bounded under concurrent completion", ConcurrentExport);
        run("production routing records diagnostics by default and ignores unowned blank clicks", DefaultControllerRecording);
        run("semantic promotion preserves one interaction and its original snapshot age", SemanticPromotion);
    }
    private static void DefaultControllerRecording()
    {
        var controller = (WallpaperContext)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(WallpaperContext));
        GC.SuppressFinalize(controller);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var traceMethod = typeof(WallpaperContext).GetMethod("TraceDesktopClick", flags);
        traceMethod.Invoke(controller, new object[] { DesktopClickRoute.None, DesktopSpecialAction.None });
        var trace = (InteractionDiagnostics)typeof(WallpaperContext).GetProperty("Diagnostics", flags).GetValue(controller, null);
        Assert(!trace.Export().Split('\n').Any(row => row.Length > 0 && char.IsDigit(row[0])), "Blank desktop click was counted as a routed action.");
        traceMethod.Invoke(controller, new object[] { DesktopClickRoute.SpecialControl, DesktopSpecialAction.ClickQuality });
        Assert(trace.Export().Contains("ClickQuality"), "Recording incorrectly requires the verbose diagnostics flag.");
    }
    private static void SemanticPromotion()
    {
        var trace = new InteractionDiagnostics(() => 10);
        trace.Begin(1, DesktopClickRoute.GenericControl, DesktopSpecialAction.None, 17);
        trace.Begin(1, DesktopClickRoute.SpecialControl, DesktopSpecialAction.ShowNativeComments, -1);
        string[] rows = trace.Export().Split('\n').Where(row => row.Length > 0 && char.IsDigit(row[0])).ToArray();
        Assert(rows.Length == 1 && rows[0].Contains("ShowNativeComments") && rows[0].Contains("\t17\t"), "Promotion duplicated or replaced the original interaction.");
    }
    private static void Add(InteractionDiagnostics trace, long sequence, InteractionOutcome outcome)
    {
        trace.Begin(sequence, DesktopClickRoute.SpecialControl, DesktopSpecialAction.ClickQuality, 25);
        trace.Complete(sequence, sequence + 100, outcome);
    }
    private static void BoundedExport()
    {
        long now = 100;
        var trace = new InteractionDiagnostics(() => now);
        for (int i = 1; i <= 40; i++) { now += 15; Add(trace, i, InteractionOutcome.Presented); }
        string[] rows = trace.Export().Split('\n').Where(row => row.Length > 0 && char.IsDigit(row[0])).ToArray();
        Assert(rows.Length == 32 && rows[0].StartsWith("9\t") && rows[31].StartsWith("40\t"), "Ring bounds/order incorrect.");
    }
    private static void QuietCancellation()
    {
        var trace = new InteractionDiagnostics(() => 100);
        Add(trace, 1, InteractionOutcome.PopupTimeout);
        Add(trace, 2, InteractionOutcome.Cancelled);
        Add(trace, 3, InteractionOutcome.SourceChanged);
        Assert(!trace.TakeNotice(out var ignored), "An isolated failure/cancellation must not alert.");
    }
    private static void ConsecutiveFailures()
    {
        long now = 100;
        var trace = new InteractionDiagnostics(() => now);
        for (int i = 1; i <= 3; i++) Add(trace, i, InteractionOutcome.AmbiguousTarget);
        Assert(trace.TakeNotice(out var reason) && reason == InteractionOutcome.AmbiguousTarget, "Missing actionable notice.");
        Assert(!trace.TakeNotice(out reason), "Notice must be consumed once.");
        Add(trace, 4, InteractionOutcome.PopupTimeout);
        Assert(!trace.TakeNotice(out reason), "Cooldown did not suppress repeated notification.");
        now += 60001;
        Add(trace, 5, InteractionOutcome.PopupTimeout);
        Assert(trace.TakeNotice(out reason), "Failure after cooldown should remain actionable.");
    }
    private static void SuccessResets()
    {
        var trace = new InteractionDiagnostics(() => 100);
        for (int i = 1; i <= 3; i++) Add(trace, i, InteractionOutcome.DispatchFailed);
        Add(trace, 4, InteractionOutcome.Presented);
        Assert(!trace.TakeNotice(out var ignored), "Recovered interaction retained an obsolete notice.");
        Add(trace, 5, InteractionOutcome.DispatchFailed);
        Assert(!trace.TakeNotice(out ignored), "A new isolated failure must be silent.");
    }
    private static void LateResult()
    {
        var trace = new InteractionDiagnostics(() => 100);
        for (int i = 1; i <= 40; i++) Add(trace, i, InteractionOutcome.Presented);
        string before = trace.Export();
        trace.Complete(1, 1, InteractionOutcome.PopupTimeout);
        Assert(before == trace.Export() && !trace.TakeNotice(out var ignored), "Late result replaced recent evidence.");
    }
    private static void AllowlistedSchema()
    {
        foreach (var method in typeof(InteractionDiagnostics).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            Assert(!method.GetParameters().Any(p => p.ParameterType == typeof(string)), "Raw text could enter diagnostic storage.");
        var trace = new InteractionDiagnostics();
        Add(trace, 1, InteractionOutcome.TargetMissing);
        string export = trace.Export();
        Assert(!export.Contains(Environment.UserName) && !export.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), "Private machine identity entered export.");
    }
    private static void ConcurrentExport()
    {
        var trace = new InteractionDiagnostics();
        var threads = new Thread[4];
        for (int i = 0; i < threads.Length; i++)
        {
            int worker = i;
            threads[i] = new Thread(() => { for (int n = 0; n < 100; n++) { Add(trace, worker * 1000 + n, InteractionOutcome.Presented); trace.Export(); } });
            threads[i].Start();
        }
        foreach (var thread in threads) Assert(thread.Join(3000), "Diagnostic recording stalled.");
        Assert(trace.Export().Split('\n').Count(row => row.Length > 0 && char.IsDigit(row[0])) == 32, "Concurrent recording escaped capacity.");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
