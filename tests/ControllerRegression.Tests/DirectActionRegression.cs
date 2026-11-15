using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;
using QQMusicWallpaper;

namespace ControllerRegression.Tests;

internal static class DirectActionRegression
{
    internal static void Register(Action<string, Action> run)
    {
        run("direct controls invoke a real accessible button without mouse messages", NativeButton);
        run("direct controls reject disabled controls", Disabled);
        run("direct controls cancel an expired request before invoking", Expired);
        run("direct controls do not turn list selection into activation", ListSelection);
        run("direct controller dispatch suppresses mouse compatibility after success", () => ControllerRoute(DirectActionResult.Delivered));
        run("direct controller dispatch retains explicitly unsupported compatibility", () => ControllerRoute(DirectActionResult.Unsupported));
        run("direct controller dispatch never repeats an uncertain action", () => ControllerRoute(DirectActionResult.Uncertain));
        run("direct controller dispatch never repeats a rejected action", () => ControllerRoute(DirectActionResult.Rejected));
        run("direct controller bounds a stalled provider without a second click", StalledProvider);
        run("direct media dispatch maps commands and does not click after success", MediaRoute);
        run("direct media dispatch rejects a replaced source before sending", MediaIdentity);
        run("direct media dispatch does not repeat an uncertain command", MediaUncertain);
        run("direct media backend rejects a non-player process", () => Assert(MediaSessionCommands.Execute((uint)Process.GetCurrentProcess().Id, MediaSessionCommand.Toggle, 0) == DirectActionResult.Rejected, "Non-player process accepted."));
        run("direct seek runs outside its caller and submits one position", Seek);
        run("direct seek retains one compatibility click when unsupported", () => SeekCompatibility(false));
        run("direct seek cancels compatibility after a newer interaction", () => SeekCompatibility(true));
    }
    private static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(object o, string name, object value) { typeof(WallpaperContext).GetField(name, Instance).SetValue(o, value); }
    private static object Call(object o, string name, params object[] args) { return typeof(WallpaperContext).GetMethod(name, Instance).Invoke(o, args); }
    private static WallpaperContext Controller(Fixture source)
    {
        var c = (WallpaperContext)FormatterServices.GetUninitializedObject(typeof(WallpaperContext));
        Set(c, "qqWindow", source.Handle);Set(c, "qqWindowProcessIdSource", source.Handle);
        Set(c, "qqWindowProcessId", (uint)Process.GetCurrentProcess().Id);
        return c;
    }
    private static AccessibleNodeSnapshot Target(Fixture source)
    {
        return new AccessibleNodeSnapshot("Test action", "Action", "", 43, false, 1,
            source.Button.RectangleToScreen(source.Button.ClientRectangle));
    }
    private static void NativeButton()
    {
        using(var f = new Fixture())
        {
            var target = Target(f);
            var result=DirectControlActions.Invoke(f.Button.Handle, target, false);
            Assert(result == DirectActionResult.Delivered, "Owned MSAA button did not invoke: "+result+"; "+target.Role+"; "+target.Name+"; "+target.Description);
            Application.DoEvents();
            Assert(f.Clicks == 1 && f.Button.MouseMessages == 0, "Direct invocation used mouse messages or repeated the click.");
        }
    }
    private static void Disabled()
    {
        var target = new AccessibleNodeSnapshot("Test", "", "", 43, false, 0, new Rectangle(1,1,20,20), true);
        Assert(DirectControlActions.Invoke(IntPtr.Zero,target,false)==DirectActionResult.Rejected,"Disabled action allowed compatibility click.");
    }
    private static void Expired()
    {
        using(var f=new Fixture())
        {
            int checks=0;
            Assert(DirectControlActions.Invoke(f.Button.Handle,Target(f),false,()=>++checks==1)==DirectActionResult.Rejected,
                "Expired request was not checked immediately before the default action.");
            Assert(f.Clicks==0 && f.Button.MouseMessages==0,"Cancelled request still activated the control.");
        }
    }
    private static void ListSelection()
    {
        var target = new AccessibleNodeSnapshot("Test", "", "", 34, false, 0, new Rectangle(1,1,20,20));
        Assert(DirectControlActions.Invoke(IntPtr.Zero,target,false)==DirectActionResult.Unsupported,"Single list selection attempted activation.");
    }
    private static void ControllerRoute(DirectActionResult result)
    {
        using(var f = new Fixture())
        {
            var c = Controller(f);var t = Target(f);int calls=0;
            Set(c,"directControlAction",new Func<IntPtr,AccessibleNodeSnapshot,bool,Func<bool>,DirectActionResult>((h,n,d,current)=>{calls++;Assert(h==f.Handle && n.Bounds==t.Bounds,"Semantic identity changed.");return result;}));
            bool delivered=(bool)Call(c,"TryActivateSemantic",f.Handle,t,new Point(t.Bounds.Left+5,t.Bounds.Top+5),false);
            Application.DoEvents();
            Assert(calls==1,"Direct action repeated.");
            Assert(result==DirectActionResult.Unsupported ? f.Button.MouseMessages>0 : f.Button.MouseMessages==0,"Compatibility routing violated outcome.");
            Assert(delivered==(result==DirectActionResult.Delivered || result==DirectActionResult.Unsupported),"Result was misreported.");
        }
    }
    private static void MediaRoute()
    {
        using(var f = new Fixture())
        {
            var c=Controller(f);int calls=0;MediaSessionCommand last=MediaSessionCommand.Seek;
            Set(c,"directMediaAction",new Func<uint,MediaSessionCommand,long,DirectActionResult>((pid,cmd,t)=>{Assert(pid==(uint)Process.GetCurrentProcess().Id,"Wrong source process.");calls++;last=cmd;return DirectActionResult.Delivered;}));
            foreach(byte key in new byte[]{179,177,176})
            {
                Assert((bool)Call(c,"ExecuteMediaButton",key),"Native media action failed.");
                Assert(last==(key==179?MediaSessionCommand.Toggle:key==177?MediaSessionCommand.Previous:MediaSessionCommand.Next),"Media command mapping changed.");
            }
            Assert(calls==3 && f.Button.MouseMessages==0,"Media command fell through or repeated.");
        }
    }
    private static void StalledProvider()
    {
        using(var f=new Fixture())
        using(var release=new ManualResetEvent(false))
        using(var finished=new ManualResetEvent(false))
        {
            var c=Controller(f);var t=Target(f);
            Set(c,"directControlAction",new Func<IntPtr,AccessibleNodeSnapshot,bool,Func<bool>,DirectActionResult>((h,n,d,current)=>{release.WaitOne(2000);finished.Set();return DirectActionResult.Delivered;}));
            var watch=Stopwatch.StartNew();
            try
            {
                bool delivered=(bool)Call(c,"TryActivateSemantic",f.Handle,t,new Point(t.Bounds.Left+5,t.Bounds.Top+5),false);
                Assert(watch.ElapsedMilliseconds<700 && !delivered,"An external provider blocked the controller or a timeout was reported as delivered.");
                Assert(f.Button.MouseMessages==0,"Timed-out provider caused a duplicate compatibility click.");
            }
            finally {release.Set();Assert(finished.WaitOne(2500),"Provider fixture did not finish.");}
        }
    }
    private static void MediaIdentity()
    {
        using(var f=new Fixture())
        {
            var c=Controller(f);int calls=0;Set(c,"qqWindowProcessId",uint.MaxValue);
            Set(c,"directMediaAction",new Func<uint,MediaSessionCommand,long,DirectActionResult>((p,m,t)=>{calls++;return DirectActionResult.Delivered;}));
            Assert(!(bool)Call(c,"ExecuteMediaButton",(byte)179)&&calls==0,"Stale source reached media session.");
        }
    }
    private static void MediaUncertain()
    {
        using(var f=new Fixture())
        {
            var c=Controller(f);int calls=0;
            Set(c,"directMediaAction",new Func<uint,MediaSessionCommand,long,DirectActionResult>((p,m,t)=>{calls++;return DirectActionResult.Uncertain;}));
            Assert(!(bool)Call(c,"ExecuteMediaButton",(byte)179)&&calls==1,"Uncertain media action repeated or was reported successful.");
        }
    }
    private static void Seek()
    {
        using(var f=new Fixture())
        using(var entered=new ManualResetEvent(false))
        using(var release=new ManualResetEvent(false))
        using(var done=new ManualResetEvent(false))
        {
            var c=Controller(f);Set(c,"playbackTotalSeconds",100);int calls=0;long ticks=-1;
            Set(c,"directMediaAction",new Func<uint,MediaSessionCommand,long,DirectActionResult>((p,m,t)=>{calls++;ticks=t;entered.Set();release.WaitOne(3000);done.Set();return DirectActionResult.Delivered;}));
            var watch=Stopwatch.StartNew();
            try
            {
                typeof(WallpaperContext).GetMethod("ForwardProgressClick",Instance,null,new[]{typeof(Point),typeof(Rectangle),typeof(Rectangle)},null)
                    .Invoke(c,new object[]{new Point(60,10),new Rectangle(0,0,320,240),new Rectangle(10,5,101,10)});
                Assert(watch.ElapsedMilliseconds<150 && entered.WaitOne(1000),"Seek blocked its input caller.");
                Assert(calls==1 && ticks==50*TimeSpan.TicksPerSecond,"Seek sent an incorrect or duplicate position.");
            }
            finally {release.Set();Assert(done.WaitOne(2000),"Seek worker did not complete.");}
        }
    }
    private sealed class CountingButton : Button
    {
        internal int MouseMessages;
        protected override void WndProc(ref Message m) { if(m.Msg==0x201)MouseMessages++;base.WndProc(ref m); }
    }
    private static void SeekCompatibility(bool cancel)
    {
        using(var f=new Fixture())
        using(var entered=new ManualResetEvent(false))
        using(var release=new ManualResetEvent(false))
        using(var done=new ManualResetEvent(false))
        {
            var c=Controller(f);Set(c,"playbackTotalSeconds",100);
            Set(c,"directMediaAction",new Func<uint,MediaSessionCommand,long,DirectActionResult>((p,m,t)=>{entered.Set();release.WaitOne(3000);done.Set();return DirectActionResult.Unsupported;}));
            try
            {
                typeof(WallpaperContext).GetMethod("ForwardProgressClick",Instance,null,new[]{typeof(Point),typeof(Rectangle),typeof(Rectangle)},null)
                    .Invoke(c,new object[]{new Point(60,160),new Rectangle(0,0,320,240),new Rectangle(10,155,101,10)});
                Assert(entered.WaitOne(1000),"Seek backend did not start.");
                if(cancel)Set(c,"desktopClickSequence",1L);
                release.Set();Assert(done.WaitOne(1000),"Seek backend did not finish.");
                var watch=Stopwatch.StartNew();
                while(watch.ElapsedMilliseconds<(cancel?150:1000) && (cancel || f.MouseMessages==0)){Application.DoEvents();Thread.Sleep(2);}
                Assert(f.MouseMessages==(cancel?0:1),"Seek compatibility was duplicated, lost, or survived cancellation.");
            }
            finally {release.Set();done.WaitOne(2000);}
        }
    }
    private sealed class Fixture : Form
    {
        internal readonly CountingButton Button=new CountingButton();internal int Clicks, MouseMessages;
        protected override void WndProc(ref Message m) {if(m.Msg==0x201)MouseMessages++;base.WndProc(ref m);}
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
        internal Fixture()
        {
            ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Bounds=new Rectangle(-20000,-20000,320,240);
            foreach(var s in Screen.AllScreens)Assert(!s.Bounds.IntersectsWith(Bounds),"Fixture must stay offscreen.");
            Button.Text="Test action";Button.AccessibleName="Test action";Button.AccessibleDescription="Action";
            Button.SetBounds(20,20,110,35);Button.Click+=(s,e)=>Clicks++;Controls.Add(Button);Show();Application.DoEvents();
        }
    }
}
