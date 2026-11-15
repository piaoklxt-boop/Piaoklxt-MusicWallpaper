using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace QQMusicWallpaper;

// Durable last-known native lyric color; no window or image sampling.
internal static class PlaybackAccent
{
    private static volatile int sampledColor;
    internal static Color LastSampledColor { get { int value = sampledColor; return value == 0 ? Color.Empty : Color.FromArgb(value); } }
    private const string ControlIdentity = "QQMusic.Lyrics.SV_HighlightColor.v1";
    private static readonly object colorGate = new object();
    private static readonly object saveGate = new object();
    private static string cachePath;
    private static int persistedColor;
    private static bool saveInProgress;
    private static bool saveFailureReported;
    internal static Action<string, string> WriteCache = (path, text) => AppearanceIni.WriteAtomic(path, text, Encoding.UTF8);

    // Only values read from the named native property may enter this cache.
    internal static void RememberColor(Color value)
    {
        Action save = AcceptColor(value);
        if (save != null) save();
    }

    // Commit the accepted property in memory only. The caller may hold its
    // session lock; disk work must run after releasing that lock.
    internal static Action AcceptColor(Color value)
    {
        if (value.IsEmpty) return null;
        lock (colorGate)
        {
            sampledColor = value.ToArgb();
            return string.IsNullOrEmpty(cachePath) || (persistedColor == sampledColor && !saveInProgress) ? null : (Action)SaveColor;
        }
    }

    private static void SaveColor()
    {
        lock (saveGate)
        {
            string path; int color;
            lock (colorGate)
            {
                path = cachePath; color = sampledColor;
                if (string.IsNullOrEmpty(path) || color == 0 || persistedColor == color) return;
                saveInProgress = true;
            }
            try
            {
                WriteCache(path, "Control=" + ControlIdentity + "\nColor=" +
                    (color & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture) + "\n");
                lock (colorGate) { persistedColor = color; saveFailureReported = false; }
            }
            catch (IOException) { ReportSaveFailure(); }
            catch (UnauthorizedAccessException) { ReportSaveFailure(); }
            finally { lock (colorGate) { saveInProgress = false; } }
        }
    }

    private static void ReportSaveFailure()
    {
        if (!saveFailureReported) RuntimeLog.Write("Confirmed playback color could not be saved; retrying on the next accepted property read.");
        saveFailureReported = true;
    }

    internal static void ConfigureCache(string path)
    {
        lock (saveGate)
        lock (colorGate)
        {
            cachePath = path; sampledColor = persistedColor = 0; saveFailureReported = false;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                bool identified = false; int rgb = -1;
                foreach (string line in File.ReadAllLines(path))
                {
                    if (line == "Control=" + ControlIdentity) identified = true;
                    if (line.StartsWith("Color=") && line.Length == 12 &&
                        int.TryParse(line.Substring(6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int parsed)) rgb = parsed;
                }
                if (identified && rgb >= 0 && rgb <= 0xFFFFFF) sampledColor = persistedColor = Color.FromArgb(255, Color.FromArgb(rgb)).ToArgb();
            }
            catch (IOException) { RuntimeLog.Write("Confirmed playback color cache could not be read."); }
            catch (UnauthorizedAccessException) { RuntimeLog.Write("Confirmed playback color cache is not readable."); }
        }
    }
}
