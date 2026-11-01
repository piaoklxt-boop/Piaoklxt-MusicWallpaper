using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using QQMusicWallpaperThemeSync;

namespace QQMusicWallpaper;

internal sealed class WallpaperContext : ApplicationContext
{
	private sealed class CommentControlMap
	{
		public readonly Rectangle Bounds;
		public readonly AccessibleTreeSnapshot Snapshot;
		public CommentControlMap(Rectangle bounds, AccessibleTreeSnapshot snapshot)
		{
			Bounds = bounds;
			Snapshot = snapshot;
		}
	}
	private sealed class HiddenWindow : Form
	{
		private readonly Action onHotkey;

		public HiddenWindow(Action onHotkeyAction)
		{
			onHotkey = onHotkeyAction;
			base.ShowInTaskbar = false;
			base.FormBorderStyle = FormBorderStyle.None;
			base.Opacity = 0.0;
			base.Width = 1;
			base.Height = 1;
			base.StartPosition = FormStartPosition.Manual;
			base.Location = new Point(-32000, -32000);
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

	private const long WsExNoActivate = 134217728L;

	private const long WsExNoRedirectionBitmap = 2097152L;

	private const uint SwpFrameChanged = 32u;

	private const uint SwpShowWindow = 64u;

	private const uint SwpNoActivate = 16u;

	private const uint WmLButtonDown = 513u;

	private const uint WmLButtonUp = 514u;

	private const uint WmLButtonDblClk = 515u;

	private const uint WmRButtonDown = 516u;

	private const uint WmRButtonUp = 517u;

	private const uint WmMouseMove = 512u;

	private const uint WmMouseWheel = 522u;

	private const uint WmMouseHover = 673u;

	private const uint WmClose = 16u;

	private const uint WmCancelMode = 31u;

	private const uint WmKeyDown = 256u;

	private const uint WmKeyUp = 257u;

	private const int VkEscape = 27;

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

	private readonly HiddenWindow window;

	private readonly VolumeOverlayForm volumeOverlay;

	private readonly PlaybackModeOverlayForm playbackModeOverlay;
	private readonly ProgressPreviewForm progressPreview;
	private readonly ExplorerThemeRuntime explorerThemeRuntime;

	private readonly NotifyIcon tray;

	private ThemeContext themeContext;

	private Thread themeThread;

	private Thread mouseHookThread;

	private readonly object mouseHookSync = new object();

	private uint mouseHookThreadId;

	private int mouseHookGeneration;

	private long lastMouseHookEventTicks;
	private long lastMouseHookPoint;
	private int hasMouseHookPoint;
	private long mouseHookCallbackCount;
	private long lastMouseHookDurationTicks;
	private long maxMouseHookDurationTicks;
	private int activeMouseHookCallbacks;
	private volatile int mouseHookStage;
	private long accessibilityCaptureCount;
	private readonly bool diagnosticMode = Array.IndexOf(
		Environment.GetCommandLineArgs(), "--diagnostics") >= 0;
	private DateTime nextDiagnosticSummary = DateTime.UtcNow.AddSeconds(30);

	private Native.POINT lastHookWatchdogPoint;

	private bool hookWatchdogPointCaptured;

	private int hookReinstallCount;

	private DateTime hookReinstallWindowStart = DateTime.MinValue;

	private readonly System.Windows.Forms.Timer timer;

	private readonly AppSettings settings;

	private string qqPath;

	private readonly string rendererPath;

	private readonly Native.LowLevelMouseProc mouseProc;

	private IntPtr mouseHook;

	private IntPtr qqWindow;

	private IntPtr qqWindowProcessIdSource;

	private uint qqWindowProcessId;

	private IntPtr progmanWindow;

	private IntPtr desktopRootWindow;

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

	private DateTime nextEmbedAttempt = DateTime.MinValue;

	private bool sourceStateCaptured;

	private bool interactionActive;

	private IntPtr interactionTarget;

	private DateTime interactionStarted = DateTime.MinValue;

	private volatile bool progressSeekActive;
	private Rectangle activeProgressTimeline;
	private int progressPreviewGeneration;

	private Point pendingProgressSeekPoint = Point.Empty;

	private DateTime taskbarClickUntil = DateTime.MinValue;

	private DateTime lastHoverForwarded = DateTime.MinValue;

	private bool sourcePresented;

	private volatile Rectangle[] qqInteractiveAreas = new Rectangle[0];

	private volatile AccessibleTreeSnapshot qqAccessibilitySnapshot =
		new AccessibleTreeSnapshot(new AccessibleNodeSnapshot[0]);

	private volatile AccessibleControlSurface[] qqPopupControlSurfaces =
		new AccessibleControlSurface[0];

	private volatile string[] activeTransientToggleDescriptions = new string[0];

	private volatile bool activeTransientPopupPersistent;

	private volatile Rectangle[] volumeControlAreas = new Rectangle[0];

	private volatile Rectangle[] playbackModeControlAreas = new Rectangle[0];

	private volatile Rectangle[] commentControlAreas = new Rectangle[0];
	private volatile CommentControlMap commentControlMap;
	private long desktopClickSequence;
	private long lastAccessibilitySnapshotTicks;
	private int commentMissProbeRunning;

	private volatile Rectangle[] lyricTranslationControlAreas = new Rectangle[0];

	private volatile Rectangle[] accompanimentControlAreas = new Rectangle[0];

	private volatile Rectangle[] playerStyleControlAreas = new Rectangle[0];

	private volatile Rectangle[] qualityControlAreas = new Rectangle[0];

	private volatile Rectangle[] soundEffectsControlAreas = new Rectangle[0];

	private volatile Rectangle[] playlistControlAreas = new Rectangle[0];

	private volatile int bottomControlCorrectionX;

	private volatile int bottomControlCorrectionY;

	private volatile Rectangle[] progressControlAreas = new Rectangle[0];

	private volatile TimestampControlArea[] lyricTimestampAreas =
		new TimestampControlArea[0];

	private volatile int playbackTotalSeconds;

	private volatile MediaControlArea[] mediaControlAreas = new MediaControlArea[0];

	private volatile string currentPlaybackMode = "列表循环";

	private int accessibilityRefreshRunning;

	private DateTime nextAccessibilityRefresh = DateTime.MinValue;

	private bool exiting;

	private bool missingQQPromptShown;

	private bool checkingForUpdates;

	public WallpaperContext()
	{
		AppPaths.Ensure();
		settings = AppSettings.Load();
		qqPath = QQMusicLocator.Detect(settings);
		rendererPath = Path.Combine(AppPaths.BaseDirectory,
			"Piaoklxt-MusicWallpaperRenderer.exe");
		StartupManager.ApplyDefault(settings);
		CleanupOrphanedRenderers();
		rendererJob = CreateRendererJob();
		window = new HiddenWindow(OnHotkey);
		volumeOverlay = new VolumeOverlayForm();
		volumeOverlay.VolumeRequested += delegate(float level)
		{
			Native.GetWindowThreadProcessId(qqWindow, out var processId);
			float applied;
			if (processId != 0) AudioSessionVolume.TrySet(processId, level, out applied);
		};
		playbackModeOverlay = new PlaybackModeOverlayForm();
		playbackModeOverlay.ModeRequested += QueueSetPlaybackMode;
		progressPreview = new ProgressPreviewForm();
		explorerThemeRuntime = new ExplorerThemeRuntime();
		explorerThemeRuntime.StartAsync();
		if (!Native.RegisterHotKey(window.Handle, 20813, 3u, 85u))
		{
			Log("Could not register Ctrl+Alt+U; Win32=" + Marshal.GetLastWin32Error());
		}
		ContextMenu contextMenu = new ContextMenu
		{
			MenuItems =
			{
				{
					L10n.Text("切换动态壁纸 (Ctrl+Alt+U)", "Toggle wallpaper (Ctrl+Alt+U)"),
					(EventHandler)delegate
					{
						Log("Tray menu toggle.");
						Toggle();
					}
				},
				{
					L10n.Text("恢复 QQ 音乐窗口", "Restore QQ Music window"),
					(EventHandler)delegate
					{
						Log("Tray menu restore window.");
						DisableWallpaper(showQQ: true);
					}
				}
			}
		};
		MenuItem item = new MenuItem(L10n.Text("QQ 音乐", "QQ Music"))
		{
			MenuItems =
			{
				{
					L10n.Text("重新检测", "Detect again"),
					(EventHandler)delegate
					{
						RedetectQQMusic(notify: true);
					}
				},
				{
					L10n.Text("手动选择 QQMusic.exe", "Choose QQMusic.exe"),
					(EventHandler)delegate
					{
						SelectQQMusic();
					}
				},
				{
					L10n.Text("打开 QQ 音乐官网", "Open QQ Music website"),
					(EventHandler)delegate
					{
						OpenUrl("https://y.qq.com/download/download.html");
					}
				}
			}
		};
		contextMenu.MenuItems.Add(item);
		MenuItem startupItem = null;
		startupItem = new MenuItem(L10n.Text("开机自动启动", "Start with Windows"), delegate
		{
			bool flag2 = !startupItem.Checked;
			StartupManager.SetEnabled(flag2);
			settings.SetBool("AutoStart", flag2);
			settings.Save();
			startupItem.Checked = flag2;
		})
		{
			Checked = StartupManager.IsEnabled()
		};
		contextMenu.MenuItems.Add(startupItem);
		contextMenu.MenuItems.Add(L10n.Text("检查更新", "Check for updates"), delegate
		{
			CheckForUpdates(userInitiated: true);
		});
		string path = Path.Combine(AppPaths.ThemeDirectory, "theme-sync.ini");
		string text = (File.Exists(path) ? File.ReadAllText(path) : "");
		bool flag = text.Contains("Enabled=1");
		MenuItem themeMenu = new MenuItem(L10n.Text("系统主题色", "System theme colors"));
		MenuItem accentItem = null;
		accentItem = new MenuItem(L10n.Text("随 QQ 音乐桌面同步", "Follow the current QQ Music artwork"), delegate
		{
			if (themeContext == null)
			{
				return;
			}
			themeContext.RequestToggleAccent();
			accentItem.Checked = !accentItem.Checked;
			foreach (MenuItem menuItem in themeMenu.MenuItems)
			{
				if (menuItem != accentItem)
				{
					menuItem.Enabled = accentItem.Checked;
				}
			}
		})
		{
			Checked = flag
		};
		themeMenu.MenuItems.Add(accentItem);
		MenuItem brightnessItem = null;
		brightnessItem = new MenuItem(L10n.Text("同时切换系统明暗", "Also switch light/dark mode"), delegate
		{
			if (themeContext != null)
			{
				themeContext.RequestToggleBrightness();
				brightnessItem.Checked = !brightnessItem.Checked;
			}
		})
		{
			Checked = text.Contains("MatchBrightness=1"),
			Enabled = flag
		};
		themeMenu.MenuItems.Add(brightnessItem);
		MenuItem shellItem = null;
		shellItem = new MenuItem(L10n.Text("任务栏与开始菜单跟随颜色", "Color the taskbar and Start menu"), delegate
		{
			if (themeContext != null)
			{
				themeContext.RequestToggleShell();
				shellItem.Checked = !shellItem.Checked;
			}
		})
		{
			Checked = text.Contains("ColorizeShell=1"),
			Enabled = flag
		};
		themeMenu.MenuItems.Add(shellItem);
		MenuItem appsItem = null;
		appsItem = new MenuItem(L10n.Text("文件资源管理器等使用深色", "Use dark mode for apps and Explorer"), delegate
		{
			if (themeContext != null)
			{
				themeContext.RequestToggleApps();
				appsItem.Checked = !appsItem.Checked;
			}
		})
		{
			Checked = text.Contains("DarkApps=1"),
			Enabled = flag
		};
		themeMenu.MenuItems.Add(appsItem);
		themeMenu.MenuItems.Add(new MenuItem(L10n.Text("立即取色", "Refresh color now"), delegate
		{
			if (themeContext != null)
			{
				themeContext.RequestUpdateColor();
			}
		})
		{
			Enabled = flag
		});
		contextMenu.MenuItems.Add("-");
		contextMenu.MenuItems.Add(themeMenu);
		contextMenu.MenuItems.Add(L10n.Text("关于作者", "About the author"), delegate
		{
			MessageBox.Show(
				"Piaoklxt-MusicWallpaper v" + AppInfo.Version + "\n\n" +
				L10n.Text("作者：", "Author: ") + "piaoklxt\n" +
				L10n.Text("邮箱：", "Email: ") + "piaoklxt@gmail.com\n\n" +
				AppInfo.RepositoryUrl,
				L10n.Text("关于作者", "About the author"),
				MessageBoxButtons.OK, MessageBoxIcon.Information);
		});
		contextMenu.MenuItems.Add(L10n.Text("退出", "Exit"), delegate
		{
			Exit();
		});
		tray = new NotifyIcon();
		string text2 = Path.Combine(AppPaths.BaseDirectory,
			"Piaoklxt-MusicWallpaper.ico");
		tray.Icon = (File.Exists(text2) ? new Icon(text2) : Icon.ExtractAssociatedIcon(Application.ExecutablePath));
		tray.Text = "Piaoklxt-MusicWallpaper";
		tray.ContextMenu = contextMenu;
		tray.Visible = true;
		tray.DoubleClick += delegate
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
		timer.Tick += delegate
		{
			try
			{
				MaintainWallpaper();
			}
			catch (Exception ex)
			{
				Log("MaintainWallpaper failed; restoring QQ Music: " + ex);
				AbortFailedStartup();
				nextEmbedAttempt = DateTime.UtcNow.AddSeconds(3.0);
			}
		};
		timer.Start();
		themeThread = new Thread((ThreadStart)delegate
		{
			try
			{
				themeContext = new ThemeContext(AppPaths.ThemeDirectory, showTray: false);
				if (enabled && embedded && !suspended)
				{
					themeContext.RequestBeginWallpaperSession();
				}
				Application.Run(themeContext);
			}
			catch (Exception ex)
			{
				Log("Theme thread failed: " + ex);
			}
		});
		themeThread.IsBackground = true;
		themeThread.SetApartmentState(ApartmentState.STA);
		themeThread.Start();
		window.BeginInvoke((MethodInvoker)delegate
		{
			if (!enabled || suspended || exiting)
			{
				return;
			}
			if (FindQQMusicMainWindow() == IntPtr.Zero)
			{
				nextLaunchAttempt = DateTime.UtcNow.AddSeconds(10.0);
				TryLaunchQQMusic();
			}
			MaintainWallpaper();
		});
		Log("Started capture-first-v2. Shortcut=Ctrl+Alt+U");
		ThreadPool.QueueUserWorkItem(delegate
		{
			Thread.Sleep(3000);
			CheckForUpdates(userInitiated: false);
		});
	}

	private void RedetectQQMusic(bool notify)
	{
		qqPath = QQMusicLocator.Detect(settings);
		missingQQPromptShown = false;
		if (notify)
		{
			MessageBox.Show(QQMusicLocator.IsQQMusic(qqPath) ? L10n.Text("已找到：\n" + qqPath, "Found:\n" + qqPath) : L10n.Text("仍未找到 QQ 音乐。你可以使用托盘菜单手动选择 QQMusic.exe。", "QQ Music was not found. Use the tray menu to choose QQMusic.exe manually."), tray.Text, MessageBoxButtons.OK, QQMusicLocator.IsQQMusic(qqPath) ? MessageBoxIcon.Asterisk : MessageBoxIcon.Exclamation);
		}
	}

	private bool SelectQQMusic()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog();
		openFileDialog.Title = L10n.Text("选择 QQMusic.exe", "Choose QQMusic.exe");
		openFileDialog.Filter = "QQMusic.exe|QQMusic.exe|" + L10n.Text("可执行文件", "Executable files") + " (*.exe)|*.exe";
		openFileDialog.CheckFileExists = true;
		if (openFileDialog.ShowDialog() != DialogResult.OK)
		{
			return false;
		}
		if (!QQMusicLocator.IsQQMusic(openFileDialog.FileName))
		{
			MessageBox.Show(L10n.Text("请选择名为 QQMusic.exe 的程序。", "Please choose the file named QQMusic.exe."), tray.Text, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return false;
		}
		qqPath = QQMusicLocator.Save(settings, openFileDialog.FileName);
		missingQQPromptShown = false;
		Log("QQ Music path selected: " + qqPath);
		return true;
	}

	private void PromptForQQMusic()
	{
		if (!missingQQPromptShown)
		{
			missingQQPromptShown = true;
			switch (MessageBox.Show(L10n.Text("没有找到 QQ 音乐。\n\n是：重新检测\n否：手动选择 QQMusic.exe\n取消：稍后处理", "QQ Music was not found.\n\nYes: detect again\nNo: choose QQMusic.exe manually\nCancel: decide later"), tray.Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Asterisk))
			{
			case DialogResult.Yes:
				RedetectQQMusic(notify: true);
				break;
			case DialogResult.No:
				SelectQQMusic();
				break;
			}
		}
	}

	private void CheckForUpdates(bool userInitiated)
	{
		if (checkingForUpdates)
		{
			return;
		}
		checkingForUpdates = true;
		UpdateChecker.CheckAsync(delegate(UpdateResult result)
		{
			try
			{
				window.BeginInvoke((Action)delegate
				{
					checkingForUpdates = false;
					if (!string.IsNullOrEmpty(result.Error))
					{
						if (userInitiated)
						{
							MessageBox.Show(L10n.Text("检查更新失败：\n", "Update check failed:\n") + result.Error, tray.Text, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
						}
					}
					else if (result.HasUpdate)
					{
						if (MessageBox.Show(L10n.Text("发现新版本 v", "A new version is available: v") + result.LatestVersion + L10n.Text("。\n\n现在打开下载页面吗？", ".\n\nOpen the download page now?"), tray.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) == DialogResult.Yes)
						{
							OpenUrl(AppInfo.ReleasesUrl + "/latest");
						}
					}
					else if (userInitiated)
					{
						MessageBox.Show(L10n.Text("当前已是最新版本 v", "You already have the latest version: v") + AppInfo.Version, tray.Text, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
					}
				});
			}
			catch
			{
				checkingForUpdates = false;
			}
		});
	}

	private static void OpenUrl(string url)
	{
		try
		{
			Process.Start(new ProcessStartInfo(url)
			{
				UseShellExecute = true
			});
		}
		catch
		{
		}
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
			if (themeContext != null)
			{
				themeContext.RequestEndWallpaperSession();
			}
			StopRenderer();
			embedded = false;
			qqWindow = IntPtr.Zero;
			sourceStateCaptured = false;
		}
		else if (embedded && !IsRendererAlive())
		{
			Log("Wallpaper renderer exited; restoring and restarting.");
			DisableWallpaper(showQQ: false);
			nextEmbedAttempt = DateTime.UtcNow.AddSeconds(3.0);
			return;
		}
		if (embedded)
		{
			WatchMouseHook();
			LogDiagnosticSummary();
			PruneQQPopupSurfaces();
			if (!interactionActive) RefreshInteractiveAreasIfDue();
			if (Native.IsIconic(qqWindow))
			{
				Log("QQ Music source was minimized; restoring it maximized behind the desktop.");
				KeepSourceMaximizedBehindDesktop();
			}
			if (interactionActive && (DateTime.UtcNow - interactionStarted).TotalSeconds > 10.0)
			{
				Log("Interaction watchdog moved the QQ Music source offscreen.");
				KeepSourceMaximizedBehindDesktop();
				interactionActive = false;
				progressSeekActive = false;
				QueueHideProgressPreview();
				interactionStarted = DateTime.MinValue;
			}
			if (interactionActive)
			{
				return;
			}
			IntPtr foregroundWindow = Native.GetForegroundWindow();
			DateTime now = DateTime.UtcNow;
			bool foregroundIsQQMusic = IsQQMusicWindow(foregroundWindow);
			if (DesktopInputPolicy.ShouldKeepNativeSurfacePresented(sourcePresented,
				foregroundIsQQMusic))
			{
				return;
			}
			if (!sourcePresented || !foregroundIsQQMusic)
			{
				if (foregroundWindow == qqWindow && now <= taskbarClickUntil)
				{
					PresentSourceWindow();
				}
				else
				{
					KeepSourceMaximizedBehindDesktop();
				}
			}
			return;
		}
		if (DateTime.UtcNow < nextEmbedAttempt)
		{
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
			if (!EmbedQQMusic())
			{
				nextEmbedAttempt = DateTime.UtcNow.AddSeconds(3.0);
			}
			else if (themeContext != null)
			{
				themeContext.RequestBeginWallpaperSession();
			}
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
				return;
			}
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
		catch (Exception ex)
		{
			Log("QQ Music launch failed: " + ex.Message);
		}
	}

	private IntPtr FindQQMusicMainWindow()
	{
		IntPtr best = IntPtr.Zero;
		long bestArea = 0L;
		Native.EnumWindows(delegate(IntPtr hwnd, IntPtr data)
		{
			if (!Native.IsWindowVisible(hwnd))
			{
				return true;
			}
			Native.GetWindowThreadProcessId(hwnd, out var processId);
			try
			{
				if (!string.Equals(Process.GetProcessById((int)processId).ProcessName, "QQMusic", StringComparison.OrdinalIgnoreCase))
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

	private void CaptureSourceState()
	{
		originalParent = Native.GetParent(qqWindow);
		originalStyle = Native.GetWindowLongPtr(qqWindow, -16).ToInt64();
		originalExStyle = Native.GetWindowLongPtr(qqWindow, -20).ToInt64();
		Native.GetWindowRect(qqWindow, out originalRect);
		originalPlacement = default(Native.WINDOWPLACEMENT);
		originalPlacement.length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT));
		Native.GetWindowPlacement(qqWindow, ref originalPlacement);
		sourceStateCaptured = true;
	}

	private bool EmbedQQMusic()
	{
		if (!File.Exists(rendererPath))
		{
			Log("Renderer not found: " + rendererPath);
			AbortFailedStartup();
			return false;
		}
		IntPtr captureReady = Native.CreateEvent(IntPtr.Zero, manualReset: true,
			initialState: false, "Local\\PiaoklxtMusicWallpaperCaptureReady");
		try
		{
			if (captureReady != IntPtr.Zero)
			{
				Native.ResetEvent(captureReady);
			}
			ProcessStartInfo startInfo = new ProcessStartInfo(rendererPath)
			{
				UseShellExecute = false,
				WorkingDirectory = AppPaths.BaseDirectory
			};
			startInfo.EnvironmentVariables["QQMUSIC_WALLPAPER_TARGET_HWND"] =
				qqWindow.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);
			rendererProcess = Process.Start(startInfo);
			if (rendererProcess != null && rendererJob != IntPtr.Zero && !Native.AssignProcessToJobObject(rendererJob, rendererProcess.Handle))
			{
				Log("Could not attach renderer to controller job; Win32=" + Marshal.GetLastWin32Error());
			}
			bool started = rendererProcess != null;
			if (started)
			{
				uint waitResult = captureReady == IntPtr.Zero
					? 258u
					: Native.WaitForSingleObject(captureReady, 10000u);
				if (waitResult == 0u && IsRendererAlive())
				{
					Log("STAGE 1 PASS: renderer presented the first frame from the untouched QQ Music window.");
					Log("STAGE 2 START: resizing QQ Music behind the desktop without activation.");
					if (!KeepSourceMaximizedBehindDesktop())
					{
						Log("STAGE 2 FAIL: QQ Music did not reach the verified offscreen desktop size.");
						AbortFailedStartup();
						return false;
					}
					Thread.Sleep(150);
					if (!IsRendererAlive())
					{
						Log("STAGE 2 FAIL: renderer exited immediately after the first frame.");
						AbortFailedStartup();
						return false;
					}
					embedded = true;
					Log("STAGE 2 COMPLETE: renderer and source geometry are verified.");
				}
				else
				{
					string reason = rendererProcess != null && rendererProcess.HasExited
						? "renderer exited with code " + rendererProcess.ExitCode
						: "first-frame handshake timed out";
					Log("Renderer startup failed: " + reason + ". Restoring QQ Music.");
					AbortFailedStartup();
					return false;
				}
				RestartMouseHook();
			}
			Log("Started desktop renderer, pid=" + ((rendererProcess != null) ? rendererProcess.Id : 0));
			return embedded;
		}
		catch (Exception ex)
		{
			Log("Renderer launch failed: " + ex);
			AbortFailedStartup();
			return false;
		}
		finally
		{
			if (captureReady != IntPtr.Zero)
			{
				Native.CloseHandle(captureReady);
			}
		}
	}

	private bool IsRendererAlive()
	{
		try
		{
			return rendererProcess != null && !rendererProcess.HasExited;
		}
		catch
		{
			return false;
		}
	}

	private void AbortFailedStartup()
	{
		StopRenderer();
		embedded = false;
		interactionActive = false;
		interactionTarget = IntPtr.Zero;
		if (sourceStateCaptured && qqWindow != IntPtr.Zero && Native.IsWindow(qqWindow))
		{
			RestoreSourceState(showQQ: false);
		}
		qqWindow = IntPtr.Zero;
		sourceStateCaptured = false;
	}

	private void ShowDesktop()
	{
		object shell = null;
		try
		{
			Type shellType = Type.GetTypeFromProgID("Shell.Application");
			if (shellType == null)
			{
				throw new InvalidOperationException("Shell.Application is unavailable.");
			}
			shell = Activator.CreateInstance(shellType);
			shellType.InvokeMember("ToggleDesktop", System.Reflection.BindingFlags.InvokeMethod,
				null, shell, null);
			Log("STAGE 2 COMMAND SENT: Shell.ToggleDesktop returned successfully.");
		}
		catch (Exception ex)
		{
			Log("Could not show the desktop: " + ex.Message);
		}
		finally
		{
			if (shell != null && Marshal.IsComObject(shell))
			{
				Marshal.FinalReleaseComObject(shell);
			}
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
		Native.EnumWindows(delegate(IntPtr top, IntPtr data)
		{
			if (Native.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
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
		ResetPointerInteraction();
		if (themeContext != null)
		{
			themeContext.RequestEndWallpaperSession();
		}
		StopRenderer();
		if (!sourceStateCaptured || qqWindow == IntPtr.Zero || !Native.IsWindow(qqWindow))
		{
			embedded = false;
			return;
		}
		try
		{
			RestoreSourceState(showQQ);
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

	private void RestoreSourceState(bool showQQ)
	{
		Native.SetParent(qqWindow, originalParent);
		Native.SetWindowLongPtr(qqWindow, -16, new IntPtr(originalStyle));
		Native.SetWindowLongPtr(qqWindow, -20, new IntPtr(originalExStyle));
		Native.SetWindowPos(qqWindow, IntPtr.Zero, originalRect.Left, originalRect.Top,
			originalRect.Right - originalRect.Left, originalRect.Bottom - originalRect.Top, 96u);
		Native.SetWindowPlacement(qqWindow, ref originalPlacement);
		if (showQQ)
		{
			Native.ShowWindow(qqWindow, 9);
			Native.SetForegroundWindow(qqWindow);
		}
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
		Process[] processesByName = Process.GetProcessesByName(
			"Piaoklxt-MusicWallpaperRenderer");
		foreach (Process process in processesByName)
		{
			try
			{
				if (string.Equals((process.MainModule == null) ? null : process.MainModule.FileName, rendererPath, StringComparison.OrdinalIgnoreCase))
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
		Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION jOBOBJECT_EXTENDED_LIMIT_INFORMATION = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION
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
			Marshal.StructureToPtr((object)jOBOBJECT_EXTENDED_LIMIT_INFORMATION, intPtr2, fDeleteOld: false);
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
		long started = Stopwatch.GetTimestamp();
		Interlocked.Increment(ref mouseHookCallbackCount);
		Interlocked.Increment(ref activeMouseHookCallbacks);
		mouseHookStage = 1;
		try
		{
			if (code == 0 && lParam != IntPtr.Zero)
			{
				long point = PackMousePoint(Marshal.ReadInt32(lParam),
					Marshal.ReadInt32(lParam, sizeof(int)));
				Interlocked.Exchange(ref lastMouseHookPoint, point);
				Interlocked.Exchange(ref hasMouseHookPoint, 1);
			}
			return ProcessMouseHookEvent(code, wParam, lParam);
		}
		finally
		{
			long elapsed = Stopwatch.GetTimestamp() - started;
			Interlocked.Exchange(ref lastMouseHookDurationTicks, elapsed);
			long maximum;
			do
			{
				maximum = Interlocked.Read(ref maxMouseHookDurationTicks);
				if (elapsed <= maximum) break;
			} while (Interlocked.CompareExchange(ref maxMouseHookDurationTicks,
				elapsed, maximum) != maximum);
			if (Interlocked.Decrement(ref activeMouseHookCallbacks) == 0)
				mouseHookStage = 0;
		}
	}

	private static long PackMousePoint(int x, int y)
	{
		return ((long)(uint)x << 32) | (uint)y;
	}

	private IntPtr CallNextMouseHook(int code, IntPtr wParam, IntPtr lParam)
	{
		mouseHookStage = 3;
		return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
	}

	private IntPtr ProcessMouseHookEvent(int code, IntPtr wParam, IntPtr lParam)
	{
		if (code < 0 || !enabled || suspended || !embedded || !Native.IsWindow(qqWindow))
		{
			return CallNextMouseHook(code, wParam, lParam);
		}
		uint num = (uint)wParam.ToInt64();
		if (num == WmLButtonDown || num == WmRButtonDown)
			Interlocked.Increment(ref desktopClickSequence);
		if (num != WmMouseMove && num != WmLButtonDown && num != WmLButtonUp &&
			num != WmRButtonDown && num != WmRButtonUp && num != WmMouseWheel)
		{
			return CallNextMouseHook(code, wParam, lParam);
		}
		Native.MSLLHOOKSTRUCT mSLLHOOKSTRUCT = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
		Point point = new Point(mSLLHOOKSTRUCT.pt.X, mSLLHOOKSTRUCT.pt.Y);
		Rectangle bounds = Screen.PrimaryScreen.Bounds;
		Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
		if (interactionActive && num == WmRButtonDown)
		{
			interactionActive = false;
			progressSeekActive = false;
			QueueHideProgressPreview();
			interactionStarted = DateTime.MinValue;
			return CallNextMouseHook(code, wParam, lParam);
		}
		if (progressSeekActive && (num == WmMouseMove || num == WmLButtonUp))
		{
			ProgressPointerAction progressAction =
				DesktopInputPolicy.ResolveProgressPointerAction(num, true);
			if (progressAction == ProgressPointerAction.UpdatePendingPosition)
			{
				pendingProgressSeekPoint = point;
				progressPreview?.UpdatePointer(point.X);
			}
			if (progressAction == ProgressPointerAction.CommitSeek)
			{
				pendingProgressSeekPoint = point;
				ForwardProgressClick(pendingProgressSeekPoint, workingArea, activeProgressTimeline);
				progressSeekActive = false;
				QueueHideProgressPreview();
				interactionActive = false;
				interactionTarget = IntPtr.Zero;
				interactionStarted = DateTime.MinValue;
				nextAccessibilityRefresh = DateTime.MinValue;
				Log("Progress scrub completed.");
			}
			return DesktopInputPolicy.ShouldConsumeProgressPointerEvent(num)
				? new IntPtr(1)
				: CallNextMouseHook(code, wParam, lParam);
		}
		if (interactionActive && (num == WmMouseMove || num == WmLButtonUp))
		{
			ForwardPointerToSource(point, workingArea, num,
				num == WmMouseMove ? new IntPtr(1) : IntPtr.Zero, interactionTarget);
			if (num == WmLButtonUp)
			{
				interactionActive = false;
				interactionTarget = IntPtr.Zero;
				interactionStarted = DateTime.MinValue;
				nextAccessibilityRefresh = DateTime.MinValue;
			}
			return new IntPtr(1);
		}
		if (!bounds.Contains(point))
		{
			return CallNextMouseHook(code, wParam, lParam);
		}
		if (num == WmLButtonUp || num == WmRButtonUp)
		{
			return CallNextMouseHook(code, wParam, lParam);
		}
		if (point.Y >= workingArea.Bottom)
		{
			if (num == 513)
			{
				taskbarClickUntil = DateTime.UtcNow.AddSeconds(2.0);
			}
			return CallNextMouseHook(code, wParam, lParam);
		}
		IntPtr ancestor = Native.GetAncestor(Native.WindowFromPoint(new Native.POINT
		{
			X = point.X,
			Y = point.Y
		}), 2u);
		bool num2 = IsDesktopSurface(ancestor);
		bool flag = !num2 && IsQQMusicWindow(ancestor);
		if (!num2 && !flag)
		{
			return CallNextMouseHook(code, wParam, lParam);
		}
		if (num == 512)
		{
			if (flag)
			{
				return CallNextMouseHook(code, wParam, lParam);
			}
			DateTime utcNow = DateTime.UtcNow;
			if ((utcNow - lastHoverForwarded).TotalMilliseconds < 25.0)
			{
				return CallNextMouseHook(code, wParam, lParam);
			}
			lastHoverForwarded = utcNow;
			ForwardHoverToSource(point, workingArea);
			return CallNextMouseHook(code, wParam, lParam);
		}
		if (flag)
		{
			if (sourcePresented)
			{
				// Native QQ Music (including its comment surface) stays available while
				// it owns the foreground. MaintainWallpaper parks it again after the user
				// switches away instead of hiding it after a single click.
			}
			return CallNextMouseHook(code, wParam, lParam);
		}
		mouseHookStage = 2;
		IntPtr intPtr2 = FindDesktopListView();
		if (intPtr2 == IntPtr.Zero || IsDesktopRenameActive(intPtr2) ||
			DesktopInputPolicy.ShouldCheckDesktopIcon(num) && IsDesktopIconAt(intPtr2, point))
		{
			return CallNextMouseHook(code, wParam, lParam);
		}
		mouseHookStage = 1;
		switch (num)
		{
		case 513u:
			CommentControlMap clickCommentMap = commentControlMap;
			DesktopSpecialAction specialAction = DesktopInputPolicy.ResolveSpecialAction(
				IsVolumePoint(point), IsPlaybackModePoint(point),
				clickCommentMap != null && clickCommentMap.Bounds.Contains(point),
				IsLyricTranslationPoint(point), IsAccompanimentPoint(point),
				IsPlayerStylePoint(point), IsQualityPoint(point),
				IsSoundEffectsPoint(point), IsPlaylistPoint(point));
			bool hasMediaControl = DesktopInputPolicy.TryGetMediaKey(point,
				mediaControlAreas, progressControlAreas, out byte mediaKey);
			bool hasLyricTimestamp = TryGetLyricTimestamp(point, out int lyricSeconds);
			bool hasProgressControl = IsProgressPoint(point);
			bool hasGenericControl = IsQQInteractionPoint(point);
			DesktopClickRoute clickRoute = DesktopInputPolicy.ResolveDesktopClickRoute(
				specialAction, hasMediaControl, hasLyricTimestamp,
				hasProgressControl, hasGenericControl);
			TraceDesktopClick(clickRoute, specialAction);
			if (clickRoute == DesktopClickRoute.None)
			{
				nextAccessibilityRefresh = DateTime.MinValue;
				if (DesktopInputPolicy.ShouldDismissTransientUi(num, num2,
					isDesktopIcon: false)) DismissTransientQQUi();
				QueueCommentMissProbe(point, workingArea);
				return CallNextMouseHook(code, wParam, lParam);
			}
			if (clickRoute == DesktopClickRoute.LyricTimestamp)
			{
				QueueSeekToSeconds(lyricSeconds, workingArea);
				return new IntPtr(1);
			}
			if (clickRoute == DesktopClickRoute.ProgressControl)
			{
				Rectangle[] timelines = progressControlAreas;
				if (timelines.Length == 0) return CallNextMouseHook(code, wParam, lParam);
				activeProgressTimeline = timelines[0];
				pendingProgressSeekPoint = point;
				progressSeekActive = true;
				interactionActive = true;
				interactionTarget = qqWindow;
				interactionStarted = DateTime.UtcNow;
				QueueShowProgressPreview(activeProgressTimeline, point.X);
				return new IntPtr(1);
			}
			if (clickRoute == DesktopClickRoute.MediaControl)
			{
				QueueMediaButton(mediaKey);
				return new IntPtr(1);
			}
			switch (specialAction)
			{
			case DesktopSpecialAction.ShowVolume:
				QueueShowVolume();
				return new IntPtr(1);
			case DesktopSpecialAction.ShowPlaybackModes:
				QueueShowPlaybackModes();
				return new IntPtr(1);
			case DesktopSpecialAction.ShowNativeComments:
				QueueShowNativeComments(workingArea, clickCommentMap.Snapshot);
				return new IntPtr(1);
			case DesktopSpecialAction.ClickLyricTranslation:
				QueueAccessibleControlAction("Lyric translation", workingArea,
					new[] { "翻译歌词", "Translation lyrics" });
				return new IntPtr(1);
			case DesktopSpecialAction.ClickAccompaniment:
				QueueAccessibleControlAction("Accompaniment", workingArea,
					new[] { "伴唱", "伴奏", "Accompaniment" }, presentPopup: true);
				return new IntPtr(1);
			case DesktopSpecialAction.ClickPlayerStyle:
				QueueAccessibleControlAction("Player style", workingArea,
					new[] { "播放器样式", "Player style" }, presentPopup: true);
				return new IntPtr(1);
			case DesktopSpecialAction.ClickQuality:
				QueueAccessibleControlAction("Audio quality", workingArea,
					new[] { "切换品质", "Change quality" }, presentPopup: true);
				return new IntPtr(1);
			case DesktopSpecialAction.ClickSoundEffects:
				QueueAccessibleControlAction("Sound effects", workingArea,
					new[] { "音效", "Sound effects" }, presentPopup: true);
				return new IntPtr(1);
			case DesktopSpecialAction.ToggleNativePlaylist:
				QueueToggleNativePlaylist(workingArea);
				return new IntPtr(1);
			}
			QueueAccessiblePointAction(point, workingArea);
			return new IntPtr(1);
		case 522u:
		{
			short delta = (short)(mSLLHOOKSTRUCT.mouseData >> 16);
			if (AccessibleSurfaceRouter.TryGetWheelTarget(point,
				qqPopupControlSurfaces, out IntPtr popupWindow))
			{
				ForwardWheelToWindow(popupWindow, point, delta);
				return new IntPtr(1);
			}
			if (IsVolumePoint(point))
			{
				QueueAdjustVolume(delta);
				return new IntPtr(1);
			}
			ForwardWheelToSource(point, workingArea, delta);
			return new IntPtr(1);
		}
		default:
			return CallNextMouseHook(code, wParam, lParam);
		}
	}

	private bool IsQQInteractionPoint(Point point)
	{
		if (AccessibleSurfaceRouter.TryGetSurfaceAtPoint(point,
			qqPopupControlSurfaces, out var ignoredPopup)) return true;
		if (DesktopInputPolicy.IsPointInAny(point, volumeControlAreas) ||
			DesktopInputPolicy.IsPointInAny(point, playbackModeControlAreas) ||
			DesktopInputPolicy.IsPointInAny(point, commentControlAreas) ||
			DesktopInputPolicy.IsPointInAny(point, lyricTranslationControlAreas) ||
			DesktopInputPolicy.IsPointInAny(point, accompanimentControlAreas) ||
			DesktopInputPolicy.IsPointInAny(point, playerStyleControlAreas) ||
			DesktopInputPolicy.IsPointInAny(point, qualityControlAreas) ||
			DesktopInputPolicy.IsPointInAny(point, soundEffectsControlAreas) ||
			DesktopInputPolicy.IsPointInAny(point, playlistControlAreas) ||
			DesktopInputPolicy.IsPointInAny(point, progressControlAreas))
		{
			return true;
		}
		Rectangle[] areas = qqInteractiveAreas;
		for (int i = 0; i < areas.Length; i++)
		{
			if (areas[i].Contains(point))
			{
				return true;
			}
		}
		return false;
	}

	private bool IsVolumePoint(Point point)
	{
		Rectangle[] areas = volumeControlAreas;
		return areas.Length > 0 && areas[0].Contains(point);
	}

	private bool IsPlaybackModePoint(Point point)
	{
		Rectangle[] areas = playbackModeControlAreas;
		return areas.Length > 0 && areas[0].Contains(point);
	}

	private void TraceDesktopClick(DesktopClickRoute route, DesktopSpecialAction action)
	{
		if (!diagnosticMode) return;
		long sequence = Interlocked.Read(ref desktopClickSequence);
		long snapshotTicks = Interlocked.Read(ref lastAccessibilitySnapshotTicks);
		long age = snapshotTicks == 0 ? -1 :
			(DateTime.UtcNow.Ticks - snapshotTicks) / TimeSpan.TicksPerMillisecond;
		ThreadPool.QueueUserWorkItem(delegate
		{
			Log("Desktop click: sequence=" + sequence + "; route=" + route +
				"; action=" + action + "; snapshotAgeMs=" + age);
		});
	}

	private void QueueCommentMissProbe(Point point, Rectangle work)
	{
		if (!Native.GetWindowRect(qqWindow, out var rect) ||
			Interlocked.CompareExchange(ref commentMissProbeRunning, 1, 0) != 0) return;
		long sequence = Interlocked.Read(ref desktopClickSequence);
		long started = DateTime.UtcNow.Ticks;
		IntPtr source = qqWindow;
		Rectangle bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				if (!IsCommentProbeCurrent(sequence, started, source, bounds, point)) return;
				Interlocked.Increment(ref accessibilityCaptureCount);
				AccessibleTreeSnapshot snapshot = QQMusicAccessibility.CaptureSnapshot(source);
				if (!IsCommentProbeCurrent(sequence, started, source, bounds, point)) return;
				if (work.Width <= 0 || work.Height <= 0) return;
				Point sourcePoint = new Point(
					bounds.Left + (point.X - work.Left) * bounds.Width / work.Width,
					bounds.Top + (point.Y - work.Top) * bounds.Height / work.Height);
				bool comment = SemanticClickResolver.TryGetCommentAtPoint(snapshot, bounds,
					sourcePoint, out var target);
				if (diagnosticMode) Log("Comment miss probe: sequence=" + sequence +
					"; semanticComment=" + comment);
				if (comment && IsCommentProbeCurrent(sequence, started, source, bounds, point))
					OpenNativeComments(work, snapshot,
						() => IsCommentProbeCurrent(sequence, started, source, bounds, point));
			}
			catch (Exception error)
			{
				if (diagnosticMode) Log("Comment miss probe failed: " + error.GetType().Name);
			}
			finally { Interlocked.Exchange(ref commentMissProbeRunning, 0); }
		});
	}

	private bool IsCommentProbeCurrent(long sequence, long started, IntPtr source,
		Rectangle bounds, Point clickPoint)
	{
		if (exiting || !enabled || !embedded || interactionActive ||
			!Native.GetWindowRect(qqWindow, out var rect) ||
			!Native.GetCursorPos(out var cursor)) return false;
		if (!SemanticClickResolver.IsRequestCurrent(sequence,
			Interlocked.Read(ref desktopClickSequence), DateTime.UtcNow.Ticks - started,
			source, qqWindow, bounds,
			Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom))) return false;
		if (Math.Abs(cursor.X - clickPoint.X) > 6 || Math.Abs(cursor.Y - clickPoint.Y) > 6)
			return false;
		return IsDesktopSurface(Native.GetAncestor(Native.WindowFromPoint(cursor), 2u));
	}

	private bool IsLyricTranslationPoint(Point point)
	{
		return DesktopInputPolicy.IsPointInAny(point, lyricTranslationControlAreas);
	}

	private bool IsAccompanimentPoint(Point point)
	{
		return DesktopInputPolicy.IsPointInAny(point, accompanimentControlAreas);
	}

	private bool IsPlayerStylePoint(Point point)
	{
		return DesktopInputPolicy.IsPointInAny(point, playerStyleControlAreas);
	}

	private bool IsQualityPoint(Point point)
	{
		return DesktopInputPolicy.IsPointInAny(point, qualityControlAreas);
	}

	private bool IsSoundEffectsPoint(Point point)
	{
		return DesktopInputPolicy.IsPointInAny(point, soundEffectsControlAreas);
	}

	private bool IsPlaylistPoint(Point point)
	{
		return DesktopInputPolicy.IsPointInAny(point, playlistControlAreas);
	}

	private bool IsProgressPoint(Point point)
	{
		return DesktopInputPolicy.IsPointInAny(point, progressControlAreas);
	}

	private bool TryGetLyricTimestamp(Point point, out int seconds)
	{
		TimestampControlArea[] areas = lyricTimestampAreas;
		for (int index = 0; index < areas.Length; index++)
		{
			if (!areas[index].Bounds.Contains(point)) continue;
			seconds = areas[index].Seconds;
			return true;
		}
		seconds = 0;
		return false;
	}

	private void QueueAccessibleControlAction(string controlName, Rectangle work,
		string[] descriptions, bool presentPopup = false)
	{
		try
		{
			window.BeginInvoke((Action)delegate
			{
				ResetPointerInteraction();
				HashSet<IntPtr> visibleBefore = presentPopup
					? GetVisibleQQTopLevelWindows() : null;
				bool invoked = TryInvokeQQControl(descriptions);
				if (!presentPopup) nextAccessibilityRefresh = DateTime.MinValue;
				if (invoked && presentPopup)
				{
					activeTransientToggleDescriptions = descriptions;
					activeTransientPopupPersistent =
						string.Equals(controlName, "Accompaniment",
							StringComparison.Ordinal);
				}
				Log(controlName + " invoked through its MSAA control; applied=" + invoked);
				if (invoked && presentPopup)
					QueuePresentNewQQPopup(visibleBefore, work, controlName);
			});
		}
		catch { }
	}

	private void QueueAccessiblePointAction(Point point, Rectangle work)
	{
		long sequence = Interlocked.Read(ref desktopClickSequence);
		IntPtr source = qqWindow;
		try
		{
			window.BeginInvoke((Action)delegate
			{
				AccessibleControlSurface[] popupSurfaces = qqPopupControlSurfaces;
				if (AccessibleSurfaceRouter.TryGetControlTargetAtPoint(point,
					popupSurfaces, out IntPtr popupWindow,
					out AccessibleNodeSnapshot popupTarget))
				{
					bool popupInvoked = PostControlClick(popupWindow,
						GetControlCenter(popupTarget));
					if (!activeTransientPopupPersistent)
						activeTransientToggleDescriptions = new string[0];
					nextAccessibilityRefresh = DateTime.MinValue;
					Log("Popup control invoked through its own MSAA surface; applied=" +
						popupInvoked);
					return;
				}
				if (AccessibleSurfaceRouter.TryGetSurfaceAtPoint(point, popupSurfaces,
					out AccessibleControlSurface popupSurface))
				{
					bool popupInvoked = PostControlClick(popupSurface.Window, point);
					nextAccessibilityRefresh = DateTime.MinValue;
					Log("Popup surface received a direct control message; applied=" +
						popupInvoked);
					return;
				}
				if (!TryMapToSource(point, work, out var ignoredTarget,
					out var sourcePoint)) return;
				AccessibleTreeSnapshot snapshot = qqAccessibilitySnapshot;
				if (Native.GetWindowRect(qqWindow, out var currentRect) &&
					SemanticClickResolver.TryGetCommentAtPoint(snapshot,
						Rectangle.FromLTRB(currentRect.Left, currentRect.Top, currentRect.Right, currentRect.Bottom),
						new Point(sourcePoint.X, sourcePoint.Y), out var commentTarget))
				{
					OpenNativeComments(work, snapshot,
						() => IsCommentActionCurrent(sequence, source));
					return;
				}
				bool found = snapshot.TryGetControlTargetAtPoint(
					new Point(sourcePoint.X, sourcePoint.Y),
					out AccessibleNodeSnapshot target);
				bool presentsPopup = found && IsMoreControl(target);
				HashSet<IntPtr> visibleBefore = presentsPopup
					? GetVisibleQQTopLevelWindows() : null;
				bool invoked = found && PostQQControlClick(GetControlCenter(target));
				if (invoked && presentsPopup)
				{
					activeTransientToggleDescriptions = new[] { "更多", "More" };
					activeTransientPopupPersistent = true;
					QueuePresentNewQQPopup(visibleBefore, work, "More");
				}
				if (!presentsPopup) nextAccessibilityRefresh = DateTime.MinValue;
				Log("MSAA point control invoked; applied=" + invoked);
			});
		}
		catch { }
	}

	private void QueueToggleNativePlaylist(Rectangle work)
	{
		ResetPointerInteraction();
		ThreadPool.QueueUserWorkItem(delegate
		{
			IntPtr comments = FindQQMusicCommentWindow();
			if (comments != IntPtr.Zero && Native.IsWindowVisible(comments))
			{
				Native.PostMessage(comments, WmClose, IntPtr.Zero, IntPtr.Zero);
				if (!WaitForPopupToHide(comments, 300))
					Native.ShowWindow(comments, 0);
			}
			HashSet<IntPtr> visibleBefore = GetVisibleQQTopLevelWindows();
			bool invoked = TryInvokeQQControl("播放队列", "播放列表", "Play queue",
				"Playlist");
			if (invoked)
			{
				activeTransientToggleDescriptions = new[]
				{
					"播放队列", "播放列表", "Play queue", "Playlist"
				};
				activeTransientPopupPersistent = true;
			}
			Log("Native playlist invoked through its MSAA control; applied=" + invoked);
			if (invoked) QueuePresentNewQQPopup(visibleBefore, work, "Native playlist");
		});
	}

	private void QueueShowNativeComments(Rectangle work, AccessibleTreeSnapshot snapshot)
	{
		long sequence = Interlocked.Read(ref desktopClickSequence);
		IntPtr source = qqWindow;
		ResetPointerInteraction();
		ThreadPool.QueueUserWorkItem(delegate
		{
			if (!IsCommentActionCurrent(sequence, source)) return;
			IntPtr playlist = FindQQMusicPopupWindow("播放队列", "Play queue");
			if (playlist != IntPtr.Zero && Native.IsWindowVisible(playlist))
			{
				TryInvokeQQControl("播放队列", "播放列表", "Play queue", "Playlist");
				if (!WaitForPopupToHide(playlist, 400))
				{
					Native.PostMessage(playlist, WmClose, IntPtr.Zero, IntPtr.Zero);
					WaitForPopupToHide(playlist, 200);
				}
				Log("Native playlist closed before opening comments.");
			}
			OpenNativeComments(work, snapshot,
				() => IsCommentActionCurrent(sequence, source));
		});
	}

	private bool IsCommentActionCurrent(long sequence, IntPtr source)
	{
		return !exiting && enabled && embedded && source == qqWindow &&
			sequence == Interlocked.Read(ref desktopClickSequence);
	}

	private void OpenNativeComments(Rectangle work, AccessibleTreeSnapshot snapshot,
		Func<bool> requestCurrent = null)
	{
		if (requestCurrent != null && !requestCurrent()) return;
		IntPtr existingComments = FindQQMusicCommentWindow();
		if (existingComments != IntPtr.Zero &&
			TryPositionCommentWindow(existingComments, work))
		{
			Log("Existing native comment window restored to the working area.");
			return;
		}
		bool invoked = TryInvokeQQControlSnapshot(snapshot, "评论", "Comments");
		Log("Native comments invoked through its MSAA control; applied=" + invoked);
		if (invoked) QueuePresentCommentWindow(work, snapshot, requestCurrent);
	}

	private void QueuePresentCommentWindow(Rectangle work, AccessibleTreeSnapshot snapshot,
		Func<bool> requestCurrent)
	{
		ThreadPool.QueueUserWorkItem(delegate
		{
			if (TryPresentCommentWindow(work, 12, requestCurrent)) return;
			if (requestCurrent != null && !requestCurrent()) return;
			// QQ keeps hidden popup state internally. A second control action repairs
			// that state after a prior forced hide or playlist transition.
			TryInvokeQQControlSnapshot(snapshot, "评论", "Comments");
			Log("Native comment state required one synchronized retry.");
			if (TryPresentCommentWindow(work, 20, requestCurrent)) return;
			Log("Native comment window did not become visible after background click.");
		});
	}

	private bool TryPresentCommentWindow(Rectangle work, int attempts, Func<bool> requestCurrent)
	{
		for (int attempt = 0; attempt < attempts; attempt++)
		{
			if (requestCurrent != null && !requestCurrent()) return false;
			IntPtr comments = FindQQMusicCommentWindow();
			if (comments != IntPtr.Zero && Native.IsWindowVisible(comments) &&
				TryPositionCommentWindow(comments, work))
			{
				Log("Native comment window positioned in the working area; hwnd=" +
					comments.ToInt64());
				return true;
			}
			Thread.Sleep(50);
		}
		return false;
	}

	private bool TryInvokeQQControl(params string[] descriptions)
	{
		return TryInvokeQQControlSnapshot(qqAccessibilitySnapshot, descriptions);
	}

	private bool TryInvokeQQControlSnapshot(AccessibleTreeSnapshot snapshot, params string[] descriptions)
	{
		if (!Native.GetWindowRect(qqWindow, out var rect)) return false;
		Rectangle sourceBounds = Rectangle.FromLTRB(rect.Left, rect.Top,
			rect.Right, rect.Bottom);
		if (!TryGetQQControlTarget(snapshot, sourceBounds,
			out AccessibleNodeSnapshot target, descriptions)) return false;
		Point actionPoint = GetControlCenter(target);
		if (!sourceBounds.Contains(actionPoint))
		{
			actionPoint.Offset(bottomControlCorrectionX, bottomControlCorrectionY);
			if (!sourceBounds.Contains(actionPoint)) return false;
		}
		return PostQQControlClick(actionPoint);
	}

	private static bool TryGetQQControlTarget(AccessibleTreeSnapshot snapshot,
		Rectangle sourceBounds, out AccessibleNodeSnapshot target,
		params string[] descriptions)
	{
		// Use the same active launcher for hit testing and actual invocation.
		// QQ Music can retain a deeper, stale comment node in an older player bar.
		foreach (string description in descriptions)
		{
			if (description == "评论" || string.Equals(description, "Comments",
				StringComparison.OrdinalIgnoreCase))
				return snapshot.TryGetBottommostControlTarget(sourceBounds,
					out target, descriptions);
		}
		return snapshot.TryGetControlTarget(sourceBounds, out target, descriptions);
	}

	private static Point GetControlCenter(AccessibleNodeSnapshot target)
	{
		return new Point(target.Bounds.Left + target.Bounds.Width / 2,
			target.Bounds.Top + target.Bounds.Height / 2);
	}

	private static bool IsMoreControl(AccessibleNodeSnapshot target)
	{
		return string.Equals(target.Name, "更多", StringComparison.Ordinal) ||
			string.Equals(target.Description, "更多", StringComparison.Ordinal) ||
			string.Equals(target.Name, "More", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(target.Description, "More", StringComparison.OrdinalIgnoreCase);
	}

	private bool PostQQControlClick(Point sourcePoint)
	{
		return PostControlClick(qqWindow, sourcePoint);
	}

	private bool PostControlClick(IntPtr controlWindow, Point screenPoint)
	{
		if (!Native.IsWindow(controlWindow)) return false;
		Native.POINT clientPoint = new Native.POINT
		{
			X = screenPoint.X,
			Y = screenPoint.Y
		};
		controlWindow = FindWindowInputTarget(controlWindow, clientPoint);
		if (!Native.ScreenToClient(controlWindow, ref clientPoint)) return false;
		IntPtr lParam = MakeLParam(clientPoint.X, clientPoint.Y);
		uint[] messages = DesktopInputPolicy.GetControlActivationMessages();
		for (int index = 0; index < messages.Length; index++)
		{
			uint message = messages[index];
			IntPtr keyState = message == WmLButtonDown ? new IntPtr(1) : IntPtr.Zero;
			if (!Native.PostMessage(controlWindow, message, keyState, lParam)) return false;
			if (message == WmLButtonDown)
				Thread.Sleep(DesktopInputPolicy.ControlActivationHoldMilliseconds);
		}
		return true;
	}

	private void ResetPointerInteraction()
	{
		interactionActive = false;
		progressSeekActive = false;
		QueueHideProgressPreview();
		interactionTarget = IntPtr.Zero;
		interactionStarted = DateTime.MinValue;
	}

	private static bool WaitForPopupToHide(IntPtr popup, int milliseconds)
	{
		int attempts = Math.Max(1, milliseconds / 20);
		for (int attempt = 0; attempt < attempts; attempt++)
		{
			if (!Native.IsWindow(popup) || !Native.IsWindowVisible(popup)) return true;
			Thread.Sleep(20);
		}
		return !Native.IsWindow(popup) || !Native.IsWindowVisible(popup);
	}

	private bool TryPositionCommentWindow(IntPtr comments, Rectangle work)
	{
		if (!Native.GetWindowRect(comments, out var rect)) return false;
		Rectangle current = new Rectangle(rect.Left, rect.Top,
			rect.Right - rect.Left, rect.Bottom - rect.Top);
		Rectangle target = DesktopInputPolicy.GetVisibleCommentBounds(current, work);
		Native.ShowWindow(comments, SwShowNoActivate);
		bool positioned = Native.SetWindowPos(comments, new IntPtr(-1), target.Left,
			target.Top, target.Width, target.Height, SwpShowWindow | SwpNoActivate);
		if (positioned) PublishQQPopupSurface(comments, work);
		return positioned;
	}

	private IntPtr FindQQMusicCommentWindow()
	{
		return FindQQMusicPopupWindow("评论", "Comments");
	}

	private IntPtr FindQQMusicPopupWindow(params string[] titles)
	{
		IntPtr result = IntPtr.Zero;
		Native.GetWindowThreadProcessId(qqWindow, out var sourceProcessId);
		Native.EnumWindows(delegate(IntPtr candidate, IntPtr data)
		{
			if (candidate == qqWindow) return true;
			Native.GetWindowThreadProcessId(candidate, out var processId);
			if (processId != sourceProcessId) return true;
			StringBuilder title = new StringBuilder(64);
			Native.GetWindowText(candidate, title, title.Capacity);
			string value = title.ToString();
			for (int index = 0; index < titles.Length; index++)
			{
				if (string.Equals(value, titles[index], StringComparison.OrdinalIgnoreCase))
				{
					result = candidate;
					return false;
				}
			}
			return true;
		}, IntPtr.Zero);
		return result;
	}

	private void HideNativeCommentWindow()
	{
		IntPtr comments = FindQQMusicCommentWindow();
		if (comments == IntPtr.Zero || !Native.IsWindowVisible(comments)) return;
		Native.ShowWindow(comments, 0);
		PruneQQPopupSurfaces();
		Log("Native comment window dismissed by a blank desktop click.");
	}

	private void DismissTransientQQUi()
	{
		HideNativeCommentWindow();
		string[] toggleDescriptions = activeTransientToggleDescriptions;
		activeTransientToggleDescriptions = new string[0];
		activeTransientPopupPersistent = false;
		if (toggleDescriptions.Length > 0)
		{
			ThreadPool.QueueUserWorkItem(delegate
			{
				bool toggled = TryInvokeQQControl(toggleDescriptions);
				Log("Transient QQ control toggled closed; applied=" + toggled);
				nextAccessibilityRefresh = DateTime.MinValue;
			});
		}
		PostTransientDismissal(qqWindow,
			DesktopInputPolicy.ResolveTransientDismissalAction(isMainSurface: true));
		Native.EnumChildWindows(qqWindow, delegate(IntPtr child, IntPtr data)
		{
			PostTransientDismissal(child,
				DesktopInputPolicy.ResolveTransientDismissalAction(isMainSurface: true));
			return true;
		}, IntPtr.Zero);
		Native.GetWindowThreadProcessId(qqWindow, out var sourceProcessId);
		Native.EnumWindows(delegate(IntPtr candidate, IntPtr data)
		{
			if (candidate == qqWindow || !Native.IsWindowVisible(candidate)) return true;
			Native.GetWindowThreadProcessId(candidate, out var processId);
			if (processId == sourceProcessId)
			{
				PostTransientDismissal(candidate,
					DesktopInputPolicy.ResolveTransientDismissalAction(
						isMainSurface: false));
			}
			return true;
		}, IntPtr.Zero);
		AccessibleControlSurface[] popupSurfaces = qqPopupControlSurfaces;
		for (int index = 0; index < popupSurfaces.Length; index++)
		{
			PostTransientDismissal(popupSurfaces[index].Window,
				DesktopInputPolicy.ResolveTransientDismissalAction(
					isMainSurface: false));
		}
		try
		{
			window.BeginInvoke((Action)delegate
			{
				volumeOverlay.Hide();
				playbackModeOverlay.Hide();
			});
		}
		catch { }
		Log("Transient QQ UI dismissed by window/control messages.");
	}

	private static void PostTransientDismissal(IntPtr target,
		TransientDismissalAction action)
	{
		if (target == IntPtr.Zero || !Native.IsWindow(target)) return;
		Native.PostMessage(target, WmCancelMode, IntPtr.Zero, IntPtr.Zero);
		if (action != TransientDismissalAction.CancelAndEscape) return;
		Native.PostMessage(target, WmKeyDown, new IntPtr(VkEscape), IntPtr.Zero);
		Native.PostMessage(target, WmKeyUp, new IntPtr(VkEscape), IntPtr.Zero);
	}

	private HashSet<IntPtr> GetVisibleQQTopLevelWindows()
	{
		HashSet<IntPtr> result = new HashSet<IntPtr>();
		Native.GetWindowThreadProcessId(qqWindow, out var sourceProcessId);
		Native.EnumWindows(delegate(IntPtr candidate, IntPtr data)
		{
			Native.GetWindowThreadProcessId(candidate, out var processId);
			if (processId == sourceProcessId && Native.IsWindowVisible(candidate))
				result.Add(candidate);
			return true;
		}, IntPtr.Zero);
		return result;
	}

	private void QueuePresentNewQQPopup(HashSet<IntPtr> visibleBefore,
		Rectangle work, string controlName)
	{
		ThreadPool.QueueUserWorkItem(delegate
		{
			for (int attempt = 0; attempt < 20; attempt++)
			{
				HashSet<IntPtr> current = GetVisibleQQTopLevelWindows();
				foreach (IntPtr candidate in current)
				{
					if (candidate == qqWindow || visibleBefore.Contains(candidate)) continue;
					if (!TryPositionQQPopup(candidate, work)) continue;
					PublishQQPopupSurface(candidate, work);
					Log(controlName + " popup positioned in the working area; hwnd=" +
						candidate.ToInt64());
					return;
				}
				Thread.Sleep(50);
			}
		});
	}

	private bool TryPositionQQPopup(IntPtr popup, Rectangle work)
	{
		if (!Native.GetWindowRect(popup, out var popupRect) ||
			!Native.GetWindowRect(qqWindow, out var sourceRect)) return false;
		int width = popupRect.Right - popupRect.Left;
		int height = popupRect.Bottom - popupRect.Top;
		if (width < 2 || height < 2) return false;
		Rectangle current = Rectangle.FromLTRB(popupRect.Left, popupRect.Top,
			popupRect.Right, popupRect.Bottom);
		Rectangle source = Rectangle.FromLTRB(sourceRect.Left, sourceRect.Top,
			sourceRect.Right, sourceRect.Bottom);
		Rectangle target = DesktopInputPolicy.GetVisiblePopupBounds(current, source, work);
		Native.ShowWindow(popup, SwShowNoActivate);
		return Native.SetWindowPos(popup, new IntPtr(-1), target.Left, target.Top,
			target.Width, target.Height, SwpShowWindow | SwpNoActivate);
	}

	private void PublishQQPopupSurface(IntPtr popup, Rectangle work)
	{
		if (!Native.IsWindow(popup) ||
			!Native.GetWindowRect(popup, out var popupRect)) return;
		Rectangle bounds = Rectangle.Intersect(work, Rectangle.FromLTRB(
			popupRect.Left, popupRect.Top, popupRect.Right, popupRect.Bottom));
		if (bounds.Width < 2 || bounds.Height < 2) return;
		// The top-level bounds are enough to give the popup ownership of pointer
		// input. Reading the popup's complete MSAA tree here synchronously blocks
		// QQ Music's Chromium UI thread and makes newly opened panels feel stuck.
		AccessibleControlSurface surface = new AccessibleControlSurface(popup,
			bounds, new AccessibleTreeSnapshot(new AccessibleNodeSnapshot[0]));
		List<AccessibleControlSurface> updated =
			new List<AccessibleControlSurface> { surface };
		AccessibleControlSurface[] current = qqPopupControlSurfaces;
		for (int index = 0; index < current.Length; index++)
		{
			if (current[index].Window == popup ||
				!Native.IsWindowVisible(current[index].Window)) continue;
			updated.Add(current[index]);
		}
		qqPopupControlSurfaces = updated.ToArray();
	}

	private void PruneQQPopupSurfaces()
	{
		AccessibleControlSurface[] current = qqPopupControlSurfaces;
		if (current.Length == 0) return;
		List<AccessibleControlSurface> visible =
			new List<AccessibleControlSurface>(current.Length);
		for (int index = 0; index < current.Length; index++)
		{
			AccessibleControlSurface surface = current[index];
			if (Native.IsWindow(surface.Window) &&
				Native.IsWindowVisible(surface.Window)) visible.Add(surface);
		}
		if (visible.Count == current.Length) return;
		qqPopupControlSurfaces = visible.ToArray();
		if (visible.Count == 0)
		{
			activeTransientToggleDescriptions = new string[0];
			activeTransientPopupPersistent = false;
			nextAccessibilityRefresh = DateTime.MinValue;
		}
	}

	private void QueueSeekToSeconds(int seconds, Rectangle work)
	{
		try
		{
			window.BeginInvoke((Action)delegate
			{
				Rectangle[] progress = progressControlAreas;
				int total = playbackTotalSeconds;
				if (progress.Length == 0 || total <= 0) return;
				int clamped = Math.Max(0, Math.Min(seconds, total));
				Rectangle timeline = progress[0];
				int x = timeline.Left + (int)Math.Round(
					(double)clamped / total * Math.Max(1, timeline.Width - 1));
				ForwardProgressClick(new Point(x, timeline.Top + timeline.Height / 2),
					work);
				Log("Lyric timestamp seek: " + clamped + "/" + total + " seconds.");
			});
		}
		catch { }
	}

	private void QueueShowVolume()
	{
		Rectangle[] areas = volumeControlAreas;
		Rectangle anchor = areas.Length == 0 ? Rectangle.Empty : areas[0];
		try
		{
			window.BeginInvoke((Action)delegate
			{
				Native.GetWindowThreadProcessId(qqWindow, out var processId);
				float level;
				if (processId != 0 && AudioSessionVolume.TryGet(processId, out level))
				{
					volumeOverlay.ShowLevel(anchor, level);
				}
			});
		}
		catch { }
	}

	private void QueueAdjustVolume(short wheelDelta)
	{
		Rectangle[] areas = volumeControlAreas;
		Rectangle anchor = areas.Length == 0 ? Rectangle.Empty : areas[0];
		float delta = wheelDelta / 120f * 0.05f;
		try
		{
			window.BeginInvoke((Action)delegate
			{
				Native.GetWindowThreadProcessId(qqWindow, out var processId);
				float applied;
				if (processId != 0 && AudioSessionVolume.TryAdjust(processId, delta, out applied))
				{
					volumeOverlay.ShowLevel(anchor, applied);
				}
			});
		}
		catch { }
	}

	private void QueueShowPlaybackModes()
	{
		Rectangle[] areas = playbackModeControlAreas;
		Rectangle anchor = areas.Length == 0 ? Rectangle.Empty : areas[0];
		try
		{
			window.BeginInvoke((Action)delegate
			{
				playbackModeOverlay.ShowModes(anchor, currentPlaybackMode);
			});
		}
		catch { }
	}

	private void QueueSetPlaybackMode(string requestedMode)
	{
		if (string.IsNullOrWhiteSpace(requestedMode)) return;
		ThreadPool.QueueUserWorkItem(delegate
		{
			bool applied = false;
			try
			{
				applied = TrySetPlaybackModeThroughCommand(requestedMode);
				if (applied)
				{
					currentPlaybackMode = requestedMode;
					nextAccessibilityRefresh = DateTime.MinValue;
				}
				Log("Playback mode request: " + requestedMode + "; applied=" + applied);
			}
			catch (Exception ex)
			{
				Log("Playback mode request failed: " + ex.Message);
			}
		});
	}

	private bool TrySetPlaybackModeThroughCommand(string requestedMode)
	{
		int modeValue;
		switch (requestedMode)
		{
		case "单曲循环": modeValue = 1; break;
		case "顺序播放": modeValue = 2; break;
		case "列表循环": modeValue = 3; break;
		case "随机播放": modeValue = 4; break;
		default: return false;
		}
		if (!QQMusicLocator.IsQQMusic(qqPath)) return false;

		try
		{
			using Process command = Process.Start(new ProcessStartInfo(qqPath,
				"/playmode " + modeValue.ToString(
					System.Globalization.CultureInfo.InvariantCulture))
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
				WorkingDirectory = Path.GetDirectoryName(qqPath)
			});
			return command != null;
		}
		catch (Exception ex)
		{
			Log("QQ Music /playmode command failed: " + ex.Message);
			return false;
		}
	}

	private static readonly Rectangle AllAccessibleSpace =
		new Rectangle(-32768, -32768, 65536, 65536);

	private void RefreshInteractiveAreasIfDue()
	{
		if (!DesktopInputPolicy.ShouldRefreshAccessibility(
			qqPopupControlSurfaces.Length > 0)) return;
		if (DateTime.UtcNow < nextAccessibilityRefresh ||
			Interlocked.CompareExchange(ref accessibilityRefreshRunning, 1, 0) != 0)
		{
			return;
		}
		nextAccessibilityRefresh = DateTime.UtcNow.AddMilliseconds(
			DesktopInputPolicy.AccessibilityRefreshIntervalMilliseconds);
		IntPtr source = qqWindow;
		Rectangle visibleArea = Screen.PrimaryScreen.WorkingArea;
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				if (!Native.GetWindowRect(source, out var scanStart)) return;
				Interlocked.Increment(ref accessibilityCaptureCount);
				AccessibleTreeSnapshot snapshot =
					QQMusicAccessibility.CaptureSnapshot(source);
				Rectangle[] areas = snapshot.GetInteractiveBounds(AllAccessibleSpace,
					includeHidden: true);
				if (source == qqWindow)
				{
					if (Native.GetWindowRect(source, out var sourceRect))
					{
						if (scanStart.Left != sourceRect.Left || scanStart.Top != sourceRect.Top ||
							scanStart.Right != sourceRect.Right || scanStart.Bottom != sourceRect.Bottom)
						{
							nextAccessibilityRefresh = DateTime.MinValue;
							return;
						}
						Rectangle sourceBounds = Rectangle.FromLTRB(sourceRect.Left,
							sourceRect.Top, sourceRect.Right, sourceRect.Bottom);
						for (int i = 0; i < areas.Length; i++)
						{
							areas[i].Offset(visibleArea.Left - sourceRect.Left,
								visibleArea.Top - sourceRect.Top);
						}
						List<Rectangle> combined = new List<Rectangle>(areas);
						combined.AddRange(GetQQAccessoryAreas(source, sourceRect, visibleArea));
						areas = combined.ToArray();
						Rectangle volumeBounds;
						if (snapshot.TryFindVisibleControl("调节音量",
							AllAccessibleSpace, out volumeBounds) ||
							snapshot.TryFindVisibleControl("Adjust volume",
							AllAccessibleSpace, out volumeBounds))
						{
							volumeBounds.Offset(visibleArea.Left - sourceRect.Left,
								visibleArea.Top - sourceRect.Top);
							volumeControlAreas = new[] { volumeBounds };
						}
						string[] playbackModes =
						{
							"随机播放", "顺序播放", "单曲循环", "列表循环",
							"雷达播放", "Radar play", "Shuffle", "Sequential play",
							"Single loop", "List loop"
						};
						if (snapshot.TryGetUnambiguousControlTarget(sourceBounds,
							out AccessibleNodeSnapshot playbackTarget, playbackModes))
						{
							currentPlaybackMode = string.IsNullOrWhiteSpace(
								playbackTarget.Description) ? playbackTarget.Name :
								playbackTarget.Description;
							Rectangle playbackBounds = playbackTarget.Bounds;
							playbackBounds.Offset(visibleArea.Left - sourceRect.Left,
								visibleArea.Top - sourceRect.Top);
							playbackModeControlAreas = new[] { playbackBounds };
						}
						else playbackModeControlAreas = new Rectangle[0];
						if (TryGetQQControlTarget(snapshot, sourceBounds,
							out AccessibleNodeSnapshot commentTarget,
							"评论", "Comments"))
						{
							Rectangle commentBounds = commentTarget.Bounds;
							commentBounds.Offset(visibleArea.Left - sourceRect.Left,
								visibleArea.Top - sourceRect.Top);
							commentControlAreas = new[] { commentBounds };
							commentControlMap = new CommentControlMap(commentBounds, snapshot);
						}
						else
						{
							commentControlAreas = new Rectangle[0];
							commentControlMap = null;
						}
						Rectangle[] qualityCandidates = GetMappedControlCandidates(snapshot,
							sourceRect, visibleArea, includeHidden: true, "切换品质",
							"Change quality");
						Point inferredBottomCorrection = Point.Empty;
						DesktopInputPolicy.TryInferOffscreenControlCorrection(
							qualityCandidates, visibleArea, out inferredBottomCorrection);
						bottomControlCorrectionX = inferredBottomCorrection.X;
						bottomControlCorrectionY = inferredBottomCorrection.Y;
						lyricTranslationControlAreas = GetUsableControlAreas(
							GetMappedControlCandidates(snapshot, sourceRect, visibleArea,
								includeHidden: true, "翻译歌词", "Translation lyrics"),
							visibleArea, Point.Empty);
						accompanimentControlAreas = GetUsableControlAreas(
							GetMappedControlCandidates(snapshot, sourceRect, visibleArea,
								includeHidden: true, "伴唱", "伴奏", "Accompaniment"),
							visibleArea, Point.Empty);
						playerStyleControlAreas = GetUsableControlAreas(
							GetMappedControlCandidates(snapshot, sourceRect, visibleArea,
								includeHidden: true, "播放器样式", "Player style"),
							visibleArea, inferredBottomCorrection);
						qualityControlAreas = GetUsableControlAreas(qualityCandidates,
							visibleArea, inferredBottomCorrection);
						soundEffectsControlAreas = GetUsableControlAreas(
							GetMappedControlCandidates(snapshot, sourceRect, visibleArea,
								includeHidden: true, "音效", "Sound effects"),
							visibleArea, inferredBottomCorrection);
						playlistControlAreas = GetUsableControlAreas(
							GetMappedControlCandidates(snapshot, sourceRect, visibleArea,
								includeHidden: true, "播放队列", "播放列表", "Play queue",
								"Playlist"), visibleArea, inferredBottomCorrection);

						List<MediaControlArea> mediaAreas = new List<MediaControlArea>();
						AddMediaControlAreas(mediaAreas, snapshot, sourceRect, visibleArea,
							179, "播放", "暂停", "Play", "Pause");
						AddMediaControlAreas(mediaAreas, snapshot, sourceRect, visibleArea,
							177, "上一首", "上一曲", "Previous", "Previous track");
						AddMediaControlAreas(mediaAreas, snapshot, sourceRect, visibleArea,
							176, "下一首", "下一曲", "Next", "Next track");
						mediaControlAreas = mediaAreas.ToArray();

						AccessibleTimeLabel[] timeLabels = snapshot.GetTimeLabels(
							sourceBounds, includeHidden: true);
						Rectangle bestTimeline = Rectangle.Empty;
						int bestCurrentIndex = -1;
						int bestTotalIndex = -1;
						for (int currentIndex = 0; currentIndex < timeLabels.Length;
							currentIndex++)
						{
							for (int totalIndex = 0; totalIndex < timeLabels.Length;
								totalIndex++)
							{
								if (currentIndex == totalIndex ||
									timeLabels[currentIndex].Bounds.Left >=
										timeLabels[totalIndex].Bounds.Left) continue;
								if (DesktopInputPolicy.TryBuildTimeline(
									timeLabels[currentIndex].Bounds,
									timeLabels[currentIndex].Seconds,
									timeLabels[totalIndex].Bounds,
									timeLabels[totalIndex].Seconds,
									sourceRect.Bottom, out Rectangle timeline) &&
									timeline.Width > bestTimeline.Width)
								{
									bestTimeline = timeline;
									bestCurrentIndex = currentIndex;
									bestTotalIndex = totalIndex;
								}
							}
						}

						if (bestTotalIndex >= 0)
						{
							bestTimeline.Offset(visibleArea.Left - sourceRect.Left,
								visibleArea.Top - sourceRect.Top);
							bestTimeline = Rectangle.Intersect(visibleArea, bestTimeline);
							progressControlAreas = bestTimeline.IsEmpty
								? new Rectangle[0] : new[] { bestTimeline };
							playbackTotalSeconds = timeLabels[bestTotalIndex].Seconds;
						}
						else
						{
							progressControlAreas = new Rectangle[0];
							playbackTotalSeconds = 0;
						}

						List<TimestampControlArea> lyricAreas =
							new List<TimestampControlArea>();
						int sourceWidth = sourceRect.Right - sourceRect.Left;
						for (int labelIndex = 0; labelIndex < timeLabels.Length;
							labelIndex++)
						{
							if (labelIndex == bestCurrentIndex || labelIndex == bestTotalIndex ||
								timeLabels[labelIndex].Bounds.Top >= sourceRect.Bottom - 120 ||
								timeLabels[labelIndex].Bounds.Left >= sourceRect.Left +
									sourceWidth / 2 || playbackTotalSeconds <= 0 ||
								timeLabels[labelIndex].Seconds > playbackTotalSeconds) continue;
							Rectangle mapped = timeLabels[labelIndex].Bounds;
							mapped.Offset(visibleArea.Left - sourceRect.Left,
								visibleArea.Top - sourceRect.Top);
							mapped = Rectangle.FromLTRB(mapped.Left - 32, mapped.Top - 6,
								mapped.Right + 8, mapped.Bottom + 6);
							mapped = Rectangle.Intersect(visibleArea, mapped);
							if (!mapped.IsEmpty)
							{
								lyricAreas.Add(new TimestampControlArea(mapped,
									timeLabels[labelIndex].Seconds));
							}
						}
						lyricTimestampAreas = lyricAreas.ToArray();
					}
					qqAccessibilitySnapshot = snapshot;
					Interlocked.Exchange(ref lastAccessibilitySnapshotTicks, DateTime.UtcNow.Ticks);
					qqInteractiveAreas = areas;
				}
			}
			finally
			{
				Interlocked.Exchange(ref accessibilityRefreshRunning, 0);
			}
		});
	}

	private static Rectangle[] GetMappedControlCandidates(AccessibleTreeSnapshot snapshot,
		Native.RECT sourceRect, Rectangle visibleArea, bool includeHidden,
		params string[] descriptions)
	{
		List<Rectangle> mappedAreas = new List<Rectangle>();
		for (int descriptionIndex = 0; descriptionIndex < descriptions.Length;
			descriptionIndex++)
		{
			Rectangle[] controls = snapshot.GetControlBounds(
				descriptions[descriptionIndex], AllAccessibleSpace, includeHidden);
			for (int controlIndex = 0; controlIndex < controls.Length; controlIndex++)
			{
				Rectangle mapped = controls[controlIndex];
				mapped.Offset(visibleArea.Left - sourceRect.Left,
					visibleArea.Top - sourceRect.Top);
				if (mapped.Width <= 1 || mapped.Height <= 1 || mappedAreas.Contains(mapped))
					continue;
				mappedAreas.Add(mapped);
			}
		}
		return mappedAreas.ToArray();
	}

	private static Rectangle[] GetUsableControlAreas(Rectangle[] candidates,
		Rectangle visibleArea, Point offscreenCorrection)
	{
		List<Rectangle> result = new List<Rectangle>();
		for (int index = 0; index < candidates.Length; index++)
		{
			Rectangle mapped = candidates[index];
			if (!visibleArea.Contains(mapped.Left + mapped.Width / 2,
				mapped.Top + mapped.Height / 2) && offscreenCorrection != Point.Empty)
			{
				mapped.Offset(offscreenCorrection);
			}
			mapped = Rectangle.Intersect(visibleArea, mapped);
			if (mapped.Width <= 1 || mapped.Height <= 1 || result.Contains(mapped)) continue;
			result.Add(mapped);
		}
		return result.ToArray();
	}

	private static void AddMediaControlAreas(List<MediaControlArea> result,
		AccessibleTreeSnapshot snapshot, Native.RECT sourceRect,
		Rectangle visibleArea, byte virtualKey,
		params string[] descriptions)
	{
		for (int descriptionIndex = 0; descriptionIndex < descriptions.Length;
			descriptionIndex++)
		{
			Rectangle[] controls = snapshot.GetControlBounds(
				descriptions[descriptionIndex], AllAccessibleSpace, includeHidden: true);
			for (int controlIndex = 0; controlIndex < controls.Length; controlIndex++)
			{
				Rectangle mapped = controls[controlIndex];
				mapped.Offset(visibleArea.Left - sourceRect.Left,
					visibleArea.Top - sourceRect.Top);
				mapped = Rectangle.Intersect(visibleArea, mapped);
				if (mapped.Width <= 1 || mapped.Height <= 1) continue;
				bool duplicate = false;
				for (int existingIndex = 0; existingIndex < result.Count; existingIndex++)
				{
					if (result[existingIndex].VirtualKey == virtualKey &&
						result[existingIndex].Bounds == mapped)
					{
						duplicate = true;
						break;
					}
				}
				if (!duplicate) result.Add(new MediaControlArea(mapped, virtualKey));
			}
		}
	}

	private void ForwardHoverToSource(Point screenPoint, Rectangle work)
	{
		IntPtr fixedTarget = IsVolumePoint(screenPoint) ? qqWindow : IntPtr.Zero;
		ForwardPointerToSource(screenPoint, work, WmMouseMove, IntPtr.Zero, fixedTarget);
		if (fixedTarget != IntPtr.Zero)
		{
			ForwardPointerToSource(screenPoint, work, WmMouseHover, IntPtr.Zero, fixedTarget);
		}
	}

	private IntPtr ForwardPointerToSource(Point screenPoint, Rectangle work, uint message,
		IntPtr keyState, IntPtr fixedTarget)
	{
		if (!TryMapToSource(screenPoint, work, out var target, out var sourcePoint))
		{
			return IntPtr.Zero;
		}
		if (fixedTarget != IntPtr.Zero && Native.IsWindow(fixedTarget))
		{
			target = fixedTarget;
		}
		Native.POINT clientPoint = sourcePoint;
		Native.ScreenToClient(target, ref clientPoint);
		Native.PostMessage(target, message, keyState, MakeLParam(clientPoint.X, clientPoint.Y));
		if (message == WmLButtonDown)
		{
			StringBuilder className = new StringBuilder(96);
			Native.GetClassName(target, className, className.Capacity);
			Log("Background click target: " + className + "; hwnd=" +
				target.ToInt64());
		}
		return target;
	}

	private void ForwardProgressClick(Point screenPoint, Rectangle work)
	{
		Rectangle[] progress = progressControlAreas;
		if (progress.Length == 0) return;
		ForwardProgressClick(screenPoint, work, progress[0]);
	}

	private void ForwardProgressClick(Point screenPoint, Rectangle work, Rectangle timeline)
	{
		if (timeline.Width <= 0 || timeline.Height <= 0) return;
		Point target = new Point(
			Math.Max(timeline.Left, Math.Min(screenPoint.X, timeline.Right - 1)),
			timeline.Top + timeline.Height / 2);
		ForwardPointerToSource(target, work, WmLButtonDown, new IntPtr(1), qqWindow);
		ForwardPointerToSource(target, work, WmLButtonUp, IntPtr.Zero, qqWindow);
	}

	private void QueueShowProgressPreview(Rectangle timeline, int pointerX)
	{
		if (progressPreview == null || window == null) return;
		int generation = Interlocked.Increment(ref progressPreviewGeneration);
		progressPreview.UpdatePointer(pointerX);
		try
		{
			window.BeginInvoke((Action)delegate
			{
				if (progressSeekActive && generation == progressPreviewGeneration)
					progressPreview.ShowProgress(timeline, pendingProgressSeekPoint.X);
			});
		}
		catch (InvalidOperationException) { }
	}

	private void QueueHideProgressPreview()
	{
		int generation = Interlocked.Increment(ref progressPreviewGeneration);
		if (progressPreview == null || window == null) return;
		try
		{
			window.BeginInvoke((Action)delegate
			{
				if (generation == progressPreviewGeneration) progressPreview.HidePreview();
			});
		}
		catch (InvalidOperationException) { }
	}

	private Rectangle[] GetQQAccessoryAreas(IntPtr source, Native.RECT sourceRect,
		Rectangle visibleArea)
	{
		List<Rectangle> result = new List<Rectangle>();
		Native.GetWindowThreadProcessId(source, out var sourceProcessId);
		Native.EnumWindows(delegate(IntPtr candidate, IntPtr data)
		{
			if (candidate == source || !Native.IsWindowVisible(candidate))
			{
				return true;
			}
			Native.GetWindowThreadProcessId(candidate, out var candidateProcessId);
			if (candidateProcessId != sourceProcessId ||
				!Native.GetWindowRect(candidate, out var rect))
			{
				return true;
			}
			int width = rect.Right - rect.Left;
			int height = rect.Bottom - rect.Top;
			if (width < 20 || height < 20 ||
				(long)width * height > (long)visibleArea.Width * visibleArea.Height / 2)
			{
				return true;
			}
			Rectangle mapped = new Rectangle(
				visibleArea.Left + rect.Left - sourceRect.Left,
				visibleArea.Top + rect.Top - sourceRect.Top,
				width, height);
			mapped = Rectangle.Intersect(visibleArea, mapped);
			if (!mapped.IsEmpty)
			{
				result.Add(mapped);
			}
			return true;
		}, IntPtr.Zero);
		return result.ToArray();
	}

	private bool TryMapToSource(Point screenPoint, Rectangle work, out IntPtr target,
		out Native.POINT sourcePoint)
	{
		target = qqWindow;
		sourcePoint = default(Native.POINT);
		if (!Native.GetWindowRect(qqWindow, out var sourceRect) || work.Width <= 0 || work.Height <= 0)
		{
			return false;
		}
		int width = Math.Max(1, sourceRect.Right - sourceRect.Left);
		int height = Math.Max(1, sourceRect.Bottom - sourceRect.Top);
		sourcePoint.X = sourceRect.Left + (screenPoint.X - work.Left) * width / work.Width;
		sourcePoint.Y = sourceRect.Top + (screenPoint.Y - work.Top) * height / work.Height;
		IntPtr inputTarget = FindSourceInputTarget(sourcePoint);
		if (inputTarget != IntPtr.Zero) target = inputTarget;
		return true;
	}

	private IntPtr FindSourceInputTarget(Native.POINT sourcePoint)
	{
		return FindWindowInputTarget(qqWindow, sourcePoint);
	}

	private static IntPtr FindWindowInputTarget(IntPtr root, Native.POINT screenPoint)
	{
		IntPtr direct = Native.WindowFromPoint(screenPoint);
		if (direct != IntPtr.Zero && (direct == root ||
			Native.GetAncestor(direct, 2u) == root)) return direct;

		IntPtr best = IntPtr.Zero;
		int bestPriority = -1;
		long bestArea = long.MaxValue;
		Native.EnumChildWindows(root, delegate(IntPtr child, IntPtr data)
		{
			if (!Native.GetWindowRect(child, out var rect) ||
				screenPoint.X < rect.Left || screenPoint.X >= rect.Right ||
				screenPoint.Y < rect.Top || screenPoint.Y >= rect.Bottom)
			{
				return true;
			}
			int width = rect.Right - rect.Left;
			int height = rect.Bottom - rect.Top;
			if (width <= 1 || height <= 1) return true;

			StringBuilder name = new StringBuilder(96);
			Native.GetClassName(child, name, name.Capacity);
			string windowClass = name.ToString();
			bool browserSurface = windowClass.IndexOf("qbcore", StringComparison.OrdinalIgnoreCase) >= 0 ||
				windowClass.IndexOf("Chrome_RenderWidget", StringComparison.OrdinalIgnoreCase) >= 0;
			if (!browserSurface && !Native.IsWindowVisible(child)) return true;

			int priority = browserSurface ? 2 : 1;
			long area = (long)width * height;
			if (priority > bestPriority || priority == bestPriority && area < bestArea)
			{
				best = child;
				bestPriority = priority;
				bestArea = area;
			}
			return true;
		}, IntPtr.Zero);
		return best != IntPtr.Zero ? best : root;
	}

	private void ForwardWheelToSource(Point screenPoint, Rectangle work, short delta)
	{
		if (TryMapToSource(screenPoint, work, out var target, out var sourcePoint))
		{
			long value = (long)((ulong)(ushort)delta << 16);
			Native.PostMessage(target, WmMouseWheel, new IntPtr(value),
				MakeLParam(sourcePoint.X, sourcePoint.Y));
		}
	}

	private void ForwardWheelToWindow(IntPtr target, Point screenPoint, short delta)
	{
		if (!Native.IsWindow(target)) return;
		Native.POINT nativePoint = new Native.POINT
		{
			X = screenPoint.X,
			Y = screenPoint.Y
		};
		target = FindWindowInputTarget(target, nativePoint);
		long value = (long)((ulong)(ushort)delta << 16);
		Native.PostMessage(target, WmMouseWheel, new IntPtr(value),
			MakeLParam(screenPoint.X, screenPoint.Y));
	}

	private void QueueMediaButton(byte key)
	{
		ThreadPool.QueueUserWorkItem(delegate
		{
			bool invoked = false;
			switch (key)
			{
			case 179:
				invoked = TryInvokeQQControl("播放", "暂停", "Play", "Pause");
				break;
			case 177:
				invoked = TryInvokeQQControl("上一首", "上一曲", "Previous",
					"Previous track");
				break;
			case 176:
				invoked = TryInvokeQQControl("下一首", "下一曲", "Next", "Next track");
				break;
			}
			if (invoked) nextAccessibilityRefresh = DateTime.MinValue;
		});
	}

	private bool IsQQMusicWindow(IntPtr hwnd)
	{
		if (hwnd == IntPtr.Zero)
		{
			return false;
		}
		Native.GetWindowThreadProcessId(hwnd, out var processId);
		if (processId == 0)
		{
			return false;
		}
		if (qqWindowProcessIdSource != qqWindow || qqWindowProcessId == 0)
		{
			Native.GetWindowThreadProcessId(qqWindow, out var processId2);
			qqWindowProcessId = processId2;
			qqWindowProcessIdSource = qqWindow;
		}
		if (qqWindowProcessId != 0)
		{
			return processId == qqWindowProcessId;
		}
		return false;
	}

	private IntPtr GetProgmanWindow()
	{
		if (progmanWindow != IntPtr.Zero && Native.IsWindow(progmanWindow))
		{
			return progmanWindow;
		}
		progmanWindow = Native.FindWindow("Progman", null);
		return progmanWindow;
	}

	private bool KeepSourceMaximizedBehindDesktop()
	{
		if (!(qqWindow == IntPtr.Zero) && Native.IsWindow(qqWindow))
		{
			Rectangle bounds = Screen.FromHandle(qqWindow).WorkingArea;
			long wallpaperStyle = originalStyle & ~(WsCaption | WsThickFrame | WsSysMenu |
				WsMinimizeBox | WsMaximizeBox);
			// Keep the source eligible for DWM/WGC redirection while it is parked
			// offscreen.  Re-applying QQ Music's WS_EX_NOREDIRECTIONBITMAP (or
			// converting it into a no-activate tool window) makes its captured
			// animation stop as soon as another application receives focus.
			// SW_SHOWNOACTIVATE and SWP_NOACTIVATE below already prevent focus theft.
			long wallpaperExStyle = (originalExStyle &
				~(WsExNoRedirectionBitmap | WsExToolWindow | WsExNoActivate)) |
				WsExAppWindow;
			bool capturedBounds = Native.GetWindowRect(qqWindow, out var previousRect);
			long previousStyle = Native.GetWindowLongPtr(qqWindow, GwlStyle).ToInt64();
			long previousExStyle = Native.GetWindowLongPtr(qqWindow, GwlExStyle).ToInt64();
			bool sourceRecovered = sourcePresented || Native.IsIconic(qqWindow) ||
				!Native.IsWindowVisible(qqWindow);
			Native.SetWindowLongPtr(qqWindow, GwlStyle, new IntPtr(wallpaperStyle));
			Native.SetWindowLongPtr(qqWindow, GwlExStyle, new IntPtr(wallpaperExStyle));
			// SW_SHOWNOACTIVATE keeps WGC alive without returning the user to QQ Music.
			Native.ShowWindow(qqWindow, SwShowNoActivate);
			// Keep the full-size source outside the visible monitor. WGC continues to
			// capture a visible, non-minimized window, while Explorer owns every real
			// desktop pixel and its icon layer can no longer be covered by the source.
			bool positioned = Native.SetWindowPos(qqWindow, new IntPtr(1), bounds.Left - bounds.Width - 256, bounds.Top,
				bounds.Width, bounds.Height, SwpFrameChanged | SwpShowWindow | 16u);
			bool capturedNewBounds = Native.GetWindowRect(qqWindow, out var rect);
			sourcePresented = false;
			// ShowWindow normalizes dynamic style bits such as WS_MAXIMIZE.
			// Compare observed states, not the original requested style, or every
			// maintenance tick would look like another recovery.
			bool styleChanged = previousStyle != Native.GetWindowLongPtr(qqWindow, GwlStyle).ToInt64() ||
				previousExStyle != Native.GetWindowLongPtr(qqWindow, GwlExStyle).ToInt64();
			bool sourceMoved = !capturedBounds || !capturedNewBounds ||
				previousRect.Left != rect.Left || previousRect.Top != rect.Top ||
				previousRect.Right != rect.Right || previousRect.Bottom != rect.Bottom;
			// A successful no-op SetWindowPos is routine maintenance, not a cache
			// invalidation. Preserve the idle refresh deadline until geometry or
			// window state actually changes.
			if (positioned && (sourceMoved || sourceRecovered || styleChanged))
				nextAccessibilityRefresh = DateTime.MinValue;
			return positioned && capturedNewBounds && rect.Left < bounds.Left &&
				(rect.Right - rect.Left) == bounds.Width && (rect.Bottom - rect.Top) == bounds.Height;
		}
		return false;
	}

	private void PresentSourceWindow()
	{
		Rectangle bounds = Screen.PrimaryScreen.WorkingArea;
		Native.ShowWindow(qqWindow, SwShowNoActivate);
		Native.SetWindowPos(qqWindow, IntPtr.Zero, bounds.Left, bounds.Top,
			bounds.Width, bounds.Height,
			SwpFrameChanged | SwpShowWindow | SwpNoActivate);
		sourcePresented = true;
	}

	private void AlignSourceToWorkingArea()
	{
		Native.ShowWindow(qqWindow, 3);
		Native.SetWindowPos(qqWindow, IntPtr.Zero, 0, 0, 0, 0, 83u);
	}

	private IntPtr FindDesktopListView()
	{
		IntPtr defView = Native.FindWindowEx(GetProgmanWindow(), IntPtr.Zero, "SHELLDLL_DefView", null);
		if (defView == IntPtr.Zero)
		{
			Native.EnumWindows(delegate(IntPtr top, IntPtr data)
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
			IntPtr listView = Native.FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
			if (listView != IntPtr.Zero)
			{
				desktopRootWindow = Native.GetAncestor(listView, 2u);
			}
			return listView;
		}
		return IntPtr.Zero;
	}

	private bool IsDesktopSurface(IntPtr root)
	{
		if (root == IntPtr.Zero) return false;
		if (root == GetProgmanWindow() || root == desktopRootWindow) return true;
		IntPtr listView = FindDesktopListView();
		return listView != IntPtr.Zero && root == desktopRootWindow;
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
			// Explorer may reject cross-process memory access after a restart or
			// across integrity levels.  Treating that diagnostic failure as an icon
			// made every desktop point non-interactive.  The precise QQ control-area
			// check that follows is the safer fallback.
			return false;
		}
		int value = Marshal.SizeOf(typeof(Native.LVHITTESTINFO));
		IntPtr intPtr2 = Native.VirtualAllocEx(intPtr, IntPtr.Zero, new UIntPtr((uint)value), 12288u, 4u);
		if (intPtr2 == IntPtr.Zero)
		{
			Native.CloseHandle(intPtr);
			return false;
		}
		try
		{
			byte[] array = StructureToBytes(new Native.LVHITTESTINFO
			{
				pt = point
			});
			Native.WriteProcessMemory(intPtr, intPtr2, array, array.Length, out var _);
			if (Native.SendMessageTimeout(listView, 4114u, IntPtr.Zero, intPtr2, 2u, 100u, out var result) == IntPtr.Zero)
			{
				return false;
			}
			return (long)result.ToUInt64() >= 0L;
		}
		finally
		{
			Native.VirtualFreeEx(intPtr, intPtr2, UIntPtr.Zero, 32768u);
			Native.CloseHandle(intPtr);
		}
	}

	private static bool IsDesktopRenameActive(IntPtr listView)
	{
		IntPtr intPtr = Native.FindWindowEx(listView, IntPtr.Zero, "Edit", null);
		if (intPtr != IntPtr.Zero)
		{
			return Native.IsWindowVisible(intPtr);
		}
		return false;
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
		lock (mouseHookSync)
		{
			int generation = ++mouseHookGeneration;
			Thread thread = new Thread((ThreadStart)delegate
			{
				uint currentThreadId = Native.GetCurrentThreadId();
				IntPtr intPtr = Native.SetWindowsHookEx(14, mouseProc, IntPtr.Zero, 0u);
				lock (mouseHookSync)
				{
					if (generation == mouseHookGeneration)
					{
						mouseHookThreadId = currentThreadId;
						mouseHook = intPtr;
					}
				}
				ready.Set();
				if (intPtr == IntPtr.Zero)
				{
					Log("Could not install mouse hook; Win32=" + Marshal.GetLastWin32Error());
				}
				else
				{
					Interlocked.Exchange(ref lastMouseHookEventTicks, DateTime.UtcNow.Ticks);
					Application.Run(new ApplicationContext());
					Native.UnhookWindowsHookEx(intPtr);
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
		IntPtr intPtr;
		uint num;
		lock (mouseHookSync)
		{
			mouseHookGeneration++;
			thread = mouseHookThread;
			intPtr = mouseHook;
			num = mouseHookThreadId;
			mouseHookThread = null;
			mouseHook = IntPtr.Zero;
			mouseHookThreadId = 0u;
		}
		if (intPtr != IntPtr.Zero)
		{
			Native.UnhookWindowsHookEx(intPtr);
		}
		if (num != 0)
		{
			Native.PostThreadMessage(num, 18u, IntPtr.Zero, IntPtr.Zero);
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
		}
		else
		{
			if (point.X == lastHookWatchdogPoint.X && point.Y == lastHookWatchdogPoint.Y)
			{
				return;
			}
			lastHookWatchdogPoint = point;
			long num = Interlocked.Read(ref lastMouseHookEventTicks);
			bool hasPoint = Interlocked.CompareExchange(ref hasMouseHookPoint, 0, 0) != 0;
			bool matchesCallback = hasPoint && Interlocked.Read(ref lastMouseHookPoint) ==
				PackMousePoint(point.X, point.Y);
			long callbackAge = DateTime.UtcNow.Ticks - num;
			if (num != 0L && MouseHookWatchdog.ShouldRestart(true, hasPoint,
				matchesCallback, callbackAge))
			{
				if (hookReinstallWindowStart == DateTime.MinValue || (DateTime.UtcNow - hookReinstallWindowStart).TotalMinutes > 10.0)
				{
					hookReinstallWindowStart = DateTime.UtcNow;
					hookReinstallCount = 0;
					Log("Mouse hook callback-gap recovery: new 10-minute window.");
				}
				hookReinstallCount++;
				Log("Mouse hook callback gap; reinstalling hook. count=" + hookReinstallCount +
					" in 10 min; ageMs=" + (callbackAge / TimeSpan.TicksPerMillisecond) +
					"; cursorMatchesLastCallback=" + matchesCallback + "; " + GetInputDiagnostics());
				if (hookReinstallCount == 3)
				{
					Log("WARN: repeated mouse callback gaps. Callback duration includes downstream hooks; " +
						"a gap alone does not establish a hook timeout or removal.");
				}
				RestartMouseHook();
			}
		}
	}

	private string GetInputDiagnostics()
	{
		int stage = mouseHookStage;
		string stageName = stage == 3 ? "downstream-hook" :
			stage == 2 ? "desktop-hit-test" : stage == 1 ? "routing" : "idle";
		return "callbacks=" + Interlocked.Read(ref mouseHookCallbackCount) +
			"; active=" + Interlocked.CompareExchange(ref activeMouseHookCallbacks, 0, 0) +
			"; stage=" + stageName +
			"; lastCallbackMsIncludingChain=" +
				(Interlocked.Read(ref lastMouseHookDurationTicks) * 1000L / Stopwatch.Frequency) +
			"; maxCallbackMsIncludingChain=" +
				(Interlocked.Read(ref maxMouseHookDurationTicks) * 1000L / Stopwatch.Frequency) +
			"; accessibilityScans=" + Interlocked.Read(ref accessibilityCaptureCount);
	}

	private void LogDiagnosticSummary()
	{
		if (!diagnosticMode || DateTime.UtcNow < nextDiagnosticSummary) return;
		nextDiagnosticSummary = DateTime.UtcNow.AddSeconds(30);
		Log("Input diagnostics: " + GetInputDiagnostics());
	}

	private void Exit()
	{
		if (!exiting)
		{
			exiting = true;
			enabled = false;
			DisableWallpaper(showQQ: true);
			if (themeContext != null)
			{
				themeContext.Shutdown();
			}
			timer.Stop();
			StopMouseHook();
			Native.UnregisterHotKey(window.Handle, 20813);
			SystemEvents.SessionSwitch -= OnSessionSwitch;
			SystemEvents.PowerModeChanged -= OnPowerModeChanged;
			tray.Visible = false;
			tray.Dispose();
			volumeOverlay.Dispose();
			playbackModeOverlay.Dispose();
			progressPreview.Dispose();
			explorerThemeRuntime.Dispose();
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
		RuntimeLog.Write(message);
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
