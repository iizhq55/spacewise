namespace Spacewise;

/// <summary>How safe it is to delete something. The numbers are sent to the page, so only add at the end.</summary>
public enum Verdict
{
    /// <summary>Part of Windows. Deleting it can break the PC.</summary>
    DontTouch,
    /// <summary>Core system data and settings (AppData, ProgramData, hibernation file…). Leave it unless you know what it is.</summary>
    Careful,
    /// <summary>Your own files. Fine to delete what you no longer need.</summary>
    Yours,
    /// <summary>Caches and temporary files. Safe to delete; they are recreated when needed.</summary>
    Safe,
    /// <summary>Nothing known about it.</summary>
    Unknown,
    /// <summary>An installed program or game, or its data. Remove it by uninstalling.</summary>
    App,
}

/// <summary>What something is, in English and Arabic.</summary>
public sealed record Info(Verdict Verdict, string What, string Ar);

/// <summary>Plain-language knowledge about common Windows folders and files.</summary>
public static class Knowledge
{
    static readonly string Win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static readonly string SysDrive = Path.GetPathRoot(Win) ?? @"C:\";
    static readonly string ProgramFiles = Path.Combine(SysDrive, "Program Files");
    static readonly string ProgramFiles86 = Path.Combine(SysDrive, "Program Files (x86)");
    static readonly string ProgramData = Path.Combine(SysDrive, "ProgramData");
    static readonly string XboxGames = Path.Combine(SysDrive, "XboxGames");
    static readonly string UsersDir = Path.Combine(SysDrive, "Users");
    static readonly Info MyAccount = new(Verdict.Careful, "Your account folder: your files, settings and your programs' data. Clean what's inside, never the folder itself.", "مجلد حسابك: ملفاتك وإعداداتك وبيانات برامجك. نظّف ما بداخله، لا المجلد نفسه.");
    static readonly Info OtherAccount = new(Verdict.Careful, "Another account on this PC (or one Windows uses). Remove accounts from Settings > Accounts.", "حساب آخر على هذا الجهاز (أو حساب يستخدمه ويندوز). احذف الحسابات من الإعدادات > الحسابات.");
    static readonly string LocalLow = Path.Combine(Home, "AppData", "LocalLow");
    public static readonly string AppData = Path.Combine(Home, "AppData");

    static Info Temp => new(Verdict.Safe, "Your temporary files. Safe to delete; programs recreate what they need.", "ملفاتك المؤقتة. حذفها آمن؛ البرامج تعيد إنشاء ما تحتاجه.");

    // Exact paths, checked first. Longest match wins, so specific entries beat their parents.
    static readonly (string Path, Info Info)[] Exact =
    [
        (Win, new(Verdict.DontTouch, "Windows itself. Deleting anything here can stop your PC from working.", "ويندوز نفسه. حذف أي شيء هنا قد يوقف جهازك عن العمل.")),
        (Path.Combine(Win, "WinSxS"), new(Verdict.DontTouch, "Windows' component store. It looks huge, but much of it is shared with other folders. Shrink it only with Disk Cleanup.", "مخزن مكوّنات ويندوز. يبدو ضخمًا لكن أغلبه مشترك مع مجلدات أخرى. لا تصغّره إلا بأداة تنظيف القرص (Disk Cleanup).")),
        (Path.Combine(Win, "Temp"), new(Verdict.Safe, "Temporary files Windows and installers left behind. Safe to delete (some need admin rights).", "ملفات مؤقتة تركها ويندوز وبرامج التثبيت. حذفها آمن (بعضها يحتاج صلاحيات المسؤول).")),
        (Path.Combine(Win, "SoftwareDistribution", "Download"), new(Verdict.Safe, "Windows Update downloads that are already installed. Safe to delete (needs admin rights).", "تحديثات ويندوز التي تم تثبيتها. حذفها آمن (يحتاج صلاحيات المسؤول).")),
        (Path.Combine(Win, "Installer"), new(Verdict.DontTouch, "Windows needs these to repair or uninstall programs. Don't delete.", "يحتاجها ويندوز لإصلاح البرامج أو إلغاء تثبيتها. لا تحذفها.")),
        (Path.Combine(Win, "System32"), new(Verdict.DontTouch, "The core of Windows. Never delete anything here.", "قلب ويندوز. لا تحذف أي شيء هنا أبدًا.")),
        (ProgramFiles, new(Verdict.DontTouch, "Where installed programs live. To free space, uninstall programs from Settings > Apps instead of deleting folders.", "مكان البرامج المثبّتة. لتوفير مساحة، ألغِ تثبيت البرامج من الإعدادات > التطبيقات بدل حذف المجلدات.")),
        (ProgramFiles86, new(Verdict.DontTouch, "Where installed 32-bit programs live. Uninstall from Settings > Apps instead of deleting folders.", "مكان البرامج المثبّتة (32-بت). ألغِ تثبيتها من الإعدادات > التطبيقات بدل حذف المجلدات.")),
        (ProgramData, new(Verdict.Careful, "Settings and data that programs share between all users. Hidden on purpose; leave it alone.", "إعدادات وبيانات تتشاركها البرامج بين كل المستخدمين. مخفي عمدًا؛ اتركه.")),
        (Path.Combine(ProgramData, "Microsoft"), new(Verdict.Careful, "Windows and Microsoft data shared by all users. Leave it alone.", "بيانات ويندوز ومايكروسوفت المشتركة بين كل المستخدمين. اتركه.")),
        (Path.Combine(ProgramData, "Package Cache"), new(Verdict.Careful, "Installer copies that programs need to repair or uninstall themselves. Don't delete.", "نسخ من ملفات التثبيت تحتاجها البرامج لإصلاح نفسها أو إلغاء تثبيتها. لا تحذفها.")),
        (XboxGames, new(Verdict.App, "Games installed from the Xbox app or Game Pass. Uninstall them from the Xbox app.", "ألعاب مثبّتة من تطبيق Xbox أو Game Pass. ألغِ تثبيتها من تطبيق Xbox.")),
        (UsersDir, new(Verdict.Careful, "Every account's files on this PC.", "ملفات كل الحسابات على هذا الجهاز.")),
        (Path.Combine(SysDrive, "System Volume Information"), new(Verdict.DontTouch, "System Restore points. Manage them in System Properties > System Protection.", "نقاط استعادة النظام. تحكّم بها من خصائص النظام > حماية النظام.")),
        (Path.Combine(SysDrive, "$Recycle.Bin"), new(Verdict.Safe, "The Recycle Bin. Empty it to get this space back.", "سلة المحذوفات. أفرغها لتسترجع هذه المساحة.")),
        (Path.Combine(SysDrive, "Windows.old"), new(Verdict.Safe, "Your previous Windows installation, kept after an upgrade. Remove it from Settings > System > Storage > Temporary files.", "نسخة ويندوز السابقة، محفوظة بعد الترقية. احذفها من الإعدادات > النظام > التخزين > الملفات المؤقتة.")),
        (Path.Combine(SysDrive, "$WINDOWS.~BT"), new(Verdict.Safe, "Leftovers from a Windows upgrade. Remove from Settings > System > Storage > Temporary files.", "بقايا ترقية ويندوز. احذفها من الإعدادات > النظام > التخزين > الملفات المؤقتة.")),
        (Path.Combine(SysDrive, "pagefile.sys"), new(Verdict.DontTouch, "Windows' extra memory on disk. Windows manages it; don't delete.", "ذاكرة إضافية يستخدمها ويندوز على القرص. ويندوز يديرها؛ لا تحذفها.")),
        (Path.Combine(SysDrive, "swapfile.sys"), new(Verdict.DontTouch, "Memory file for Store apps. Windows manages it; don't delete.", "ملف ذاكرة لتطبيقات المتجر. ويندوز يديره؛ لا تحذفه.")),
        (Path.Combine(SysDrive, "hiberfil.sys"), new(Verdict.Careful, "Saves your session for Hibernate and Fast Startup. If you never hibernate, turning it off (powercfg /h off) frees this space.", "يحفظ جلستك لوضع السبات وبدء التشغيل السريع. إذا كنت لا تستخدم السبات، فإيقافه (powercfg /h off) يوفّر هذه المساحة.")),
        (AppData, new(Verdict.Careful, "Your programs' settings and data. Don't delete the folder; some caches inside are safe to clean.", "إعدادات برامجك وبياناتها. لا تحذف المجلد؛ بعض الكاش داخله آمن تنظيفه.")),
        (Local, new(Verdict.Careful, "Program data kept only on this PC (caches, game saves, app data).", "بيانات البرامج المحفوظة على هذا الجهاز فقط (كاش، حفظ الألعاب، بيانات التطبيقات).")),
        (Roaming, new(Verdict.Careful, "Program settings that follow your account (browser profiles, app settings). Deleting resets those programs.", "إعدادات البرامج المرتبطة بحسابك (ملفات المتصفح، إعدادات التطبيقات). حذفها يعيد تلك البرامج لوضعها الأول.")),
        (LocalLow, new(Verdict.Careful, "Data for programs running with low permissions, often games and browsers.", "بيانات برامج تعمل بصلاحيات محدودة، غالبًا ألعاب ومتصفحات.")),
        (Path.Combine(Local, "Microsoft"), new(Verdict.Careful, "Data for Windows and Microsoft apps. Leave it alone; some caches inside are safe to clean.", "بيانات ويندوز وتطبيقات مايكروسوفت. اتركه؛ بعض الكاش داخله آمن تنظيفه.")),
        (Path.Combine(Roaming, "Microsoft"), new(Verdict.Careful, "Settings for Windows and Microsoft apps. Leave it alone.", "إعدادات ويندوز وتطبيقات مايكروسوفت. اتركه.")),
        (Path.GetTempPath().TrimEnd('\\'), Temp),
        (Path.Combine(Local, "Temp"), Temp),
        (Path.Combine(Local, "CrashDumps"), new(Verdict.Safe, "Crash reports from programs. Safe to delete.", "تقارير أعطال البرامج. حذفها آمن.")),
        (Path.Combine(Local, "Microsoft", "Windows", "INetCache"), new(Verdict.Safe, "Internet cache. Safe to delete.", "كاش الإنترنت. حذفه آمن.")),
        (Path.Combine(Local, "Microsoft", "Windows", "Explorer"), new(Verdict.Safe, "Thumbnail previews File Explorer saved. Safe to delete; they're rebuilt.", "معاينات الصور المصغّرة التي حفظها مستكشف الملفات. حذفها آمن؛ تُبنى من جديد.")),
        (Path.Combine(Local, "Packages"), new(Verdict.Careful, "Data for Microsoft Store apps. Deleting breaks those apps; reset them from Settings > Apps instead.", "بيانات تطبيقات متجر مايكروسوفت. حذفها يعطّل التطبيقات؛ أعد ضبطها من الإعدادات > التطبيقات بدلًا من ذلك.")),
        (Path.Combine(Local, "npm-cache"), new(Verdict.Safe, "Downloaded npm packages kept for reuse. Safe to delete; they download again when needed.", "حزم npm محمّلة ومحفوظة لإعادة الاستخدام. حذفها آمن؛ تُحمَّل من جديد عند الحاجة.")),
        (Path.Combine(Local, "pip", "Cache"), new(Verdict.Safe, "Downloaded Python packages kept for reuse. Safe to delete.", "حزم بايثون محمّلة ومحفوظة لإعادة الاستخدام. حذفها آمن.")),
        (Path.Combine(Local, "NuGet"), new(Verdict.Safe, "NuGet download cache. Safe to delete.", "كاش تحميلات NuGet. حذفه آمن.")),
        (Path.Combine(Home, ".nuget", "packages"), new(Verdict.Safe, ".NET packages kept for reuse. Safe to delete; they download again when needed.", "حزم ‎.NET محفوظة لإعادة الاستخدام. حذفها آمن؛ تُحمَّل من جديد عند الحاجة.")),
        (Path.Combine(Home, ".cache"), new(Verdict.Safe, "Caches from developer tools. Usually safe to delete.", "كاش أدوات المطوّرين. حذفه آمن غالبًا.")),
        (Path.Combine(Home, "Downloads"), new(Verdict.Yours, "Things you downloaded. Old installers and archives here are often safe to delete.", "الأشياء التي حمّلتها. ملفات التثبيت والملفات المضغوطة القديمة هنا غالبًا آمن حذفها.")),
        (Path.Combine(Home, "Desktop"), new(Verdict.Yours, "Your desktop files.", "ملفات سطح المكتب.")),
        (Path.Combine(Home, "Documents"), new(Verdict.Yours, "Your documents. Some programs and games also save here, so check before deleting.", "مستنداتك. بعض البرامج والألعاب تحفظ هنا أيضًا، فتأكد قبل الحذف.")),
        (Path.Combine(Home, "Pictures"), new(Verdict.Yours, "Your photos and images.", "صورك.")),
        (Path.Combine(Home, "Videos"), new(Verdict.Yours, "Your videos and screen recordings.", "فيديوهاتك وتسجيلات الشاشة.")),
        (Path.Combine(Home, "Music"), new(Verdict.Yours, "Your music.", "موسيقاك وملفاتك الصوتية.")),
        (Path.Combine(Home, "OneDrive"), new(Verdict.Yours, "Your files synced with OneDrive. Deleting here also deletes them in the cloud and on your other devices.", "ملفاتك المتزامنة مع OneDrive. حذفها هنا يحذفها من السحابة ومن أجهزتك الأخرى أيضًا.")),
    ];

    // Folders whose direct children each belong to one program.
    static readonly string[] ProgramRoots = [ProgramFiles, ProgramFiles86, XboxGames];
    static readonly string[] ProgramDataRoots = [ProgramData, Local, Roaming, LocalLow];

    // Things inside Program Files that are part of Windows, not a program you installed.
    static readonly HashSet<string> WindowsParts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Common Files", "Internet Explorer", "WindowsApps", "ModifiableWindowsApps", "Microsoft.NET", "dotnet",
        "Reference Assemblies", "MSBuild", "Uninstall Information", "Microsoft Update Health Tools", "desktop.ini",
    };

    static Info Program(string name) => new(Verdict.App,
        $"“{name}”, an installed program. To free this space, uninstall it from Settings > Apps instead of deleting the folder.",
        $"«{name}» برنامج مثبّت. لتوفير مساحته، ألغِ تثبيته من الإعدادات > التطبيقات بدل حذف المجلد.");

    static Info ProgramStuff(string name) => new(Verdict.App,
        $"Data and settings of “{name}”. Deleting it resets that program. If you don't use the program anymore, uninstall it from Settings > Apps.",
        $"بيانات وإعدادات «{name}». حذفها يعيد البرنامج لوضعه الأول. إذا لم تعد تستخدمه، ألغِ تثبيته من الإعدادات > التطبيقات.");

    static readonly Info WindowsPart = new(Verdict.DontTouch, "Part of Windows. Don't delete.", "جزء من ويندوز. لا تحذفه.");

    // Folder names recognised anywhere on the disk.
    static readonly (string Name, Info Info)[] Names =
    [
        ("node_modules", new(Verdict.Safe, "A JavaScript project's downloaded libraries. Safe to delete if you don't work on that project; 'npm install' brings it back.", "مكتبات مشروع JavaScript المحمّلة. حذفها آمن إذا لم تعد تعمل على المشروع؛ الأمر npm install يعيدها.")),
        ("Cache", new(Verdict.Safe, "A program's cache. Usually safe to delete; it's rebuilt.", "كاش برنامج. حذفه آمن غالبًا؛ يُبنى من جديد.")),
        ("Code Cache", new(Verdict.Safe, "Browser or app code cache. Safe to delete.", "كاش أكواد المتصفح أو التطبيق. حذفه آمن.")),
        ("GPUCache", new(Verdict.Safe, "Graphics cache. Safe to delete.", "كاش الرسوميات. حذفه آمن.")),
        ("ShaderCache", new(Verdict.Safe, "Game or graphics shader cache. Safe to delete; games rebuild it (first launch may be slower).", "كاش مظلّلات الألعاب والرسوميات. حذفه آمن؛ الألعاب تعيد بناءه (أول تشغيل قد يكون أبطأ).")),
        ("DXCache", new(Verdict.Safe, "DirectX shader cache. Safe to delete.", "كاش مظلّلات DirectX. حذفه آمن.")),
        ("__pycache__", new(Verdict.Safe, "Python's compiled files. Safe to delete.", "ملفات بايثون المترجمة. حذفها آمن.")),
        (".git", new(Verdict.Yours, "A project's full history (Git). Deleting it loses the history.", "سجل المشروع الكامل (Git). حذفه يضيّع السجل.")),
        ("steamapps", new(Verdict.App, "Steam games. Uninstall games from Steam instead of deleting folders.", "ألعاب Steam. ألغِ تثبيت الألعاب من Steam بدل حذف المجلدات.")),
        ("Steam", new(Verdict.App, "Steam and its games. Uninstall games from Steam.", "Steam وألعابه. ألغِ تثبيت الألعاب من Steam.")),
        ("Epic Games", new(Verdict.App, "Epic Games and its games. Uninstall games from the Epic launcher.", "Epic Games وألعابه. ألغِ تثبيت الألعاب من مشغّل Epic.")),
        ("Riot Games", new(Verdict.App, "Riot Games (Valorant, League of Legends). Uninstall from Settings > Apps.", "ألعاب Riot (فالورانت، ليق أوف ليجندز). ألغِ تثبيتها من الإعدادات > التطبيقات.")),
        ("Ubisoft", new(Verdict.App, "Ubisoft Connect and its games. Uninstall games from Ubisoft Connect.", "Ubisoft Connect وألعابه. ألغِ تثبيت الألعاب من Ubisoft Connect.")),
        ("EA Games", new(Verdict.App, "EA games. Uninstall them from the EA app.", "ألعاب EA. ألغِ تثبيتها من تطبيق EA.")),
        ("Battle.net", new(Verdict.App, "Battle.net and Blizzard games. Uninstall games from Battle.net.", "Battle.net وألعاب Blizzard. ألغِ تثبيت الألعاب من Battle.net.")),
        ("Rockstar Games", new(Verdict.App, "Rockstar games. Uninstall them from the Rockstar launcher.", "ألعاب Rockstar. ألغِ تثبيتها من مشغّل Rockstar.")),
        ("GOG Galaxy", new(Verdict.App, "GOG Galaxy and its games. Uninstall games from GOG Galaxy.", "GOG Galaxy وألعابه. ألغِ تثبيت الألعاب من GOG Galaxy.")),
    ];

    static Info Archive => new(Verdict.Yours, "A compressed archive. Delete it if you already extracted it.", "ملف مضغوط. احذفه إذا كنت فككته من قبل.");
    static Info Video => new(Verdict.Yours, "A video.", "فيديو.");

    static readonly Dictionary<string, Info> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".tmp"] = new(Verdict.Safe, "A temporary file. Usually safe to delete.", "ملف مؤقت. حذفه آمن غالبًا."),
        [".log"] = new(Verdict.Safe, "A log file programs write. Usually safe to delete.", "ملف سجل تكتبه البرامج. حذفه آمن غالبًا."),
        [".dmp"] = new(Verdict.Safe, "A crash dump. Safe to delete.", "ملف عطل (crash dump). حذفه آمن."),
        [".iso"] = new(Verdict.Yours, "A disk image (often an installer). Delete it if you don't need it anymore.", "صورة قرص (غالبًا ملف تثبيت). احذفها إذا لم تعد تحتاجها."),
        [".exe"] = new(Verdict.Yours, "A program or installer. Installers in Downloads are usually safe to delete after installing.", "برنامج أو ملف تثبيت. ملفات التثبيت في التنزيلات آمن حذفها غالبًا بعد التثبيت."),
        [".msi"] = new(Verdict.Yours, "An installer. Usually safe to delete after installing.", "ملف تثبيت. حذفه آمن غالبًا بعد التثبيت."),
        [".zip"] = Archive, [".rar"] = Archive, [".7z"] = Archive,
        [".mp4"] = Video, [".mkv"] = Video, [".mov"] = Video,
        [".vhdx"] = new(Verdict.Careful, "A virtual disk (WSL, Docker or a virtual machine). Deleting it deletes everything inside.", "قرص افتراضي (WSL أو Docker أو جهاز وهمي). حذفه يحذف كل ما بداخله."),
        [".sys"] = new(Verdict.DontTouch, "A Windows system file. Don't delete.", "ملف نظام ويندوز. لا تحذفه."),
        [".dll"] = new(Verdict.DontTouch, "Part of a program. Don't delete on its own.", "جزء من برنامج. لا تحذفه وحده."),
    };

    public static readonly Info SmallFiles = new(Verdict.Unknown, "Lots of small files in this folder, added together.", "ملفات صغيرة كثيرة في هذا المجلد، مجموعة مع بعض.");

    /// <summary>What a path is and whether it is safe to delete.</summary>
    public static Info Explain(string fullPath, bool isDir)
    {
        var p = fullPath.TrimEnd('\\');
        Info? best = null;
        var bestRoot = "";
        foreach (var (path, info) in Exact)
        {
            var root = path.TrimEnd('\\');
            // an entry for this exact item always wins (e.g. hiberfil.sys beats the ".sys" rule)
            if (p.Equals(root, StringComparison.OrdinalIgnoreCase)) return info;
            if (p.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) && root.Length > bestRoot.Length)
            {
                best = info; bestRoot = root;
            }
        }
        // C:\Users: the account folders themselves are system folders; what's inside them is judged on its own
        if (best is not null && bestRoot.Equals(UsersDir, StringComparison.OrdinalIgnoreCase))
        {
            var rest = p[(bestRoot.Length + 1)..].Split('\\');
            best = rest.Length > 1 ? null
                : rest[0].Equals(Path.GetFileName(Home), StringComparison.OrdinalIgnoreCase) ? MyAccount : OtherAccount;
            if (best is not null) return best;
        }
        // Inside Program Files, AppData or ProgramData, each folder belongs to one program.
        if (best is not null)
        {
            var first = p[(bestRoot.Length + 1)..].Split('\\')[0];
            if (ProgramRoots.Any(r => r.Equals(bestRoot, StringComparison.OrdinalIgnoreCase)))
                best = WindowsParts.Contains(first) || first.StartsWith("Windows", StringComparison.OrdinalIgnoreCase) ? WindowsPart : Program(first);
            else if (ProgramDataRoots.Any(r => r.Equals(bestRoot, StringComparison.OrdinalIgnoreCase)))
                best = ProgramStuff(first);
        }
        // A folder name that marks a cache (or a game launcher) beats a broad parent.
        foreach (var part in p.Split('\\').Reverse())
        {
            foreach (var (name, info) in Names)
            {
                if (!part.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                if (best is null || best.Verdict is Verdict.Careful or Verdict.Yours or Verdict.Unknown or Verdict.App)
                    return InheritDanger(best, info);
            }
        }
        if (!isDir && Extensions.TryGetValue(Path.GetExtension(p), out var ext))
        {
            if (best is null) return ext;
            // inside Windows, a program, or a temp folder, the folder decides
            if (best.Verdict is Verdict.DontTouch or Verdict.Safe or Verdict.App) return best;
            // inside program data, only a stricter file type wins
            if (best.Verdict == Verdict.Careful) return ext.Verdict == Verdict.DontTouch ? ext : best;
            return ext;
        }
        return best ?? (isDir
            ? new(Verdict.Unknown, "A folder Spacewise doesn't know. Check what made it before deleting.", "مجلد لا يعرفه Spacewise. تأكد من مصدره قبل الحذف.")
            : new(Verdict.Unknown, "A file Spacewise doesn't know.", "ملف لا يعرفه Spacewise."));
    }

    /// <summary>
    /// Whether the app offers to delete it at all. Windows can't be deleted, and neither can the "careful" system folders
    /// (Users, AppData, ProgramData…): deleting one would break every program at once. Careful files (a virtual disk) can.
    /// </summary>
    public static bool Deletable(Info info, bool isDir) => info.Verdict != Verdict.DontTouch && !(info.Verdict == Verdict.Careful && isDir);

    // Never let a name rule make something inside a "don't touch" area look deletable.
    static Info InheritDanger(Info? parent, Info child) =>
        parent?.Verdict == Verdict.DontTouch ? parent : child;

    /// <summary>Places that are always the user's own files (for the old-files finder).</summary>
    public static string[] UserFolders =>
        new[] { "Downloads", "Desktop", "Documents", "Pictures", "Videos", "Music" }
            .Select(n => Path.Combine(Home, n)).Where(Directory.Exists).ToArray();

    /// <summary>Folders the find-by-type search skips: Windows, programs, program data and caches.</summary>
    public static bool SkipWhenSearching(string dir)
    {
        if (dir.Equals(AppData, StringComparison.OrdinalIgnoreCase) || dir.Equals(ProgramData, StringComparison.OrdinalIgnoreCase)) return true;
        return Explain(dir, true).Verdict is Verdict.DontTouch or Verdict.App or Verdict.Safe;
    }
}
