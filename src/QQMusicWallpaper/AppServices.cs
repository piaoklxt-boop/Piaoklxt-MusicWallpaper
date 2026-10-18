using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace QQMusicWallpaper;

internal static class AppInfo
{
	public const string Version = "1.0.0";
	public const string RepositoryUrl = "https://github.com/piaoklxt-boop/QQMusicWallpaper";
	public const string ReleasesUrl = RepositoryUrl + "/releases";
	public const string LatestReleaseApi = "https://api.github.com/repos/piaoklxt-boop/QQMusicWallpaper/releases/latest";
	public const string QQMusicDownloadUrl = "https://y.qq.com/download/download.html";
}

internal static class L10n
{
	public static readonly bool IsChinese = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
		.Equals("zh", StringComparison.OrdinalIgnoreCase);

	public static string Text(string chinese, string english)
	{
		return IsChinese ? chinese : english;
	}
}

internal static class AppPaths
{
	public static readonly string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
	public static readonly string DataDirectory = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QQMusicWallpaper");
	public static readonly string ThemeDirectory = Path.Combine(DataDirectory, "theme-sync");
	public static readonly string SettingsPath = Path.Combine(DataDirectory, "settings.ini");
	public static readonly string LogPath = Path.Combine(DataDirectory, "qqmusic-wallpaper.log");
	public static readonly string ThemeLogPath = Path.Combine(DataDirectory, "theme-sync.log");

	public static void Ensure()
	{
		Directory.CreateDirectory(DataDirectory);
		Directory.CreateDirectory(ThemeDirectory);
		MigrateIfNeeded("theme-sync.ini");
		MigrateIfNeeded("theme-original.reg.txt");
		string themeSettings = Path.Combine(ThemeDirectory, "theme-sync.ini");
		if (!File.Exists(themeSettings))
		{
			File.WriteAllText(themeSettings,
				"Enabled=1" + Environment.NewLine +
				"MatchBrightness=0" + Environment.NewLine +
				"ColorizeShell=1" + Environment.NewLine +
				"DarkApps=1" + Environment.NewLine);
		}
	}

	private static void MigrateIfNeeded(string fileName)
	{
		string destination = Path.Combine(ThemeDirectory, fileName);
		string source = Path.Combine(BaseDirectory, "theme-sync", fileName);
		if (!File.Exists(destination) && File.Exists(source))
		{
			File.Copy(source, destination, false);
		}
	}
}

internal sealed class AppSettings
{
	private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public bool Exists { get; private set; }

	public static AppSettings Load()
	{
		var settings = new AppSettings();
		if (!File.Exists(AppPaths.SettingsPath)) return settings;
		settings.Exists = true;
		foreach (string line in File.ReadAllLines(AppPaths.SettingsPath))
		{
			int separator = line.IndexOf('=');
			if (separator <= 0) continue;
			settings.values[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
		}
		return settings;
	}

	public string Get(string key)
	{
		string value;
		return values.TryGetValue(key, out value) ? value : null;
	}

	public bool GetBool(string key, bool fallback)
	{
		string value = Get(key);
		if (value == null) return fallback;
		return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
	}

	public void Set(string key, string value)
	{
		values[key] = value ?? "";
	}

	public void SetBool(string key, bool value)
	{
		Set(key, value ? "1" : "0");
	}

	public void Save()
	{
		var lines = new List<string>();
		foreach (var pair in values) lines.Add(pair.Key + "=" + pair.Value);
		lines.Sort(StringComparer.OrdinalIgnoreCase);
		File.WriteAllLines(AppPaths.SettingsPath, lines.ToArray());
		Exists = true;
	}
}

internal static class StartupManager
{
	private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string ValueName = "QQMusicWallpaper";

	public static bool IsEnabled()
	{
		using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
		{
			return key != null && key.GetValue(ValueName) != null;
		}
	}

	public static void ApplyDefault(AppSettings settings)
	{
		bool enabled = settings.GetBool("AutoStart", true);
		SetEnabled(enabled);
		if (settings.Get("AutoStart") == null)
		{
			settings.SetBool("AutoStart", true);
			settings.Save();
		}
	}

	public static void SetEnabled(bool enabled)
	{
		using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
		{
			if (enabled)
			{
				key.SetValue(ValueName, "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"");
			}
			else
			{
				key.DeleteValue(ValueName, false);
			}
		}
	}
}

internal static class QQMusicLocator
{
	public static string Detect(AppSettings settings)
	{
		string configured = settings.Get("QQMusicPath");
		if (IsQQMusic(configured)) return configured;

		foreach (Process process in Process.GetProcessesByName("QQMusic"))
		{
			try
			{
				string path = process.MainModule.FileName;
				if (IsQQMusic(path)) return Save(settings, path);
			}
			catch { }
			finally { process.Dispose(); }
		}

		foreach (string path in RegistryCandidates())
		{
			if (IsQQMusic(path)) return Save(settings, path);
		}

		string[] candidates =
		{
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tencent", "QQMusic", "QQMusic.exe"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tencent", "QQMusic", "QQMusic.exe"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "QQMusic", "QQMusic.exe"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tencent", "QQMusic", "QQMusic.exe")
		};
		foreach (string path in candidates)
		{
			if (IsQQMusic(path)) return Save(settings, path);
		}
		return null;
	}

	public static bool IsQQMusic(string path)
	{
		return !string.IsNullOrWhiteSpace(path) && File.Exists(path) &&
			Path.GetFileName(path).Equals("QQMusic.exe", StringComparison.OrdinalIgnoreCase);
	}

	public static string Save(AppSettings settings, string path)
	{
		settings.Set("QQMusicPath", Path.GetFullPath(path));
		settings.Save();
		return Path.GetFullPath(path);
	}

	private static IEnumerable<string> RegistryCandidates()
	{
		foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
		{
			foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
			{
				RegistryKey baseKey = null;
				RegistryKey uninstall = null;
				try
				{
					baseKey = RegistryKey.OpenBaseKey(hive, view);
					uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
					if (uninstall == null) continue;
					foreach (string name in uninstall.GetSubKeyNames())
					{
						using (RegistryKey item = uninstall.OpenSubKey(name))
						{
							string displayName = item == null ? null : item.GetValue("DisplayName") as string;
							if (string.IsNullOrEmpty(displayName) ||
								(displayName.IndexOf("QQ音乐", StringComparison.OrdinalIgnoreCase) < 0 &&
								 displayName.IndexOf("QQMusic", StringComparison.OrdinalIgnoreCase) < 0 &&
								 displayName.IndexOf("QQ Music", StringComparison.OrdinalIgnoreCase) < 0)) continue;
							string location = item.GetValue("InstallLocation") as string;
							if (!string.IsNullOrEmpty(location)) yield return Path.Combine(location.Trim('"'), "QQMusic.exe");
							string icon = item.GetValue("DisplayIcon") as string;
							if (!string.IsNullOrEmpty(icon)) yield return icon.Split(',')[0].Trim().Trim('"');
						}
					}
				}
				finally
				{
					if (uninstall != null) uninstall.Dispose();
					if (baseKey != null) baseKey.Dispose();
				}
			}
		}
	}
}

internal sealed class UpdateResult
{
	public string LatestVersion;
	public string Error;
	public bool HasUpdate;
}

internal static class UpdateChecker
{
	public static void CheckAsync(Action<UpdateResult> completed)
	{
		ThreadPool.QueueUserWorkItem(_ =>
		{
			var result = new UpdateResult();
			try
			{
				ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
				using (var client = new WebClient())
				{
					client.Headers[HttpRequestHeader.UserAgent] = "QQMusicWallpaper/" + AppInfo.Version;
					string json = client.DownloadString(AppInfo.LatestReleaseApi);
					Match match = Regex.Match(json, "\\\"tag_name\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
					if (!match.Success) throw new InvalidDataException("GitHub response did not contain tag_name.");
					result.LatestVersion = match.Groups[1].Value.TrimStart('v', 'V');
					Version current;
					Version latest;
					if (Version.TryParse(AppInfo.Version, out current) && Version.TryParse(result.LatestVersion, out latest))
						result.HasUpdate = latest > current;
				}
			}
			catch (Exception ex) { result.Error = ex.Message; }
			completed(result);
		});
	}
}
