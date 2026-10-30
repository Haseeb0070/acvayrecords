using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;

namespace RecordKeeper.Setup;

/// <summary>
/// The one page of the installer: where the records go, where the program goes, and
/// which shortcuts to make.
///
/// The records folder is asked first and explained most, because it is the only
/// answer that still matters a year later. Everything else can be changed by
/// installing again.
/// </summary>
public partial class SetupWindow : Window
{
    private string _installedFolder = string.Empty;

    public SetupWindow()
    {
        InitializeComponent();

        ShowIcon();

        RecordsBox.Text = Installer.DefaultRecordsFolder;
        ProgramBox.Text = Installer.DefaultProgramFolder;

        RecordsBox.TextChanged += (_, _) => DescribeRecordsFolder();
        DescribeRecordsFolder();

        BrowseRecordsButton.Click += (_, _) => Browse(RecordsBox, "Where should your records be kept?");
        BrowseProgramButton.Click += (_, _) => Browse(ProgramBox, "Where should the program be installed?");

        InstallButton.Click += (_, _) => Install();
        CancelButton.Click += (_, _) => Close();
        CloseButton.Click += (_, _) => Close();
        OpenButton.Click += (_, _) => OpenTheApplication();

        Loaded += (_, _) =>
        {
            if (!Installer.HasPayload)
            {
                InstallButton.IsEnabled = false;
                Say("This copy of the installer has no application inside it.");
            }

            RecordsBox.Focus();
            RecordsBox.SelectAll();
        };
    }

    private void ShowIcon()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(Installer.IconName);

            if (stream is null) return;

            // An icon file holds ten sizes. Loading it as a plain image picks one of
            // the small ones and stretches it, which looks like a thumbnail of a logo
            // rather than a logo, so the largest is chosen and scaled down instead.
            var decoder = BitmapDecoder.Create(stream,
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            Mark.Source = decoder.Frames.OrderByDescending(frame => frame.PixelWidth).First();
        }
        catch (Exception)
        {
            // A missing picture is not a reason to stop someone installing.
        }
    }

    /// <summary>
    /// Says what is already in the chosen folder. Pointing a fresh install at records
    /// that are already there is the normal way to move to a new computer, so it
    /// should be obvious when that is what is about to happen.
    /// </summary>
    private void DescribeRecordsFolder()
    {
        var folder = RecordsBox.Text.Trim();

        if (folder.Length == 0 || !Path.IsPathFullyQualified(folder))
        {
            RecordsNote.Text = string.Empty;
            return;
        }

        if (File.Exists(Path.Combine(folder, "records.db")))
        {
            RecordsNote.Text =
                "There are already records in this folder. They will be opened as they are — " +
                "nothing is replaced or emptied.";
            RecordsNote.Foreground = (System.Windows.Media.Brush)FindResource("Good");
            return;
        }

        RecordsNote.Text = Directory.Exists(folder)
            ? "This folder exists and is empty of records. A new, empty database will be made in it."
            : "This folder will be made for you.";

        RecordsNote.Foreground = (System.Windows.Media.Brush)FindResource("Subtle");
    }

    private void Browse(System.Windows.Controls.TextBox box, string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };

        var current = box.Text.Trim();
        if (current.Length > 0 && Directory.Exists(current)) dialog.InitialDirectory = current;

        if (dialog.ShowDialog(this) == true) box.Text = dialog.FolderName;
    }

    private async void Install()
    {
        var records = RecordsBox.Text.Trim();
        var program = ProgramBox.Text.Trim();

        if (!Valid(records, "the folder for your records", RecordsBox)) return;
        if (!Valid(program, "the folder for the program", ProgramBox)) return;

        // Installing the program inside the records folder would mean an uninstall
        // walking through the user's own data. Keep them apart.
        if (Inside(program, records) || Inside(records, program))
        {
            Fail("Please keep the two folders apart. The program is removed when you " +
                 "uninstall, and your records must not be inside anything that gets removed.");
            RecordsBox.Focus();
            return;
        }

        SetBusy(true);

        try
        {
            await Task.Run(() =>
            {
                Installer.CopyProgram(program, Say);
                Installer.SetRecordsFolder(records, Say);
                Installer.CreateShortcuts(program,
                    desktop: Dispatcher.Invoke(() => DesktopBox.IsChecked == true),
                    startMenu: Dispatcher.Invoke(() => StartMenuBox.IsChecked == true),
                    Say);
                Installer.Register(program, Say);
            });
        }
        catch (Exception exception)
        {
            SetBusy(false);
            Fail("The installation did not finish. " + exception.Message);
            return;
        }

        _installedFolder = program;
        Succeed(program, records);
    }

    private bool Valid(string folder, string what, System.Windows.Controls.TextBox box)
    {
        if (folder.Length == 0)
        {
            Fail($"Please say {what}.");
            box.Focus();
            return false;
        }

        if (!Path.IsPathFullyQualified(folder))
        {
            Fail($"Please give a full path for {what}, starting with a drive letter, " +
                 "for example C:\\Users.");
            box.Focus();
            box.SelectAll();
            return false;
        }

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception exception)
        {
            Fail($"That folder cannot be used for {what}. {exception.Message}");
            box.Focus();
            box.SelectAll();
            return false;
        }

        return true;
    }

    private static bool Inside(string candidate, string parent)
    {
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));

        return a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               a.Equals(b, StringComparison.OrdinalIgnoreCase);
    }

    private void SetBusy(bool busy)
    {
        Questions.IsEnabled = !busy;
        InstallButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }

    private void Succeed(string program, string records)
    {
        SetBusy(false);

        InstallButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        OpenButton.Visibility = Visibility.Visible;
        CloseButton.Visibility = Visibility.Visible;

        ResultText.Visibility = Visibility.Visible;
        ResultText.Foreground = (System.Windows.Media.Brush)FindResource("Good");
        ResultText.Text =
            $"acvayrecords is installed.\n\n" +
            $"The program is in {program}.\n" +
            $"Your records will be kept in {records}.\n\n" +
            "It is in the Start menu, and can be removed at any time from Add or remove " +
            "programs. Removing it leaves your records exactly where they are.";

        Say("Installed.");
        OpenButton.Focus();
    }

    private void Fail(string message)
    {
        ResultText.Visibility = Visibility.Visible;
        ResultText.Foreground = (System.Windows.Media.Brush)FindResource("Error");
        ResultText.Text = message;

        Say("Not installed.");
    }

    private void OpenTheApplication()
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                Path.Combine(_installedFolder, Installer.ExeName))
            {
                UseShellExecute = true,
                WorkingDirectory = _installedFolder
            });

            Close();
        }
        catch (Exception exception)
        {
            Fail("The program is installed, but it could not be started from here. " +
                 "Open it from the Start menu instead. " + exception.Message);
        }
    }

    private void Say(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Say(message));
            return;
        }

        StatusText.Text = message;
    }
}
