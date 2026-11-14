using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;
using QQMusicWallpaper;
using QQMusicWallpaperThemeSync;
using System.Runtime.Serialization;

namespace ControllerRegression.Tests;
internal static class PlaybackAccentRegression
{
    internal static void Register(Action<string,Action> run)
    {
        run("lyrics accent rejects ordinary-page and ambiguous playback controls", Identity);
        run("lyrics accent extracts button body or transparent glyph without artwork", Foreground);
        run("lyrics accent retains confirmed control color when hidden or moved", Missing);
        run("lyrics accent applies confirmed control color without a visible frame", AppliedColor);
        run("lyrics accent restores only its identified control color across sessions", Cache);
        run("lyrics accent failed capture immediately discards the previous sample", FailedSample);
    }
    private static void Assert(bool value,string message) {if(!value)throw new Exception(message);}
    private static AccessibleNodeSnapshot Node(string name,Rectangle bounds,bool hidden=false)
    {return new AccessibleNodeSnapshot(name,name,"press",43,hidden,2,bounds);}
    private static Form Source()
    {var f=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(-18000,-18000,800,600)};var h=f.Handle;return f;}
    private static void Identity()
    {
        using(var source=Source())
        {
            var play=Node("暂停",new Rectangle(source.Left+300,source.Top+450,40,30));
            var lyrics=Node("收起歌曲详情页",new Rectangle(source.Left+20,source.Top+20,30,30));
            Rectangle b;
            PlaybackAccent.Publish(source.Handle,source.Bounds,new AccessibleTreeSnapshot(new[]{play}));
            Assert(!PlaybackAccent.TryGetBounds(source.Size,out b),"An ordinary-page button was accepted as the lyrics control.");
            PlaybackAccent.Publish(source.Handle,source.Bounds,new AccessibleTreeSnapshot(new[]{lyrics,play}));
            Assert(PlaybackAccent.TryGetBounds(source.Size,out b) && b==new Rectangle(300,450,40,30),"The unique visible lyrics button was lost.");
            var second=Node("播放",new Rectangle(source.Left+500,source.Top+500,40,30));
            PlaybackAccent.Publish(source.Handle,source.Bounds,new AccessibleTreeSnapshot(new[]{lyrics,play,second}));
            Assert(!PlaybackAccent.TryGetBounds(source.Size,out b),"Selected the bottommost of two distinct playback controls.");
            var hidden=Node("播放",second.Bounds,true);
            PlaybackAccent.Publish(source.Handle,source.Bounds,new AccessibleTreeSnapshot(new[]{lyrics,play,hidden}));
            Assert(PlaybackAccent.TryGetBounds(source.Size,out b) && b.X==300,"A hidden duplicate displaced the visible control.");
        }
    }
    private static void Foreground()
    {
        using(var frame=new Bitmap(80,60))
        {
            var button=new Rectangle(20,10,40,40);
            for(int y=0;y<frame.Height;y++)for(int x=0;x<frame.Width;x++)frame.SetPixel(x,y,Color.FromArgb(100+x,20+y,100+y));
            using(var g=Graphics.FromImage(frame)){g.FillRectangle(Brushes.White,31,19,5,22);g.FillRectangle(Brushes.White,43,19,5,22);}
            Color c;Assert(PlaybackAccent.TryColor(frame,button,out c) && c.ToArgb()==Color.White.ToArgb(),"The colorful backdrop replaced the white lyrics pause glyph.");
            for(int y=0;y<frame.Height;y++)for(int x=0;x<frame.Width;x++)frame.SetPixel(x,y,Color.FromArgb(100+x,20+y,100+y));
            Assert(!PlaybackAccent.TryColor(frame,button,out c),"A bare gradient was accepted as a playback control color.");
            using(var g=Graphics.FromImage(frame))
            {
                g.Clear(Color.LightCyan);g.FillEllipse(Brushes.CornflowerBlue,button);
                g.FillRectangle(Brushes.White,31,19,5,22);g.FillRectangle(Brushes.White,43,19,5,22);
            }
            Assert(PlaybackAccent.TryColor(frame,button,out c) && c.ToArgb()==Color.CornflowerBlue.ToArgb(),
                "The white pause mark displaced the blue lyrics button body.");
        }
    }
    private static void Missing()
    {
        using(var source=Source())
        {
            PlaybackAccent.RememberColor(Color.Red);
            PlaybackAccent.Publish(source.Handle,source.Bounds,new AccessibleTreeSnapshot(null));
            Assert(PlaybackAccent.LastSampledColor.ToArgb()==Color.Red.ToArgb(),"Hiding the control erased its confirmed color.");
            Rectangle bounds;
            Assert(!PlaybackAccent.TryGetBounds(source.Size,out bounds),"Missing control reused a stale pixel region.");
            var play=Node("暂停",new Rectangle(source.Left+300,source.Top+450,40,30));
            var lyrics=Node("收起歌曲详情页",new Rectangle(source.Left+20,source.Top+20,30,30));
            PlaybackAccent.Publish(source.Handle,source.Bounds,new AccessibleTreeSnapshot(new[]{lyrics,play}));
            source.Left+=20;
            Assert(!PlaybackAccent.TryGetBounds(source.Size,out bounds) && PlaybackAccent.LastSampledColor.ToArgb()==Color.Red.ToArgb(),
                "Movement must invalidate geometry without erasing control color.");
        }
    }
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
            File.WriteAllText(path,"Control=Another.Control\nColor=FF0000\n");
            PlaybackAccent.ConfigureCache(path);
            Assert(PlaybackAccent.LastSampledColor.IsEmpty,"Unrelated control cache was accepted.");
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
