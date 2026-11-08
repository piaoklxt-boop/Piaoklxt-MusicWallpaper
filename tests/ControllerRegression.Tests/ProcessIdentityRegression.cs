using System;
using System.Diagnostics;
using QQMusicWallpaper;

namespace ControllerRegression.Tests;

internal static class ProcessIdentityRegression
{
    internal static void Register(Action<string, Action> run)
    {
        run("process identity handles an exited process and disposes it", () =>
        {
            var unavailable = new TrackedProcess();
            bool matched = ProcessIdentity.Matches(1, "explorer", id => unavailable);
            if (matched || !unavailable.WasDisposed)
                throw new Exception("An unavailable process must be rejected and disposed.");
        });
        run("process identity treats access failure as unavailable", () =>
        {
            if (ProcessIdentity.Matches(1, "explorer",
                id => { throw new System.ComponentModel.Win32Exception(5); }))
                throw new Exception("Access failure must not match.");
        });
        run("process identity still matches a live process", () =>
        {
            using (var current = Process.GetCurrentProcess())
            {
                if (!ProcessIdentity.Matches((uint)current.Id, current.ProcessName.ToUpperInvariant()) ||
                    ProcessIdentity.Matches((uint)current.Id, "unrelated-name"))
                    throw new Exception("Live-process matching changed.");
            }
        });
    }

    private sealed class TrackedProcess : Process
    {
        public bool WasDisposed;
        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
