using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using QQMusicWallpaper;
using QQMusicWallpaperThemeSync;
using System.Runtime.Serialization;
namespace ControllerRegression.Tests;
internal static class PlaybackAccentRegression
{
    internal static void Register(Action<string,Action> run)
    {
        run("lyrics accent applies confirmed property color without a visible frame",AppliedColor);
        run("lyrics accent persists native property color and rejects image cache",Cache);
        run("lyrics accent failed read discards the previous transient sample",FailedSample);
    }
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static void AppliedColor()
    {
        using(var surfaces=new AppearanceSurfaces((h,s)=>SurfaceKind.None))
        using(var samples=new ThemeSamples(()=>null))
        {
            var context=(ThemeContext)FormatterServices.GetUninitializedObject(typeof(ThemeContext));
            Action<string,object> set=(name,value)=>typeof(ThemeContext).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(context,value);
            set("updateLock",new object());set("enabled",true);set("wallpaperSessionActive",true);
            set("samples",samples);set("surfaces",surfaces);set("explorerPreferences",new ExplorerAppearanceSettings());
            set("officePreferences",new ThemePreferences());set("palette",ThemePalette.Artwork);set("lastColor",Color.Red);
            PlaybackAccent.RememberColor(Color.CornflowerBlue);
            Color applied=Color.Empty;
            set("applyRegistry",new Action<Color,bool?,bool,bool>((c,l,s,d)=>{applied=c;}));
            bool updated=(bool)typeof(ThemeContext).GetMethod("UpdateColor",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(context,new object[]{false});
            var color=(Color)typeof(ThemeContext).GetField("lastColor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(context);
            Assert(updated && color.ToArgb()==Color.CornflowerBlue.ToArgb() && applied.ToArgb()==Color.CornflowerBlue.ToArgb(),"Hidden control lost its confirmed color or reused a different palette.");
        }
    }
    private static void Cache()
    {
        string path=Path.Combine(Path.GetTempPath(),"lyrics-accent-"+Guid.NewGuid().ToString("N"),"color.ini");
        try
        {
            PlaybackAccent.ConfigureCache(path);
            Assert(PlaybackAccent.LastSampledColor.IsEmpty,"Invented an initial color.");
            PlaybackAccent.RememberColor(Color.CornflowerBlue);
            PlaybackAccent.RememberColor(Color.Empty);
            PlaybackAccent.ConfigureCache(path);
            Assert(PlaybackAccent.LastSampledColor.ToArgb()==Color.CornflowerBlue.ToArgb(),"Restart lost the confirmed control color.");
            PlaybackAccent.RememberColor(Color.Green);
            PlaybackAccent.ConfigureCache(path);
            Assert(PlaybackAccent.LastSampledColor.ToArgb()==Color.Green.ToArgb(),"Confirmed recoloring was not persisted.");
            File.WriteAllText(path,"Control=QQMusic.Lyrics.PlayPause.v3\nColor=FF0000\n");
            PlaybackAccent.ConfigureCache(path);
            Assert(PlaybackAccent.LastSampledColor.IsEmpty,"Legacy image-sampled cache was accepted as a native property.");
        }
        finally { PlaybackAccent.ConfigureCache(null);if(File.Exists(path))File.Delete(path);if(Directory.Exists(Path.GetDirectoryName(path)))Directory.Delete(Path.GetDirectoryName(path)); }
    }
    private static void FailedSample()
    {
        int captures=0;
        using(var samples=new ThemeSamples(()=>Interlocked.Increment(ref captures)==1?new ThemeSample(Color.Red,.5):null))
        {
            samples.Read();var clock=Stopwatch.StartNew();while(samples.Read()==null&&clock.ElapsedMilliseconds<1000)Thread.Sleep(2);
            Assert(samples.Read()!=null,"Initial sample did not complete.");
            typeof(ThemeSamples).GetField("requested",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(samples,DateTime.MinValue);
            samples.Read();clock.Restart();while(captures<2&&clock.ElapsedMilliseconds<1000)Thread.Sleep(2);
            Thread.Sleep(30);Assert(samples.Read()==null,"Failed control capture kept publishing the previous color.");
        }
    }
}
