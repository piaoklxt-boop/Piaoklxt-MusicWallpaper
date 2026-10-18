using System;
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
		if (args.Length > 0 && args[0] == "--theme-worker")
		{
			QQMusicWallpaperThemeSync.Program.RunWorker(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "theme-sync"));
			return;
		}
		instanceMutex = new Mutex(initiallyOwned: true, "Local\\QQMusicWallpaper.Controller", out var createdNew);
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
		finally
		{
			instanceMutex.ReleaseMutex();
			instanceMutex.Dispose();
		}
	}
}
