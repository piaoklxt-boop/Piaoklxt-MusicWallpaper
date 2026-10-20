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

	private const uint WmLButtonDown = 513u;

	private const uint WmLButtonUp = 514u;

	private const uint WmLButtonDblClk = 515u;

	private const uint WmRButtonDown = 516u;

	private const uint WmRButtonUp = 517u;

	private const uint WmMouseMove = 512u;

	private const uint WmMouseWheel = 522u;

	private const uint WmMouseHover = 673u;

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

	private readonly VolumeOverlayForm volumeOverlay;

	private readonly PlaybackModeOverlayForm playbackModeOverlay;
	private readonly ExplorerThemeRuntime explorerThemeRuntime;

	private readonly NotifyIcon tray;

	private ThemeContext themeContext;

	private Thread themeThread;

	private Thread mouseHookThread;

	private readonly object mouseHookSync = new object();

	private uint mouseHookThreadId;

	private int mouseHookGeneration;

	private long lastMouseHookEventTicks;

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

	private IntPtr interactionForeground;

	private System.Threading.Timer interactionTimer;

	private DateTime interactionStarted = DateTime.MinValue;

	private DateTime taskbarClickUntil = DateTime.MinValue;

	private DateTime lastHoverForwarded = DateTime.MinValue;

	private bool sourcePresented;

	private volatile Rectangle[] qqInteractiveAreas = new Rectangle[0];

	private volatile Rectangle[] volumeControlAreas = new Rectangle[0];

	private volatile Rectangle[] playbackModeControlAreas = new Rectangle[0];

	private volatile Rectangle[] commentControlAreas = new Rectangle[0];

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
			RefreshInteractiveAreasIfDue();
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
		if (code < 0 || !enabled || suspended || !embedded || !Native.IsWindow(qqWindow))
		{
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
		uint num = (uint)wParam.ToInt64();
		if (num != WmMouseMove && num != WmLButtonDown && num != WmLButtonUp &&
			num != WmRButtonDown && num != WmRButtonUp && num != WmMouseWheel)
		{
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
		Native.MSLLHOOKSTRUCT mSLLHOOKSTRUCT = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
		if (mSLLHOOKSTRUCT.extraInfo.ToInt64() == 1364023120)
		{
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
		Point point = new Point(mSLLHOOKSTRUCT.pt.X, mSLLHOOKSTRUCT.pt.Y);
		Rectangle bounds = Screen.PrimaryScreen.Bounds;
		Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
		if (interactionActive && num == WmRButtonDown)
		{
			interactionActive = false;
			interactionStarted = DateTime.MinValue;
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
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
		IntPtr ancestor = Native.GetAncestor(Native.WindowFromPoint(new Native.POINT
		{
			X = point.X,
			Y = point.Y
		}), 2u);
		bool num2 = IsDesktopSurface(ancestor);
		bool flag = !num2 && IsQQMusicWindow(ancestor);
		if (!num2 && !flag)
		{
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
		if (num == 512)
		{
			if (flag)
			{
				return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
			}
			DateTime utcNow = DateTime.UtcNow;
			if ((utcNow - lastHoverForwarded).TotalMilliseconds < 25.0)
			{
				return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
			}
			lastHoverForwarded = utcNow;
			ForwardHoverToSource(point, workingArea);
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
		if (flag)
		{
			if (num == 513 && TryHandleMediaButton(point, workingArea))
			{
				return new IntPtr(1);
			}
			if (sourcePresented)
			{
				switch (num)
				{
				case 513u:
					CancelInteractionTimer();
					break;
				case 514u:
					ScheduleLowerSource(1200);
					break;
				case 522u:
					ScheduleLowerSource(1500);
					break;
				}
			}
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
		IntPtr intPtr2 = FindDesktopListView();
		if (intPtr2 == IntPtr.Zero || IsDesktopRenameActive(intPtr2) || IsDesktopIconAt(intPtr2, point))
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
			if (!IsQQInteractionPoint(point, workingArea, includePlayerFallback: true))
			{
				return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
			}
			if (IsVolumePoint(point))
			{
				QueueShowVolume();
				return new IntPtr(1);
			}
			if (IsPlaybackModePoint(point))
			{
				QueueShowPlaybackModes();
				return new IntPtr(1);
			}
			if (IsCommentPoint(point))
			{
				// QQ Music opens comments in a separate native surface which cannot be
				// mirrored reliably.  Treat the desktop copy as intentionally disabled.
				return new IntPtr(1);
			}
			interactionTarget = ForwardPointerToSource(point, workingArea,
				WmLButtonDown, new IntPtr(1), IntPtr.Zero);
			if (interactionTarget != IntPtr.Zero)
			{
				interactionActive = true;
				interactionStarted = DateTime.UtcNow;
				return new IntPtr(1);
			}
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		case 522u:
		{
			short delta = (short)(mSLLHOOKSTRUCT.mouseData >> 16);
			if (IsVolumePoint(point))
			{
				QueueAdjustVolume(delta);
				return new IntPtr(1);
			}
			ForwardWheelToSource(point, workingArea, delta);
			return new IntPtr(1);
		}
		default:
			return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
		}
	}

	private bool IsQQInteractionPoint(Point point, Rectangle work, bool includePlayerFallback)
	{
		Rectangle[] areas = qqInteractiveAreas;
		for (int i = 0; i < areas.Length; i++)
		{
			if (areas[i].Contains(point))
			{
				return true;
			}
		}
		// The bottom player strip is kept as a fallback because some QQ Music
		// builds expose its canvas controls incompletely through MSAA.
		return includePlayerFallback && point.Y >= work.Bottom - 150;
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

	private bool IsCommentPoint(Point point)
	{
		Rectangle[] areas = commentControlAreas;
		return areas.Length > 0 && areas[0].Contains(point);
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
		if (DateTime.UtcNow < nextAccessibilityRefresh ||
			Interlocked.CompareExchange(ref accessibilityRefreshRunning, 1, 0) != 0)
		{
			return;
		}
		nextAccessibilityRefresh = DateTime.UtcNow.AddMilliseconds(interactionActive ? 350 : 1200);
		IntPtr source = qqWindow;
		Rectangle visibleArea = Screen.PrimaryScreen.WorkingArea;
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				Rectangle[] areas = QQMusicAccessibility.GetInteractiveBounds(source,
					new Rectangle(-32768, -32768, 65536, 65536));
				if (source == qqWindow)
				{
					if (Native.GetWindowRect(source, out var sourceRect))
					{
						for (int i = 0; i < areas.Length; i++)
						{
							areas[i].Offset(visibleArea.Left - sourceRect.Left,
								visibleArea.Top - sourceRect.Top);
						}
						List<Rectangle> combined = new List<Rectangle>(areas);
						combined.AddRange(GetQQAccessoryAreas(source, sourceRect, visibleArea));
						areas = combined.ToArray();
						Rectangle volumeBounds;
						if (QQMusicAccessibility.TryFindVisibleControl(source, "调节音量",
							new Rectangle(-32768, -32768, 65536, 65536), out volumeBounds) ||
							QQMusicAccessibility.TryFindVisibleControl(source, "Adjust volume",
							new Rectangle(-32768, -32768, 65536, 65536), out volumeBounds))
						{
							volumeBounds.Offset(visibleArea.Left - sourceRect.Left,
								visibleArea.Top - sourceRect.Top);
							volumeControlAreas = new[] { volumeBounds };
						}
						string[] playbackModes =
						{
							"随机播放", "顺序播放", "单曲循环", "列表循环"
						};
						for (int modeIndex = 0; modeIndex < playbackModes.Length; modeIndex++)
						{
							if (QQMusicAccessibility.TryFindVisibleControl(source,
								playbackModes[modeIndex], AllAccessibleSpace,
								out var playbackBounds))
							{
								currentPlaybackMode = playbackModes[modeIndex];
								playbackBounds.Offset(visibleArea.Left - sourceRect.Left,
									visibleArea.Top - sourceRect.Top);
								playbackModeControlAreas = new[] { playbackBounds };
								break;
							}
						}
						if (QQMusicAccessibility.TryFindVisibleControl(source, "评论",
							AllAccessibleSpace, out var commentBounds))
						{
							commentBounds.Offset(visibleArea.Left - sourceRect.Left,
								visibleArea.Top - sourceRect.Top);
							commentControlAreas = new[] { commentBounds };
						}
					}
					qqInteractiveAreas = areas;
				}
			}
			finally
			{
				Interlocked.Exchange(ref accessibilityRefreshRunning, 0);
			}
		});
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
		IntPtr direct = Native.WindowFromPoint(sourcePoint);
		if (direct != IntPtr.Zero &&
			Native.GetAncestor(direct, 2u) == qqWindow) return direct;

		IntPtr best = IntPtr.Zero;
		int bestPriority = -1;
		long bestArea = long.MaxValue;
		Native.EnumChildWindows(qqWindow, delegate(IntPtr child, IntPtr data)
		{
			if (!Native.GetWindowRect(child, out var rect) ||
				sourcePoint.X < rect.Left || sourcePoint.X >= rect.Right ||
				sourcePoint.Y < rect.Top || sourcePoint.Y >= rect.Bottom)
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
		return best;
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

	private void RaiseSourceForInput()
	{
		CancelInteractionTimer();
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
		CancelInteractionTimer();
		interactionTimer = new System.Threading.Timer(delegate
		{
			try
			{
				KeepSourceMaximizedBehindDesktop();
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

	private void CancelInteractionTimer()
	{
		Interlocked.Exchange(ref interactionTimer, null)?.Dispose();
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
			Native.SetWindowLongPtr(qqWindow, GwlStyle, new IntPtr(wallpaperStyle));
			Native.SetWindowLongPtr(qqWindow, GwlExStyle, new IntPtr(wallpaperExStyle));
			// SW_SHOWNOACTIVATE keeps WGC alive without returning the user to QQ Music.
			Native.ShowWindow(qqWindow, SwShowNoActivate);
			// Keep the full-size source outside the visible monitor. WGC continues to
			// capture a visible, non-minimized window, while Explorer owns every real
			// desktop pixel and its icon layer can no longer be covered by the source.
			bool positioned = Native.SetWindowPos(qqWindow, new IntPtr(1), bounds.Left - bounds.Width - 256, bounds.Top,
				bounds.Width, bounds.Height, SwpFrameChanged | SwpShowWindow | 16u);
			Native.GetWindowRect(qqWindow, out var rect);
			sourcePresented = false;
			return positioned && rect.Left < bounds.Left &&
				(rect.Right - rect.Left) == bounds.Width && (rect.Bottom - rect.Top) == bounds.Height;
		}
		return false;
	}

	private void PresentSourceWindow()
	{
		AlignSourceToWorkingArea();
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
			if (num != 0L && DateTime.UtcNow.Ticks - num > TimeSpan.FromSeconds(2.0).Ticks)
			{
				if (hookReinstallWindowStart == DateTime.MinValue || (DateTime.UtcNow - hookReinstallWindowStart).TotalMinutes > 10.0)
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
