using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.Serialization;
using QQMusicWallpaper;
using QQMusicWallpaperThemeSync;
namespace ControllerRegression.Tests;
internal static class NativeLyricsColorRegression
{
    internal static void Register(Action<string,Action> run)
    {
        run("native lyrics color path has no image sampler or capture API", NoImageSampling);
        run("native lyrics color reads the shared highlight rather than global theme or foreground", NamedProperty);
        run("native lyrics color rejects unknown services and missing or translucent properties", InvalidProperty);
        run("native lyrics color rejects cyclic oversized or changing native trees", InvalidTree);
        run("native lyrics color rejects mixed reads during a property update", UpdatingProperty);
        run("native lyrics color validates loaded binary identity", ImageIdentity);
        run("native lyrics color production sampler updates without a control or renderer", ProductionSampler);
        run("native lyrics color applies small authoritative property changes", SmallChange);
    }
    private static void Assert(bool value,string message) { if(!value)throw new Exception(message); }
    private static void NoImageSampling()
    {
        foreach(Type type in new[]{typeof(Sampler),typeof(PlaybackAccent),typeof(QQMusicThemeProperties)})
        foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static))
        {
            foreach(var arg in method.GetParameters())
                Assert(!typeof(Image).IsAssignableFrom(arg.ParameterType),"Theme color still depends on a rendered image: "+method.Name);
            var import=(DllImportAttribute)Attribute.GetCustomAttribute(method,typeof(DllImportAttribute));
            Assert(import==null || import.Value!="user32.dll","Theme color still imports window capture/geometry APIs.");
        }
    }

    private static void NamedProperty()
    {
        var memory = new Fixture(); Color color; double light;
        Assert(QQMusicThemeProperties.TryRead(memory.Read,Fixture.Image,out color,out light) &&
            color.ToArgb()==unchecked((int)0xff123456) && light==1,"Selected the global theme color or black text instead of lyric highlight.");
        memory.Word(memory.AccentNode+20,0xffad34ef); memory.Word(memory.MajorNode+20,0xffffffff);
        Assert(QQMusicThemeProperties.TryRead(memory.Read,Fixture.Image,out color,out light) &&
            color.ToArgb()==unchecked((int)0xffad34ef) && Math.Abs(light)<1e-9,"Native recoloring or independent light/dark mode was lost.");
        Assert(memory.Reads<300,"Native property read exceeded its small bounded traversal.");
    }
    private static void InvalidProperty()
    {
        foreach(int failure in new[]{0,1,2,3})
        {
            var memory=new Fixture();
            if(failure==0)memory.Word(Fixture.Service,0xdeadbeef);
            if(failure==1)memory.Text(memory.AccentKey,"Other_color_name_");
            if(failure==2)memory.Word(memory.AccentNode+20,0x80123456);
            if(failure==3)memory.Word(Fixture.Image+QQMusicThemeProperties.ThemeVtableRva+32,Fixture.Image+0x44b20);
            Color color;double light;
            Assert(!QQMusicThemeProperties.TryRead(memory.Read,Fixture.Image,out color,out light),"An unrelated or unverified native property was accepted: "+failure);
        }
    }
    private static void InvalidTree()
    {
        foreach(int failure in new[]{0,1,2})
        {
            var memory=new Fixture();
            if(failure==0)memory.Word(memory.AccentNode,memory.AccentNode);
            if(failure==1)memory.Word(Fixture.Service+0x60,1000000);
            if(failure==2)memory.Word(memory.AccentKey-8,0xffffffff);
            Color color;double light;
            Assert(!QQMusicThemeProperties.TryRead(memory.Read,Fixture.Image,out color,out light),"Malformed native property tree was accepted.");
            Assert(memory.Reads<100,"Malformed tree did not stop promptly.");
        }
    }
    private static void UpdatingProperty()
    {
        var memory=new Fixture();int seen=0;Color color;double light;
        Func<uint,int,byte[]> read=(address,size)=>
        {
            if(address==memory.AccentNode && ++seen==2)memory.Word(memory.AccentNode+20,0xffabcdef);
            return memory.Read(address,size);
        };
        Assert(!QQMusicThemeProperties.TryRead(read,Fixture.Image,out color,out light),"A color changing halfway through the read was published.");
    }
    private static void ImageIdentity()
    {
        var memory=new Fixture();memory.Fill(Fixture.Image,256);
        memory.Word(Fixture.Image,0x5a4d);memory.Word(Fixture.Image+60,128);
        memory.Word(Fixture.Image+128,0x4550);memory.Word(Fixture.Image+132,0x14c);
        memory.Word(Fixture.Image+136,0x12345678);memory.Word(Fixture.Image+152,0x10b);
        memory.Word(Fixture.Image+208,0x292000);
        Assert(QQMusicThemeProperties.ImageMatches(memory.Read,Fixture.Image,0x12345678,0x292000),"Verified image header rejected.");
        Assert(!QQMusicThemeProperties.ImageMatches(memory.Read,Fixture.Image,0x87654321,0x292000),"Unknown loaded binary accepted.");
    }
    private static void ProductionSampler()
    {
        var old=Sampler.ReadProperties;var memory=new Fixture();
        PlaybackAccent.ConfigureCache(null);
        try
        {
            Sampler.ReadProperties=()=>
            {
                Color c;double l;
                return QQMusicThemeProperties.TryRead(memory.Read,Fixture.Image,out c,out l)?new ThemeSample(c,l):null;
            };
            Color color;double light;
            Assert(Sampler.TrySample(out color,out light)&&color.ToArgb()==unchecked((int)0xff123456),"Production sampler requires visible control pixels.");
            memory.Word(memory.AccentNode+20,0xffad34ef);
            Assert(Sampler.TrySample(out color,out light)&&color.ToArgb()==unchecked((int)0xffad34ef),"Authoritative property update was delayed by pixel stability heuristics.");
            Sampler.ReadProperties=()=>null;
            Assert(!Sampler.TrySample(out color,out light)&&PlaybackAccent.LastSampledColor.ToArgb()==unchecked((int)0xffad34ef),"Unavailable property cleared the known native color or invented a screenshot fallback.");
        }
        finally {Sampler.ReadProperties=old;PlaybackAccent.ConfigureCache(null);}
    }
    private static void SmallChange()
    {
        using(var surfaces=new AppearanceSurfaces((h,s)=>SurfaceKind.None))
        using(var samples=new ThemeSamples(()=>null))
        {
            var context=(ThemeContext)FormatterServices.GetUninitializedObject(typeof(ThemeContext));
            Action<string,object> set=(name,value)=>typeof(ThemeContext).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(context,value);
            set("updateLock",new object());set("enabled",true);set("wallpaperSessionActive",true);
            set("samples",samples);set("surfaces",surfaces);set("explorerPreferences",new ExplorerAppearanceSettings());
            set("officePreferences",new ThemePreferences());set("palette",ThemePalette.Artwork);set("lastColor",Color.FromArgb(71,140,237));
            PlaybackAccent.ConfigureCache(null);var expected=Color.FromArgb(72,140,237);PlaybackAccent.RememberColor(expected);
            Color applied=Color.Empty;set("applyRegistry",new Action<Color,bool?,bool,bool>((c,l,s,d)=>applied=c));
            typeof(ThemeContext).GetMethod("UpdateColor",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(context,new object[]{false});
            Assert(applied.ToArgb()==expected.ToArgb(),"Pixel noise threshold swallowed a real native property change.");
            PlaybackAccent.ConfigureCache(null);
        }
    }
    private sealed class Fixture
    {
        internal const uint Image=0x10000000,Service=0x30000;
        private readonly Dictionary<uint,byte> bytes=new Dictionary<uint,byte>();
        internal uint AccentNode=0x41000,MajorNode=0x42000,AccentKey=0x50010;
        internal int Reads;
        internal Fixture()
        {
            uint serviceHead=0x20000,serviceNode=0x21000,propertyHead=0x40000;
            Map(Image+QQMusicThemeProperties.ServiceMapRva,serviceHead,serviceNode,1);
            Node(serviceNode,serviceHead,36);Put(serviceNode+16,QQMusicThemeProperties.ThemeServiceId);Word(serviceNode+32,Service);
            Word(Service,Image+QQMusicThemeProperties.ThemeVtableRva);
            Word(Image+QQMusicThemeProperties.ThemeVtableRva+32,Image+0x44b10);
            Map(Service+0x5c,propertyHead,AccentNode,3);
            Node(AccentNode,propertyHead,24);Word(AccentNode+8,MajorNode);
            Node(MajorNode,propertyHead,24);Word(MajorNode+8,0x43000);
            Node(0x43000,propertyHead,24);
            Text(AccentKey,QQMusicThemeProperties.PropertyName);Word(AccentNode+16,AccentKey);Word(AccentNode+20,0xff123456);
            Text(0x51010,"SV_Major");Word(MajorNode+16,0x51010);Word(MajorNode+20,0xff000000);
            Text(0x52010,"themeColor");Word(0x43010,0x52010);Word(0x43014,0xffff7d36);
        }
        internal void Fill(uint address,int size){for(uint i=0;i<size;i++)bytes[address+i]=0;}
        private void Map(uint map,uint head,uint root,uint count)
        {Word(map,head);Word(map+4,count);Fill(head,16);bytes[head+13]=1;Word(head+4,root);}
        private void Node(uint node,uint head,int size)
        {Fill(node,size);Word(node,head);Word(node+8,head);}
        internal void Text(uint pointer,string value)
        {byte[] text=Encoding.Unicode.GetBytes(value+"\0");Word(pointer-8,(uint)text.Length-2);Word(pointer-4,(uint)text.Length-2);Put(pointer,text);}
        internal void Word(uint address,uint value){Put(address,BitConverter.GetBytes(value));}
        private void Put(uint address,byte[] data){for(uint i=0;i<data.Length;i++)bytes[address+i]=data[i];}
        internal byte[] Read(uint address,int size)
        {
            Reads++;var result=new byte[size];
            for(uint i=0;i<size;i++){byte value;if(!bytes.TryGetValue(address+i,out value))throw new IOException("Unmapped fixture address.");result[i]=value;}
            return result;
        }
    }
}
