using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace QQMusicWallpaper;

/// <summary>
/// Starts the bundled, portable Explorer XAML styling runtime.  The runtime is
/// deliberately kept out of the controller process: Explorer styling requires
/// code inside explorer.exe, while the wallpaper controller stays MIT licensed
/// and can continue running even if Explorer rejects the optional extension.
/// </summary>
internal sealed class ExplorerThemeRuntime : IDisposable
{
	private readonly string runtimeDirectory;
	private readonly string executablePath;
	private readonly string dataDirectory;
	private readonly string settingsPath;
	private readonly IAppearanceRuntimeHost host;
	private volatile bool ownsRuntime;
	private int disposed;

	public ExplorerThemeRuntime() : this(Path.Combine(AppPaths.BaseDirectory, "ExplorerThemeRuntime"),
        Path.Combine(AppPaths.DataDirectory, "ExplorerThemeRuntime"), ExplorerAppearanceSettings.SettingsPath, null)
	{
	}

    internal ExplorerThemeRuntime(string runtime, string data, string settings, IAppearanceRuntimeHost runtimeHost)
	{
		runtimeDirectory = runtime;
		executablePath = Path.Combine(runtimeDirectory, "windhawk.exe");
        dataDirectory = data;
        settingsPath = settings;
        host = runtimeHost;
	}

    private readonly object runtimeLock = new object();
    private ExplorerAppearanceSettings activeAppearance;

    public void StartAsync()
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            lock (runtimeLock)
            {
                if (disposed != 0) return;
                try { ApplyCore(ExplorerAppearanceSettings.Load(settingsPath), false); }
                catch (Exception ex) { RuntimeLog.Write("Explorer theme runtime startup failed: " + ex.Message); }
            }
        });
    }

    public void ApplyAsync(ExplorerAppearanceSettings appearance, Action<string> completed)
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            string error = null;
            lock (runtimeLock)
            {
                try
                {
                    if (disposed != 0) throw new InvalidOperationException("程序正在退出。");
                    ApplyCore(appearance, true);
                }
                catch (Exception ex)
                {
                    RuntimeLog.Write("Explorer appearance apply failed: " + ex);
                    error = ex is InvalidOperationException ? ex.Message : "资源管理器设置未能完整应用，请重试。";
                }
            }
            if (completed != null) completed(error);
        });
    }

    internal void ApplyCore(ExplorerAppearanceSettings appearance, bool save)
    {
        if (!File.Exists(executablePath)) throw new InvalidOperationException("缺少资源管理器外观组件，请使用完整安装包。");
        if (HasExternalRuntime()) throw new InvalidOperationException("另一个 Windhawk 正在运行，请先退出它再应用资源管理器设置。");
        if (appearance.SameValues(activeAppearance) && IsBundledRuntimeActive())
        {
            if (save) appearance.Save(settingsPath);
            return;
        }
        string active = Path.Combine(dataDirectory, "Engine/Mods/windows-11-file-explorer-styler.ini");
        string previous = File.Exists(active) ? File.ReadAllText(active) : null;
        string taskbarActive = Path.Combine(Path.GetDirectoryName(active), "windows-11-taskbar-styler.ini");
        string previousTaskbar = File.Exists(taskbarActive) ? File.ReadAllText(taskbarActive) : null;
        bool wasRunning = IsBundledRuntimeActive();
        string desktop = host != null ? host.CaptureDesktop() : CaptureDesktop();
        bool explorerChanged = false, taskbarChanged = false;
        try
        {
            // Seed binaries only when stopped. Live configuration must never
            // unload every extension or briefly overwrite preferences with defaults.
            if (!wasRunning) CopyTree(Path.Combine(runtimeDirectory, "Seed"), dataDirectory);
            explorerChanged = WriteModuleConfiguration(active, appearance, false);
            taskbarChanged = WriteModuleConfiguration(taskbarActive, appearance, true);
            if (!wasRunning) StartRuntime();
            if (!wasRunning || explorerChanged || taskbarChanged)
            {
                RuntimeLog.Write("Appearance apply: mode=" + (wasRunning ? "live" : "start") +
                    "; explorerChanged=" + explorerChanged + "; taskbarChanged=" + taskbarChanged);
                if (host != null) host.VerifyDesktop(desktop);
                else VerifyDesktop(desktop);
                if (!IsBundledRuntimeActive()) throw new InvalidOperationException("外观组件已退出，设置未保存。");
            }
            if (save) appearance.Save(settingsPath);
            activeAppearance = appearance.Copy();
            RuntimeLog.Write("Appearance apply verified; desktop host remained available.");
        }
        catch
        {
            try
            {
                if (explorerChanged || !wasRunning) RestoreModule(active, previous);
                if (taskbarChanged || !wasRunning) RestoreModule(taskbarActive, previousTaskbar);
                RuntimeLog.Write("Appearance apply rolled back; preferences not saved.");
            }
            catch (Exception ex) { RuntimeLog.Write("Explorer appearance rollback failed: " + ex.Message); }
            throw;
        }
    }

    private void StartRuntime()
    {
        if (host != null) { host.Start(); ownsRuntime = host.IsActive; return; }
        using (Process started = Process.Start(new ProcessStartInfo(executablePath, "-tray-only")
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = runtimeDirectory })) { }
        Thread.Sleep(1200);
        ownsRuntime = IsBundledRuntimeActive();
        if (!ownsRuntime) throw new InvalidOperationException("资源管理器外观组件没有启动成功，设置未保存。");
        RuntimeLog.Write("Bundled appearance runtime started; awaiting desktop health check.");
    }

    private void StopRuntime()
    {
        if (host != null) { host.Stop(); ownsRuntime = false; return; }
        using (Process stopper = Process.Start(new ProcessStartInfo(executablePath, "-exit -wait")
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = runtimeDirectory }))
        {
            if (!stopper.WaitForExit(10000)) throw new InvalidOperationException("外观组件仍在退出，请稍后重试。");
        }
        ownsRuntime = false;
    }

    private bool WriteModuleConfiguration(string path, ExplorerAppearanceSettings appearance, bool taskbar)
    {
        string seed = Path.Combine(runtimeDirectory, "Seed/Engine/Mods/" + Path.GetFileName(path));
        if (!File.Exists(seed))
        {
            if (taskbar && !appearance.TaskbarAcrylic) return false;
            throw new InvalidOperationException("缺少外观模块配置，请使用完整安装包。");
        }
        string old = File.Exists(path) ? File.ReadAllText(path) : null;
        string next = taskbar ? appearance.CreateTaskbarConfiguration(File.ReadAllText(seed))
            : appearance.CreateRuntimeConfiguration(File.ReadAllText(seed));
        if (old != null && NormalizeConfiguration(old) == NormalizeConfiguration(next)) return false;
        next = AppearanceIni.Set(next, "SettingsChangeTime", NextRevision(old).ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppearanceIni.WriteAtomic(path, next, System.Text.Encoding.Unicode);
        return true;
    }

    private static string NormalizeConfiguration(string text)
    {
        return AppearanceIni.Set(text, "SettingsChangeTime", "0").Replace("\r\n", "\n").Trim();
    }

    private static int NextRevision(string old)
    {
        int previous = AppearanceIni.ReadInt(old ?? "", "SettingsChangeTime", 0, 0, int.MaxValue);
        return previous == int.MaxValue ? 1 : previous + 1;
    }

    private static void RestoreModule(string path, string previous)
    {
        if (previous == null) { if (File.Exists(path)) File.Delete(path); return; }
        string current = File.Exists(path) ? File.ReadAllText(path) : null;
        AppearanceIni.WriteAtomic(path, AppearanceIni.Set(previous, "SettingsChangeTime",
            NextRevision(current).ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Text.Encoding.Unicode);
    }

    private static string CaptureDesktop()
    {
        IntPtr desktop = Native.FindWindow("Progman", null);
        uint pid;
        if (desktop == IntPtr.Zero || Native.GetWindowThreadProcessId(desktop, out pid) == 0)
            throw new InvalidOperationException("桌面宿主暂不可用，设置未保存，请等待桌面恢复后重试。");
        using (Process process = Process.GetProcessById((int)pid))
            return desktop.ToInt64() + ":" + pid + ":" + process.StartTime.ToUniversalTime().Ticks;
    }

    private static void VerifyDesktop(string identity)
    {
        // Asynchronous XAML updates can fail after the INI write returns.
        // This is a bounded health observation, not an acknowledgement from XAML.
        for (int i = 0; i < 60; i++)
        {
            Thread.Sleep(100);
            if (CaptureDesktop() != identity)
                throw new InvalidOperationException("检测到资源管理器重启，已撤回本次外观设置。请等待桌面恢复后重试。");
        }
    }

    internal static void InstallSeedFiles(string runtimeDirectory, string destination, ExplorerAppearanceSettings appearance)
    {
        string seed = Path.Combine(runtimeDirectory, "Seed");
        if (!Directory.Exists(seed)) throw new InvalidOperationException("缺少资源管理器外观配置。");
        CopyTree(seed, destination);
        string relative = "Engine/Mods/windows-11-file-explorer-styler.ini";
        string seedText = File.ReadAllText(Path.Combine(seed, relative));
        AppearanceIni.WriteAtomic(Path.Combine(destination, relative), appearance.CreateRuntimeConfiguration(seedText), System.Text.Encoding.Unicode);
        string taskbarRelative = "Engine/Mods/windows-11-taskbar-styler.ini";
        string taskbarSeed = Path.Combine(seed, taskbarRelative);
        if (File.Exists(taskbarSeed))
            AppearanceIni.WriteAtomic(Path.Combine(destination, taskbarRelative),
                appearance.CreateTaskbarConfiguration(File.ReadAllText(taskbarSeed)), System.Text.Encoding.Unicode);
        else if (appearance.TaskbarAcrylic) throw new InvalidOperationException("缺少任务栏外观组件，请使用完整安装包。");
    }

	private static void CopyTree(string source, string destination)
	{
		Directory.CreateDirectory(destination);
		foreach (string directory in Directory.GetDirectories(source, "*",
			SearchOption.AllDirectories))
		{
			Directory.CreateDirectory(Path.Combine(destination,
				directory.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar)));
		}
		foreach (string file in Directory.GetFiles(source, "*",
			SearchOption.AllDirectories))
		{
			string relative = file.Substring(source.Length)
				.TrimStart(Path.DirectorySeparatorChar);
			string target = Path.Combine(destination, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(target));
            // A previous process can still be unloading an unchanged DLL.
            // Only replace a file when its contents actually differ.
            if (File.Exists(target) && SameContents(file, target)) continue;
			File.Copy(file, target, true);
		}
	}

    private static bool SameContents(string first, string second)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length) return false;
        using (var hash = System.Security.Cryptography.SHA256.Create())
        using (var a = File.OpenRead(first))
        using (var b = File.OpenRead(second))
            return Convert.ToBase64String(hash.ComputeHash(a)) == Convert.ToBase64String(hash.ComputeHash(b));
    }

	private bool HasExternalRuntime()
	{
        if (host != null) return host.HasExternal;
		foreach (Process process in Process.GetProcessesByName("windhawk"))
		{
			try
			{
				string path = process.MainModule.FileName;
				if (!string.Equals(path, executablePath,
					StringComparison.OrdinalIgnoreCase)) return true;
			}
			catch
			{
				// A protected Windhawk service process means an installed instance is
				// active.  Starting a second daemon would only hit its global mutex.
				return true;
			}
			finally { process.Dispose(); }
		}
		return false;
	}

	private bool IsBundledRuntimeActive()
	{
        if (host != null) return host.IsActive;
		foreach (Process process in Process.GetProcessesByName("windhawk"))
		{
			try
			{
				if (string.Equals(process.MainModule.FileName, executablePath,
					StringComparison.OrdinalIgnoreCase)) return true;
			}
			catch { }
			finally { process.Dispose(); }
		}
		return false;
	}

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lock (runtimeLock)
        {
            if (!ownsRuntime || HasExternalRuntime()) return;
            try { StopRuntime(); }
            catch (Exception ex) { RuntimeLog.Write("Explorer theme runtime shutdown failed: " + ex.Message); }
        }
    }
}

// Production apply transaction with an injectable process/desktop boundary.
// Tests still write real INIs through the same live-update and rollback path.
internal interface IAppearanceRuntimeHost
{
    bool IsActive { get; }
    bool HasExternal { get; }
    void Start();
    void Stop();
    string CaptureDesktop();
    void VerifyDesktop(string identity);
}
