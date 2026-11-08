using System;
using System.ComponentModel;
using System.Diagnostics;

namespace QQMusicWallpaper;

internal static class ProcessIdentity
{
	public static bool Matches(uint processId, string expectedName)
	{
		return Matches(processId, expectedName, Process.GetProcessById);
	}

	internal static bool Matches(uint processId, string expectedName,
		Func<int, Process> openProcess)
	{
		try
		{
			using (Process process = openProcess((int)processId))
				return process.ProcessName.Equals(expectedName, StringComparison.OrdinalIgnoreCase);
		}
		catch (ArgumentException) { return false; }
		catch (InvalidOperationException) { return false; }
		catch (Win32Exception) { return false; }
	}
}
