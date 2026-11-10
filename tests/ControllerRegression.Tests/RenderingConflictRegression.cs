using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using QQMusicWallpaper;
using QQMusicWallpaperThemeSync;

namespace ControllerRegression.Tests;

internal static class RenderingConflictRegression
{
    internal static void Register(Action<string,Action> run)
    {
        run("render conflict slow theme receivers cannot block settings or animation dispatch", SlowNotification);
        run("render conflict taskbar page owns the wallpaper bottom extension", TaskbarGrouping);
        run("render conflict notifications coalesce while a receiver is slow", Coalescing);
        run("render conflict stalled capture stays single flight without blocking settings", Capture);
        run("render conflict palette and tone do not dictate Windows black or white theme", ColorIndependence);
        run("render conflict unchanged registry colors do not rebroadcast or force a dark taskbar", RegistryChanges);
        run("render conflict taskbar transparency works without acrylic", TaskbarTransparency);
        run("render conflict native theme messages time out on an unresponsive receiver", NativeTimeout);
    }
    private static void Assert(bool condition,string message) { if(!condition)throw new Exception(message); }
    private static void SlowNotification()
    {
        using(var entered=new ManualResetEvent(false))
        using(var release=new ManualResetEvent(false))
        using(var returned=new ManualResetEvent(false))
        using(var finished=new ManualResetEvent(false))
        {
            int calls=0;
            var notifications=new ThemeNotifications(()=>{Interlocked.Increment(ref calls);entered.Set();release.WaitOne(3000);finished.Set();});
            var caller=new Thread(()=>{notifications.Request();returned.Set();}){IsBackground=true};caller.Start();
            Assert(entered.WaitOne(1000),"Notification never reached the receiver");
            bool responsive=returned.WaitOne(120);
            release.Set();caller.Join(1000);
            Assert(finished.WaitOne(1000),"Slow notification fixture did not finish");
            Assert(responsive,"A slow receiver blocked the settings caller and its render loop");
        }
    }
    private static TabPage Page(Control control) {while(control!=null&&!(control is TabPage))control=control.Parent;return control as TabPage;}
    private static void TaskbarGrouping()
    {
        using(var form=new ControlPanelForm(new ThemePreferences(),new ExplorerAppearanceSettings()))
            Assert(Page(form.Controls.Find("ExtendBottom",true)[0])==Page(form.Controls.Find("TaskbarAcrylic",true)[0]),"Bottom extension is detached from taskbar settings");
    }
    private static void Coalescing()
    {
        using(var entered=new ManualResetEvent(false))
        using(var release=new ManualResetEvent(false))
        using(var drained=new ManualResetEvent(false))
        {
            int calls=0,active=0,maximum=0;
            var notifications=new ThemeNotifications(()=>
            {
                int current=Interlocked.Increment(ref active);maximum=Math.Max(maximum,current);
                int call=Interlocked.Increment(ref calls);
                if(call==1){entered.Set();release.WaitOne(3000);}
                Interlocked.Decrement(ref active);if(call==2)drained.Set();
            });
            notifications.Request();Assert(entered.WaitOne(1000),"Notification did not start");
            for(int i=0;i<100;i++)notifications.Request();
            release.Set();Assert(drained.WaitOne(1500),"Latest theme notification was dropped");
            Thread.Sleep(40);Assert(calls==2 && maximum==1,"Notification requests grew into concurrent broadcasts");
        }
    }
    private static void Capture()
    {
        using(var entered=new ManualResetEvent(false))
        using(var release=new ManualResetEvent(false))
        using(var finished=new ManualResetEvent(false))
        {
            int calls=0;
            using(var samples=new ThemeSamples(()=>{Interlocked.Increment(ref calls);entered.Set();release.WaitOne(3000);finished.Set();return new ThemeSample(Color.Coral,.8);}))
            {
                var watch=Stopwatch.StartNew();Assert(samples.Read()==null,"Invented an initial frame");
                Assert(watch.ElapsedMilliseconds<120 && entered.WaitOne(1000),"Capture blocked its caller");
                watch.Restart();for(int i=0;i<100;i++)Assert(samples.Read()==null,"Blocked capture published a frame");
                Assert(watch.ElapsedMilliseconds<120 && calls==1,"Slow renderer queued repeated captures");
                release.Set();Assert(finished.WaitOne(1000),"Capture did not finish");
                var wait=Stopwatch.StartNew();ThemeSample result;
                do {result=samples.Read();if(result==null)Thread.Sleep(2);}while(result==null&&wait.ElapsedMilliseconds<500);
                Assert(result!=null && result.Color==Color.Coral,"Completed frame was not published");
                samples.Dispose();Assert(samples.Read()==null,"Disposed session revived its sample");
            }
        }
    }
    private static void Set(object instance,string field,object value)
    {instance.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(instance,value);}
    private static void ColorIndependence()
    {
        using(var surfaces=new AppearanceSurfaces((h,s)=>SurfaceKind.None))
        using(var samples=new ThemeSamples(()=>new ThemeSample(Color.Coral,.9)))
        {
            samples.Read();var wait=Stopwatch.StartNew();while(samples.Read()==null&&wait.ElapsedMilliseconds<1000)Thread.Sleep(3);
            var context=(ThemeContext)FormatterServices.GetUninitializedObject(typeof(ThemeContext));
            Set(context,"updateLock",new object());Set(context,"enabled",true);Set(context,"wallpaperSessionActive",true);
            Set(context,"samples",samples);Set(context,"surfaces",surfaces);Set(context,"explorerPreferences",new ExplorerAppearanceSettings());
            Color received=Color.Empty;bool? receivedLight=null;bool receivedDark=false;int calls=0;
            Set(context,"applyRegistry",new Action<Color,bool?,bool,bool>((c,l,s,d)=>{received=c;receivedLight=l;receivedDark=d;calls++;}));
            foreach(var palette in new[]{ThemePalette.Warm,ThemePalette.Cool,ThemePalette.Spectrum})
            foreach(ThemeTone tone in Enum.GetValues(typeof(ThemeTone)))
            foreach(bool automatic in new[]{false,true})
            {
                Set(context,"palette",palette);Set(context,"officePreferences",new ThemePreferences{Palette=palette,Tone=tone});
                Set(context,"matchBrightness",automatic);Set(context,"darkApps",false);Set(context,"lastColor",Color.Empty);Set(context,"lastLight",null);
                typeof(ThemeContext).GetMethod("UpdateColor",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(context,new object[]{true});
                Assert(received==ThemePaletteColors.Resolve(palette,0),"Display tone recolored the system accent");
                Assert(receivedLight==(automatic?(bool?)true:null) && !receivedDark,"Palette/tone overrode black-white theme or music brightness");
                using(var panel=new ControlPanelForm(new ThemePreferences{Palette=palette,Tone=tone},new ExplorerAppearanceSettings()))
                    Assert(panel.Controls.Find("DarkApps",true)[0].Enabled,"Palette tone disabled the independent system setting");
            }
            Assert(calls==18,"Production color update combinations were skipped");
            Set(context,"applyRegistry",new Action<Color,bool?,bool,bool>((c,l,s,d)=>{throw new InvalidOperationException("fixture");}));
            Assert(!(bool)typeof(ThemeContext).GetMethod("UpdateColor",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(context,new object[]{true}),"Theme registry failure was reported as success");
        }
    }
    private static void RegistryChanges()
    {
        string root=@"Software\Piaoklxt-Regression-"+Guid.NewGuid().ToString("N");
        Func<string,RegistryKey> open=path=>Registry.CurrentUser.CreateSubKey(root+"\\"+path);
        const string personalize=@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        try
        {
            using(var key=open(personalize))key.SetValue("SystemUsesLightTheme",1);
            int notified=0;Action notify=()=>notified++;
            ThemeRegistry.Apply(Color.Coral,true,true,false,open,notify);
            ThemeRegistry.Apply(Color.Coral,true,true,false,open,notify);
            Assert(notified==1,"Unchanged DWORD or binary palette caused another broadcast");
            using(var key=open(personalize))Assert((int)key.GetValue("SystemUsesLightTheme")==1 && (int)key.GetValue("AppsUseLightTheme")==1,"Accent forced black taskbar or lost explicit app brightness");
            ThemeRegistry.Apply(Color.Coral,false,true,false,open,notify);
            Assert(notified==2,"Actual brightness change did not notify");
        }
        finally {Registry.CurrentUser.DeleteSubKeyTree(root);}
    }
    private static void TaskbarTransparency()
    {
        using(var panel=new ControlPanelForm(new ThemePreferences{TaskbarColors=true},new ExplorerAppearanceSettings{TaskbarAcrylic=false}))
            Assert(panel.Controls.Find("TaskbarTransparency",true)[0].Enabled,"Independent color transparency is disabled without acrylic");
        using(var owner=new Form{Bounds=new Rectangle(-18000,-18000,500,40),ShowInTaskbar=false})
        {
            Native.ShowWindow(owner.Handle,4);
            using(var manager=new AppearanceSurfaces((h,s)=>h==owner.Handle?SurfaceKind.Taskbar:SurfaceKind.None))
            {
                var field=typeof(AppearanceSurfaces).GetField("surfaces",BindingFlags.NonPublic|BindingFlags.Instance);
                foreach(bool acrylic in new[]{false,true})
                {
                    manager.Configure(new SurfaceAppearanceState(new ThemePreferences{Palette=ThemePalette.Warm},new ExplorerAppearanceSettings{TaskbarAcrylic=acrylic,TaskbarTransparency=100},Color.Coral,true));manager.Tick();
                    var layers=(System.Collections.Generic.Dictionary<IntPtr,TintSurface>)field.GetValue(manager);
                    Assert(!layers[owner.Handle].Visible,"100% transparency leaves a taskbar color layer");
                }
            }
        }
    }
    private sealed class SlowReceiver : NativeWindow
    {
        internal SlowReceiver(){CreateHandle(new CreateParams{Parent=new IntPtr(-3)});}
        protected override void WndProc(ref Message message)
        {
            if(message.Msg==0x1A||message.Msg==0x31A)Thread.Sleep(700);
            if(message.Msg==0x10){DestroyHandle();Application.ExitThread();return;}
            base.WndProc(ref message);
        }
    }
    private static void NativeTimeout()
    {
        using(var ready=new ManualResetEvent(false))
        {
            IntPtr handle=IntPtr.Zero;
            var thread=new Thread(()=>{var receiver=new SlowReceiver();handle=receiver.Handle;ready.Set();Application.Run();GC.KeepAlive(receiver);}){IsBackground=true};
            thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert(ready.WaitOne(1000),"Native receiver unavailable");
            try
            {
                var watch=Stopwatch.StartNew();bool delivered=ThemeRegistry.NotifyWindow(handle);
                Assert(!delivered && watch.ElapsedMilliseconds<500,"Native theme notification exceeded its recipient deadline");
            }
            finally {Native.PostMessage(handle,0x10,IntPtr.Zero,IntPtr.Zero);thread.Join(2000);}
        }
    }
}
