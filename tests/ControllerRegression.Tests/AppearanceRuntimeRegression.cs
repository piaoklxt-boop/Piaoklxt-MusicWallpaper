using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using QQMusicWallpaper;

namespace ControllerRegression.Tests;

internal static class AppearanceRuntimeRegression
{
    internal static void Register(Action<string, Action> run)
    {
        run("appearance live apply never restarts the runtime or rewrites the other module", LiveApply);
        run("appearance renderer-only changes do not notify XAML", RendererOnly);
        run("appearance revisions remain distinct across rapid applies and rollback", RapidApply);
        run("appearance desktop loss rolls back without saving or restarting", Rollback);
        run("appearance input mutation cannot bypass the apply transaction", MutableSettings);
        run("appearance async failure reaches the panel callback", AsyncFailure);
        run("renderer health rejects a destroyed surface while its process survives", SurfaceLifetime);
    }

    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    private sealed class Host : IAppearanceRuntimeHost
    {
        public bool IsActive { get; set; }
        public bool HasExternal { get { return false; } }
        internal int Starts, Stops, Checks;
        internal bool Fail;
        public void Start() { Starts++; IsActive = true; }
        public void Stop() { Stops++; IsActive = false; }
        public string CaptureDesktop() { return "owned-desktop"; }
        public void VerifyDesktop(string identity)
        {
            Checks++;
            if (Fail) throw new InvalidOperationException("Desktop host replaced during apply");
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "MusicWallpaper-appearance-" + Guid.NewGuid().ToString("N"));
        internal readonly Host Host = new Host();
        internal readonly ExplorerThemeRuntime Runtime;
        internal readonly string Explorer, Taskbar, Saved;
        internal readonly ExplorerAppearanceSettings Settings = new ExplorerAppearanceSettings();
        internal Fixture()
        {
            string runtime = Path.Combine(Root, "runtime"), data = Path.Combine(Root, "data");
            string seed = Path.Combine(runtime, "Seed/Engine/Mods");
            Directory.CreateDirectory(seed);
            File.WriteAllText(Path.Combine(runtime, "windhawk.exe"), "test boundary");
            foreach (string module in new[] { "windows-11-file-explorer-styler.ini", "windows-11-taskbar-styler.ini" })
                File.WriteAllText(Path.Combine(seed, module), "[Mod]\nLibraryFileName=fixture.dll\nSettingsChangeTime=1\nDisabled=0\n[Settings]\n", Encoding.Unicode);
            Explorer = Path.Combine(data, "Engine/Mods/windows-11-file-explorer-styler.ini");
            Taskbar = Path.Combine(data, "Engine/Mods/windows-11-taskbar-styler.ini");
            Saved = Path.Combine(Root, "preferences.ini");
            Runtime = new ExplorerThemeRuntime(runtime, data, Saved, Host);
            Runtime.ApplyCore(Settings, true);
            Assert(Host.Starts == 1 && Host.Checks == 1, "Cold start skipped the host observation.");
        }
        public void Dispose() { Runtime.Dispose(); Directory.Delete(Root, true); }
    }

    private static void LiveApply()
    {
        using (var f = new Fixture())
        {
            string explorer = File.ReadAllText(f.Explorer);
            var next = f.Settings.Copy(); next.TaskbarAcrylic = true;
            f.Runtime.ApplyCore(next, true);
            Assert(File.ReadAllText(f.Explorer) == explorer, "Taskbar toggle reconfigured Explorer.");
            string taskbar = File.ReadAllText(f.Taskbar);
            next = next.Copy(); next.Transparency++;
            f.Runtime.ApplyCore(next, true);
            Assert(File.ReadAllText(f.Taskbar) == taskbar, "File-area transparency reconfigured taskbar.");
            Assert(f.Host.Starts == 1 && f.Host.Stops == 0, "Live apply unloaded the native runtime.");
        }
    }

    private static void RendererOnly()
    {
        using (var f = new Fixture())
        {
            string explorer = File.ReadAllText(f.Explorer), taskbar = File.ReadAllText(f.Taskbar);
            var next = f.Settings.Copy(); next.ExtendBottom = true; next.EdgeDepth = 55;
            f.Runtime.ApplyCore(next, true);
            Assert(File.ReadAllText(f.Explorer) == explorer && File.ReadAllText(f.Taskbar) == taskbar,
                "Renderer-only settings notified native XAML extensions.");
            Assert(f.Host.Checks == 1 && f.Host.Starts == 1 && f.Host.Stops == 0, "Renderer-only change touched runtime lifecycle.");
            Assert(ExplorerAppearanceSettings.Load(f.Saved).ExtendBottom, "Renderer settings were not saved.");
        }
    }

    private static int Revision(string path) { return AppearanceIni.ReadInt(File.ReadAllText(path), "SettingsChangeTime", 0, 0, int.MaxValue); }
    private static void RapidApply()
    {
        using (var f = new Fixture())
        {
            int before = Revision(f.Explorer);
            for (int i = 1; i <= 5; i++)
            {
                var next = f.Settings.Copy(); next.Transparency += i;
                f.Runtime.ApplyCore(next, true);
                Assert(Revision(f.Explorer) == before + i, "Rapid update lost a SettingsChangeTime revision.");
            }
        }
    }

    private static void Rollback()
    {
        using (var f = new Fixture())
        {
            string saved = File.ReadAllText(f.Saved), taskbar = File.ReadAllText(f.Taskbar);
            int revision = Revision(f.Explorer);
            var next = f.Settings.Copy(); next.Transparency++;
            f.Host.Fail = true;
            bool failed = false;
            try { f.Runtime.ApplyCore(next, true); } catch (InvalidOperationException) { failed = true; }
            Assert(failed && File.ReadAllText(f.Saved) == saved, "A shell crash was reported or saved as success.");
            Assert(File.ReadAllText(f.Taskbar) == taskbar && Revision(f.Explorer) == revision + 2,
                "Rollback touched the other module or reused the failed revision.");
            Assert(File.ReadAllText(f.Explorer).Contains("piaoklxtAcrylicOpacity=179"), "Rollback did not restore original opacity.");
            Assert(f.Host.Stops == 0 && f.Host.Starts == 1, "Rollback unloaded the runtime again.");
            f.Host.Fail = false;
            f.Runtime.ApplyCore(next, true);
            Assert(ExplorerAppearanceSettings.Load(f.Saved).Transparency == next.Transparency, "Retry after rollback was skipped.");
        }
    }

    private static void MutableSettings()
    {
        using (var f = new Fixture())
        {
            f.Settings.TaskbarAcrylic = true;
            f.Runtime.ApplyCore(f.Settings, true);
            Assert(File.ReadAllText(f.Taskbar).Contains("Disabled=0"), "A mutable cached settings object suppressed the update.");
        }
    }

    private static void AsyncFailure()
    {
        using (var f = new Fixture())
        using (var done = new System.Threading.ManualResetEvent(false))
        {
            f.Host.Fail = true;
            var next = f.Settings.Copy(); next.TaskbarAcrylic = true;
            string error = null;
            f.Runtime.ApplyAsync(next, message => { error = message; done.Set(); });
            Assert(done.WaitOne(3000) && !string.IsNullOrEmpty(error), "Panel did not receive native apply failure.");
        }
    }

    private sealed class Surface : NativeWindow, IDisposable
    {
        internal Surface(IntPtr parent)
        {
            IntPtr handle = CreateWindowEx(0, "Win32CaptureSample.SampleWindow", "", 0x40000000, 0, 0, 10, 10, parent, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            Assert(handle != IntPtr.Zero, "Could not create owned surface.");
            AssignHandle(handle);
        }
        public void Dispose() { DestroyHandle(); }
    }

    private static void SurfaceLifetime()
    {
        using (var parent = new Form())
        using (var process = Process.GetCurrentProcess())
        {
            // Register an owned fixture class; never create or manipulate an Explorer window.
            var wc = new WindowClass { Name = "Win32CaptureSample.SampleWindow", Procedure = DefaultProcedure, Instance = GetModuleHandle(null) };
            RegisterClass(ref wc);
            var surface = new Surface(parent.Handle);
            Assert(RendererAppearance.FindSurface((uint)process.Id, parent.Handle) == surface.Handle, "Owned surface not discovered.");
            Assert(RendererAppearance.FindSurface(uint.MaxValue, parent.Handle) == IntPtr.Zero, "Wrong PID accepted.");
            surface.Dispose();
            Assert(!process.HasExited && RendererAppearance.FindSurface((uint)process.Id, parent.Handle) == IntPtr.Zero,
                "Live process hid a destroyed renderer surface.");
            Assert(RendererAppearance.FindSurface((uint)process.Id, IntPtr.Zero) == IntPtr.Zero, "Missing desktop enumerated unrelated windows.");
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string name);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string name, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    private delegate IntPtr WindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    private static readonly WindowProc DefaultProcedure = DefWindowProc;
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct WindowClass
    {
        internal uint Style; internal WindowProc Procedure; internal int ClassExtra, WindowExtra;
        internal IntPtr Instance, Icon, Cursor, Background; internal string Menu, Name;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WindowClass value);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
}
