using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QQMusicWallpaper;

internal sealed class WallpaperContext : ApplicationContext
{
	private sealed class HiddenWindow : Form
	{
		private readonly Action onHotkey;

		public HiddenWindow(Action onHotkeyAction)
		{
			onHotkey = onHotkeyAction;
			ShowInTaskbar = false;
			FormBorderStyle = FormBorderStyle.None;
			Opacity = 0.0;
			Width = 1;
			Height = 1;
			StartPosition = FormStartPosition.Manual;
			Location = new Point(-32000, -32000);
			CreateControl();
		}

		protected override void WndProc(ref Message message)
		{
			if (message.Msg == 786 && message.WParam.ToInt32() == 20813)
			{
				onHotkey();
			}
			base.WndProc(ref message);
		}
	}

	private const int HotkeyId = 20813;

	private const uint ModAlt = 1u;

	private const uint ModControl = 2u;

	private const uint VkU = 85u;

	private const int SwRestore = 9;

	private const int SwShowNoActivate = 4;

	private const int SwMaximize = 3;

	private const int GwlStyle = -16;

	private const int GwlExStyle = -20;

	private const long WsChild = 1073741824L;

	private const long WsVisible = 268435456L;

	private const long WsCaption = 12582912L;

	private const long WsThickFrame = 262144L;

	private const long WsSysMenu = 524288L;

	private const long WsMinimizeBox = 131072L;

	private const long WsMaximizeBox = 65536L;

	private const long WsExAppWindow = 262144L;

	private const long WsExToolWindow = 128L;

	private const uint SwpFrameChanged = 32u;

	private const uint SwpShowWindow = 64u;

	private const uint WmLButtonDown = 513u;

	private const uint WmLButtonUp = 514u;

	private const uint WmLButtonDblClk = 515u;

	private const uint WmMouseMove = 512u;

	private const uint WmMouseWheel = 522u;

	private const int WhMouseLl = 14;

	private const uint LvmFirst = 4096u;

	private const uint LvmHitTest = 4114u;

	private const uint ProcessVmOperation = 8u;

	private const uint ProcessVmRead = 16u;

	private const uint ProcessVmWrite = 32u;

	private const uint ProcessQueryInformation = 1024u;

	private const uint MemCommit = 4096u;

	private const uint MemReserve = 8192u;

	private const uint MemRelease = 32768u;

	private const uint PageReadWrite = 4u;

	private static readonly UIntPtr InputMarker = new UIntPtr(1364023120u);

	private readonly HiddenWindow window;

	private readonly NotifyIcon tray;
	private QQMusicWallpaperThemeSync.ThemeContext themeContext;
	private Thread themeThread;
	private Thread mouseHookThread;

	private readonly object mouseHookSync = new object();

	private uint mouseHookThreadId;

	private int mouseHookGeneration;

	private long lastMouseHookEventTicks;

	private Native.POINT lastHookWatchdogPoint;

	private bool hookWatchdogPointCaptured;

	// Rolling 10-minute counter of hook reinstalls. A nonzero rate means the hook
	// callback is being starved past LowLevelHooksTimeout; surfacing it in the log
	// turns a silent failure ("wallpaper just stops responding") into something
	// diagnosable.
	private int hookReinstallCount;

	private DateTime hookReinstallWindowStart = DateTime.MinValue;

	private readonly System.Windows.Forms.Timer timer;

	private readonly AppSettings settings;

	private string qqPath;

	private readonly string rendererPath;

	private readonly string logPath;

	private readonly Native.LowLevelMouseProc mouseProc;

	private IntPtr mouseHook;

	private IntPtr qqWindow;

	// Cache for IsQQMusicWindow(): the process id of qqWindow only changes when the
	// handle itself changes, so re-querying it on every mouse event is wasted work.
	private IntPtr qqWindowProcessIdSource;

	private uint qqWindowProcessId;

	// Cache for the Progman (desktop) window handle. FindWindow walks the top-level
	// window list, and the hover path needs this up to 40 times per second.
	private IntPtr progmanWindow;

	private Process rendererProcess;

	private IntPtr rendererJob;

	private IntPtr originalParent;

	private IntPtr workerWindow;

	private long originalStyle;

	private long originalExStyle;

	private Native.RECT originalRect;

	private Native.WINDOWPLACEMENT originalPlacement;

	private volatile bool enabled = true;

	private volatile bool embedded;

	private volatile bool suspended;

	private DateTime nextLaunchAttempt = DateTime.MinValue;

	private int preparedProcessId = -1;

	private bool sourceStateCaptured;

	private bool interactionActive;

	private IntPtr interactionForeground;

	private System.Threading.Timer interactionTimer;

	private DateTime interactionStarted = DateTime.MinValue;

	private DateTime taskbarClickUntil = DateTime.MinValue;

	private DateTime lastHoverForwarded = DateTime.MinValue;

	private bool sourcePresented;

	private bool keepSourcePresentedAfterClick;

	private bool exiting;

	private bool missingQQPromptShown;

	private bool checkingForUpdates;

	public WallpaperContext()
	{
		AppPaths.Ensure();
		settings = AppSettings.Load();
		qqPath = QQMusicLocator.Detect(settings);
		rendererPath = Path.Combine(AppPaths.BaseDirectory, "QQMusicWallpaperRenderer.exe");
		logPath = AppPaths.LogPath;
		StartupManager.ApplyDefault(settings);
		CleanupOrphanedRenderers();
		rendererJob = CreateRendererJob();
		window = new HiddenWindow(OnHotkey);
		if (!Native.RegisterHotKey(window.Handle, 20813, 3u, 85u))
		{
			Log("Could not register Ctrl+Alt+U; Win32=" + Marshal.GetLastWin32Error());
		}
		ContextMenu contextMenu = new ContextMenu();
		contextMenu.MenuItems.Add(L10n.Text("切换动态壁纸 (Ctrl+Alt+U)", "Toggle wallpaper (Ctrl+Alt+U)"), (s, e) =>
		{
			Log("Tray menu toggle.");
			Toggle();
		});
		contextMenu.MenuItems.Add(L10n.Text("恢复 QQ 音乐窗口", "Restore QQ Music window"), (s, e) =>
		{
			Log("Tray menu restore window.");
			DisableWallpaper(showQQ: true);
		});

		MenuItem qqMenu = new MenuItem(L10n.Text("QQ 音乐", "QQ Music"));
		qqMenu.MenuItems.Add(L10n.Text("重新检测", "Detect again"), (s, e) => RedetectQQMusic(true));
		qqMenu.MenuItems.Add(L10n.Text("手动选择 QQMusic.exe", "Choose QQMusic.exe"), (s, e) => SelectQQMusic());
		qqMenu.MenuItems.Add(L10n.Text("打开 QQ 音乐官网", "Open QQ Music website"), (s, e) => OpenUrl(AppInfo.QQMusicDownloadUrl));
		contextMenu.MenuItems.Add(qqMenu);

		MenuItem startupItem = null;
		startupItem = new MenuItem(L10n.Text("开机自动启动", "Start with Windows"), (s, e) =>
		{
			bool enabled = !startupItem.Checked;
			StartupManager.SetEnabled(enabled);
			settings.SetBool("AutoStart", enabled);
			settings.Save();
			startupItem.Checked = enabled;
		}) { Checked = StartupManager.IsEnabled() };
		contextMenu.MenuItems.Add(startupItem);
		contextMenu.MenuItems.Add(L10n.Text("检查更新", "Check for updates"), (s, e) => CheckForUpdates(true));

		string themeSettingsPath = Path.Combine(AppPaths.ThemeDirectory, "theme-sync.ini");
		string themeSettings = File.Exists(themeSettingsPath) ? File.ReadAllText(themeSettingsPath) : "";
		bool themeEnabled = themeSettings.Contains("Enabled=1");
		MenuItem themeMenu = new MenuItem(L10n.Text("系统主题色", "System theme colors"));
		MenuItem accentItem = null;
		accentItem = new MenuItem(L10n.Text("随 QQ 音乐桌面同步", "Follow the current QQ Music artwork"), (s, e) =>
		{
			if (themeContext == null) return;
			themeContext.RequestToggleAccent();
			accentItem.Checked = !accentItem.Checked;
			foreach (MenuItem item in themeMenu.MenuItems)
				if (item != accentItem) item.Enabled = accentItem.Checked;
		}) { Checked = themeEnabled };
		themeMenu.MenuItems.Add(accentItem);
		MenuItem brightnessItem = null;
		brightnessItem = new MenuItem(L10n.Text("同时切换系统明暗", "Also switch light/dark mode"), (s, e) =>
		{
			if (themeContext == null) return;
			themeContext.RequestToggleBrightness();
			brightnessItem.Checked = !brightnessItem.Checked;
		}) { Checked = themeSettings.Contains("MatchBrightness=1"), Enabled = themeEnabled };
		themeMenu.MenuItems.Add(brightnessItem);
		MenuItem shellItem = null;
		shellItem = new MenuItem(L10n.Text("任务栏与开始菜单跟随颜色", "Color the taskbar and Start menu"), (s, e) =>
		{
			if (themeContext == null) return;
			themeContext.RequestToggleShell();
			shellItem.Checked = !shellItem.Checked;
		}) { Checked = themeSettings.Contains("ColorizeShell=1"), Enabled = themeEnabled };
		themeMenu.MenuItems.Add(shellItem);
		MenuItem appsItem = null;
		appsItem = new MenuItem(L10n.Text("文件资源管理器等使用深色", "Use dark mode for apps and Explorer"), (s, e) =>
		{
			if (themeContext == null) return;
			themeContext.RequestToggleApps();
			appsItem.Checked = !appsItem.Checked;
		}) { Checked = themeSettings.Contains("DarkApps=1"), Enabled = themeEnabled };
		themeMenu.MenuItems.Add(appsItem);
		themeMenu.MenuItems.Add(new MenuItem(L10n.Text("立即取色", "Refresh color now"), (s, e) => { if (themeContext != null) themeContext.RequestUpdateColor(); }) { Enabled = themeEnabled });
		contextMenu.MenuItems.Add("-");
		contextMenu.MenuItems.Add(themeMenu);
		contextMenu.MenuItems.Add(L10n.Text("退出", "Exit"), (s, e) => Exit());
		tray = new NotifyIcon();
		string trayIconPath = Path.Combine(AppPaths.BaseDirectory, "QQMusicWallpaper.ico");
		tray.Icon = File.Exists(trayIconPath) ? new Icon(trayIconPath) : Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		tray.Text = L10n.Text("QQ 音乐动态壁纸", "QQ Music Wallpaper");
		tray.ContextMenu = contextMenu;
		tray.Visible = true;
		tray.DoubleClick += (object param0, EventArgs param1) =>
		{
			Log("Tray double click.");
			Toggle();
		};
		mouseProc = MouseHookCallback;
		StartMouseHook();
		SystemEvents.SessionSwitch += OnSessionSwitch;
		SystemEvents.PowerModeChanged += OnPowerModeChanged;
		timer = new System.Windows.Forms.Timer();
		timer.Interval = 750;
		timer.Tick += (object param0, EventArgs param1) =>
		{
			MaintainWallpaper();
		};
		timer.Start();
		themeThread = new Thread(() =>
		{
			try
			{
				themeContext = new QQMusicWallpaperThemeSync.ThemeContext(
					AppPaths.ThemeDirectory, false);
				Application.Run(themeContext);
			}
			catch (Exception ex) { Log("Theme thread failed: " + ex); }
		});
		themeThread.IsBackground = true;
		themeThread.SetApartmentState(ApartmentState.STA);
		themeThread.Start();
		Log("Started. Shortcut=Ctrl+Alt+U");
		ThreadPool.QueueUserWorkItem(_ =>
		{
			Thread.Sleep(3000);
			CheckForUpdates(false);
		});
	}

	private void RedetectQQMusic(bool notify)
	{
		qqPath = QQMusicLocator.Detect(settings);
		missingQQPromptShown = false;
		if (!notify) return;
		MessageBox.Show(
			QQMusicLocator.IsQQMusic(qqPath)
				? L10n.Text("已找到：\n" + qqPath, "Found:\n" + qqPath)
				: L10n.Text("仍未找到 QQ 音乐。你可以使用托盘菜单手动选择 QQMusic.exe。", "QQ Music was not found. Use the tray menu to choose QQMusic.exe manually."),
			tray.Text, MessageBoxButtons.OK,
			QQMusicLocator.IsQQMusic(qqPath) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
	}

	private bool SelectQQMusic()
	{
		using (var dialog = new OpenFileDialog())
		{
			dialog.Title = L10n.Text("选择 QQMusic.exe", "Choose QQMusic.exe");
			dialog.Filter = "QQMusic.exe|QQMusic.exe|" + L10n.Text("可执行文件", "Executable files") + " (*.exe)|*.exe";
			dialog.CheckFileExists = true;
			if (dialog.ShowDialog() != DialogResult.OK) return false;
			if (!QQMusicLocator.IsQQMusic(dialog.FileName))
			{
				MessageBox.Show(L10n.Text("请选择名为 QQMusic.exe 的程序。", "Please choose the file named QQMusic.exe."),
					tray.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return false;
			}
			qqPath = QQMusicLocator.Save(settings, dialog.FileName);
			missingQQPromptShown = false;
			Log("QQ Music path selected: " + qqPath);
			return true;
		}
	}

	private void PromptForQQMusic()
	{
		if (missingQQPromptShown) return;
		missingQQPromptShown = true;
		DialogResult answer = MessageBox.Show(
			L10n.Text(
				"没有找到 QQ 音乐。\n\n是：重新检测\n否：手动选择 QQMusic.exe\n取消：稍后处理",
				"QQ Music was not found.\n\nYes: detect again\nNo: choose QQMusic.exe manually\nCancel: decide later"),
			tray.Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
		if (answer == DialogResult.Yes) RedetectQQMusic(true);
		else if (answer == DialogResult.No) SelectQQMusic();
	}

	private void CheckForUpdates(bool userInitiated)
	{
		if (checkingForUpdates) return;
		checkingForUpdates = true;
		UpdateChecker.CheckAsync(result =>
		{
			try
			{
				window.BeginInvoke((Action)(() =>
				{
					checkingForUpdates = false;
					if (!string.IsNullOrEmpty(result.Error))
					{
						if (userInitiated)
							MessageBox.Show(L10n.Text("检查更新失败：\n", "Update check failed:\n") + result.Error,
								tray.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
						return;
					}
					if (result.HasUpdate)
					{
						DialogResult answer = MessageBox.Show(
							L10n.Text("发现新版本 v", "A new version is available: v") + result.LatestVersion +
							L10n.Text("。\n\n现在打开下载页面吗？", ".\n\nOpen the download page now?"),
							tray.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
						if (answer == DialogResult.Yes) OpenUrl(AppInfo.ReleasesUrl + "/latest");
					}
					else if (userInitiated)
					{
						MessageBox.Show(L10n.Text("当前已是最新版本 v", "You already have the latest version: v") + AppInfo.Version,
							tray.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
					}
				}));
			}
			catch { checkingForUpdates = false; }
		});
	}

	private static void OpenUrl(string url)
	{
		try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
		catch { }
	}

	private void MaintainWallpaper()
	{
		if (!enabled || suspended)
		{
			return;
		}
		if (embedded && !Native.IsWindow(qqWindow))
		{
			Log("QQ Music source disappeared; recovering.");
			StopRenderer();
			embedded = false;
			qqWindow = IntPtr.Zero;
			sourceStateCaptured = false;
		}
		else if (embedded && (rendererProcess == null || rendererProcess.HasExited))
		{
			Log("Wallpaper renderer exited; restoring and restarting.");
			DisableWallpaper(showQQ: false);
		}
		if (embedded)
		{
			WatchMouseHook();
			if (Native.IsIconic(qqWindow))
			{
				Log("QQ Music source was minimized; restoring wallpaper capture.");
				Native.ShowWindow(qqWindow, 4);
				LowerSourceOffscreen();
			}
			if (interactionActive && (DateTime.UtcNow - interactionStarted).TotalSeconds > 10.0)
			{
				Log("Interaction watchdog moved the QQ Music source offscreen.");
				LowerSourceOffscreen();
				interactionActive = false;
				keepSourcePresentedAfterClick = false;
				interactionStarted = DateTime.MinValue;
			}
			if (interactionActive)
			{
				return;
			}
			IntPtr foregroundWindow = Native.GetForegroundWindow();
			if (!sourcePresented || !IsQQMusicWindow(foregroundWindow))
			{
				if (foregroundWindow == qqWindow && DateTime.UtcNow <= taskbarClickUntil)
				{
					PresentSourceWindow();
				}
				else
				{
					LowerSourceOffscreen();
				}
			}
			return;
		}
		IntPtr intPtr = FindQQMusicMainWindow();
		if (intPtr == IntPtr.Zero)
		{
			if (DateTime.UtcNow >= nextLaunchAttempt)
			{
				nextLaunchAttempt = DateTime.UtcNow.AddSeconds(10.0);
				TryLaunchQQMusic();
			}
		}
		else
		{
			qqWindow = intPtr;
			CaptureSourceState();
			PrepareLyricsPage();
			Thread.Sleep(700);
			EmbedQQMusic();
		}
	}

	private void TryLaunchQQMusic()
	{
		try
		{
			if (QQMusicLocator.IsQQMusic(qqPath))
			{
				Process.Start(qqPath);
				Log("Requested QQ Music launch.");
			}
			else
			{
				qqPath = QQMusicLocator.Detect(settings);
				if (QQMusicLocator.IsQQMusic(qqPath))
				{
					Process.Start(qqPath);
					Log("Requested QQ Music launch after automatic detection.");
				}
				else
				{
					Log("QQ Music executable not found.");
					PromptForQQMusic();
				}
			}
		}
		catch (Exception ex)
		{
			Log("QQ Music launch failed: " + ex.Message);
		}
	}

	private IntPtr FindQQMusicMainWindow()
	{
		IntPtr best = IntPtr.Zero;
		long bestArea = 0L;
		Native.EnumWindows((IntPtr hwnd, IntPtr data) =>
		{
			if (!Native.IsWindowVisible(hwnd))
			{
				return true;
			}
			Native.GetWindowThreadProcessId(hwnd, out var processId);
			try
			{
				Process processById = Process.GetProcessById((int)processId);
				if (!string.Equals(processById.ProcessName, "QQMusic", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			catch
			{
				return true;
			}
			StringBuilder stringBuilder = new StringBuilder(128);
			Native.GetClassName(hwnd, stringBuilder, stringBuilder.Capacity);
			if (!string.Equals(stringBuilder.ToString(), "TXGuiFoundation", StringComparison.Ordinal))
			{
				return true;
			}
			if (!Native.GetWindowRect(hwnd, out var rect))
			{
				return true;
			}
			long num = (long)Math.Max(0, rect.Right - rect.Left) * (long)Math.Max(0, rect.Bottom - rect.Top);
			if (num > bestArea)
			{
				bestArea = num;
				best = hwnd;
			}
			return true;
		}, IntPtr.Zero);
		return best;
	}

	private void PrepareLyricsPage()
	{
		IntPtr foregroundWindow = Native.GetForegroundWindow();
		Native.GetCursorPos(out var point);
		Native.ShowWindow(qqWindow, 9);
		Native.ShowWindow(qqWindow, 3);
		Native.SetForegroundWindow(qqWindow);
		Thread.Sleep(400);
		Native.GetWindowThreadProcessId(qqWindow, out var processId);
		if (preparedProcessId != (int)processId)
		{
			Rectangle bounds = Screen.PrimaryScreen.Bounds;
			int x = bounds.Left + Math.Min(300, Math.Max(80, bounds.Width / 5));
			int y = bounds.Top + Math.Max(80, bounds.Height - 100);
			Native.SetCursorPos(x, y);
			Native.mouse_event(2u, 0u, 0u, 0u, UIntPtr.Zero);
			Native.mouse_event(4u, 0u, 0u, 0u, UIntPtr.Zero);
			Thread.Sleep(500);
			preparedProcessId = (int)processId;
			Log("Opened QQ Music player/lyrics page with a real input click.");
		}
		Native.SetCursorPos(point.X, point.Y);
		if (foregroundWindow != IntPtr.Zero && Native.IsWindow(foregroundWindow))
		{
			Native.SetForegroundWindow(foregroundWindow);
		}
	}

	private void CaptureSourceState()
	{
		originalParent = Native.GetParent(qqWindow);
		originalStyle = Native.GetWindowLongPtr(qqWindow, -16).ToInt64();
		originalExStyle = Native.GetWindowLongPtr(qqWindow, -20).ToInt64();
		Native.GetWindowRect(qqWindow, out originalRect);
		originalPlacement = default;
		originalPlacement.length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT));
		Native.GetWindowPlacement(qqWindow, ref originalPlacement);
		sourceStateCaptured = true;
	}

	private void EmbedQQMusic()
	{
		if (!File.Exists(rendererPath))
		{
			Log("Renderer not found: " + rendererPath);
			return;
		}
		try
		{
			rendererProcess = Process.Start(new ProcessStartInfo(rendererPath)
			{
				UseShellExecute = false
			});
			if (rendererProcess != null && rendererJob != IntPtr.Zero && !Native.AssignProcessToJobObject(rendererJob, rendererProcess.Handle))
			{
				Log("Could not attach renderer to controller job; Win32=" + Marshal.GetLastWin32Error());
			}
			embedded = rendererProcess != null;
			if (embedded)
			{
				// Windows may silently remove a low-level hook when one callback takes too
				// long. Rebuilding it whenever desktop mode becomes ready guarantees that
				// a later mode switch does not inherit a stale, nonfunctional hook handle.
				RestartMouseHook();
			}
			Log("Started desktop renderer, pid=" + ((rendererProcess != null) ? rendererProcess.Id : 0));
		}
		catch (Exception ex)
		{
			Log("Renderer launch failed: " + ex.Message);
			embedded = false;
		}
	}

	private void FitToPrimaryScreen()
	{
		if (embedded && !(qqWindow == IntPtr.Zero) && !(workerWindow == IntPtr.Zero))
		{
			Native.GetWindowRect(workerWindow, out var rect);
			Rectangle bounds = Screen.PrimaryScreen.Bounds;
			Native.SetWindowPos(qqWindow, new IntPtr(1), bounds.Left - rect.Left, bounds.Top - rect.Top, bounds.Width, bounds.Height, 96u);
		}
	}

	private IntPtr FindWallpaperWorker()
	{
		IntPtr intPtr = Native.FindWindow("Progman", null);
		Native.SendMessageTimeout(intPtr, 1324u, new IntPtr(13), new IntPtr(1), 0u, 1000u, out var result);
		Native.SendMessageTimeout(intPtr, 1324u, new IntPtr(13), IntPtr.Zero, 0u, 1000u, out result);
		Native.SendMessageTimeout(intPtr, 1324u, IntPtr.Zero, IntPtr.Zero, 0u, 1000u, out result);
		IntPtr intPtr2 = Native.FindWindowEx(intPtr, IntPtr.Zero, "WorkerW", null);
		if (intPtr2 != IntPtr.Zero)
		{
			return intPtr2;
		}
		IntPtr worker = IntPtr.Zero;
		Native.EnumWindows((IntPtr top, IntPtr data) =>
		{
			IntPtr intPtr3 = Native.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
			if (intPtr3 != IntPtr.Zero)
			{
				worker = Native.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
				return false;
			}
			return true;
		}, IntPtr.Zero);
		if (!(worker != IntPtr.Zero))
		{
			return intPtr;
		}
		return worker;
	}

	private void Toggle()
	{
		enabled = !enabled;
		if (!enabled)
		{
			DisableWallpaper(showQQ: true);
			return;
		}
		tray.ShowBalloonTip(1200, "QQ 音乐动态壁纸", "已启用", ToolTipIcon.Info);
		MaintainWallpaper();
	}

	private void DisableWallpaper(bool showQQ)
	{
		keepSourcePresentedAfterClick = false;
		StopRenderer();
		if (!sourceStateCaptured || qqWindow == IntPtr.Zero || !Native.IsWindow(qqWindow))
		{
			embedded = false;
			return;
		}
		try
		{
			Native.SetParent(qqWindow, originalParent);
			Native.SetWindowLongPtr(qqWindow, -16, new IntPtr(originalStyle));
			Native.SetWindowLongPtr(qqWindow, -20, new IntPtr(originalExStyle));
			Native.SetWindowPos(qqWindow, IntPtr.Zero, originalRect.Left, originalRect.Top, originalRect.Right - originalRect.Left, originalRect.Bottom - originalRect.Top, 96u);
			Native.SetWindowPlacement(qqWindow, ref originalPlacement);
			if (showQQ)
			{
				Native.ShowWindow(qqWindow, 9);
				Native.SetForegroundWindow(qqWindow);
			}
			Log("Restored QQ Music window.");
		}
		catch (Exception ex)
		{
			Log("Restore failed: " + ex.Message);
		}
		embedded = false;
		workerWindow = IntPtr.Zero;
		sourceStateCaptured = false;
	}

	private void StopRenderer()
	{
		try
		{
			if (rendererProcess != null && !rendererProcess.HasExited)
			{
				rendererProcess.Kill();
				rendererProcess.WaitForExit(2000);
			}
		}
		catch (Exception ex)
		{
			Log("Renderer stop failed: " + ex.Message);
		}
		finally
		{
			if (rendererProcess != null)
			{
				rendererProcess.Dispose();
			}
			rendererProcess = null;
		}
	}

	private void CleanupOrphanedRenderers()
	{
		Process[] processesByName = Process.GetProcessesByName("QQMusicWallpaperRenderer");
		foreach (Process process in processesByName)
		{
			try
			{
				string a = ((process.MainModule == null) ? null : process.MainModule.FileName);
				if (string.Equals(a, rendererPath, StringComparison.OrdinalIgnoreCase))
				{
					int id = process.Id;
					process.Kill();
					process.WaitForExit(2000);
					Log("Removed orphaned renderer, pid=" + id);
				}
			}
			catch (Exception ex)
			{
				Log("Orphaned renderer cleanup failed: " + ex.Message);
			}
			finally
			{
				process.Dispose();
			}
		}
	}

	private IntPtr CreateRendererJob()
	{
		IntPtr intPtr = Native.CreateJobObject(IntPtr.Zero, null);
		if (intPtr == IntPtr.Zero)
		{
			Log("Could not create renderer job; Win32=" + Marshal.GetLastWin32Error());
			return IntPtr.Zero;
		}
		Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION structure = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION
		{
			BasicLimitInformation = 
			{
				LimitFlags = 8192u
			}
		};
		int num = Marshal.SizeOf(typeof(Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
		IntPtr intPtr2 = Marshal.AllocHGlobal(num);
		try
		{
			Marshal.StructureToPtr(structure, intPtr2, fDeleteOld: false);
			if (!Native.SetInformationJobObject(intPtr, 9, intPtr2, (uint)num))
			{
				Log("Could not configure renderer job; Win32=" + Marshal.GetLastWin32Error());
				Native.CloseHandle(intPtr);
				return IntPtr.Zero;
			}
			return intPtr;
		}
		finally
		{
			Marshal.FreeHGlobal(intPtr2);
		}
	}

	private void OnHotkey()
	{
		Log("Hotkey invoked.");
		Toggle();
	}

	private void OnSessionSwitch(object sender, SessionSwitchEventArgs args)
	{
		Log("Session switch: " + args.Reason);
		if (args.Reason == SessionSwitchReason.SessionLock || args.Reason == SessionSwitchReason.RemoteDisconnect)
		{
			suspended = true;
			DisableWallpaper(showQQ: false);
		}
		else if (args.Reason == SessionSwitchReason.SessionUnlock || args.Reason == SessionSwitchReason.RemoteConnect)
		{
			suspended = false;
			MaintainWallpaper();
		}
	}

	private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
	{
		Log("Power mode: " + args.Mode);
		if (args.Mode == PowerModes.Suspend)
		{
			suspended = true;
			DisableWallpaper(showQQ: false);
		}
		else if (args.Mode == PowerModes.Resume)
		{
			suspended = false;
			MaintainWallpaper();
		}
	}

	private IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
	{
		Interlocked.Exchange(ref lastMouseHookEventTicks, DateTime.UtcNow.Ticks);
		if (code < 0 || !enabled || suspended || !embedded || !Native.IsWindow(qqWindow))
		{
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
		uint num = (uint)wParam.ToInt64();
		if (num != 512 && num != 513 && num != 514 && num != 522)
		{
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
		Native.MSLLHOOKSTRUCT mSLLHOOKSTRUCT = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
		if (mSLLHOOKSTRUCT.extraInfo.ToInt64() == 1364023120)
		{
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
		if (interactionActive && (num == 512 || num == 514))
		{
			if (num == 514)
			{
				if (keepSourcePresentedAfterClick)
				{
					keepSourcePresentedAfterClick = false;
					interactionActive = false;
					sourcePresented = true;
					Log("Keeping QQ Music visible for a native player control.");
				}
				else
				{
					ScheduleLowerSource(100);
				}
			}
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
		if (num == WmMouseMove)
		{
			DateTime utcNow = DateTime.UtcNow;
			if ((utcNow - lastHoverForwarded).TotalMilliseconds < 25.0)
			{
				return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
			}
			lastHoverForwarded = utcNow;
		}
		Point point = new Point(mSLLHOOKSTRUCT.pt.X, mSLLHOOKSTRUCT.pt.Y);
		Rectangle bounds = Screen.PrimaryScreen.Bounds;
		Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
		if (!bounds.Contains(point))
		{
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
		if (point.Y >= workingArea.Bottom)
		{
			if (num == 513)
			{
				taskbarClickUntil = DateTime.UtcNow.AddSeconds(2.0);
			}
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
		Native.POINT point2 = new Native.POINT
		{
			X = point.X,
			Y = point.Y
		};
		IntPtr hwnd = Native.WindowFromPoint(point2);
		IntPtr ancestor = Native.GetAncestor(hwnd, 2u);
		IntPtr intPtr = GetProgmanWindow();
		bool flag = ancestor == intPtr;
		bool flag2 = !flag && IsQQMusicWindow(ancestor);
		if (!flag && !flag2)
		{
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
		if (num == 512)
		{
			ForwardHoverToSource(point, workingArea);
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
		if (flag2)
		{
			if (num == 513 && TryHandleMediaButton(point, workingArea))
			{
				return new IntPtr(1);
			}
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
		IntPtr intPtr2 = FindDesktopListView();
		if (intPtr2 == IntPtr.Zero || IsDesktopIconAt(intPtr2, point))
		{
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
		if (num == 513 && TryHandleMediaButton(point, workingArea))
		{
			return new IntPtr(1);
		}
		switch (num)
		{
		case 513u:
		{
			double num2 = (double)(point.X - workingArea.Left) / (double)workingArea.Width;
			keepSourcePresentedAfterClick = point.Y >= workingArea.Bottom - 90 && ((num2 >= 0.055 && num2 <= 0.11) || (num2 >= 0.53 && num2 <= 0.59) || num2 >= 0.88);
			RaiseSourceForInput();
			Native.mouse_event(2u, 0u, 0u, 0u, InputMarker);
			return new IntPtr(1);
		}
		case 522u:
		{
			RaiseSourceForInput();
			uint data = (uint)(short)(mSLLHOOKSTRUCT.mouseData >> 16);
			Native.mouse_event(2048u, 0u, 0u, data, InputMarker);
			ScheduleLowerSource(150);
			return new IntPtr(1);
		}
		default:
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
	}

	private void ForwardHoverToSource(Point screenPoint, Rectangle work)
	{
		int num = Math.Max(0, Math.Min(65535, screenPoint.X - work.Left));
		int num2 = Math.Max(0, Math.Min(65535, screenPoint.Y - work.Top));
		Native.PostMessage(lParam: new IntPtr((num2 << 16) | (num & 0xFFFF)), hwnd: qqWindow, message: 512u, wParam: IntPtr.Zero);
	}

	private void RaiseSourceForInput()
	{
		if (interactionTimer != null)
		{
			interactionTimer.Dispose();
		}
		interactionTimer = null;
		interactionForeground = Native.GetForegroundWindow();
		interactionActive = true;
		interactionStarted = DateTime.UtcNow;
		sourcePresented = false;
		AlignSourceToWorkingArea();
		Native.SetForegroundWindow(qqWindow);
	}

	private bool TryHandleMediaButton(Point point, Rectangle work)
	{
		int num = work.Bottom - 40;
		if (Math.Abs(point.Y - num) > 28)
		{
			return false;
		}
		int num2 = work.Left + work.Width / 2;
		int num3 = point.X - num2;
		byte key;
		if (Math.Abs(num3) <= 24)
		{
			key = 179;
		}
		else if (Math.Abs(num3 + 52) <= 20)
		{
			key = 177;
		}
		else
		{
			if (Math.Abs(num3 - 52) > 20)
			{
				return false;
			}
			key = 176;
		}
		Native.keybd_event(key, 0, 0u, UIntPtr.Zero);
		Native.keybd_event(key, 0, 2u, UIntPtr.Zero);
		return true;
	}

	private void ScheduleLowerSource(int delayMilliseconds)
	{
		if (interactionTimer != null)
		{
			interactionTimer.Dispose();
		}
		interactionTimer = new System.Threading.Timer((object param0) =>
		{
			try
			{
				LowerSourceOffscreen();
				if (interactionForeground != IntPtr.Zero && Native.IsWindow(interactionForeground))
				{
					Native.SetForegroundWindow(interactionForeground);
				}
			}
			finally
			{
				interactionActive = false;
				interactionStarted = DateTime.MinValue;
			}
		}, null, delayMilliseconds, -1);
	}

	private bool IsQQMusicWindow(IntPtr hwnd)
	{
		if (hwnd == IntPtr.Zero)
		{
			return false;
		}
		Native.GetWindowThreadProcessId(hwnd, out var processId);
		if (processId == 0u)
		{
			return false;
		}
		if (qqWindowProcessIdSource != qqWindow || qqWindowProcessId == 0u)
		{
			Native.GetWindowThreadProcessId(qqWindow, out var processId2);
			qqWindowProcessId = processId2;
			qqWindowProcessIdSource = qqWindow;
		}
		return qqWindowProcessId != 0u && processId == qqWindowProcessId;
	}

	/// <summary>
	/// Returns the Progman desktop window, caching the handle across calls.
	/// FindWindow enumerates the top-level window list, and the mouse hook needs this
	/// on the hover path (up to 40 times per second), so it must not run every event.
	/// </summary>
	private IntPtr GetProgmanWindow()
	{
		if (progmanWindow != IntPtr.Zero && Native.IsWindow(progmanWindow))
		{
			return progmanWindow;
		}
		progmanWindow = Native.FindWindow("Progman", null);
		return progmanWindow;
	}

	private void LowerSourceOffscreen()
	{
		if (!(qqWindow == IntPtr.Zero) && Native.IsWindow(qqWindow))
		{
			Rectangle bounds = Screen.PrimaryScreen.Bounds;
			Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
			Native.SetWindowPos(qqWindow, new IntPtr(1), bounds.Left - bounds.Width - 512, workingArea.Top, bounds.Width, workingArea.Height, 80u);
			sourcePresented = false;
		}
	}

	private void PresentSourceWindow()
	{
		AlignSourceToWorkingArea();
		sourcePresented = true;
	}

	private void AlignSourceToWorkingArea()
	{
		Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
		Native.ShowWindow(qqWindow, 9);
		int num = workingArea.Left;
		int num2 = workingArea.Top;
		for (int i = 0; i < 3; i++)
		{
			Native.SetWindowPos(qqWindow, IntPtr.Zero, num, num2, workingArea.Width, workingArea.Height, 80u);
			// No Thread.Sleep here. SetWindowPos sends WM_WINDOWPOSCHANGED synchronously,
			// so GetWindowRect already reflects the new position. Sleeping used to cost up
			// to 60 ms on the mouse-hook thread and could push the hook callback past
			// LowLevelHooksTimeout (300 ms by default), which makes Windows silently drop
			// the hook -- the "Mouse hook watchdog ... reinstalling hook" entries.
			if (!Native.GetWindowRect(qqWindow, out var rect))
			{
				break;
			}
			int num3 = rect.Left - workingArea.Left;
			int num4 = rect.Top - workingArea.Top;
			if (num3 == 0 && num4 == 0)
			{
				break;
			}
			num -= num3;
			num2 -= num4;
		}
	}

	private IntPtr FindDesktopListView()
	{
		IntPtr defView = Native.FindWindowEx(GetProgmanWindow(), IntPtr.Zero, "SHELLDLL_DefView", null);
		if (defView == IntPtr.Zero)
		{
			Native.EnumWindows((IntPtr top, IntPtr data) =>
			{
				IntPtr intPtr = Native.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
				if (intPtr != IntPtr.Zero)
				{
					defView = intPtr;
					return false;
				}
				return true;
			}, IntPtr.Zero);
		}
		if (!(defView == IntPtr.Zero))
		{
			return Native.FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
		}
		return IntPtr.Zero;
	}

	private bool IsDesktopIconAt(IntPtr listView, Point screenPoint)
	{
		Native.POINT point = new Native.POINT
		{
			X = screenPoint.X,
			Y = screenPoint.Y
		};
		Native.ScreenToClient(listView, ref point);
		Native.GetWindowThreadProcessId(listView, out var processId);
		IntPtr intPtr = Native.OpenProcess(1080u, inherit: false, processId);
		if (intPtr == IntPtr.Zero)
		{
			return true;
		}
		int value = Marshal.SizeOf(typeof(Native.LVHITTESTINFO));
		IntPtr intPtr2 = Native.VirtualAllocEx(intPtr, IntPtr.Zero, new UIntPtr((uint)value), 12288u, 4u);
		if (intPtr2 == IntPtr.Zero)
		{
			Native.CloseHandle(intPtr);
			return true;
		}
		try
		{
			byte[] array = StructureToBytes(new Native.LVHITTESTINFO
			{
				pt = point
			});
			Native.WriteProcessMemory(intPtr, intPtr2, array, array.Length, out var _);
			// Explorer can briefly stop pumping messages while it refreshes the
			// desktop. A blocking SendMessage here runs inside WH_MOUSE_LL and can
			// make Windows silently remove our hook. Preserve icon clicks on timeout,
			// but never keep the hook callback blocked indefinitely.
			if (Native.SendMessageTimeout(listView, 4114u, IntPtr.Zero, intPtr2, 2u, 100u, out var hitResult) == IntPtr.Zero)
			{
				return true;
			}
			return unchecked((long)hitResult.ToUInt64()) >= 0;
		}
		finally
		{
			Native.VirtualFreeEx(intPtr, intPtr2, UIntPtr.Zero, 32768u);
			Native.CloseHandle(intPtr);
		}
	}

	private static byte[] StructureToBytes(object value)
	{
		int num = Marshal.SizeOf(value);
		byte[] array = new byte[num];
		IntPtr intPtr = Marshal.AllocHGlobal(num);
		try
		{
			Marshal.StructureToPtr(value, intPtr, fDeleteOld: false);
			Marshal.Copy(intPtr, array, 0, num);
			return array;
		}
		finally
		{
			Marshal.FreeHGlobal(intPtr);
		}
	}

	private static IntPtr MakeLParam(int x, int y)
	{
		return new IntPtr((y << 16) | (x & 0xFFFF));
	}

	private static string GetWindowTitle(IntPtr hwnd)
	{
		StringBuilder stringBuilder = new StringBuilder(512);
		Native.GetWindowText(hwnd, stringBuilder, stringBuilder.Capacity);
		return stringBuilder.ToString();
	}

	private void StartMouseHook()
	{
		ManualResetEvent ready = new ManualResetEvent(initialState: false);
		int generation;
		Thread thread;
		lock (mouseHookSync)
		{
			generation = ++mouseHookGeneration;
			thread = new Thread(() =>
			{
				uint threadId = Native.GetCurrentThreadId();
				IntPtr hook = Native.SetWindowsHookEx(WhMouseLl, mouseProc, IntPtr.Zero, 0u);
				lock (mouseHookSync)
				{
					if (generation == mouseHookGeneration)
					{
						mouseHookThreadId = threadId;
						mouseHook = hook;
					}
				}
				ready.Set();
				if (hook == IntPtr.Zero)
				{
					Log("Could not install mouse hook; Win32=" + Marshal.GetLastWin32Error());
				}
				else
				{
					Interlocked.Exchange(ref lastMouseHookEventTicks, DateTime.UtcNow.Ticks);
					Application.Run(new ApplicationContext());
					Native.UnhookWindowsHookEx(hook);
				}
				lock (mouseHookSync)
				{
					if (generation == mouseHookGeneration)
					{
						mouseHook = IntPtr.Zero;
						mouseHookThreadId = 0u;
						mouseHookThread = null;
					}
				}
			});
			thread.IsBackground = true;
			thread.SetApartmentState(ApartmentState.STA);
			mouseHookThread = thread;
			thread.Start();
		}
		if (!ready.WaitOne(2000))
		{
			Log("Mouse hook installation timed out.");
		}
		else
		{
			ready.Dispose();
		}
	}

	private void StopMouseHook()
	{
		Thread thread;
		IntPtr hook;
		uint threadId;
		lock (mouseHookSync)
		{
			++mouseHookGeneration;
			thread = mouseHookThread;
			hook = mouseHook;
			threadId = mouseHookThreadId;
			mouseHookThread = null;
			mouseHook = IntPtr.Zero;
			mouseHookThreadId = 0u;
		}
		if (hook != IntPtr.Zero)
		{
			Native.UnhookWindowsHookEx(hook);
		}
		if (threadId != 0u)
		{
			Native.PostThreadMessage(threadId, 18u, IntPtr.Zero, IntPtr.Zero);
		}
		if (thread != null && thread != Thread.CurrentThread && thread.IsAlive)
		{
			thread.Join(1000);
		}
	}

	private void RestartMouseHook()
	{
		StopMouseHook();
		StartMouseHook();
	}

	private void WatchMouseHook()
	{
		if (!Native.GetCursorPos(out var point))
		{
			return;
		}
		if (!hookWatchdogPointCaptured)
		{
			lastHookWatchdogPoint = point;
			hookWatchdogPointCaptured = true;
			return;
		}
		if (point.X == lastHookWatchdogPoint.X && point.Y == lastHookWatchdogPoint.Y)
		{
			return;
		}
		lastHookWatchdogPoint = point;
		long lastEvent = Interlocked.Read(ref lastMouseHookEventTicks);
		if (lastEvent != 0L && DateTime.UtcNow.Ticks - lastEvent > TimeSpan.FromSeconds(2.0).Ticks)
		{
			if (hookReinstallWindowStart == DateTime.MinValue ||
				(DateTime.UtcNow - hookReinstallWindowStart).TotalMinutes > 10.0)
			{
				hookReinstallWindowStart = DateTime.UtcNow;
				hookReinstallCount = 0;
				Log("Mouse hook watchdog detected lost input events; reinstalling hook. (new 10-minute window)");
			}
			hookReinstallCount++;
			Log("Mouse hook watchdog detected lost input events; reinstalling hook. (count=" + hookReinstallCount + " in 10 min)");
			if (hookReinstallCount == 3)
			{
				Log("WARN: the mouse hook was dropped 3 times within 10 minutes. The hook callback is likely exceeding LowLevelHooksTimeout (default 300 ms); check for a hung QQ Music window or a slow IsDesktopIconAt/AlignSourceToWorkingArea path.");
			}
			RestartMouseHook();
		}
	}

	private void Exit()
	{
		if (!exiting)
		{
			exiting = true;
			enabled = false;
			DisableWallpaper(showQQ: true);
			if (themeContext != null) themeContext.Shutdown();
			timer.Stop();
			StopMouseHook();
			Native.UnregisterHotKey(window.Handle, 20813);
			SystemEvents.SessionSwitch -= OnSessionSwitch;
			SystemEvents.PowerModeChanged -= OnPowerModeChanged;
			tray.Visible = false;
			tray.Dispose();
			window.Dispose();
			if (rendererJob != IntPtr.Zero)
			{
				Native.CloseHandle(rendererJob);
				rendererJob = IntPtr.Zero;
			}
			Log("Exited.");
			Application.Exit();
		}
	}

	private void Log(string message)
	{
		try
		{
			File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + message + Environment.NewLine);
		}
		catch
		{
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			Exit();
		}
		base.Dispose(disposing);
	}
}
