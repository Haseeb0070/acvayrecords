using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace RecordKeeper.Setup;

/// <summary>
/// Everything the installer actually does to the machine, in one place and with no
/// user interface, so each step can be read and reasoned about on its own.
///
/// Nothing here needs administrator rights: the program goes under the user's own
/// Local AppData, the shortcuts are the user's own, and the entry in Add or remove
/// programs is written under HKEY_CURRENT_USER.
/// </summary>
public static class Installer
{
    public const string ProductName = "acvayrecords";
    public const string Publisher = "acvaysoft";
    public const string PoweredBy = "Powered by Haseeb";
    public const string Version = "1.0.0";

    public const string ExeName = "acvayrecords.exe";
    public const string UninstallExeName = "uninstall.exe";
    public const string IconName = "acvayrecords.ico";

    private const string RegistryKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\acvayrecords";

    /// <summary>
    /// Where the application keeps the one file that says where the records are.
    ///
    /// This path is fixed, and is the only thing the application knows in advance.
    /// It must match AppPaths in the application itself; a test asserts that the file
    /// written here is the file the application reads.
    /// </summary>
    public static string RedirectFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RecordKeeper",
        "datapath.txt");

    public static string DefaultProgramFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        ProductName);

    public static string DefaultRecordsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ProductName);

    /// <summary>True when this build has an application inside it to install.</summary>
    public static bool HasPayload => Resource(ExeName) is not null;

    // ------------------------------------------------------------------ installing

    public static void CopyProgram(string programFolder, Action<string> say)
    {
        Directory.CreateDirectory(programFolder);

        var payload = Resource(ExeName)
            ?? throw new InvalidOperationException(
                "This copy of the installer has no application inside it.");

        say("Copying the program…");

        var target = Path.Combine(programFolder, ExeName);
        using (payload)
        using (var file = File.Create(target))
        {
            payload.CopyTo(file);
        }

        // The icon goes alongside, so the shortcuts have something to point at that
        // does not depend on reading it back out of the program.
        if (Resource(IconName) is { } icon)
        {
            using (icon)
            using (var file = File.Create(Path.Combine(programFolder, IconName)))
            {
                icon.CopyTo(file);
            }
        }

        // The installer copies itself in as the uninstaller.
        var self = Environment.ProcessPath;
        if (self is not null)
        {
            var uninstaller = Path.Combine(programFolder, UninstallExeName);
            if (!string.Equals(self, uninstaller, StringComparison.OrdinalIgnoreCase))
                File.Copy(self, uninstaller, overwrite: true);
        }
    }

    /// <summary>
    /// Points the application at the folder the user chose for their records.
    ///
    /// Existing records are never touched: if there is already a database in the
    /// chosen folder, it is simply used, which is what makes reinstalling safe.
    /// </summary>
    public static void SetRecordsFolder(string recordsFolder, Action<string> say)
    {
        say("Setting up the records folder…");

        Directory.CreateDirectory(recordsFolder);

        var redirect = RedirectFile;
        Directory.CreateDirectory(Path.GetDirectoryName(redirect)!);

        var defaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RecordKeeper");

        // Choosing the default location means there is nothing to redirect to, and a
        // redirect file pointing at itself would be one more thing to go wrong.
        if (string.Equals(Path.TrimEndingDirectorySeparator(recordsFolder),
                          Path.TrimEndingDirectorySeparator(defaultRoot),
                          StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(redirect)) File.Delete(redirect);
            return;
        }

        File.WriteAllText(redirect, recordsFolder);
    }

    /// <summary>
    /// Makes the Start menu and desktop shortcuts.
    ///
    /// The two folders can be given, which is how the check harness writes shortcuts
    /// into a scratch folder instead of onto the real desktop. Left out, they are the
    /// user's own Start menu and desktop, which is what an install means.
    /// </summary>
    public static void CreateShortcuts(string programFolder, bool desktop, bool startMenu,
                                       Action<string> say,
                                       string? startMenuFolder = null,
                                       string? desktopFolder = null)
    {
        var target = Path.Combine(programFolder, ExeName);
        var icon = Path.Combine(programFolder, IconName);

        if (startMenu)
        {
            say("Adding it to the Start menu…");

            var folder = startMenuFolder
                ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);

            CreateShortcut(Path.Combine(folder, ProductName + ".lnk"), target, icon, programFolder);
        }

        if (desktop)
        {
            say("Putting a shortcut on the desktop…");

            var folder = desktopFolder
                ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            CreateShortcut(Path.Combine(folder, ProductName + ".lnk"), target, icon, programFolder);
        }
    }

    /// <summary>
    /// Writes a .lnk through the Windows Script Host, which is present on every
    /// Windows and needs no extra library. Called through late binding so the
    /// installer carries no COM reference of its own.
    /// </summary>
    private static void CreateShortcut(string linkPath, string target, string iconPath,
                                       string workingFolder)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;

        object? shell = null;
        object? link = null;

        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return;

            link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                null, shell, new object[] { linkPath });
            if (link is null) return;

            var linkType = link.GetType();

            void Set(string name, string value) =>
                linkType.InvokeMember(name, BindingFlags.SetProperty, null, link, new object[] { value });

            Set("TargetPath", target);
            Set("WorkingDirectory", workingFolder);
            Set("Description", $"{ProductName} — {PoweredBy}");

            if (File.Exists(iconPath)) Set("IconLocation", iconPath);

            linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
        finally
        {
            if (link is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
            if (shell is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>Lists it in Add or remove programs, for the user's account only.</summary>
    public static void Register(string programFolder, Action<string> say)
    {
        say("Listing it in Add or remove programs…");

        using var key = Registry.CurrentUser.CreateSubKey(RegistryKey);
        if (key is null) return;

        var uninstaller = Path.Combine(programFolder, UninstallExeName);

        key.SetValue("DisplayName", ProductName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", Publisher);
        key.SetValue("DisplayIcon", Path.Combine(programFolder, IconName));
        key.SetValue("InstallLocation", programFolder);
        key.SetValue("UninstallString", $"\"{uninstaller}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("Comments", PoweredBy);

        try
        {
            var size = new DirectoryInfo(programFolder)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);

            key.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
        }
        catch (IOException) { /* the entry is still useful without a size */ }
    }

    // ---------------------------------------------------------------- uninstalling

    public static string? InstalledFolder()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
        return key?.GetValue("InstallLocation") as string;
    }

    /// <summary>
    /// Removes the program, the shortcuts and the listing. The records are never
    /// touched: they are the user's, and they are not what was installed.
    /// </summary>
    public static void Uninstall(Action<string> say)
    {
        say("Removing the shortcuts…");

        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                 })
        {
            var link = Path.Combine(folder, ProductName + ".lnk");
            try { if (File.Exists(link)) File.Delete(link); }
            catch (IOException) { /* a shortcut left behind is not worth failing over */ }
        }

        say("Removing the listing…");
        try { Registry.CurrentUser.DeleteSubKeyTree(RegistryKey, throwOnMissingSubKey: false); }
        catch (IOException) { /* likewise */ }

        say("Removing the program…");

        var folderToRemove = Path.GetDirectoryName(Environment.ProcessPath);
        if (folderToRemove is null) return;

        var stuck = new List<string>();

        foreach (var file in Directory.EnumerateFiles(folderToRemove))
        {
            // Everything except the uninstaller, which is the program doing this.
            if (string.Equals(file, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                continue;

            try { File.Delete(file); }
            catch (IOException) { stuck.Add(Path.GetFileName(file)); }
            catch (UnauthorizedAccessException) { stuck.Add(Path.GetFileName(file)); }
        }

        // Saying it is gone when it is still sitting there would be a lie, and the
        // usual reason is simply that the program is still open.
        if (stuck.Count > 0)
        {
            throw new IOException(
                "These could not be removed, which usually means acvayrecords is still " +
                "open: " + string.Join(", ", stuck) + ". Close it and try again.");
        }

        ScheduleSelfRemoval(folderToRemove);
    }

    /// <summary>True while the application is running, which would block its removal.</summary>
    public static bool ApplicationIsRunning()
    {
        try
        {
            return System.Diagnostics.Process
                .GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName))
                .Length > 0;
        }
        catch (Exception)
        {
            // If it cannot be checked, let the removal try and report for itself.
            return false;
        }
    }

    /// <summary>
    /// A running program cannot delete itself, so a detached shell waits a moment and
    /// removes the folder once this one has closed.
    /// </summary>
    private static void ScheduleSelfRemoval(string folder)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c ping -n 3 127.0.0.1 > nul & rmdir /s /q \"{folder}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch (Exception)
        {
            // The folder being left behind is untidy, not harmful, and the user has
            // already been told the program itself is gone.
        }
    }

    private static Stream? Resource(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
}
