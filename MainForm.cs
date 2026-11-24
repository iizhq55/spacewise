using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Spacewise;

/// <summary>The window: a local HTML interface (ui.html) talking to the scanner through WebView2 messages.</summary>
public sealed class MainForm : Form
{
    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(13, 15, 21) };
    readonly Dictionary<int, Node> ids = [];
    readonly Dictionary<Node, int> idOf = [];
    Node? tree;
    DateTime treeWhen;
    List<OldFile> olds = [];
    List<OldFile> typed = [];
    List<List<OldFile>> dups = [];
    List<CleanTarget> targets = [];
    CancellationTokenSource? cts, typeCts, dupCts;
    readonly SemaphoreSlim saving = new(1, 1);
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spacewise");
    static readonly string SettingsFile = Path.Combine(DataDir, "settings.json");
    const string RepoUrl = "https://github.com/iizhq55/spacewise";
    static readonly bool IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    /// <summary>Arabic interface (English is the default).</summary>
    bool ar;

    public MainForm()
    {
        Text = IsAdmin ? "Spacewise (admin)" : "Spacewise";
        BackColor = Color.FromArgb(13, 15, 21);
        MinimumSize = new Size(980, 660);
        Size = new Size(1220, 820);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        try { ar = JsonDocument.Parse(File.ReadAllText(SettingsFile)).RootElement.GetProperty("lang").GetString() == "ar"; } catch { }
        Controls.Add(web);
        Load += async (_, _) => await StartAsync();
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Dark title bar, so the window matches the app instead of a white strip on top.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var on = 1;
        DwmSetWindowAttribute(Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
        var caption = 0x00150F0D; // COLORREF (BGR) of #0d0f15, the app background (Windows 11)
        DwmSetWindowAttribute(Handle, 35 /* DWMWA_CAPTION_COLOR */, ref caption, sizeof(int));
    }

    async Task StartAsync()
    {
        // The window is drawn by Microsoft Edge WebView2, built into Windows 11 and most Windows 10 PCs.
        // On a PC without it, say so plainly instead of failing silently.
        try { CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (WebView2RuntimeNotFoundException)
        {
            var arabic = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
            var go = MessageBox.Show(this,
                arabic ? "يحتاج Spacewise إلى مكوّن Microsoft Edge WebView2، وهو غير مثبّت على هذا الجهاز.\n\nهل تريد فتح صفحة تحميله المجانية من مايكروسوفت؟"
                       : "Spacewise needs Microsoft Edge WebView2, which isn't installed on this PC.\n\nOpen Microsoft's free download page?",
                "Spacewise", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (go == DialogResult.Yes)
                Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
            Close();
            return;
        }
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(DataDir, "WebView"));
        await web.EnsureCoreWebView2Async(env);
        var s = web.CoreWebView2.Settings;
        s.AreDevToolsEnabled = false;
        s.AreDefaultContextMenusEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        // the page is local; it never navigates anywhere
        web.CoreWebView2.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith("data:") && e.Uri != "about:blank") e.Cancel = true; };
        // the Ko-fi panel opens payment pages (PayPal, Stripe) in a new window: send those to the user's own browser
        web.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
                Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
        };
        web.CoreWebView2.WebMessageReceived += (_, e) => OnMessage(e.WebMessageAsJson);
        web.NavigateToString(LoadUi());
    }

    static string LoadUi()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Spacewise.ui.html")!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    void Send(object msg)
    {
        var json = JsonSerializer.Serialize(msg, Json);
        if (InvokeRequired) BeginInvoke(() => web.CoreWebView2?.PostWebMessageAsJson(json));
        else web.CoreWebView2?.PostWebMessageAsJson(json);
    }

    string T(string en, string arabic) => ar ? arabic : en;
    /// <summary>A size kept left-to-right inside Arabic text.</summary>
    static string H(long bytes) => "⁦" + Scanner.Human(bytes) + "⁩";
    void Toast(string en, string arabic) => Send(new { type = "toast", text = T(en, arabic) });

    async void OnMessage(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var m = doc.RootElement;
            switch (m.GetProperty("cmd").GetString())
            {
                case "ready":
                    Send(new { type = "lang", lang = ar ? "ar" : "en" });
                    SendDrives();
                    await LoadSavedScanAsync(lastPath);
                    await MeasureCleanAsync();
                    break;
                case "lang":
                    ar = m.GetProperty("lang").GetString() == "ar";
                    try { Directory.CreateDirectory(DataDir); File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new { lang = ar ? "ar" : "en" })); } catch { }
                    break;
                case "usage":
                    var up = m.GetProperty("path").GetString()!;
                    Send(new { type = "usage", usage = Usage(up) });
                    if (cts is null && !string.Equals(up, lastPath, StringComparison.OrdinalIgnoreCase)) { lastPath = up; await LoadSavedScanAsync(up); }
                    break;
                case "scan":
                    await ScanAsync(m.GetProperty("path").GetString()!);
                    break;
                case "cancel":
                    cts?.Cancel();
                    break;
                case "open":
                    if (ids.TryGetValue(m.GetProperty("id").GetInt32(), out var dir) && dir.IsDir) SendDir(dir);
                    break;
                case "up":
                    if (ids.TryGetValue(m.GetProperty("id").GetInt32(), out var child) && child.Parent is { } parent) SendDir(parent);
                    break;
                case "search":
                    SendSearch(m.GetProperty("q").GetString() ?? "");
                    break;
                case "explorer":
                    if (ids.TryGetValue(m.GetProperty("id").GetInt32(), out var n))
                        Process.Start("explorer.exe", n.IsDir || n.IsGroup ? $"\"{n.FullPath}\"" : $"/select,\"{n.FullPath}\"");
                    break;
                case "reveal":
                    // only a file one of the lists found, never an arbitrary path from the page
                    var rp = m.GetProperty("path").GetString();
                    if (AllFound().Any(o => string.Equals(o.FullPath, rp, StringComparison.OrdinalIgnoreCase)))
                        Process.Start("explorer.exe", $"/select,\"{rp}\"");
                    break;
                case "launch":
                    // a file from the scan, opened with its own program (a video plays, a picture opens…)
                    if (ids.TryGetValue(m.GetProperty("id").GetInt32(), out var file) && !file.IsDir && !file.IsGroup) Launch(file.FullPath);
                    break;
                case "launchPath":
                    // only a file one of the lists found, never an arbitrary path from the page
                    var lp = m.GetProperty("path").GetString();
                    if (AllFound().Any(o => string.Equals(o.FullPath, lp, StringComparison.OrdinalIgnoreCase))) Launch(lp!);
                    break;
                case "link":
                    if (m.GetProperty("to").GetString() == "star") Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });
                    break;
                case "apps":
                    Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
                    break;
                case "hibernateOff":
                    await HibernateOffAsync();
                    break;
                case "admin":
                    RestartAsAdmin();
                    break;
                case "remove":
                    if (ids.TryGetValue(m.GetProperty("id").GetInt32(), out var target)) await RemoveNodeAsync(target, Permanent(m));
                    break;
                case "findOld":
                    var months = Math.Clamp(m.GetProperty("months").GetInt32(), 1, 240);
                    var mb = Math.Clamp(m.GetProperty("mb").GetInt64(), 1, 1_000_000);
                    olds = await Task.Run(() => Scanner.FindOld(months, mb << 20, CancellationToken.None));
                    SendFound("old", olds);
                    break;
                case "removeOld":
                    olds = await RemoveFoundAsync(olds, m);
                    SendFound("old", olds);
                    break;
                case "findType":
                    await FindTypeAsync(m);
                    break;
                case "cancelType":
                    typeCts?.Cancel();
                    break;
                case "removeType":
                    typed = await RemoveFoundAsync(typed, m);
                    SendFound("typed", typed);
                    break;
                case "findDup":
                    await FindDupAsync(m);
                    break;
                case "cancelDup":
                    dupCts?.Cancel();
                    break;
                case "removeDup":
                    var left = await RemoveFoundAsync(dups.SelectMany(g => g).ToList(), m, keepOnePerGroup: true);
                    var stillThere = left.ToHashSet();
                    dups = dups.Select(g => g.Where(stillThere.Contains).ToList()).Where(g => g.Count > 1).ToList();
                    SendDups();
                    break;
                case "clean":
                    var names = m.GetProperty("names").EnumerateArray().Select(x => x.GetString()).ToHashSet();
                    var pick = targets.Where(t => names.Contains(t.Name)).ToList();
                    var got = await Task.Run(() => pick.Sum(Cleanup.Clean));
                    Toast($"Freed {H(got)} ✓", $"تم تحرير {H(got)} ✓");
                    await MeasureCleanAsync();
                    SendUsageOfCurrent();
                    break;
            }
        }
        catch (Exception ex)
        {
            Toast("Something went wrong: " + ex.Message, "حدث خطأ: " + ex.Message);
        }
    }

    /// <summary>Things that would run code instead of showing something. Spacewise never starts these; it shows them in their folder.</summary>
    static readonly HashSet<string> RunsCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".bat", ".cmd", ".com", ".scr", ".pif", ".ps1", ".psm1",
        ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".cpl", ".msc", ".jar", ".lnk", ".url", ".reg", ".inf", ".sys", ".dll",
    };

    void Launch(string path)
    {
        if (!File.Exists(path)) { Toast("That file isn't there anymore.", "الملف لم يعد موجودًا."); return; }
        if (RunsCode.Contains(Path.GetExtension(path)))
        {
            Toast("Spacewise doesn't run programs or scripts, so it's shown in its folder instead.", "لا يشغّل Spacewise البرامج أو السكربتات، لذلك عرضناه في مجلده بدلًا من ذلك.");
            Process.Start("explorer.exe", $"/select,\"{path}\"");
            return;
        }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { Toast("No program on this PC opens this kind of file.", "لا يوجد برنامج على هذا الجهاز يفتح هذا النوع من الملفات."); }
    }

    static bool Permanent(JsonElement m) => m.TryGetProperty("mode", out var mode) && mode.GetString() == "perm";

    IEnumerable<OldFile> AllFound() => olds.Concat(typed).Concat(dups.SelectMany(g => g));

    // ---------------- drives & usage ----------------

    string lastPath = "";

    void SendDrives()
    {
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
            .Select(d => new { path = d.RootDirectory.FullName, letter = d.Name.TrimEnd('\\') }).ToList();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        drives.Add(new { path = home, letter = "" }); // "" = the user folder
        lastPath = drives[0].path;
        Send(new { type = "drives", drives, usage = Usage(lastPath) });
    }

    static object Usage(string path)
    {
        var root = Path.GetPathRoot(path);
        var isDrive = root is not null && string.Equals(root.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        if (!isDrive) return new { letter = "", used = -1L, total = -1L };
        var d = new DriveInfo(root!);
        return new { letter = d.Name.TrimEnd('\\'), used = d.TotalSize - d.AvailableFreeSpace, total = d.TotalSize };
    }

    void SendUsageOfCurrent() { if (lastPath != "") Send(new { type = "usage", usage = Usage(lastPath) }); }

    // ---------------- scan & explore ----------------

    static string SaveFile(string path) =>
        Path.Combine(DataDir, "scans", Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(path.TrimEnd('\\').ToLowerInvariant())))[..16] + ".bin");

    async Task LoadSavedScanAsync(string path)
    {
        var file = SaveFile(path);
        var saved = File.Exists(file) ? await Task.Run(() => Scanner.Load(file)) : null;
        ids.Clear(); idOf.Clear();
        if (saved is null) { tree = null; Send(new { type = "noScan" }); return; }
        (tree, treeWhen) = saved.Value;
        Send(new { type = "scanInfo", when = treeWhen.ToString("yyyy-MM-ddTHH:mm") });
        SendDir(tree);
    }

    void SaveTreeLater()
    {
        if (tree is null) return;
        var (root, when, file) = (tree, treeWhen, SaveFile(tree.FullPath));
        _ = Task.Run(async () =>
        {
            await saving.WaitAsync();
            try { Scanner.Save(root, when, file); } catch { } finally { saving.Release(); }
        });
    }

    async Task ScanAsync(string path)
    {
        if (cts is not null) return;
        cts = new CancellationTokenSource();
        lastPath = path;
        Send(new { type = "usage", usage = Usage(path) });
        // on a whole drive we know how much is used, so progress can be a real percentage
        long expected = -1;
        try { var r = Path.GetPathRoot(path); if (r is not null && string.Equals(r.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) { var d = new DriveInfo(r); expected = d.TotalSize - d.AvailableFreeSpace; } } catch { }
        Send(new { type = "scanning", on = true, path, files = 0, bytes = 0, expected });
        var sw = Stopwatch.StartNew();
        try
        {
            var token = cts.Token;
            var root = await Task.Run(() => Scanner.Scan(path, (where, n, b) => Send(new { type = "scanning", on = true, path = where, files = n, bytes = b, expected, secs = (int)sw.Elapsed.TotalSeconds }), token));
            ids.Clear(); idOf.Clear();
            (tree, treeWhen) = (root, DateTime.Now);
            Send(new { type = "scanInfo", when = treeWhen.ToString("yyyy-MM-ddTHH:mm"), fresh = true });
            SendDir(root);
            SaveTreeLater();
            var secs = sw.Elapsed.TotalSeconds;
            Toast($"Done in {secs:0}s · {root.Files:N0} files", $"انتهى في {secs:0} ث · {root.Files:N0} ملف");
        }
        catch (OperationCanceledException) { Toast("Scan stopped", "توقف الفحص"); }
        finally
        {
            cts = null;
            Send(new { type = "scanning", on = false });
        }
    }

    int Id(Node n)
    {
        if (idOf.TryGetValue(n, out var id)) return id;
        id = idOf.Count + 1;
        idOf[n] = id; ids[id] = n;
        return id;
    }

    object Child(Node n, bool withWhere = false)
    {
        var info = n.IsGroup ? Knowledge.SmallFiles : Knowledge.Explain(n.FullPath, n.IsDir);
        return new
        {
            id = Id(n), name = n.Name, size = n.Size, files = n.Files, isDir = n.IsDir, isGroup = n.IsGroup,
            verdict = (int)info.Verdict, what = info.What, whatAr = info.Ar,
            lastUsed = n.LastUsed == default ? null : n.LastUsed.ToString("yyyy-MM-dd"),
            recyclable = !n.IsGroup && Cleanup.CanRecycle(n.FullPath, n.Size),
            deletable = !n.IsGroup && Knowledge.Deletable(info, n.IsDir),
            hiberfil = !n.IsDir && n.Name.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(n.FullPath)?.Length <= 3,
            where = withWhere ? (n.IsGroup ? n.FullPath : Path.GetDirectoryName(n.FullPath)) : null,
        };
    }

    void SendDir(Node dir) =>
        Send(new { type = "dir", dir = new { id = Id(dir), path = dir.FullPath, size = dir.Size, hasParent = dir.Parent is not null, children = dir.Children.Select(c => Child(c)).ToList() } });

    void SendSearch(string q)
    {
        q = q.Trim();
        if (tree is null || q.Length == 0) return;
        var found = Scanner.Search(tree, q, 300);
        Send(new { type = "search", q, children = found.Select(c => Child(c, withWhere: true)).ToList() });
    }

    async Task RemoveNodeAsync(Node n, bool permanent)
    {
        if (n.IsGroup || !Knowledge.Deletable(Knowledge.Explain(n.FullPath, n.IsDir), n.IsDir)) return;
        var r = await Task.Run(() => Cleanup.Remove([(n.FullPath, n.IsDir, n.Size)], permanent));
        if (r.NoBin.Count > 0) { Toast("This can't go to the Recycle Bin (too big, or the drive has none). Use Delete permanently if you're sure.", "لا يمكن نقله لسلة المحذوفات (حجمه كبير أو القرص بدون سلة). استخدم «احذف نهائيًا» إذا كنت متأكدًا."); return; }
        if (r.Done == 0) { Toast("Couldn't delete it. It may be open in another program.", "تعذّر حذفه. قد يكون مفتوحًا في برنامج آخر."); return; }
        for (var p = n.Parent; p is not null; p = p.Parent) { p.Size -= r.Freed; p.Files -= n.Files; }
        n.Parent?.Children.Remove(n);
        if (permanent) Toast($"Deleted for good ({H(r.Freed)}). The space is free now.", $"حُذف نهائيًا ({H(r.Freed)}). المساحة صارت متاحة الآن.");
        else Toast($"Moved to the Recycle Bin ({H(r.Freed)}). Empty it in Quick clean to free the space.", $"نُقل إلى سلة المحذوفات ({H(r.Freed)}). أفرغها من «تنظيف سريع» لتتحرر المساحة.");
        _ = MeasureCleanAsync();
        if (n.Parent is not null) SendDir(n.Parent);
        SendUsageOfCurrent();
        SaveTreeLater();
    }

    // ---------------- old files, find by type, duplicates & cleanup ----------------

    static object FileRow(OldFile o) => new
    {
        path = o.FullPath, name = Path.GetFileName(o.FullPath), dir = Path.GetDirectoryName(o.FullPath), size = o.Size,
        lastUsed = o.LastUsed.ToString("yyyy-MM-dd"), recyclable = Cleanup.CanRecycle(o.FullPath, o.Size),
    };

    void SendFound(string type, List<OldFile> list) => Send(new
    {
        type,
        count = list.Count,
        total = list.Sum(o => o.Size),
        // the page shows the biggest ones; thousands of rows would only slow it down
        files = list.Take(3000).Select(FileRow),
    });

    void SendDups() => Send(new
    {
        type = "dups",
        count = dups.Count,
        wasted = dups.Sum(g => g[0].Size * (g.Count - 1)),
        groups = dups.Take(500).Select(g => new { size = g[0].Size, files = g.Select(FileRow) }),
    });

    /// <summary>Delete the chosen files. Only files this window found, never an arbitrary path from the page.</summary>
    async Task<List<OldFile>> RemoveFoundAsync(List<OldFile> list, JsonElement m, bool keepOnePerGroup = false)
    {
        var permanent = Permanent(m);
        var paths = m.GetProperty("paths").EnumerateArray().Select(p => p.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chosen = list.Where(o => paths.Contains(o.FullPath)).ToList();
        // duplicates: never delete every copy of a file
        if (keepOnePerGroup)
            foreach (var g in dups.Where(g => g.All(chosen.Contains))) chosen.Remove(g[0]);
        var r = await Task.Run(() => Cleanup.Remove(chosen.Select(o => (o.FullPath, false, o.Size)), permanent));
        var notes = "";
        if (r.InUse.Count > 0) notes += T($" {r.InUse.Count} were in use.", $" {r.InUse.Count} منها مفتوحة حاليًا.");
        if (r.NoBin.Count > 0) notes += T($" {r.NoBin.Count} can't go to the Recycle Bin (too big, or the drive has none); use Delete permanently for those.",
                                           $" {r.NoBin.Count} لا يمكن نقلها لسلة المحذوفات (حجمها كبير أو القرص بدون سلة)؛ استخدم «احذف نهائيًا» لها.");
        if (permanent) Toast($"Deleted {r.Done} files for good ({H(r.Freed)}). The space is free now.{notes}", $"حُذف {r.Done} ملف نهائيًا ({H(r.Freed)}). المساحة صارت متاحة الآن.{notes}");
        else Toast($"Moved {r.Done} files ({H(r.Freed)}) to the Recycle Bin. Empty it in Quick clean to free the space.{notes}",
                   $"نُقل {r.Done} ملف ({H(r.Freed)}) إلى سلة المحذوفات. أفرغها من «تنظيف سريع» لتتحرر المساحة.{notes}");
        _ = MeasureCleanAsync();
        SendUsageOfCurrent();
        var kept = r.InUse.Concat(r.NoBin).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return list.Where(o => !chosen.Contains(o) || kept.Contains(o.FullPath)).ToList();
    }

    static readonly Regex ExtRx = new(@"^\.[a-z0-9]{1,12}$", RegexOptions.IgnoreCase);

    async Task FindTypeAsync(JsonElement m)
    {
        if (typeCts is not null) return;
        var everything = m.TryGetProperty("all", out var a) && a.GetBoolean();
        var exts = everything ? null : m.GetProperty("exts").EnumerateArray().Select(x => x.GetString() ?? "").Where(x => ExtRx.IsMatch(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var where = m.GetProperty("path").GetString()!;
        var minBytes = (long)(Math.Clamp(m.GetProperty("mb").GetDouble(), 0, 1_000_000) * 1024 * 1024);
        if (exts is { Count: 0 } || !Directory.Exists(where)) return;
        typeCts = new CancellationTokenSource();
        try
        {
            var token = typeCts.Token;
            var found = await Task.Run(() => Scanner.FindByType(where, exts, minBytes, (dir, n) => Send(new { type = "typeProgress", path = dir, files = n }), token));
            typed = everything ? found.Take(500).ToList() : found;
            SendFound("typed", typed);
        }
        catch (OperationCanceledException) { Send(new { type = "typeStopped" }); }
        finally { typeCts = null; }
    }

    async Task FindDupAsync(JsonElement m)
    {
        if (dupCts is not null) return;
        var where = m.GetProperty("path").GetString()!;
        var minBytes = (long)(Math.Clamp(m.GetProperty("mb").GetDouble(), 0, 1_000_000) * 1024 * 1024);
        if (!Directory.Exists(where)) return;
        dupCts = new CancellationTokenSource();
        try
        {
            var token = dupCts.Token;
            dups = await Task.Run(() => Scanner.FindDuplicates(where, minBytes, (phase, done, total) => Send(new { type = "dupProgress", phase, done, total }), token));
            SendDups();
        }
        catch (OperationCanceledException) { Send(new { type = "dupStopped" }); }
        finally { dupCts = null; }
    }

    async Task HibernateOffAsync()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powercfg.exe", "/hibernate off") { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            if (p is not null) await p.WaitForExitAsync();
            var hib = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "hiberfil.sys");
            if (File.Exists(hib)) { Toast("Hibernation is still on.", "السبات ما زال مفعّلًا."); return; }
            Toast("Hibernation is off and its file is gone. Turn it back on any time with: powercfg /h on",
                  "تم إيقاف السبات وحُذف ملفه. يمكنك إعادة تفعيله في أي وقت بالأمر: powercfg /h on");
            if (tree is not null)
            {
                var node = tree.Children.FirstOrDefault(c => c.Name.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase));
                if (node is not null) { tree.Size -= node.Size; tree.Files--; tree.Children.Remove(node); SendDir(tree); SaveTreeLater(); }
            }
            SendUsageOfCurrent();
        }
        catch (System.ComponentModel.Win32Exception) { Toast("Cancelled.", "تم الإلغاء."); }
    }

    void RestartAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Application.ExecutablePath) { Verb = "runas", UseShellExecute = true });
            Close();
        }
        catch (System.ComponentModel.Win32Exception) { Toast("Cancelled.", "تم الإلغاء."); }
    }

    async Task MeasureCleanAsync()
    {
        targets = Cleanup.Targets();
        await Task.Run(() => Parallel.ForEach(targets, Cleanup.Measure));
        Send(new
        {
            type = "cleans", isAdmin = IsAdmin,
            targets = targets.Select(t => new { name = t.Name, what = t.What, nameAr = t.NameAr, whatAr = t.WhatAr, size = t.Size, isRecycleBin = t.IsRecycleBin, needsAdmin = t.NeedsAdmin }),
        });
    }
}
