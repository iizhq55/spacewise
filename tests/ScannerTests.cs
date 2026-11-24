using Spacewise;
using Xunit;

public sealed class ScannerTests : IDisposable
{
    // next to the test binaries (not in Temp: the searches skip temp folders on purpose)
    readonly string dir = Path.Combine(AppContext.BaseDirectory, "work-" + Guid.NewGuid().ToString("N")[..8]);

    public ScannerTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

    string Write(string name, byte[] data) { var p = Path.Combine(dir, name); Directory.CreateDirectory(Path.GetDirectoryName(p)!); System.IO.File.WriteAllBytes(p, data); return p; }
    static byte[] Bytes(int size, int seed) { var b = new byte[size]; new Random(seed).NextBytes(b); return b; }

    [Fact]
    public void Finds_exact_copies_only()
    {
        var content = Bytes(300_000, 1);
        var a = Write("a.bin", content);
        var b = Write(Path.Combine("sub", "copy of a.bin"), content);
        var sameSizeDifferentMiddle = (byte[])content.Clone(); sameSizeDifferentMiddle[150_000] ^= 0xFF;
        Write("c.bin", sameSizeDifferentMiddle);
        Write("d.bin", Bytes(300_001, 2));

        var groups = Scanner.FindDuplicates(dir, 1, (_, _, _) => { }, CancellationToken.None);

        var g = Assert.Single(groups);
        Assert.Equal(new[] { a, b }.OrderBy(x => x), g.Select(f => f.FullPath).OrderBy(x => x));
    }

    [Fact]
    public void Type_search_finds_by_extension_biggest_first()
    {
        Write("small.mp4", Bytes(10_000, 3));
        Write("big.MP4", Bytes(50_000, 4));
        Write("note.txt", Bytes(60_000, 5));
        var found = Scanner.FindByType(dir, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4" }, 0, (_, _) => { }, CancellationToken.None);
        Assert.Equal(new[] { "big.MP4", "small.mp4" }, found.Select(f => Path.GetFileName(f.FullPath)));
    }

    [Fact]
    public void Type_search_respects_the_minimum_size()
    {
        Write("small.mp4", Bytes(10_000, 3));
        Write("big.mp4", Bytes(50_000, 4));
        var found = Scanner.FindByType(dir, new HashSet<string> { ".mp4" }, 20_000, (_, _) => { }, CancellationToken.None);
        Assert.Equal("big.mp4", Path.GetFileName(Assert.Single(found).FullPath));
    }

    [Fact]
    public void A_saved_scan_loads_back_the_same()
    {
        Write("one.bin", Bytes(2_000_000, 6));
        Write(Path.Combine("deep", "two.bin"), Bytes(3_000_000, 7));
        Write(Path.Combine("deep", "tiny.txt"), Bytes(100, 8));
        var tree = Scanner.Scan(dir, (_, _, _) => { }, CancellationToken.None);
        var file = Path.Combine(dir, "saved.bin");
        var when = new DateTime(2026, 11, 24, 15, 30, 0);

        Scanner.Save(tree, when, file);
        var (back, backWhen) = Scanner.Load(file)!.Value;

        Assert.Equal(when, backWhen);
        Assert.Equal(tree.Size, back.Size);
        Assert.Equal(tree.Files, back.Files);
        var deep = back.Children.Single(c => c.Name == "deep");
        Assert.Equal(Path.Combine(dir, "deep"), deep.FullPath);
        Assert.Same(back, deep.Parent);
        Assert.Equal(Path.Combine(dir, "deep", "two.bin"), deep.Children.Single(c => c.Name == "two.bin").FullPath);
        Assert.Contains(deep.Children, c => c.IsGroup && c.FullPath == deep.FullPath);
    }

    [Fact]
    public void A_broken_save_file_is_ignored() => Assert.Null(Scanner.Load(Write("broken.bin", Bytes(500, 9))));

    [Fact]
    public void Search_finds_names_anywhere_biggest_first()
    {
        Write(Path.Combine("x", "Holiday photo.jpg"), Bytes(2_000_000, 10));
        Write(Path.Combine("y", "holiday video.mp4"), Bytes(4_000_000, 11));
        Write("other.bin", Bytes(3_000_000, 12));
        var tree = Scanner.Scan(dir, (_, _, _) => { }, CancellationToken.None);
        Assert.Equal(new[] { "holiday video.mp4", "Holiday photo.jpg" }, Scanner.Search(tree, "HOLIDAY", 10).Select(n => n.Name));
    }

    [Fact] public void A_small_file_on_the_system_drive_can_be_recycled() => Assert.True(Cleanup.CanRecycle(Write("r.txt", Bytes(10, 13)), 10));
    [Fact] public void A_file_bigger_than_the_bin_cannot_be_recycled() => Assert.False(Cleanup.CanRecycle(Write("r2.txt", Bytes(10, 14)), 1L << 50));

    [Fact] public void Permanent_delete_removes_the_file()
    {
        var p = Write("gone.bin", Bytes(1000, 15));
        var r = Cleanup.Remove([(p, false, 1000)], permanent: true);
        Assert.Equal(1, r.Done);
        Assert.False(System.IO.File.Exists(p));
    }

    [Fact] public void Recycle_bin_mode_never_deletes_what_cannot_be_recycled()
    {
        var p = Write("huge.bin", Bytes(1000, 16));
        var r = Cleanup.Remove([(p, false, 1L << 50)], permanent: false); // pretend it's bigger than the bin
        Assert.Equal(0, r.Done);
        Assert.Single(r.NoBin);
        Assert.True(System.IO.File.Exists(p));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 GB")]
    public void Sizes_read_naturally(long bytes, string text) => Assert.Equal(text, Scanner.Human(bytes));
}
