using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using QQMusicWallpaper;

namespace ControllerRegression.Tests;

internal static class BroadThemeRegression
{
    [DllImport("user32.dll")] private static extern void NotifyWinEvent(uint type, IntPtr hwnd, int obj, int child);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int ex, string name, string title, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr data);
    private sealed class Fixture : Form
    {
        internal Fixture() { Bounds = new Rectangle(-18000, -18000, 360, 240); StartPosition = FormStartPosition.Manual; }
        protected override bool ShowWithoutActivation { get { return true; } }
    }
    internal static int RunFixture(string path)
    {
        using (var fixture = new Fixture())
        using (var timeout = new System.Windows.Forms.Timer { Interval = 60000 })
        {
            File.WriteAllText(path, fixture.Handle.ToInt64().ToString());
            timeout.Tick += delegate { fixture.Close(); }; timeout.Start();
            Application.Run(fixture);
        }
        return 0;
    }
    internal static void Register(Action<string, Action> run)
    {
        run("broad theme light and dark persist with independent application switches", Settings);
        run("broad theme reaches all surface kinds and clears stale animation", Surfaces);
        run("broad theme discovers ordinary external windows and rejects tool windows", Discovery);
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static Dictionary<IntPtr,TintSurface> Layers(AppearanceSurfaces manager)
    { return (Dictionary<IntPtr,TintSurface>)typeof(AppearanceSurfaces).GetField("surfaces", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager); }
    private static void Pump(int ms)
    { var watch=Stopwatch.StartNew(); do { Application.DoEvents(); Thread.Sleep(3); } while(watch.ElapsedMilliseconds<ms); }
    private static void Settings()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")+".ini");
        try
        {
            File.WriteAllText(path, "Palette=4\n");
            var legacy=ThemePreferences.Load(path);
            Assert(legacy.GeneralApps && legacy.TaskbarColors && legacy.Tone==ThemeTone.Original, "Upgrade lost default app coverage or changed existing tone");
            foreach (ThemeTone tone in Enum.GetValues(typeof(ThemeTone)))
            {
                using(var panel=new ControlPanelForm(new ThemePreferences(),new ExplorerAppearanceSettings()))
                {
                    ((ComboBox)panel.Controls.Find("ThemeTone",true)[0]).SelectedIndex=(int)tone;
                    ((CheckBox)panel.Controls.Find("GeneralApps",true)[0]).Checked=false;
                    ((CheckBox)panel.Controls.Find("TaskbarColors",true)[0]).Checked=false;
                    ((TrackBar)panel.Controls.Find("GeneralTint",true)[0]).Value=31;
                    File.WriteAllText(path,panel.ReadTheme().Serialize());
                }
                var saved=ThemePreferences.Load(path);
                Assert(saved.Tone==tone && !saved.GeneralApps && !saved.TaskbarColors && saved.GeneralTint==31, "Panel settings did not persist");
            }
        }
        finally {File.Delete(path);}
    }
    private static void Surfaces()
    {
        foreach(var kind in new[]{SurfaceKind.Explorer,SurfaceKind.Taskbar,SurfaceKind.Word,SurfaceKind.Excel,SurfaceKind.PowerPoint,SurfaceKind.Application})
        using(var owner=new Fixture())
        using(var tree=new TreeView { Dock=DockStyle.Fill })
        {
            owner.Controls.Add(tree); owner.Show(); Pump(10);
            if(kind==SurfaceKind.Explorer)
                Assert(CreateWindowEx(0,"SysTreeView32","",0x50000000,0,40,320,160,owner.Handle,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero)!=IntPtr.Zero,"Native sidebar fixture missing");
            using(var manager=new AppearanceSurfaces((h,s)=>h==owner.Handle?kind:SurfaceKind.None))
            {
                double light=0,dark=0;
                foreach(var tone in new[]{ThemeTone.Light,ThemeTone.Dark})
                {
                    var theme=new ThemePreferences{Palette=ThemePalette.Spectrum,Tone=tone};
                    manager.Configure(new SurfaceAppearanceState(theme,new ExplorerAppearanceSettings {TaskbarAcrylic=false},Color.Teal,true)); manager.Tick();
                    var layer=Layers(manager)[owner.Handle];
                    Assert(layer.Visible && layer.Field!=null, "Missing flowing layer for "+kind);
                    double brightness=0; foreach(int x in new[]{8,24,40,56}) foreach(int y in new[]{8,20,32}) brightness+=layer.Field.GetPixel(x,y).GetBrightness();
                    if(tone==ThemeTone.Light)light=brightness;else dark=brightness;
                }
                Assert(light-dark>3, "Light and dark look identical for "+kind);
                manager.Configure(new SurfaceAppearanceState(new ThemePreferences{Palette=ThemePalette.Artwork,Tone=ThemeTone.Light},new ExplorerAppearanceSettings(),Color.Teal,true)); manager.Tick();
                var solid=Layers(manager)[owner.Handle];
                Assert(solid.Field==null && solid.BackColor==ThemePaletteColors.ApplyTone(Color.Teal,ThemeTone.Light), "Old animation survived solid theme on "+kind);
            }
        }
    }
    private static void Discovery()
    {
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".handle");
        var start=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--ordinary-fixture \""+path+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        using(var child=Process.Start(start))
        {
            try
            {
                var watch=Stopwatch.StartNew();while(!File.Exists(path)&&watch.ElapsedMilliseconds<5000)Thread.Sleep(20);
                Assert(File.Exists(path),"External window fixture did not start");
                var hwnd=new IntPtr(long.Parse(File.ReadAllText(path))); Pump(150);
                var theme=new ThemePreferences {Palette=ThemePalette.Spectrum};
                var state=new SurfaceAppearanceState(theme,new ExplorerAppearanceSettings(),Color.Teal,true);
                Assert(SurfaceWindows.Identify(hwnd,state)==SurfaceKind.Application,"Ordinary external window not discovered");
                using(var manager=new AppearanceSurfaces((h,s)=>h==hwnd?SurfaceWindows.Identify(h,s):SurfaceKind.None))
                {
                    manager.Configure(state);manager.Tick();Assert(Layers(manager).Count==1,"General app did not receive a display layer");
                    theme.GeneralApps=false;manager.Configure(new SurfaceAppearanceState(theme,new ExplorerAppearanceSettings(),Color.Teal,true));manager.Tick();Assert(Layers(manager).Count==0,"Disabled general apps retained color");
                }
                long style=Native.GetWindowLongPtr(hwnd,-20).ToInt64();Native.SetWindowLongPtr(hwnd,-20,new IntPtr(style|0x80));
                Assert(SurfaceWindows.Identify(hwnd,state)==SurfaceKind.None,"Tool window was tinted as an application");
                Native.SetWindowLongPtr(hwnd,-20,new IntPtr(style));
                using(var own=new Fixture()){own.Show();Assert(SurfaceWindows.Identify(own.Handle,state)==SurfaceKind.None,"Controller decorated itself recursively");}
                Native.PostMessage(hwnd,0x10,IntPtr.Zero,IntPtr.Zero);child.WaitForExit(2000);
            }
            finally { if(!child.HasExited)child.Kill();File.Delete(path); }
        }
    }
    private static IntPtr InstalledLayer(uint pid,IntPtr owner)
    {
        IntPtr found=IntPtr.Zero; Native.EnumWindows((h,p)=>{uint id;Native.GetWindowThreadProcessId(h,out id);if(id==pid && SurfaceWindows.GetWindow(h,4)==owner)found=h;return true;},IntPtr.Zero);return found;
    }
    internal static int CheckInstalled(uint pid)
    {
        try
        {
            using(var owner=new Fixture())
            {
                owner.Show();Pump(2200);var layer=InstalledLayer(pid,owner.Handle);
                Assert(layer!=IntPtr.Zero && Native.IsWindowVisible(layer),"Installed app did not tint ordinary fixture");
                NotifyWinEvent(0xA,owner.Handle,0,0);Pump(40);Assert(!Native.IsWindowVisible(layer),"Installed drag left stale frame");
                for(int i=0;i<12;i++){owner.Left+=18;owner.Width+=2;Pump(18);Assert(!Native.IsWindowVisible(layer),"Installed animation reappeared during drag");}
                NotifyWinEvent(0xB,owner.Handle,0,0);Pump(200);
                Rectangle actual;SurfaceWindows.TryBounds(owner.Handle,out actual);Native.RECT r;Native.GetWindowRect(layer,out r);
                Assert(Native.IsWindowVisible(layer)&&actual==Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom),"Installed color did not recover at final position");
                Console.WriteLine("PASS installed ordinary-app coverage, drag suppression, resized geometry and resume");
            }
            return 0;
        }
        catch(Exception e){Console.WriteLine("FAIL "+e.Message);return 1;}
    }
}
