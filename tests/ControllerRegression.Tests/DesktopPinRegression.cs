using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using QQMusicWallpaper;

namespace ControllerRegression.Tests;

internal static class DesktopPinRegression
{
    internal static void Register(Action<string, Action> run)
    {
        run("desktop pin reapplies after source process and window replacement", Replacement);
        run("desktop pin never dispatches an obsolete source or disposed request", Cancellation);
        run("desktop pin limits steady-state work and overlapping workers", Bounded);
        run("desktop pin controller capture binds a replacement source", ControllerCapture);
    }
    private static void Check(bool ok) { if (!ok) throw new Exception("Desktop pin regression failed"); }
    private static void Replacement()
    {
        var queue = new Queue<Action>(); var seen = new List<IntPtr>();
        using(var p = new SourceDesktopPinning((h,id)=>true,(h,current)=>{Check(current());seen.Add(h);return DesktopPinResult.Pinned;},queue.Enqueue))
        {
            p.EnsureAsync(new IntPtr(1), 10); queue.Dequeue()();
            p.EnsureAsync(new IntPtr(2), 11); queue.Dequeue()();
            p.EnsureAsync(new IntPtr(3), 11); queue.Dequeue()();
            Check(seen.Count==3 && seen[2]==new IntPtr(3));
        }
    }
    private static void Cancellation()
    {
        var queue = new Queue<Action>(); int calls=0;
        var p = new SourceDesktopPinning((h,id)=>true,(h,current)=>{calls++;return DesktopPinResult.Pinned;},queue.Enqueue);
        p.EnsureAsync(new IntPtr(1),10); p.EnsureAsync(new IntPtr(2),11); queue.Dequeue()(); Check(calls==0);
        p.EnsureAsync(new IntPtr(2),11); p.Dispose();queue.Dequeue()();Check(calls==0);
        using(var missing = new SourceDesktopPinning((h,id)=>false,(h,c)=>{calls++;return DesktopPinResult.Pinned;},a=>a()))
            missing.EnsureAsync(new IntPtr(3),12);
        Check(calls==0);
    }
    private static void Bounded()
    {
        var queue=new Queue<Action>(); int calls=0;
        using(var p=new SourceDesktopPinning((h,id)=>true,(h,c)=>{calls++;return DesktopPinResult.Pinned;},queue.Enqueue))
        {
            for(int i=0;i<100;i++) p.EnsureAsync(new IntPtr(1),10);
            Check(queue.Count==1);queue.Dequeue()();
            for(int i=0;i<100;i++) p.EnsureAsync(new IntPtr(1),10);
            Check(queue.Count==0 && calls==1);
        }
    }
    internal static void NativePin()
    {
        using(var f = new Form { StartPosition=FormStartPosition.Manual, Location=new Point(-24000,-24000), Size=new Size(100,80), Text="Owned desktop pin regression" })
        {
            f.Show();Application.DoEvents();
            try
            {
                var handle=f.Handle;
                var result=DesktopPinResult.Unavailable;
                var completed=new ManualResetEvent(false);
                ThreadPool.QueueUserWorkItem(_=> { try { result=VirtualDesktopPinApi.Pin(handle,()=>!f.IsDisposed); } finally {completed.Set();} });
                var deadline=DateTime.UtcNow.AddSeconds(5);
                while(!completed.WaitOne(5) && DateTime.UtcNow<deadline) Application.DoEvents();
                Check(completed.WaitOne(0));completed.Dispose();
                Check(result==DesktopPinResult.Pinned || result==DesktopPinResult.AlreadyPinned);
                Check(VirtualDesktopPinApi.Pin(f.Handle,()=>true)==DesktopPinResult.AlreadyPinned);
                Check(VirtualDesktopPinApi.Pin(f.Handle,()=>false)==DesktopPinResult.Stale);
            }
            finally { VirtualDesktopPinApi.SetPinned(f.Handle,false,()=>true); }
        }
    }
    private static void ControllerCapture()
    {
        int calls=0;
        using(var p=new SourceDesktopPinning((h,id)=>id==(uint)System.Diagnostics.Process.GetCurrentProcess().Id,
            (h,current)=>{Check(current());calls++;return DesktopPinResult.Pinned;},a=>a()))
        using(var one=new Form()) using(var two=new Form())
        {
            var context=(WallpaperContext)FormatterServices.GetUninitializedObject(typeof(WallpaperContext));
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(WallpaperContext).GetField("sourceDesktopPinning",flags).SetValue(context,p);
            foreach(var f in new[]{one,two}) {
                typeof(WallpaperContext).GetField("qqWindow",flags).SetValue(context,f.Handle);
                typeof(WallpaperContext).GetMethod("CaptureSourceState",flags).Invoke(context,null);
            }
            Check(calls==2);
        }
    }
}
