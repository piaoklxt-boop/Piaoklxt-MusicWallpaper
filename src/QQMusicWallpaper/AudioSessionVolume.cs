using System;
using System.Runtime.InteropServices;

namespace QQMusicWallpaper;

internal static class AudioSessionVolume
{
	private enum EDataFlow { Render, Capture, All }
	private enum ERole { Console, Multimedia, Communications }
	[Flags] private enum ClsCtx : uint { All = 23 }

	[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
	private class MMDeviceEnumerator { }

	[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
	private interface IMMDeviceEnumerator
	{
		int EnumAudioEndpoints(EDataFlow flow, uint mask, out IntPtr devices);
		int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice device);
	}

	[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 Guid("D666063F-1587-4E43-81F1-B948E807363F")]
	private interface IMMDevice
	{
		int Activate(ref Guid iid, ClsCtx context, IntPtr activation,
			[MarshalAs(UnmanagedType.IUnknown)] out object value);
	}

	[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
	private interface IAudioSessionManager2
	{
		int GetAudioSessionControl(IntPtr sessionGuid, uint flags, out IntPtr control);
		int GetSimpleAudioVolume(IntPtr sessionGuid, uint flags, out IntPtr volume);
		int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
	}

	[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
	private interface IAudioSessionEnumerator
	{
		int GetCount(out int count);
		int GetSession(int index, out IAudioSessionControl control);
	}

	[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
	private interface IAudioSessionControl
	{
		int GetState(out int state);
		int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
		int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr context);
		int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
		int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr context);
		int GetGroupingParam(out Guid grouping);
		int SetGroupingParam(ref Guid grouping, IntPtr context);
		int RegisterAudioSessionNotification(IntPtr client);
		int UnregisterAudioSessionNotification(IntPtr client);
	}

	[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
	private interface IAudioSessionControl2
	{
		int GetState(out int state);
		int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
		int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr context);
		int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
		int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr context);
		int GetGroupingParam(out Guid grouping);
		int SetGroupingParam(ref Guid grouping, IntPtr context);
		int RegisterAudioSessionNotification(IntPtr client);
		int UnregisterAudioSessionNotification(IntPtr client);
		int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
		int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
		int GetProcessId(out uint processId);
	}

	[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
	private interface ISimpleAudioVolume
	{
		int SetMasterVolume(float level, IntPtr context);
		int GetMasterVolume(out float level);
		int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr context);
		int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
	}

	public static bool TryGet(uint processId, out float level)
	{
		level = 0f;
		ISimpleAudioVolume volume;
		if (!TryFind(processId, out volume)) return false;
		try { return volume.GetMasterVolume(out level) == 0; }
		catch { return false; }
		finally { Release(volume); }
	}

	public static bool TrySet(uint processId, float level, out float applied)
	{
		applied = Math.Max(0f, Math.Min(1f, level));
		ISimpleAudioVolume volume;
		if (!TryFind(processId, out volume)) return false;
		try { return volume.SetMasterVolume(applied, IntPtr.Zero) == 0; }
		catch { return false; }
		finally { Release(volume); }
	}

	public static bool TryAdjust(uint processId, float delta, out float applied)
	{
		applied = 0f;
		float current;
		if (!TryGet(processId, out current)) return false;
		return TrySet(processId, current + delta, out applied);
	}

	private static bool TryFind(uint processId, out ISimpleAudioVolume result)
	{
		result = null;
		object rawManager = null;
		IMMDevice device = null;
		IAudioSessionEnumerator sessions = null;
		try
		{
			var deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
			if (deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia,
				out device) != 0) return false;
			Guid managerId = typeof(IAudioSessionManager2).GUID;
			if (device.Activate(ref managerId, ClsCtx.All, IntPtr.Zero, out rawManager) != 0)
				return false;
			var manager = (IAudioSessionManager2)rawManager;
			if (manager.GetSessionEnumerator(out sessions) != 0) return false;
			int count;
			if (sessions.GetCount(out count) != 0) return false;
			for (int index = 0; index < count; index++)
			{
				IAudioSessionControl control;
				if (sessions.GetSession(index, out control) != 0) continue;
				try
				{
					var control2 = control as IAudioSessionControl2;
					uint candidate = 0;
					if (control2 != null && control2.GetProcessId(out candidate) == 0 &&
						candidate == processId)
					{
						result = control as ISimpleAudioVolume;
						if (result != null) return true;
					}
				}
				finally
				{
					if (result == null) Release(control);
				}
			}
		}
		catch { }
		finally
		{
			Release(sessions);
			Release(rawManager);
			Release(device);
		}
		return false;
	}

	private static void Release(object value)
	{
		if (value != null && Marshal.IsComObject(value))
		{
			try { Marshal.ReleaseComObject(value); } catch { }
		}
	}
}
