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

	public void StartAsync()
	{
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				if (!File.Exists(executablePath))
				{
					RuntimeLog.Write("Explorer theme runtime is not packaged; using native Explorer colors.");
					return;
				}
				if (HasExternalRuntime())
				{
					RuntimeLog.Write("An external Windhawk runtime is already active; bundled Explorer theme runtime was not started.");
					return;
				}
				InstallSeedFiles();
				using Process started = Process.Start(new ProcessStartInfo(executablePath,
					"-tray-only")
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					WorkingDirectory = runtimeDirectory
				});
				Thread.Sleep(1800);
				ownsRuntime = IsBundledRuntimeActive();
				RuntimeLog.Write(ownsRuntime
					? "Bundled Explorer theme runtime started."
					: "Bundled Explorer theme runtime did not remain active.");
			}
			catch (Exception ex)
			{
				RuntimeLog.Write("Explorer theme runtime startup failed: " + ex.Message);
			}
		});
	}

	private void InstallSeedFiles()
	{
		string seed = Path.Combine(runtimeDirectory, "Seed");
		string destination = Path.Combine(AppPaths.DataDirectory,
			"ExplorerThemeRuntime");
		if (!Directory.Exists(seed)) return;
		CopyTree(seed, destination);
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
			File.Copy(file, target, true);
		}
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
		if (Interlocked.Exchange(ref disposed, 1) != 0 || !ownsRuntime) return;
		try
		{
			using Process stopper = Process.Start(new ProcessStartInfo(executablePath,
				"-exit -wait")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = runtimeDirectory
			});
			stopper.WaitForExit(5000);
		}
		catch (Exception ex)
		{
			RuntimeLog.Write("Explorer theme runtime shutdown failed: " + ex.Message);
		}
	}
}
