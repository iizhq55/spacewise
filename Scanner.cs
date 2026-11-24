using System.Collections.Concurrent;

namespace Spacewise;

/// <summary>A folder (with children) or a single large file.</summary>
public sealed class Node
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsDir { get; init; }
    /// <summary>A stand-in for many small files in one folder.</summary>
    public bool IsGroup { get; init; }
    public long Size;
    public long Files;
    public DateTime LastUsed;
    public Node? Parent { get; init; }
    public List<Node> Children { get; } = [];
}

/// <summary>A file the user may no longer need.</summary>
public sealed record OldFile(string FullPath, long Size, DateTime LastUsed);

public static class Scanner
{
    /// <summary>Files smaller than this are grouped per folder, so a full disk scan stays light on memory.</summary>
    const long KeepFileOver = 1L << 20; // 1 MB

    /// <summary>Walk a folder tree and add up sizes. Skips links (so nothing is counted twice) and folders it can't read.</summary>
    /// <param name="progress">Called about 8 times a second with the folder being read, files so far and bytes so far.</param>
    public static Node Scan(string root, Action<string, long, long> progress, CancellationToken ct)
    {
        var top = new Node { Name = root, FullPath = root, IsDir = true };
        long seen = 0, bytes = 0, lastTicks = 0;
        var parallel = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount * 2) };
        void Report(string where)
        {
            var now = Environment.TickCount64;
            var last = Interlocked.Read(ref lastTicks);
            if (now - last < 120 || Interlocked.CompareExchange(ref lastTicks, now, last) != last) return;
            progress(where, Interlocked.Read(ref seen), Interlocked.Read(ref bytes));
        }
        void Walk(Node dir, int depth)
        {
            ct.ThrowIfCancellationRequested();
            long smallSize = 0, smallCount = 0;
            var subdirs = new List<Node>();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(dir.FullPath).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false });
                foreach (var e in entries)
                {
                    if (e.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue; // junctions, symlinks, OneDrive placeholders
                    if (e is DirectoryInfo d)
                    {
                        subdirs.Add(new Node { Name = d.Name, FullPath = d.FullName, IsDir = true, Parent = dir, LastUsed = d.LastWriteTime });
                    }
                    else if (e is FileInfo f)
                    {
                        var used = f.LastWriteTime /* access dates are unreliable: antivirus and indexing touch every file */;
                        var size = OnDisk(f);
                        dir.Size += size;
                        dir.Files++;
                        if (used > dir.LastUsed) dir.LastUsed = used;
                        if (size >= KeepFileOver)
                            lock (dir) dir.Children.Add(new Node { Name = f.Name, FullPath = f.FullName, Size = size, Files = 1, Parent = dir, LastUsed = used });
                        else { smallSize += size; smallCount++; }
                        Interlocked.Add(ref bytes, size);
                        if (Interlocked.Increment(ref seen) % 256 == 0) Report(dir.FullPath);
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            if (smallCount > 0)
                dir.Children.Add(new Node { Name = $"({smallCount:N0} small files)", FullPath = dir.FullPath, IsGroup = true, Size = smallSize, Files = smallCount, Parent = dir });

            Report(dir.FullPath);
            // folders are read in parallel several levels deep, so the disk and every CPU core stay busy
            if (depth < 8 && subdirs.Count > 1) Parallel.ForEach(subdirs, parallel, s => Walk(s, depth + 1));
            else foreach (var s in subdirs) Walk(s, depth + 1);

            foreach (var s in subdirs)
            {
                dir.Size += s.Size;
                dir.Files += s.Files;
                if (s.LastUsed > dir.LastUsed) dir.LastUsed = s.LastUsed;
                dir.Children.Add(s);
            }
            dir.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
        }
        Walk(top, 0);
        return top;
    }

    /// <summary>Your own files not opened or changed for a while (only in Downloads, Desktop, Documents, Pictures, Videos, Music).</summary>
    public static List<OldFile> FindOld(int months, long minBytes, CancellationToken ct)
    {
        var cutoff = DateTime.Now.AddMonths(-months);
        var found = new ConcurrentBag<OldFile>();
        var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        Parallel.ForEach(Knowledge.UserFolders, new ParallelOptions { CancellationToken = ct }, folder =>
        {
            foreach (var f in new DirectoryInfo(folder).EnumerateFiles("*", opts))
            {
                ct.ThrowIfCancellationRequested();
                if (f.Length < minBytes) continue;
                var used = f.LastWriteTime /* access dates are unreliable: antivirus and indexing touch every file */;
                if (used < cutoff) found.Add(new OldFile(f.FullName, f.Length, used));
            }
        });
        return found.OrderByDescending(o => o.Size).ToList();
    }

    /// <summary>
    /// Files with one of these extensions under a folder (or a whole drive), biggest first.
    /// Skips Windows, programs, program data and caches: this is for finding your own photos, videos and so on.
    /// </summary>
    /// <param name="exts">Extensions with the dot (".mp4"); null means every file (the biggest files on the drive).</param>
    public static List<OldFile> FindByType(string root, HashSet<string>? exts, long minBytes, Action<string, long> progress, CancellationToken ct)
    {
        var found = new ConcurrentBag<OldFile>();
        long seen = 0, lastTicks = 0;
        var parallel = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount * 2) };
        void Walk(string dir, int depth)
        {
            ct.ThrowIfCancellationRequested();
            var subdirs = new List<string>();
            try
            {
                foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
                {
                    if (e.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    if (e is DirectoryInfo d) { if (!Knowledge.SkipWhenSearching(d.FullName)) subdirs.Add(d.FullName); }
                    else if (e is FileInfo f)
                    {
                        Interlocked.Increment(ref seen);
                        if (exts is not null && !exts.Contains(f.Extension)) continue;
                        var size = OnDisk(f);
                        if (size >= minBytes && size > 0) found.Add(new OldFile(f.FullName, size, f.LastWriteTime));
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            var now = Environment.TickCount64;
            var last = Interlocked.Read(ref lastTicks);
            if (now - last >= 150 && Interlocked.CompareExchange(ref lastTicks, now, last) == last) progress(dir, Interlocked.Read(ref seen));
            if (depth < 8 && subdirs.Count > 1) Parallel.ForEach(subdirs, parallel, s => Walk(s, depth + 1));
            else foreach (var s in subdirs) Walk(s, depth + 1);
        }
        if (!Knowledge.SkipWhenSearching(root.TrimEnd('\\'))) Walk(root, 0);
        return found.OrderByDescending(o => o.Size).ToList();
    }

    /// <summary>
    /// Files with exactly the same content (your own files only, like the type search).
    /// Same size first, then the first and last 64 KB, then the whole file, so only real copies end up together.
    /// </summary>
    /// <param name="progress">(phase, done, total): phase 1 = listing files, phase 2 = comparing contents.</param>
    public static List<List<OldFile>> FindDuplicates(string root, long minBytes, Action<int, long, long> progress, CancellationToken ct)
    {
        var all = FindByType(root, null, Math.Max(1, minBytes), (_, n) => progress(1, n, 0), ct);
        var candidates = all.GroupBy(f => f.Size).Where(g => g.Count() > 1).SelectMany(g => g).ToList();
        long done = 0, lastTicks = 0;
        var total = candidates.Count;
        var opts = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 };
        void Tick()
        {
            var d = Interlocked.Increment(ref done);
            var now = Environment.TickCount64; var last = Interlocked.Read(ref lastTicks);
            if (now - last >= 150 && Interlocked.CompareExchange(ref lastTicks, now, last) == last) progress(2, d, total);
        }
        // quick look: the ends of each file
        var quick = new ConcurrentDictionary<OldFile, string>();
        Parallel.ForEach(candidates, opts, f => { var h = Hash(f.FullPath, edgesOnly: true); if (h is not null) quick[f] = h; Tick(); });
        var close = quick.GroupBy(kv => (kv.Key.Size, kv.Value)).Where(g => g.Count() > 1).SelectMany(g => g.Select(kv => kv.Key)).ToList();
        // full check, only for files whose ends matched
        var full = new ConcurrentDictionary<OldFile, string>();
        Parallel.ForEach(close, opts, f => { var h = f.Size <= 128 * 1024 ? quick[f] : Hash(f.FullPath, edgesOnly: false); if (h is not null) full[f] = h; });
        return full.GroupBy(kv => (kv.Key.Size, kv.Value)).Where(g => g.Count() > 1)
            .Select(g => g.Select(kv => kv.Key).OrderBy(f => f.LastUsed).ToList())
            .OrderByDescending(g => g[0].Size * (g.Count - 1)).ToList();
    }

    static string? Hash(string path, bool edgesOnly)
    {
        try
        {
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            var buf = new byte[1 << 16];
            if (edgesOnly)
            {
                int n = s.Read(buf, 0, buf.Length); sha.AppendData(buf, 0, n);
                if (s.Length > buf.Length * 2) { s.Seek(-buf.Length, SeekOrigin.End); n = s.Read(buf, 0, buf.Length); sha.AppendData(buf, 0, n); }
                else { n = s.Read(buf, 0, buf.Length); sha.AppendData(buf, 0, n); }
            }
            else
            {
                buf = new byte[1 << 20];
                int n; while ((n = s.Read(buf, 0, buf.Length)) > 0) sha.AppendData(buf, 0, n);
            }
            return Convert.ToHexString(sha.GetHashAndReset());
        }
        catch { return null; }
    }

    /// <summary>Everything in a scan whose name contains the text, biggest first.</summary>
    public static List<Node> Search(Node root, string text, int max)
    {
        var found = new List<Node>();
        var stack = new Stack<Node>(); stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            foreach (var c in n.Children)
            {
                if (!c.IsGroup && c.Name.Contains(text, StringComparison.OrdinalIgnoreCase)) found.Add(c);
                if (c.IsDir) stack.Push(c);
            }
        }
        return found.OrderByDescending(n => n.Size).Take(max).ToList();
    }

    // ---- remembering the last scan, so the app opens with results ----

    const int SaveVersion = 1;

    public static void Save(Node root, DateTime when, string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        using (var fs = File.Create(tmp))
        using (var z = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Fastest))
        using (var w = new BinaryWriter(z))
        {
            w.Write(SaveVersion); w.Write(root.FullPath); w.Write(when.ToBinary());
            void Write(Node n)
            {
                w.Write(n.Name); w.Write((byte)((n.IsDir ? 1 : 0) | (n.IsGroup ? 2 : 0)));
                w.Write(n.Size); w.Write(n.Files); w.Write(n.LastUsed.ToBinary());
                w.Write(n.Children.Count);
                foreach (var c in n.Children) Write(c);
            }
            Write(root);
        }
        File.Move(tmp, file, true);
    }

    public static (Node root, DateTime when)? Load(string file)
    {
        try
        {
            using var fs = File.OpenRead(file);
            using var z = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);
            using var r = new BinaryReader(z);
            if (r.ReadInt32() != SaveVersion) return null;
            var rootPath = r.ReadString();
            var when = DateTime.FromBinary(r.ReadInt64());
            Node Read(Node? parent)
            {
                var name = r.ReadString(); var flags = r.ReadByte();
                bool isDir = (flags & 1) != 0, isGroup = (flags & 2) != 0;
                var path = parent is null ? rootPath : isGroup ? parent.FullPath : Path.Combine(parent.FullPath, name);
                var n = new Node { Name = name, FullPath = path, IsDir = isDir, IsGroup = isGroup, Parent = parent };
                n.Size = r.ReadInt64(); n.Files = r.ReadInt64(); n.LastUsed = DateTime.FromBinary(r.ReadInt64());
                var count = r.ReadInt32();
                n.Children.Capacity = count;
                for (var i = 0; i < count; i++) n.Children.Add(Read(n));
                return n;
            }
            return (Read(null), when);
        }
        catch { return null; }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern uint GetCompressedFileSizeW(string path, out uint high);

    /// <summary>
    /// The space a file really takes. Sparse files (virtual disks, game installers), compressed files and
    /// cloud placeholders report a bigger Length than what's on the disk; for those, ask Windows.
    /// </summary>
    static long OnDisk(FileInfo f)
    {
        const FileAttributes odd = FileAttributes.SparseFile | FileAttributes.Compressed | FileAttributes.Offline | (FileAttributes)0x400000 /* RECALL_ON_DATA_ACCESS */;
        if ((f.Attributes & odd) == 0) return f.Length;
        var low = GetCompressedFileSizeW(@"\\?\" + f.FullName, out var high);
        if (low == 0xFFFFFFFF && System.Runtime.InteropServices.Marshal.GetLastWin32Error() != 0) return f.Length;
        return ((long)high << 32) | low;
    }

    public static string Human(long bytes)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : $"{v:0.##} {u[i]}";
    }
}
