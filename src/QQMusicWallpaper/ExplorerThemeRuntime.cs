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
	private volatile bool ownsRuntime;
	private int disposed;

	public ExplorerThemeRuntime()
	{
		runtimeDirectory = Path.Combine(AppPaths.BaseDirectory,
			"ExplorerThemeRuntime");
		executablePath = Path.Combine(runtimeDirectory, "windhawk.exe");
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
                try { ApplyCore(ExplorerAppearanceSettings.Load(ExplorerAppearanceSettings.SettingsPath), false); }
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

    private void ApplyCore(ExplorerAppearanceSettings appearance, bool save)
    {
        if (!File.Exists(executablePath)) throw new InvalidOperationException("缺少资源管理器外观组件，请使用完整安装包。");
        if (HasExternalRuntime()) throw new InvalidOperationException("另一个 Windhawk 正在运行，请先退出它再应用资源管理器设置。");
        if (appearance.SameValues(activeAppearance) && IsBundledRuntimeActive())
        {
            if (save) appearance.Save(ExplorerAppearanceSettings.SettingsPath);
            return;
        }
        string active = Path.Combine(AppPaths.DataDirectory, "ExplorerThemeRuntime/Engine/Mods/windows-11-file-explorer-styler.ini");
        string previous = File.Exists(active) ? File.ReadAllText(active) : null;
        bool wasRunning = IsBundledRuntimeActive();
        if (wasRunning) StopRuntime();
        try
        {
            InstallSeedFiles(appearance);
            StartRuntime();
            if (save) appearance.Save(ExplorerAppearanceSettings.SettingsPath);
            activeAppearance = appearance;
        }
        catch
        {
            try
            {
                if (IsBundledRuntimeActive()) StopRuntime();
                if (previous != null) AppearanceIni.WriteAtomic(active, previous, System.Text.Encoding.Unicode);
                if (wasRunning) StartRuntime();
            }
            catch (Exception ex) { RuntimeLog.Write("Explorer appearance rollback failed: " + ex.Message); }
            throw;
        }
    }

    private void StartRuntime()
    {
        using (Process started = Process.Start(new ProcessStartInfo(executablePath, "-tray-only")
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = runtimeDirectory })) { }
        Thread.Sleep(1200);
        ownsRuntime = IsBundledRuntimeActive();
        if (!ownsRuntime) throw new InvalidOperationException("资源管理器外观组件没有启动成功，设置未保存。");
        RuntimeLog.Write("Bundled Explorer appearance settings applied.");
    }

    private void StopRuntime()
    {
        using (Process stopper = Process.Start(new ProcessStartInfo(executablePath, "-exit -wait")
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = runtimeDirectory }))
        {
            if (!stopper.WaitForExit(10000)) throw new InvalidOperationException("外观组件仍在退出，请稍后重试。");
        }
        ownsRuntime = false;
    }

    private void InstallSeedFiles(ExplorerAppearanceSettings appearance)
    {
        string destination = Path.Combine(AppPaths.DataDirectory, "ExplorerThemeRuntime");
        InstallSeedFiles(runtimeDirectory, destination, appearance);
    }

    internal static void InstallSeedFiles(string runtimeDirectory, string destination, ExplorerAppearanceSettings appearance)
    {
        string seed = Path.Combine(runtimeDirectory, "Seed");
        if (!Directory.Exists(seed)) throw new InvalidOperationException("缺少资源管理器外观配置。");
        CopyTree(seed, destination);
        string relative = "Engine/Mods/windows-11-file-explorer-styler.ini";
        string seedText = File.ReadAllText(Path.Combine(seed, relative));
        AppearanceIni.WriteAtomic(Path.Combine(destination, relative), appearance.CreateRuntimeConfiguration(seedText), System.Text.Encoding.Unicode);
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
