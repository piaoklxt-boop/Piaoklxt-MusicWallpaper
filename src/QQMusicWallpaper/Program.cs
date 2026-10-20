using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace QQMusicWallpaper;

internal static class Program
{
	private static Mutex instanceMutex;

	[STAThread]
	private static void Main(string[] args)
	{
		RuntimeLog.Write("Controller entry. Args=" + string.Join(" ", args));
		if (args.Length > 0 && args[0] == "--theme-worker")
		{
			QQMusicWallpaperThemeSync.Program.RunWorker(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "theme-sync"));
			return;
		}
		bool shutdownOnly = Array.IndexOf(args, "--shutdown") >= 0;
		if (shutdownOnly || Array.IndexOf(args, "--takeover") >= 0)
		{
			TakeOverExistingController();
		}
		if (shutdownOnly)
		{
			return;
		}
		instanceMutex = new Mutex(initiallyOwned: true,
			"Local\\PiaoklxtMusicWallpaper.Controller", out var createdNew);
		if (!createdNew)
		{
			return;
		}
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		try
		{
			Application.Run(new WallpaperContext());
		}
		catch (Exception ex)
		{
			RuntimeLog.Write("FATAL controller exception: " + ex);
		}
		finally
		{
			instanceMutex.ReleaseMutex();
			instanceMutex.Dispose();
		}
	}

	private static void TakeOverExistingController()
	{
		int currentId = Process.GetCurrentProcess().Id;
		var controllers = new System.Collections.Generic.List<Process>();
		controllers.AddRange(Process.GetProcessesByName("Piaoklxt-MusicWallpaper"));
		controllers.AddRange(Process.GetProcessesByName("QQMusicWallpaper"));
		foreach (Process process in controllers)
		{
			try
			{
				if (process.Id == currentId)
				{
					continue;
				}
				RuntimeLog.Write("Taking over controller pid=" + process.Id);
				process.Kill();
				process.WaitForExit(5000);
			}
			catch (Exception ex)
			{
				MessageBox.Show("无法关闭旧版音乐壁纸程序。\n\n" + ex.Message,
					"Piaoklxt-MusicWallpaper", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				process.Dispose();
			}
		}
		StopBundledThemeRuntimes();
		// A terminated controller releases its named mutex asynchronously.
		Thread.Sleep(300);
	}

	private static void StopBundledThemeRuntimes()
	{
		foreach (Process process in Process.GetProcessesByName("windhawk"))
		{
			try
			{
				string path = process.MainModule == null ? null :
					process.MainModule.FileName;
				if (string.IsNullOrEmpty(path) ||
					!path.EndsWith("\\ExplorerThemeRuntime\\windhawk.exe",
						StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				using (Process stopper = Process.Start(new ProcessStartInfo(path,
					"-exit -wait")
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					WorkingDirectory = Path.GetDirectoryName(path)
				}))
				{
					stopper.WaitForExit(5000);
				}
			}
			catch { }
			finally { process.Dispose(); }
		}
	}
}
