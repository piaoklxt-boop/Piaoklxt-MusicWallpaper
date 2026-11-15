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
        run("lyrics accent retries persistence after a temporary write failure",RetryPersistence);
        run("lyrics accent rejects late property completion after session clear",()=>LateCompletion(false));
        run("lyrics accent rejects late property completion after disposal",()=>LateCompletion(true));
        run("lyrics accent deferred saving uses the latest accepted color",DeferredSave);
        run("lyrics accent persistence does not hold the session lock",UnblockedPersistence);
        run("lyrics accent preserves a return to the saved color during an in-flight write",RevertedDuringSave);
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
    private static void RetryPersistence()
    {
        string root=Path.Combine(Path.GetTempPath(),"lyrics-retry-"+Guid.NewGuid().ToString("N"));
        string parent=Path.Combine(root,"blocked"),path=Path.Combine(parent,"color.ini");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(parent,"Blocks creation of the cache directory.");
            PlaybackAccent.ConfigureCache(path);
            PlaybackAccent.RememberColor(Color.CornflowerBlue);
            Assert(PlaybackAccent.LastSampledColor.ToArgb()==Color.CornflowerBlue.ToArgb(),"A disk failure lost the usable in-memory property.");
            File.Delete(parent);
            PlaybackAccent.RememberColor(Color.CornflowerBlue);
            Assert(File.Exists(path),"An unchanged native color was never saved after storage recovered.");
            PlaybackAccent.ConfigureCache(path);
            Assert(PlaybackAccent.LastSampledColor.ToArgb()==Color.CornflowerBlue.ToArgb(),"Restart lost the recovered persistent color.");
        }
        finally
        {
            PlaybackAccent.ConfigureCache(null);
            if(File.Exists(path))File.Delete(path);
            if(File.Exists(parent))File.Delete(parent);
            if(Directory.Exists(parent))Directory.Delete(parent);
            Directory.Delete(root);
        }
    }
    private static void LateCompletion(bool dispose)
    {
        var previous=Sampler.ReadProperties;
        using(var entered=new ManualResetEvent(false))
        using(var release=new ManualResetEvent(false))
        {
            PlaybackAccent.ConfigureCache(null);
            Sampler.ReadProperties=()=>{entered.Set();release.WaitOne(3000);return new ThemeSample(Color.Red,.5);};
            using(var samples=Sampler.CreateSamples())
            try
            {
                samples.Read();Assert(entered.WaitOne(1000),"Native property read did not start.");
                if(dispose)samples.Dispose();else samples.Clear();
                PlaybackAccent.RememberColor(Color.Green);
                release.Set();WaitIdle(samples);
                Assert(PlaybackAccent.LastSampledColor.ToArgb()==Color.Green.ToArgb(),"A cancelled native read overwrote the durable color after its session ended.");
                if(!dispose)
                {
                    Sampler.ReadProperties=()=>new ThemeSample(Color.Blue,.5);
                    typeof(ThemeSamples).GetField("requested",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(samples,DateTime.MinValue);
                    samples.Read();WaitIdle(samples);
                    Assert(samples.Read()!=null && PlaybackAccent.LastSampledColor.ToArgb()==Color.Blue.ToArgb(),"A new session could not publish a fresh native color.");
                }
            }
            finally {release.Set();WaitIdle(samples);Sampler.ReadProperties=previous;PlaybackAccent.ConfigureCache(null);}
        }
    }
    private static void WaitIdle(ThemeSamples samples)
    {
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object gate=typeof(ThemeSamples).GetField("gate",flags).GetValue(samples);
        var busy=typeof(ThemeSamples).GetField("busy",flags);var watch=Stopwatch.StartNew();
        while(watch.ElapsedMilliseconds<2000)
        {
            lock(gate){if(!(bool)busy.GetValue(samples))return;}
            Thread.Sleep(2);
        }
        throw new Exception("Native property worker did not finish.");
    }
    private static void DeferredSave()
    {
        string root=Path.Combine(Path.GetTempPath(),"lyrics-deferred-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(root,"color.ini");
        try
        {
            PlaybackAccent.ConfigureCache(path);
            Action older=PlaybackAccent.AcceptColor(Color.Red);
            PlaybackAccent.AcceptColor(Color.Blue);
            older();PlaybackAccent.ConfigureCache(path);
            Assert(PlaybackAccent.LastSampledColor.ToArgb()==Color.Blue.ToArgb(),"Delayed persistence overwrote a newer accepted native color.");
        }
        finally {PlaybackAccent.ConfigureCache(null);if(File.Exists(path))File.Delete(path);if(Directory.Exists(root))Directory.Delete(root);}
    }
    private static void UnblockedPersistence()
    {
        using(var entered=new ManualResetEvent(false))
        using(var release=new ManualResetEvent(false))
        using(var samples=new ThemeSamples(()=>new ThemeSample(Color.Blue,.5),sample=>()=>{entered.Set();release.WaitOne(3000);}))
        {
            try
            {
                samples.Read();Assert(entered.WaitOne(1000),"Persistence did not start.");
                var watch=Stopwatch.StartNew();
                for(int i=0;i<100;i++)Assert(samples.Read()!=null,"Accepted color was unavailable during persistence.");
                samples.Clear();
                Assert(watch.ElapsedMilliseconds<150,"Slow storage blocked reading or clearing the session.");
            }
            finally {release.Set();WaitIdle(samples);}
        }
    }
    private static void RevertedDuringSave()
    {
        string root=Path.Combine(Path.GetTempPath(),"lyrics-inflight-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(root,"color.ini");var writer=PlaybackAccent.WriteCache;
        using(var entered=new ManualResetEvent(false))
        using(var release=new ManualResetEvent(false))
        using(var finished=new ManualResetEvent(false))
        {
            bool started=false;
            try
            {
                PlaybackAccent.ConfigureCache(path);PlaybackAccent.RememberColor(Color.Blue);
                PlaybackAccent.WriteCache=(p,text)=>{entered.Set();release.WaitOne(3000);writer(p,text);};
                ThreadPool.QueueUserWorkItem(_=>{try{PlaybackAccent.RememberColor(Color.Red);}finally{finished.Set();}});
                started=true;Assert(entered.WaitOne(1000),"Cache write did not start.");
                Action pending=PlaybackAccent.AcceptColor(Color.Blue);
                release.Set();Assert(finished.WaitOne(2000),"Cache write did not finish.");
                if(pending!=null)pending();
                PlaybackAccent.ConfigureCache(path);
                Assert(PlaybackAccent.LastSampledColor.ToArgb()==Color.Blue.ToArgb(),"In-flight save replaced a newer return to the previously saved color.");
            }
            finally
            {
                release.Set();if(started)finished.WaitOne(4000);PlaybackAccent.WriteCache=writer;
                PlaybackAccent.ConfigureCache(null);if(File.Exists(path))File.Delete(path);if(Directory.Exists(root))Directory.Delete(root);
            }
        }
    }
}
