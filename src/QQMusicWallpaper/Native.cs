using System;
using System.Runtime.InteropServices;
using System.Text;

namespace QQMusicWallpaper;

internal static class Native
{
	public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

	public delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

	public struct POINT
	{
		public int X;

		public int Y;
	}

	public struct RECT
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	public struct MSLLHOOKSTRUCT
	{
		public POINT pt;

		public uint mouseData;

		public uint flags;

		public uint time;

		public IntPtr extraInfo;
	}

	public struct LVHITTESTINFO
	{
		public POINT pt;

		public uint flags;

		public int item;

		public int subItem;

		public int group;
	}

	public struct WINDOWPLACEMENT
	{
		public int length;

		public int flags;

		public int showCmd;

		public POINT minPosition;

		public POINT maxPosition;

		public RECT normalPosition;
	}

	public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
	{
		public long PerProcessUserTimeLimit;

		public long PerJobUserTimeLimit;

		public uint LimitFlags;

		public UIntPtr MinimumWorkingSetSize;

		public UIntPtr MaximumWorkingSetSize;

		public uint ActiveProcessLimit;

		public UIntPtr Affinity;

		public uint PriorityClass;

		public uint SchedulingClass;
	}

	public struct IO_COUNTERS
	{
		public ulong ReadOperationCount;

		public ulong WriteOperationCount;

		public ulong OtherOperationCount;

		public ulong ReadTransferCount;

		public ulong WriteTransferCount;

		public ulong OtherTransferCount;
	}

	public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
	{
		public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;

		public IO_COUNTERS IoInfo;

		public UIntPtr ProcessMemoryLimit;

		public UIntPtr JobMemoryLimit;

		public UIntPtr PeakProcessMemoryUsed;

		public UIntPtr PeakJobMemoryUsed;
	}

	[DllImport("user32.dll")]
	public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	public static extern IntPtr FindWindow(string className, string windowName);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

	[DllImport("user32.dll")]
	public static extern bool IsWindow(IntPtr hwnd);

	[DllImport("user32.dll")]
	public static extern bool IsWindowVisible(IntPtr hwnd);

	[DllImport("user32.dll")]
	public static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	public static extern bool GetCursorPos(out POINT point);

	[DllImport("user32.dll")]
	public static extern bool SetCursorPos(int x, int y);

	[DllImport("user32.dll")]
	public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

	[DllImport("user32.dll")]
	public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);

	[DllImport("user32.dll")]
	public static extern IntPtr WindowFromPoint(POINT point);

	[DllImport("user32.dll")]
	public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

	[DllImport("user32.dll")]
	public static extern IntPtr GetParent(IntPtr hwnd);

	[DllImport("user32.dll")]
	public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

	[DllImport("user32.dll")]
	public static extern bool ShowWindow(IntPtr hwnd, int command);

	[DllImport("user32.dll")]
	public static extern bool IsIconic(IntPtr hwnd);

	[DllImport("user32.dll")]
	public static extern bool SetForegroundWindow(IntPtr hwnd);

	[DllImport("user32.dll")]
	public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

	[DllImport("user32.dll")]
	public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

	[DllImport("user32.dll")]
	public static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);

	[DllImport("user32.dll")]
	public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll")]
	public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

	[DllImport("user32.dll")]
	public static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);

	[DllImport("user32.dll")]
	public static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

	[DllImport("user32.dll")]
	public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc proc, IntPtr module, uint threadId);

	[DllImport("user32.dll")]
	public static extern bool UnhookWindowsHookEx(IntPtr hook);

	[DllImport("user32.dll")]
	public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	public static extern short GetAsyncKeyState(int virtualKey);

	[DllImport("user32.dll")]
	public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	public static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);

	[DllImport("kernel32.dll")]
	public static extern uint GetCurrentThreadId();

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
	public static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

	[DllImport("user32.dll", EntryPoint = "GetWindowLong")]
	public static extern IntPtr GetWindowLongPtr32(IntPtr hwnd, int index);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
	public static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

	[DllImport("user32.dll", EntryPoint = "SetWindowLong")]
	public static extern IntPtr SetWindowLongPtr32(IntPtr hwnd, int index, IntPtr value);

	public static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
	{
		if (IntPtr.Size != 8)
		{
			return GetWindowLongPtr32(hwnd, index);
		}
		return GetWindowLongPtr64(hwnd, index);
	}

	public static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
	{
		if (IntPtr.Size != 8)
		{
			return SetWindowLongPtr32(hwnd, index, value);
		}
		return SetWindowLongPtr64(hwnd, index, value);
	}

	[DllImport("kernel32.dll")]
	public static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

	[DllImport("kernel32.dll")]
	public static extern bool CloseHandle(IntPtr handle);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	public static extern IntPtr CreateJobObject(IntPtr jobAttributes, string name);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool SetInformationJobObject(IntPtr job, int informationClass, IntPtr information, uint informationLength);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

	[DllImport("kernel32.dll")]
	public static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocationType, uint protect);

	[DllImport("kernel32.dll")]
	public static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint freeType);

	[DllImport("kernel32.dll")]
	public static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out UIntPtr written);
}
