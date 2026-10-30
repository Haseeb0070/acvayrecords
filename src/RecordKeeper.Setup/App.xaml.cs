using System.IO;
using System.Windows;

namespace RecordKeeper.Setup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        MainWindow = IsUninstalling(e.Args) ? new UninstallWindow() : new SetupWindow();
        MainWindow.Show();
    }

    /// <summary>
    /// The same program does both jobs, and decides which by how it was started.
    ///
    /// Add or remove programs passes the switch. Someone opening the install folder
    /// and double-clicking uninstall.exe passes nothing at all, so the file's own
    /// name counts as well: without that, the uninstaller offers to install.
    /// </summary>
    private static bool IsUninstalling(string[] args)
    {
        if (args.Any(argument =>
                argument.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);

        return name.Equals("uninstall", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("acvayrecords-uninstall", StringComparison.OrdinalIgnoreCase);
    }
}
