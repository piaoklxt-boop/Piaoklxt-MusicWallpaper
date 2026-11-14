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
    private static string cachePath;

    // Only values read from the named native property may enter this cache.
    internal static void RememberColor(Color value)
    {
        if (value.IsEmpty) return;
        lock (colorGate)
        {
            if (sampledColor == value.ToArgb()) return;
            sampledColor = value.ToArgb();
            if (string.IsNullOrEmpty(cachePath)) return;
            try
            {
                AppearanceIni.WriteAtomic(cachePath, "Control=" + ControlIdentity + "\nColor=" +
                    (value.ToArgb() & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture) + "\n", Encoding.UTF8);
            }
            catch (IOException) { RuntimeLog.Write("Confirmed playback color could not be saved."); }
            catch (UnauthorizedAccessException) { RuntimeLog.Write("Confirmed playback color cache is not writable."); }
        }
    }

    internal static void ConfigureCache(string path)
    {
        lock (colorGate)
        {
            cachePath = path; sampledColor = 0;
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
                if (identified && rgb >= 0 && rgb <= 0xFFFFFF) sampledColor = Color.FromArgb(255, Color.FromArgb(rgb)).ToArgb();
            }
            catch (IOException) { RuntimeLog.Write("Confirmed playback color cache could not be read."); }
            catch (UnauthorizedAccessException) { RuntimeLog.Write("Confirmed playback color cache is not readable."); }
        }
    }
}
