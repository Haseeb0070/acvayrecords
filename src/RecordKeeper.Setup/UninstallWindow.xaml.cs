using System.IO;
using System.Windows;

namespace RecordKeeper.Setup;

/// <summary>
/// What Add or remove programs runs. The one thing it has to be loud about is that
/// the user's records are not part of what is being removed.
/// </summary>
public partial class UninstallWindow : Window
{
    public UninstallWindow()
    {
        InitializeComponent();

        RecordsLine.Text = "They are in " + RecordsFolder() + ".";

        RemoveButton.Click += (_, _) => Remove();
        CancelButton.Click += (_, _) => Close();

        Loaded += (_, _) => CancelButton.Focus();
    }

    private static string RecordsFolder()
    {
        try
        {
            var redirect = Installer.RedirectFile;

            if (File.Exists(redirect))
            {
                var target = File.ReadAllText(redirect).Trim();
                if (target.Length > 0) return target;
            }

            return Path.GetDirectoryName(redirect) ?? "your records folder";
        }
        catch (IOException)
        {
            return "your records folder";
        }
    }

    private async void Remove()
    {
        // Files in use cannot be deleted, so there is no point starting.
        if (Installer.ApplicationIsRunning())
        {
            ResultText.Visibility = Visibility.Visible;
            ResultText.Foreground = (System.Windows.Media.Brush)FindResource("Error");
            ResultText.Text =
                "acvayrecords is still open. Close it, then press Remove again.";

            Say("Still open.");
            return;
        }

        RemoveButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        Cursor = System.Windows.Input.Cursors.Wait;

        try
        {
            await Task.Run(() => Installer.Uninstall(Say));
        }
        catch (Exception exception)
        {
            Cursor = null;

            // Most of what stops a removal — the program still being open — can be
            // put right in a moment, so Remove stays available to try again.
            RemoveButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            CancelButton.Content = "_Close";

            ResultText.Visibility = Visibility.Visible;
            ResultText.Foreground = (System.Windows.Media.Brush)FindResource("Error");
            ResultText.Text = "It could not all be removed. " + exception.Message;

            Say("Not removed.");
            RemoveButton.Focus();
            return;
        }

        Cursor = null;

        ResultText.Visibility = Visibility.Visible;
        ResultText.Foreground = (System.Windows.Media.Brush)FindResource("Good");
        ResultText.Text = "acvayrecords has been removed. Your records are untouched.";

        Say("Removed.");

        CancelButton.Content = "_Close";
        CancelButton.IsEnabled = true;
        CancelButton.Focus();
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
