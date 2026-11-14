using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace QQMusicWallpaper;

// Read the named lyric style property, never a rendered surface. The private
// GF layout is accepted only for the verified pair of module binaries.
internal static class QQMusicThemeProperties
{
    internal const string PropertyName = "SV_HighlightColor";
    internal const uint ServiceMapRva = 0x258604;
    internal const uint ThemeVtableRva = 0x1eeaf0;
    internal static readonly byte[] ThemeServiceId = new Guid("7353be16-493d-4829-8d38-9836db56a4c0").ToByteArray();
    private const string GfHash = "4585FA83FDC18220F9F1EC297325AE34D99EB84E1F78D30267F3626D96B5EB9F";
    private const string QqHash = "F41EFBC57FCC91F5B2216FE20BCFA1CF56305BB5B23BEBAA843A4B348730D6A2";
    private static readonly object gate = new object();
    private static int checkedPid;
    private static long checkedStart;
    private static uint gfBase, qqBase;
    private static bool compatible;
    private static string lastStatus;

    internal static ThemeSample ReadCurrent()
    {
        lock (gate)
        {
            Process[] players = Process.GetProcessesByName("QQMusic");
            try
            {
                if (players.Length != 1) return Status("No unique QQ Music process.");
                Process player = players[0];
                long start = player.StartTime.ToUniversalTime().Ticks;
                if (checkedPid != player.Id || checkedStart != start)
                {
                    compatible = false;
                    ModuleEntry gf, qq;
                    if (!FindModules(player.Id, out gf, out qq)) return Status("QQ Music theme modules are not loaded.");
                    compatible = FileHash(gf.Path) == GfHash && FileHash(qq.Path) == QqHash;
                    gfBase = checked((uint)gf.Base.ToInt64());
                    qqBase = checked((uint)qq.Base.ToInt64());
                    checkedPid = player.Id; checkedStart = start;
                }
                if (!compatible) return Status("QQ Music build has no verified native color layout; retaining the last property color.");
                using (var memory = new ProcessMemory(player.Id))
                {
                    if (!ImageMatches(memory.Read, gfBase, 0x6825caf3, 0x292000) ||
                        !ImageMatches(memory.Read, qqBase, 0x6aed9823, 0xe1c000))
                        return Status("Loaded QQ Music modules differ from the verified color layout.");
                    Color color; double light;
                    if (!TryRead(memory.Read, gfBase, out color, out light))
                        return Status("QQ Music lyric color property is temporarily unavailable.");
                    Status("Reading QQ Music lyric style property SV_HighlightColor.");
                    return new ThemeSample(color, light);
                }
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) &&
                    !(error is System.ComponentModel.Win32Exception) && !(error is InvalidOperationException) &&
                    !(error is OverflowException) && !(error is ArgumentException)) throw;
                return Status("QQ Music color property read unavailable: " + error.GetType().Name);
            }
            finally { foreach (Process player in players) player.Dispose(); }
        }
    }

    private static ThemeSample Status(string message)
    {
        if (lastStatus != message) { lastStatus = message; RuntimeLog.Write(message); }
        return null;
    }

    private static string FileHash(string path)
    {
        using (var file = File.OpenRead(path))
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
    }

    // .NET 4 Process.Modules can omit WOW64 modules in a 64-bit controller.
    private static bool FindModules(int pid, out ModuleEntry gf, out ModuleEntry qq)
    {
        gf = qq = new ModuleEntry();
        using (MemoryHandle snapshot = CreateToolhelp32Snapshot(0x18, (uint)pid))
        {
            if (snapshot.IsInvalid) return false;
            var entry = new ModuleEntry { Size = (uint)Marshal.SizeOf(typeof(ModuleEntry)) };
            if (!Module32First(snapshot, ref entry)) return false;
            int count = 0;
            do
            {
                if (string.Equals(entry.Name, "GF.dll", StringComparison.OrdinalIgnoreCase)) gf = entry;
                if (string.Equals(entry.Name, "QQMusic.dll", StringComparison.OrdinalIgnoreCase)) qq = entry;
                if (gf.Base != IntPtr.Zero && qq.Base != IntPtr.Zero) return true;
                entry.Size = (uint)Marshal.SizeOf(typeof(ModuleEntry));
            } while (++count < 1024 && Module32Next(snapshot, ref entry));
            return false;
        }
    }

    internal static bool ImageMatches(Func<uint, int, byte[]> read, uint image, uint timestamp, uint size)
    {
        byte[] dos = read(image, 64);
        uint pe = U32(dos, 60);
        if (dos[0] != 'M' || dos[1] != 'Z' || pe < 64 || pe > 4096) return false;
        byte[] header = read(Add(image, pe), 84);
        return U32(header, 0) == 0x4550 && BitConverter.ToUInt16(header, 4) == 0x14c &&
            U32(header, 8) == timestamp && BitConverter.ToUInt16(header, 24) == 0x10b && U32(header, 80) == size;
    }

    // Test seam uses the same bounded property traversal as the live reader.
    // Two matching reads reject an update/rotation observed in mid-flight.
    internal static bool TryRead(Func<uint, int, byte[]> read, uint image, out Color color, out double light)
    {
        color = Color.Empty; light = 0;
        try
        {
            uint a, majorA, b, majorB;
            if (!ReadPalette(read, image, out a, out majorA) || !ReadPalette(read, image, out b, out majorB) ||
                a != b || majorA != majorB || (a >> 24) != 255 || (majorA >> 24) != 255) return false;
            color = Color.FromArgb(unchecked((int)a));
            // SV_Major is the lyric foreground: dark text means a light theme.
            Color foreground = Color.FromArgb(unchecked((int)majorA));
            light = 1 - (foreground.R * .2126 + foreground.G * .7152 + foreground.B * .0722) / 255;
            return true;
        }
        catch (Exception error)
        {
            if (error is IOException || error is OverflowException || error is ArgumentException) return false;
            throw;
        }
    }

    private static bool ReadPalette(Func<uint, int, byte[]> read, uint image, out uint accent, out uint major)
    {
        accent = major = 0;
        uint service = 0;
        if (!Walk(read, Add(image, ServiceMapRva), 512, 36, (address, node) =>
        {
            bool same = true;
            for (int i = 0; i < 16; i++) if (node[16 + i] != ThemeServiceId[i]) same = false;
            if (!same) return true;
            if (service != 0) return false;
            service = U32(node, 32); return service != 0;
        }) || service == 0) return false;
        uint vtable = U32(read(service, 4), 0);
        if (vtable != Add(image, ThemeVtableRva)) return false;
        // Slot 8 is the verified native named-color setter. This is only read,
        // never invoked; validating it prevents accepting an unrelated service.
        if (U32(read(Add(vtable, 32), 4), 0) != Add(image, 0x44b10)) return false;
        uint foundAccent = 0, foundMajor = 0; bool haveAccent = false, haveMajor = false;
        bool valid = Walk(read, Add(service, 0x5c), 1024, 24, (address, node) =>
        {
            uint key = U32(node, 16);
            if (key < 0x10008) return false;
            byte[] length = read(key - 8, 8);
            uint bytes = U32(length, 0), capacity = U32(length, 4);
            if ((bytes & 1) != 0 || bytes > 4096 || capacity < bytes) return false;
            if (bytes != PropertyName.Length * 2 && bytes != "SV_Major".Length * 2) return true;
            byte[] text = read(key, checked((int)bytes + 2));
            if (text[bytes] != 0 || text[bytes + 1] != 0) return false;
            string name = Encoding.Unicode.GetString(text, 0, (int)bytes);
            if (name == PropertyName)
            {
                if (haveAccent) return false;
                foundAccent = U32(node, 20); haveAccent = true;
            }
            if (name == "SV_Major")
            {
                if (haveMajor) return false;
                foundMajor = U32(node, 20); haveMajor = true;
            }
            return true;
        });
        if (!valid || !haveAccent || !haveMajor || U32(read(service, 4), 0) != vtable) return false;
        accent = foundAccent; major = foundMajor; return true;
    }

    private static bool Walk(Func<uint, int, byte[]> read, uint map, int limit, int nodeSize,
        Func<uint, byte[], bool> visit)
    {
        byte[] before = read(map, 8);
        uint head = U32(before, 0), count = U32(before, 4);
        if (count == 0 || count > limit) return false;
        byte[] sentinel = read(head, 16);
        if (sentinel[13] != 1) return false;
        uint root = U32(sentinel, 4);
        var stack = new Stack<uint>(); var seen = new HashSet<uint>(); stack.Push(root);
        while (stack.Count > 0)
        {
            uint address = stack.Pop();
            if (address == head) continue;
            if (!seen.Add(address) || seen.Count > count) return false;
            byte[] node = read(address, nodeSize);
            if (node[13] != 0 || !visit(address, node)) return false;
            stack.Push(U32(node, 0)); stack.Push(U32(node, 8));
        }
        byte[] after = read(map, 8);
        return seen.Count == count && U32(after, 0) == head && U32(after, 4) == count &&
            U32(read(Add(head, 4), 4), 0) == root;
    }

    private static uint U32(byte[] bytes, int offset) { return BitConverter.ToUInt32(bytes, offset); }
    private static uint Add(uint pointer, uint offset) { return checked(pointer + offset); }

    private sealed class ProcessMemory : IDisposable
    {
        private readonly MemoryHandle handle;
        internal ProcessMemory(int pid)
        {
            handle = OpenProcess(0x1010, false, pid); // QUERY_LIMITED_INFORMATION | VM_READ
            if (handle.IsInvalid) { handle.Dispose(); throw new IOException("QQ Music read handle unavailable."); }
        }
        internal byte[] Read(uint address, int length)
        {
            if (address < 0x10000 || length < 1 || length > 4096 || (ulong)address + (uint)length > uint.MaxValue)
                throw new IOException("Invalid theme property range.");
            var bytes = new byte[length]; UIntPtr actual;
            if (!ReadProcessMemory(handle, new IntPtr((long)address), bytes, new UIntPtr((uint)length), out actual) ||
                actual.ToUInt64() != (uint)length) throw new IOException("Theme property range unavailable.");
            return bytes;
        }
        public void Dispose() { handle.Dispose(); }
    }
    private sealed class MemoryHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private MemoryHandle() : base(true) { }
        protected override bool ReleaseHandle() { return CloseHandle(handle); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ModuleEntry
    {
        internal uint Size, ModuleId, ProcessId, GlobalUsage, ProcessUsage;
        internal IntPtr Base;
        internal uint ModuleSize;
        internal IntPtr Module;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Path;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern MemoryHandle CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", EntryPoint = "Module32FirstW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Module32First(MemoryHandle snapshot, ref ModuleEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Module32NextW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Module32Next(MemoryHandle snapshot, ref ModuleEntry entry);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern MemoryHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(MemoryHandle process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr read);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
