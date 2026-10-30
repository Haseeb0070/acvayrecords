using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using RecordKeeper.App.Accessibility;
using RecordKeeper.App.Views;
using RecordKeeper.Core.Data;
using RecordKeeper.Core.Models;

namespace RecordKeeper.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnFatalException;

        try
        {
            // The theme has to be up before anything is shown, because the password
            // prompt may appear before the application's services exist at all.
            var settings = SettingsStore.Load();
            ThemeManager.Apply(settings);

            if (!TryUnlockDatabase(settings, out var password))
            {
                Shutdown(0);
                return;
            }

            var services = AppServices.Initialize(password);

            ThemeManager.Apply(services.Settings);
            ThemeManager.WatchSystemChanges(() => services.Settings);

            // Keep yesterday's safety net current before the user touches anything.
            try
            {
                if (services.Backup.RunDailyBackupIfDue(services.Settings) is not null)
                    services.SaveSettings();
            }
            catch (Exception backupFailure)
            {
                Log("Daily backup failed", backupFailure);
            }

            // Build the main window first so it is warm and ready, then let the
            // startup screen cover the moment it takes to appear.
            MainWindow = new MainWindow();

            if (SplashWindow.ShouldShow(services.Settings))
            {
                try
                {
                    new SplashWindow().ShowDialog();
                }
                catch (Exception splashFailure)
                {
                    // The startup screen is decoration. It must never be the reason
                    // somebody cannot reach their records.
                    Log("The startup screen failed", splashFailure);
                }
            }

            MainWindow.Show();
            MainWindow.Activate();
        }
        catch (Exception startupFailure)
        {
            Log("Startup failed", startupFailure);

            MessageBox.Show(
                "acvayrecords could not start.\n\n" +
                startupFailure.Message +
                "\n\nA full report was written to:\n" + AppPaths.LogsDir,
                "acvayrecords could not start",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    /// <summary>
    /// Works out whether the database needs a password, and asks for it if so.
    ///
    /// The settings file and the database are separate files and can be copied apart,
    /// so neither is trusted to describe the other. What actually decides this is
    /// whether the file opens without a key.
    /// </summary>
    private static bool TryUnlockDatabase(AppSettings settings, out string? password)
    {
        password = null;

        var file = AppPaths.DatabaseFile;

        // A database that does not exist yet is about to be created, unencrypted.
        if (!File.Exists(file)) return true;

        if (Database.CanOpen(file, null))
        {
            // Opens with no password, so it is not encrypted whatever the settings say.
            if (settings.EncryptionEnabled)
            {
                settings.EncryptionEnabled = false;
                SettingsStore.Save(settings);
            }
            return true;
        }

        var prompt = new PasswordWindow(PasswordPurpose.Unlock, file);

        if (prompt.ShowDialog() != true)
        {
            // Cancelling is a decision, not a fault: the app simply does not open.
            return false;
        }

        password = prompt.Password;

        if (!settings.EncryptionEnabled)
        {
            settings.EncryptionEnabled = true;
            SettingsStore.Save(settings);
        }

        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { AppServices.Current?.Announcer.Dispose(); }
        catch (Exception) { /* nothing useful to do while shutting down */ }

        base.OnExit(e);
    }

    /// <summary>
    /// Catches anything a command forgot to handle. The app stays open and says what
    /// went wrong, rather than vanishing and leaving a screen-reader user with silence.
    /// </summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("Unhandled error", e.Exception);

        var announcer = AppServices.Current?.Announcer;
        announcer?.SayError("Something went wrong. " + e.Exception.Message);

        MessageBox.Show(
            "Something went wrong, but your records are safe and the application is still running.\n\n" +
            e.Exception.Message +
            "\n\nDetails were written to:\n" + AppPaths.LogsDir,
            "Unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }

    private void OnFatalException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception) Log("Fatal error", exception);
    }

    /// <summary>Appends to a dated plain-text log inside the data folder.</summary>
    internal static void Log(string context, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDir);
            var file = Path.Combine(AppPaths.LogsDir, $"recordkeeper-{DateTime.Now:yyyy-MM-dd}.log");

            var entry = new StringBuilder()
                .AppendLine(new string('-', 70))
                .AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {context}")
                .AppendLine(exception.ToString())
                .ToString();

            File.AppendAllText(file, entry);
        }
        catch (Exception)
        {
            // If logging itself fails there is nowhere left to report it.
        }
    }
}
