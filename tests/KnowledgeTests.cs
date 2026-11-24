using Spacewise;
using Xunit;

/// <summary>The labels are the promise Spacewise makes: nothing that keeps Windows or a program working may look deletable.</summary>
public class KnowledgeTests
{
    static readonly string Win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    static readonly string Drive = Path.GetPathRoot(Win)!;
    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    static Verdict Dir(params string[] parts) => Knowledge.Explain(Path.Combine(parts), isDir: true).Verdict;
    static Verdict File(params string[] parts) => Knowledge.Explain(Path.Combine(parts), isDir: false).Verdict;

    [Fact] public void Windows_itself_is_never_deletable() => Assert.Equal(Verdict.DontTouch, Dir(Win));
    [Fact] public void System32_is_never_deletable() => Assert.Equal(Verdict.DontTouch, Dir(Win, "System32"));
    [Fact] public void A_file_deep_in_System32_is_never_deletable() => Assert.Equal(Verdict.DontTouch, File(Win, "System32", "drivers", "etc", "hosts"));
    [Fact] public void A_cache_folder_inside_Windows_stays_dont_touch() => Assert.Equal(Verdict.DontTouch, Dir(Win, "SomeComponent", "Cache"));
    [Fact] public void Windows_temp_is_safe() => Assert.Equal(Verdict.Safe, Dir(Win, "Temp"));
    [Fact] public void Windows_installer_cache_is_never_deletable() => Assert.Equal(Verdict.DontTouch, Dir(Win, "Installer"));
    [Fact] public void Pagefile_is_never_deletable() => Assert.Equal(Verdict.DontTouch, File(Drive, "pagefile.sys"));
    [Fact] public void Hibernation_file_is_careful_not_dont_touch() => Assert.Equal(Verdict.Careful, File(Drive, "hiberfil.sys"));
    [Fact] public void System_restore_points_are_never_deletable() => Assert.Equal(Verdict.DontTouch, Dir(Drive, "System Volume Information"));

    [Fact] public void Program_Files_itself_is_dont_touch() => Assert.Equal(Verdict.DontTouch, Dir(Drive, "Program Files"));
    [Fact] public void A_program_in_Program_Files_is_a_program() => Assert.Equal(Verdict.App, Dir(Drive, "Program Files", "SomeApp"));
    [Fact] public void A_32bit_program_is_a_program() => Assert.Equal(Verdict.App, Dir(Drive, "Program Files (x86)", "SomeApp", "bin"));
    [Fact] public void A_program_exe_stays_a_program() => Assert.Equal(Verdict.App, File(Drive, "Program Files", "SomeApp", "app.exe"));
    [Fact] public void Windows_parts_in_Program_Files_are_dont_touch() => Assert.Equal(Verdict.DontTouch, Dir(Drive, "Program Files", "Windows Defender"));
    [Fact] public void Common_Files_are_dont_touch() => Assert.Equal(Verdict.DontTouch, Dir(Drive, "Program Files", "Common Files"));
    [Fact] public void Store_apps_folder_is_dont_touch() => Assert.Equal(Verdict.DontTouch, Dir(Drive, "Program Files", "WindowsApps"));

    [Fact] public void AppData_is_careful() => Assert.Equal(Verdict.Careful, Dir(Home, "AppData"));
    [Fact] public void Local_AppData_is_careful() => Assert.Equal(Verdict.Careful, Dir(Local));
    [Fact] public void ProgramData_is_careful() => Assert.Equal(Verdict.Careful, Dir(Drive, "ProgramData"));
    [Fact] public void A_program_data_folder_is_a_program() => Assert.Equal(Verdict.App, Dir(Local, "EpicGamesLauncher"));
    [Fact] public void A_roaming_program_folder_is_a_program() => Assert.Equal(Verdict.App, Dir(Roaming, "discord"));
    [Fact] public void Microsoft_data_in_AppData_is_careful() => Assert.Equal(Verdict.Careful, Dir(Local, "Microsoft"));
    [Fact] public void Store_app_data_is_careful() => Assert.Equal(Verdict.Careful, Dir(Local, "Packages"));
    [Fact] public void A_cache_inside_a_program_folder_is_safe() => Assert.Equal(Verdict.Safe, Dir(Local, "Google", "Chrome", "User Data", "Default", "Cache"));
    [Fact] public void Local_temp_is_safe() => Assert.Equal(Verdict.Safe, Dir(Local, "Temp"));

    [Fact] public void Game_launchers_are_programs() => Assert.Equal(Verdict.App, Dir(@"D:\", "Epic Games", "Fortnite"));
    [Fact] public void Steam_games_are_programs() => Assert.Equal(Verdict.App, Dir(Drive, "Program Files (x86)", "Steam", "steamapps", "common"));
    [Fact] public void Xbox_games_are_programs() => Assert.Equal(Verdict.App, Dir(Drive, "XboxGames", "Minecraft"));

    [Fact] public void Downloads_are_yours() => Assert.Equal(Verdict.Yours, Dir(Home, "Downloads"));
    [Fact] public void A_video_in_Videos_is_yours() => Assert.Equal(Verdict.Yours, File(Home, "Videos", "trip.mp4"));
    [Fact] public void A_dll_in_Downloads_is_not_deletable_on_its_own() => Assert.Equal(Verdict.DontTouch, File(Home, "Downloads", "thing.dll"));
    [Fact] public void Node_modules_is_safe() => Assert.Equal(Verdict.Safe, Dir(Home, "Documents", "project", "node_modules"));
    [Fact] public void A_git_folder_is_yours() => Assert.Equal(Verdict.Yours, Dir(Home, "Documents", "project", ".git"));
    [Fact] public void A_virtual_disk_is_careful() => Assert.Equal(Verdict.Careful, File(@"D:\", "vm", "disk.vhdx"));
    [Fact] public void Unknown_folders_are_unknown() => Assert.Equal(Verdict.Unknown, Dir(@"D:\", "mystery"));

    [Fact] public void Users_folder_is_careful() => Assert.Equal(Verdict.Careful, Dir(Drive, "Users"));
    [Fact] public void Your_account_folder_is_careful() => Assert.Equal(Verdict.Careful, Dir(Home));
    [Fact] public void Another_account_is_careful() => Assert.Equal(Verdict.Careful, Dir(Drive, "Users", "Public"));
    [Fact] public void Your_own_project_folder_is_not_careful() => Assert.Equal(Verdict.Unknown, Dir(Home, "tools"));

    static bool CanDelete(string path, bool isDir) => Knowledge.Deletable(Knowledge.Explain(path, isDir), isDir);
    [Fact] public void Windows_cannot_be_deleted() => Assert.False(CanDelete(Win, true));
    [Fact] public void The_Users_folder_cannot_be_deleted() => Assert.False(CanDelete(Path.Combine(Drive, "Users"), true));
    [Fact] public void Your_account_folder_cannot_be_deleted() => Assert.False(CanDelete(Home, true));
    [Fact] public void AppData_cannot_be_deleted() => Assert.False(CanDelete(Knowledge.AppData, true));
    [Fact] public void ProgramData_cannot_be_deleted() => Assert.False(CanDelete(Path.Combine(Drive, "ProgramData"), true));
    [Fact] public void Store_app_data_cannot_be_deleted() => Assert.False(CanDelete(Path.Combine(Local, "Packages"), true));
    [Fact] public void A_virtual_disk_file_can_be_deleted() => Assert.True(CanDelete(@"D:\vm\disk.vhdx", false));
    [Fact] public void A_program_folder_can_be_deleted_with_a_warning() => Assert.True(CanDelete(Path.Combine(Local, "OldGame"), true));
    [Fact] public void Your_downloads_can_be_deleted() => Assert.True(CanDelete(Path.Combine(Home, "Downloads", "setup.exe"), false));

    [Fact]
    public void Every_label_has_an_arabic_explanation()
    {
        foreach (var p in new[] { Win, Path.Combine(Drive, "Program Files", "X"), Path.Combine(Local, "X"), Path.Combine(Home, "Downloads"), @"D:\mystery" })
            Assert.False(string.IsNullOrWhiteSpace(Knowledge.Explain(p, true).Ar));
    }

    [Fact] public void Search_skips_windows() => Assert.True(Knowledge.SkipWhenSearching(Win));
    [Fact] public void Search_skips_programs() => Assert.True(Knowledge.SkipWhenSearching(Path.Combine(Drive, "Program Files")));
    [Fact] public void Search_skips_appdata() => Assert.True(Knowledge.SkipWhenSearching(Knowledge.AppData));
    [Fact] public void Search_skips_caches() => Assert.True(Knowledge.SkipWhenSearching(Path.Combine(Home, "Documents", "p", "node_modules")));
    [Fact] public void Search_keeps_your_folders() => Assert.False(Knowledge.SkipWhenSearching(Path.Combine(Home, "Pictures")));
    [Fact] public void Search_keeps_the_users_folder() => Assert.False(Knowledge.SkipWhenSearching(Path.Combine(Drive, "Users")));
}
