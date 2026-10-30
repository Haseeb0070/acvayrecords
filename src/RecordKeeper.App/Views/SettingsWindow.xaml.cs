using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using RecordKeeper.App.Accessibility;
using RecordKeeper.Core.Data;
using RecordKeeper.Core.Models;

namespace RecordKeeper.App.Views;

/// <summary>
/// Preferences, with the visual ones applied live.
///
/// Someone choosing a text size or an outline thickness for low vision needs to see
/// the result while they choose, not after pressing OK, so every change here is applied
/// to the running application immediately and rolled back if they cancel.
/// </summary>
public partial class SettingsWindow : Window
{
    private sealed record ColorChoice(string Name, string Hex)
    {
        public override string ToString() => Name;
    }

    /// <summary>
    /// Focus-outline colours. The first is the default on the white-and-blue theme and
    /// the second on the black theme; the rest are there because what stands out best
    /// depends on the eye looking at it.
    /// </summary>
    private static readonly ColorChoice[] FocusColors =
    {
        new("Orange", "#C2410C"),
        new("Yellow", "#FFD400"),
        new("Brand blue", "#00A9E0"),
        new("Deep blue", "#00507A"),
        new("Bright green", "#00A152"),
        new("Magenta", "#B5179E"),
        new("White", "#FFFFFF"),
        new("Black", "#000000")
    };

    private readonly AppServices _services;
    private readonly Announcer _announcer;
    private readonly AppSettings _original;

    private AppSettings _working;
    private bool _loading = true;

    public SettingsWindow(AppServices services)
    {
        InitializeComponent();

        _services = services;
        _announcer = services.Announcer;
        _original = services.Settings.Clone();
        _working = services.Settings.Clone();

        FocusColorBox.ItemsSource = FocusColors;

        LoadFromSettings();
        WireEvents();

        Loaded += (_, _) =>
        {
            _loading = false;
            FontSizeSlider.Focus();
            _announcer.Say("Settings. Changes to text size and colours take effect as you make them.");
        };
    }

    private void LoadFromSettings()
    {
        FontSizeSlider.Value = _working.FontSize;
        FontSizeText.Text = DescribeFontSize(_working.FontSize);

        ThemeSystemRadio.IsChecked = _working.Theme == AppTheme.System;
        ThemeLightRadio.IsChecked = _working.Theme == AppTheme.Light;
        ThemeDarkRadio.IsChecked = _working.Theme == AppTheme.Dark;
        ThemeHighContrastRadio.IsChecked = _working.Theme == AppTheme.HighContrast;

        FocusThicknessSlider.Value = _working.FocusOutlineThickness;
        FocusThicknessText.Text = DescribeThickness(_working.FocusOutlineThickness);

        FocusColorBox.SelectedItem =
            FocusColors.FirstOrDefault(c =>
                string.Equals(c.Hex, _working.FocusOutlineColor, StringComparison.OrdinalIgnoreCase))
            ?? FocusColors[0];

        SpeakConfirmationsBox.IsChecked = _working.SpeakConfirmations;
        PlaySoundCuesBox.IsChecked = _working.PlaySoundCues;
        ShowStartupLogoBox.IsChecked = _working.ShowStartupLogo;

        ViewFormRadio.IsChecked = _working.DefaultViewMode == RecordViewMode.Form;
        ViewTableRadio.IsChecked = _working.DefaultViewMode == RecordViewMode.Table;
        WarnDuplicatesBox.IsChecked = _working.WarnOnDuplicateTitle;

        CurrencyBox.ItemsSource = Money.Presets;
        CurrencyBox.SelectedItem = Money.PresetFor(_working.CurrencySymbol);
        UpdateCurrencyExample();

        AutoBackupBox.IsChecked = _working.AutoBackupDaily;
        BackupsToKeepBox.Text = _working.AutoBackupsToKeep.ToString(CultureInfo.CurrentCulture);

        DataFolderBox.Text = AppPaths.Root;
        UpdateBackupInfo();
        UpdateEncryptionStatus();
    }

    /// <summary>
    /// Shows a real amount in the chosen currency, and the words it is read aloud as.
    /// Picking a currency from a list of names is guesswork until you see what an
    /// amount will actually look like.
    /// </summary>
    private void UpdateCurrencyExample()
    {
        if (CurrencyBox.SelectedItem is not CurrencyPreset preset)
        {
            CurrencyExampleText.Text = string.Empty;
            return;
        }

        // Ask Money to format it without disturbing the live setting, which only
        // changes when the window is saved.
        var currentSymbol = Money.Symbol;
        var currentName = Money.SpokenName;

        Money.Use(preset.Symbol, preset.SpokenName);
        var example = Money.Text(1250.5m);
        var spoken = Money.Spoken(1250.5m);
        Money.Use(currentSymbol, currentName);

        CurrencyExampleText.Text = preset.SpokenName.Length == 0
            ? $"Amounts will look like {example}."
            : $"Amounts will look like {example}, and be read aloud as {spoken}.";
    }

    private void UpdateEncryptionStatus()
    {
        var on = _services.Settings.EncryptionEnabled;

        EncryptionStatusText.Text = on
            ? "The database is encrypted. A password is needed every time acvayrecords starts."
            : "The database is not encrypted. Anyone who can reach the file can read it.";

        EncryptionStatusText.Foreground = (System.Windows.Media.Brush)(on
            ? FindResource("App.SuccessForeground")
            : FindResource("App.Foreground"));

        EncryptionButton.Content = on ? "Turn _off encryption" : "Turn _on encryption";

        System.Windows.Automation.AutomationProperties.SetHelpText(EncryptionButton, on
            ? "Removes the password and leaves the database readable by anyone who can reach the file."
            : "Choose a password. From then on it is needed every time the application starts.");
    }

    private void WireEvents()
    {
        FontSizeSlider.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _working.FontSize = Math.Round(FontSizeSlider.Value);
            FontSizeText.Text = DescribeFontSize(_working.FontSize);
            ApplyLivePreview();
        };

        FocusThicknessSlider.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _working.FocusOutlineThickness = Math.Round(FocusThicknessSlider.Value);
            FocusThicknessText.Text = DescribeThickness(_working.FocusOutlineThickness);
            ApplyLivePreview();
        };

        FocusColorBox.SelectionChanged += (_, _) =>
        {
            if (_loading || FocusColorBox.SelectedItem is not ColorChoice choice) return;
            _working.FocusOutlineColor = choice.Hex;
            ApplyLivePreview();
        };

        foreach (var (radio, theme) in new (System.Windows.Controls.RadioButton, AppTheme)[]
                 {
                     (ThemeSystemRadio, AppTheme.System),
                     (ThemeLightRadio, AppTheme.Light),
                     (ThemeDarkRadio, AppTheme.Dark),
                     (ThemeHighContrastRadio, AppTheme.HighContrast)
                 })
        {
            var captured = theme;
            radio.Checked += (_, _) =>
            {
                if (_loading) return;
                _working.Theme = captured;
                ApplyLivePreview();
            };
        }

        CurrencyBox.SelectionChanged += (_, _) =>
        {
            UpdateCurrencyExample();
            if (CurrencyExampleText.Text.Length > 0) _announcer.Say(CurrencyExampleText.Text);
        };

        OkButton.Click += (_, _) => Commit();
        CancelButton.Click += (_, _) => Rollback();

        OpenFolderButton.Click += (_, _) => OpenDataFolder();
        MoveFolderButton.Click += (_, _) => MoveDataFolder();
        EncryptionButton.Click += (_, _) => ToggleEncryption();

        Closing += (_, e) =>
        {
            if (DialogResult is null) Rollback(close: false);
        };
    }

    /// <summary>Pushes the visual settings into the running application without saving them.</summary>
    private void ApplyLivePreview()
    {
        // Treat the appearance as chosen for the duration of the preview, otherwise the
        // theme manager would keep overriding the radio button the user just pressed
        // with the app's own default. Cancel restores the original flag along with
        // everything else.
        _services.Settings.AppearanceChosenByUser = true;

        _services.Settings.FontSize = _working.FontSize;
        _services.Settings.Theme = _working.Theme;
        _services.Settings.FocusOutlineThickness = _working.FocusOutlineThickness;
        _services.Settings.FocusOutlineColor = _working.FocusOutlineColor;

        ThemeManager.Apply(_services.Settings);
    }

    private void Commit()
    {
        if (!int.TryParse(BackupsToKeepBox.Text.Trim(), out var keep) || keep < 1 || keep > 365)
        {
            _announcer.SayError("The number of backups to keep must be a whole number between 1 and 365.");
            BackupsToKeepBox.Focus();
            BackupsToKeepBox.SelectAll();
            return;
        }

        // From here on the appearance is the user's choice, not the app's default.
        _working.AppearanceChosenByUser = true;

        _working.AutoBackupsToKeep = keep;
        _working.SpeakConfirmations = SpeakConfirmationsBox.IsChecked == true;
        _working.PlaySoundCues = PlaySoundCuesBox.IsChecked == true;
        _working.ShowStartupLogo = ShowStartupLogoBox.IsChecked == true;
        _working.DefaultViewMode = ViewTableRadio.IsChecked == true ? RecordViewMode.Table : RecordViewMode.Form;
        _working.WarnOnDuplicateTitle = WarnDuplicatesBox.IsChecked == true;
        _working.AutoBackupDaily = AutoBackupBox.IsChecked == true;

        if (CurrencyBox.SelectedItem is CurrencyPreset currency)
        {
            _working.CurrencySymbol = currency.Symbol;
            _working.CurrencyName = currency.SpokenName;
        }

        // Carry over the things this window does not edit.
        _working.LastAutoBackupUtc = _original.LastAutoBackupUtc;
        _working.LastRecordTypeId = _original.LastRecordTypeId;
        _working.WindowWidth = _original.WindowWidth;
        _working.WindowHeight = _original.WindowHeight;
        _working.WindowMaximized = _original.WindowMaximized;
        _working.NeedsFirstRun = _original.NeedsFirstRun;

        _services.SaveSettings(_working);
        DialogResult = true;
    }

    private void Rollback(bool close = true)
    {
        _services.SaveSettings(_original.Clone());
        if (close) DialogResult = false;
    }

    private static string DescribeFontSize(double size)
    {
        var label = size switch
        {
            <= 12 => "small",
            <= 16 => "standard",
            <= 22 => "large",
            <= 32 => "very large",
            _ => "extra large"
        };
        return $"{size:0} point, {label}";
    }

    private static string DescribeThickness(double thickness)
    {
        var label = thickness switch
        {
            <= 1 => "thin",
            <= 3 => "standard",
            <= 5 => "thick",
            _ => "very thick"
        };
        return $"{thickness:0} pixels, {label}";
    }

    private void UpdateBackupInfo()
    {
        try
        {
            var backups = _services.Backup.ListBackups();

            BackupInfoText.Text = backups.Count == 0
                ? "No backups have been made yet. Control plus B makes one now."
                : $"{backups.Count} {(backups.Count == 1 ? "backup" : "backups")} kept. " +
                  $"The most recent is {backups[0].Name}, from " +
                  $"{backups[0].CreationTime.ToString("d MMMM yyyy 'at' HH:mm", CultureInfo.CurrentCulture)}.";
        }
        catch (Exception exception)
        {
            App.Log("Listing backups failed", exception);
            BackupInfoText.Text = "The backup folder could not be read.";
        }
    }

    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.Root) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            App.Log("Opening the data folder failed", exception);
            _announcer.SayError("The folder could not be opened. It is at " + AppPaths.Root);
        }
    }

    // ----------------------------------------------------------------- encryption

    private void ToggleEncryption()
    {
        if (_services.Settings.EncryptionEnabled) TurnEncryptionOff();
        else TurnEncryptionOn();
    }

    private void TurnEncryptionOn()
    {
        var prompt = new PasswordWindow(PasswordPurpose.SetNew) { Owner = this };

        if (prompt.ShowDialog() != true || string.IsNullOrEmpty(prompt.Password))
        {
            _announcer.Say("Encryption was not turned on.");
            return;
        }

        ConvertDatabase(
            currentPassword: _services.DatabasePassword,
            newPassword: prompt.Password,
            nowEncrypted: true,
            doneMessage:
                "Your database is now encrypted.\n\n" +
                "acvayrecords will close. The next time you open it, it will ask for " +
                "this password.\n\n" +
                "Keep the password somewhere safe. Nobody can recover it for you.");
    }

    private void TurnEncryptionOff()
    {
        var prompt = new PasswordWindow(PasswordPurpose.Confirm) { Owner = this };

        if (prompt.ShowDialog() != true || string.IsNullOrEmpty(prompt.Password))
        {
            _announcer.Say("Encryption was left on.");
            return;
        }

        ConvertDatabase(
            currentPassword: prompt.Password,
            newPassword: null,
            nowEncrypted: false,
            doneMessage:
                "The encryption has been removed.\n\n" +
                "acvayrecords will close. The next time you open it, it will not ask " +
                "for a password, and anyone who can reach the file can read it.");
    }

    /// <summary>
    /// Converts the database, then closes the application.
    ///
    /// Closing is not laziness: every connection this session holds was opened with the
    /// old key, and carrying on with them would be asking for a half-working session on
    /// top of the user's only copy of their records. A backup is taken first, and the
    /// previous file is kept beside the new one.
    /// </summary>
    private void ConvertDatabase(string? currentPassword, string? newPassword,
                                 bool nowEncrypted, string doneMessage)
    {
        try
        {
            _services.Backup.CreateManualBackup();
        }
        catch (Exception exception)
        {
            App.Log("The backup before converting the database failed", exception);

            var carryOn = MessageBox.Show(this,
                "A backup could not be made first.\n\n" + exception.Message +
                "\n\nChange the encryption anyway? The previous database file is still " +
                "kept alongside the new one either way.",
                "The backup failed",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            if (carryOn != MessageBoxResult.Yes)
            {
                _announcer.Say("Nothing was changed.");
                return;
            }
        }

        string kept;
        try
        {
            kept = Database.Convert(AppPaths.DatabaseFile, currentPassword, newPassword);
        }
        catch (Exception exception)
        {
            App.Log("Changing the database encryption failed", exception);

            MessageBox.Show(this,
                "The database was not changed.\n\n" + exception.Message +
                "\n\nYour records are exactly as they were.",
                "Encryption could not be changed",
                MessageBoxButton.OK, MessageBoxImage.Error);

            _announcer.SayError("The database was not changed. " + exception.Message);
            return;
        }

        // Both copies of the settings have to agree, or the rollback that runs when this
        // window closes would put the old flag straight back.
        _working.EncryptionEnabled = nowEncrypted;
        _original.EncryptionEnabled = nowEncrypted;
        _services.Settings.EncryptionEnabled = nowEncrypted;
        SettingsStore.Save(_services.Settings);

        MessageBox.Show(this,
            doneMessage + "\n\nThe database as it was is kept here until you delete it:\n" + kept,
            nowEncrypted ? "Encryption is on" : "Encryption is off",
            MessageBoxButton.OK, MessageBoxImage.Information);

        Application.Current.Shutdown();
    }

    /// <summary>
    /// Copies the data folder somewhere else and points future launches at it.
    ///
    /// This copies rather than moves, and asks for a restart, because the database file
    /// is open right now. Leaving the original in place means a failure part-way through
    /// costs nothing.
    /// </summary>
    private void MoveDataFolder()
    {
        var picker = new OpenFolderDialog
        {
            Title = "Choose the folder to keep your records in",
            Multiselect = false
        };

        if (picker.ShowDialog(this) != true)
        {
            _announcer.Say("The data folder was not changed.");
            return;
        }

        var destination = Path.Combine(picker.FolderName, AppPaths.AppFolderName);

        if (string.Equals(Path.TrimEndingDirectorySeparator(destination),
                          Path.TrimEndingDirectorySeparator(AppPaths.Root),
                          StringComparison.OrdinalIgnoreCase))
        {
            _announcer.Say("That is already where your records are kept.");
            return;
        }

        if (Directory.Exists(destination) && Directory.GetFileSystemEntries(destination).Length > 0)
        {
            MessageBox.Show(this,
                $"There is already something in:\n{destination}\n\n" +
                "Choose an empty folder, or one that does not exist yet.",
                "That folder is not empty",
                MessageBoxButton.OK, MessageBoxImage.Warning);

            _announcer.SayError("That folder is not empty. Choose another one.");
            return;
        }

        var answer = MessageBox.Show(this,
            $"Copy your records to:\n{destination}\n\n" +
            "The originals are left where they are, as a safety net. acvayrecords will use the new " +
            "folder the next time it starts, so you will need to close and reopen it.\n\n" +
            "Continue?",
            "Move the data folder?",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            _announcer.Say("The data folder was not changed.");
            return;
        }

        try
        {
            // Flush the write-ahead log so the copied database is complete.
            _services.Database.CreateBackup(automatic: false);

            CopyDirectory(AppPaths.Root, destination);
            AppPaths.SetRedirect(destination);

            DataFolderBox.Text = destination + "   (in use after the next restart)";

            MessageBox.Show(this,
                $"Your records were copied to:\n{destination}\n\n" +
                "Close acvayrecords and open it again to start using the new folder.\n\n" +
                $"Once you are happy that everything is there, the old copy in\n{AppPaths.Root}\n" +
                "can be deleted by hand.",
                "Copied. Please restart acvayrecords",
                MessageBoxButton.OK, MessageBoxImage.Information);

            _announcer.SaySuccess("Records copied. Close and reopen acvayrecords to use the new folder.");
        }
        catch (Exception exception)
        {
            App.Log("Moving the data folder failed", exception);

            MessageBox.Show(this,
                "The records could not be copied.\n\n" + exception.Message +
                "\n\nNothing was changed. Your records are still in their original folder.",
                "The move did not work",
                MessageBoxButton.OK, MessageBoxImage.Error);

            _announcer.SayError("The records could not be copied. Nothing was changed.");
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            // The redirect marker belongs to the old location only.
            if (Path.GetFileName(file).Equals("datapath.txt", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
