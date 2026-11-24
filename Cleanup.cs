using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;

namespace Spacewise;

/// <summary>Something that is always safe to clean: caches and temporary files that programs recreate.</summary>
public sealed class CleanTarget
{
    public required string Name { get; init; }
    public required string What { get; init; }
    public required string NameAr { get; init; }
    public required string WhatAr { get; init; }
    public required string[] Folders { get; init; }
    public bool IsRecycleBin { get; init; }
    public bool NeedsAdmin { get; init; }
    public long Size;
}

public static class Cleanup
{
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string Win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <summary>Browser profile caches (Chrome, Edge, Brave, Opera): every profile's Cache, Code Cache and GPUCache.</summary>
    static IEnumerable<string> BrowserCaches()
    {
        string[] roots =
        [
            Path.Combine(Local, "Google", "Chrome", "User Data"),
            Path.Combine(Local, "Microsoft", "Edge", "User Data"),
            Path.Combine(Local, "BraveSoftware", "Brave-Browser", "User Data"),
            Path.Combine(Local, "Opera Software", "Opera Stable"),
        ];
        foreach (var root in roots.Where(Directory.Exists))
        {
            var profiles = new[] { root }.Concat(SafeDirs(root));
            foreach (var p in profiles)
                foreach (var c in new[] { "Cache", "Code Cache", "GPUCache" })
                {
                    var dir = Path.Combine(p, c);
                    if (Directory.Exists(dir)) yield return dir;
                }
        }
    }

    static IEnumerable<string> SafeDirs(string path)
    {
        try { return Directory.GetDirectories(path); } catch { return []; }
    }

    public static List<CleanTarget> Targets() =>
    [
        new() { Name = "Temporary files", What = "Files programs and installers leave in your Temp folder.",
            NameAr = "الملفات المؤقتة", WhatAr = "ملفات تتركها البرامج وملفات التثبيت في مجلد Temp.", Folders = [Path.GetTempPath()] },
        new() { Name = "Browser caches", What = "Saved copies of websites in Chrome, Edge, Brave and Opera. Sites load again normally; you stay signed in.",
            NameAr = "كاش المتصفحات", WhatAr = "نسخ محفوظة من المواقع في Chrome وEdge وBrave وOpera. تفتح المواقع بشكل طبيعي ويبقى تسجيل دخولك.", Folders = BrowserCaches().ToArray() },
        new() { Name = "Thumbnail cache", What = "Picture previews File Explorer saved. They're rebuilt when you open folders.",
            NameAr = "كاش الصور المصغّرة", WhatAr = "معاينات الصور التي حفظها مستكشف الملفات. تُبنى من جديد عند فتح المجلدات.", Folders = [Path.Combine(Local, "Microsoft", "Windows", "Explorer")] },
        new() { Name = "Crash reports", What = "Crash dumps from programs that closed unexpectedly.",
            NameAr = "تقارير الأعطال", WhatAr = "ملفات أعطال من برامج أُغلقت فجأة.", Folders = [Path.Combine(Local, "CrashDumps")] },
        new() { Name = "Windows temporary files", What = "Temporary files from Windows itself. Some are skipped without admin rights.",
            NameAr = "ملفات ويندوز المؤقتة", WhatAr = "ملفات مؤقتة من ويندوز نفسه. بعضها يُتخطّى بدون صلاحيات المسؤول.", Folders = [Path.Combine(Win, "Temp")], NeedsAdmin = true },
        new() { Name = "Windows Update downloads", What = "Update files that are already installed. Needs admin rights.",
            NameAr = "تحميلات تحديثات ويندوز", WhatAr = "ملفات تحديثات تم تثبيتها. تحتاج صلاحيات المسؤول.", Folders = [Path.Combine(Win, "SoftwareDistribution", "Download")], NeedsAdmin = true },
        new() { Name = "Recycle Bin", What = "Files you already deleted. Emptying it removes them for good.",
            NameAr = "سلة المحذوفات", WhatAr = "ملفات حذفتها من قبل. إفراغها يحذفها نهائيًا.", Folders = [], IsRecycleBin = true },
    ];

    public static void Measure(CleanTarget t)
    {
        if (t.IsRecycleBin) { t.Size = RecycleBinSize(); return; }
        long total = 0;
        var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var dir in t.Folders.Where(Directory.Exists))
        {
            try { foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", opts)) total += f.Length; } catch { }
        }
        t.Size = total;
    }

    /// <summary>Delete what's inside each folder (never the folder itself). Files in use are skipped. Returns bytes freed.</summary>
    public static long Clean(CleanTarget t)
    {
        if (t.IsRecycleBin)
        {
            var before = RecycleBinSize();
            SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4); // no confirm, no progress, no sound
            return before - RecycleBinSize();
        }
        long freed = 0;
        foreach (var dir in t.Folders.Where(Directory.Exists))
        {
            var dayAgo = DateTime.Now.AddDays(-1);
            foreach (var f in Enumerate(dir, files: true))
            {
                try
                {
                    var fi = new FileInfo(f);
                    // leave anything touched in the last day: a running program may still need it
                    if (fi.LastWriteTime > dayAgo) continue;
                    var len = fi.Length;
                    File.Delete(f);
                    freed += len;
                }
                catch { /* in use or protected */ }
            }
            // remove emptied subfolders, deepest first
            foreach (var d in Enumerate(dir, files: false).OrderByDescending(x => x.Length))
            {
                try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
            }
        }
        return freed;
    }

    static IEnumerable<string> Enumerate(string dir, bool files)
    {
        var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        try { return (files ? Directory.EnumerateFiles(dir, "*", opts) : Directory.EnumerateDirectories(dir, "*", opts)).ToList(); }
        catch { return []; }
    }

    /// <summary>What happened to a batch of deletions.</summary>
    public sealed record RemoveResult(int Done, long Freed, List<string> InUse, List<string> NoBin);

    /// <summary>
    /// Delete files or folders: to the Recycle Bin (restorable) or permanently.
    /// In Recycle Bin mode, anything Windows can't recycle (too big for the bin, or on a drive without one)
    /// is skipped and reported, never silently deleted for good.
    /// </summary>
    public static RemoveResult Remove(IEnumerable<(string path, bool isDir, long size)> items, bool permanent)
    {
        int done = 0; long freed = 0; List<string> inUse = [], noBin = [];
        foreach (var (path, isDir, size) in items)
        {
            if (!permanent && !CanRecycle(path, size)) { noBin.Add(path); continue; }
            try
            {
                if (permanent)
                {
                    if (isDir) FileSystem.DeleteDirectory(path, DeleteDirectoryOption.DeleteAllContents);
                    else { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
                }
                else if (!ShellRecycle(path)) { inUse.Add(path); continue; }
                if (File.Exists(path) || Directory.Exists(path)) { inUse.Add(path); continue; }
                done++; freed += size;
            }
            catch { inUse.Add(path); }
        }
        return new(done, freed, inUse, noBin);
    }

    /// <summary>
    /// Whether Windows would put this in the Recycle Bin instead of deleting it for good:
    /// only fixed drives have a bin, the bin can be switched off, and it has a size limit.
    /// </summary>
    public static bool CanRecycle(string path, long size)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (root is null || new DriveInfo(root).DriveType != DriveType.Fixed) return false;
            var vol = new System.Text.StringBuilder(64);
            if (!GetVolumeNameForVolumeMountPoint(root, vol, (uint)vol.Capacity)) return true;
            var guid = vol.ToString();
            guid = guid[(guid.IndexOf('{'))..(guid.IndexOf('}') + 1)];
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\{guid}");
            if (key?.GetValue("NukeOnDelete") is int nuke && nuke != 0) return false;
            // the bin's size limit; a drive whose bin was never set up uses Windows' default, about 5% of the drive
            var maxBytes = key?.GetValue("MaxCapacity") is int maxMb ? (long)maxMb << 20 : new DriveInfo(root).TotalSize / 20;
            return size <= maxBytes;
        }
        catch { return true; }
    }

    // SHFileOperation, so Windows itself warns (instead of silently deleting for good) if it can't recycle something after all
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd; public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags; public int fAnyOperationsAborted; public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, System.Text.StringBuilder name, uint size);

    static bool ShellRecycle(string path)
    {
        const uint FO_DELETE = 3;
        const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE, pFrom = path + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,
        };
        return SHFileOperation(ref op) == 0 && op.fAnyOperationsAborted == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint flags);

    static long RecycleBinSize()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
    }
}
