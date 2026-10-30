using System.Windows;
using System.Windows.Controls;
using RecordKeeper.Core.Data;

namespace RecordKeeper.App.Views;

public enum PasswordPurpose
{
    /// <summary>Ask for the password that opens an already-encrypted database.</summary>
    Unlock,

    /// <summary>Choose a new password, typed twice, to turn encryption on.</summary>
    SetNew,

    /// <summary>Confirm the current password before turning encryption off.</summary>
    Confirm
}

/// <summary>
/// Asks for a database password.
///
/// A "show the password" option is offered deliberately. The usual advice to hide it
/// assumes someone is reading over your shoulder; for a user working at high
/// magnification, a row of dots they cannot verify is how a password gets mistyped
/// twice and a database becomes unopenable.
/// </summary>
public partial class PasswordWindow : Window
{
    private readonly PasswordPurpose _purpose;
    private readonly string _databaseFile;

    public PasswordWindow(PasswordPurpose purpose, string? databaseFile = null)
    {
        InitializeComponent();

        _purpose = purpose;
        _databaseFile = databaseFile ?? AppPaths.DatabaseFile;

        ApplyPurpose();

        ShowPasswordBox.Checked += (_, _) => SetVisible(true);
        ShowPasswordBox.Unchecked += (_, _) => SetVisible(false);

        OkButton.Click += (_, _) => Submit();

        Loaded += (_, _) => FirstBox.Focus();
    }

    /// <summary>The password the user settled on, once the window closes with true.</summary>
    public string? Password { get; private set; }

    private void ApplyPurpose()
    {
        switch (_purpose)
        {
            case PasswordPurpose.Unlock:
                Title = "Unlock your records";
                HeadingText.Text = "Unlock your records";
                IntroText.Text =
                    "This database is encrypted. Type the password to open it.";
                FirstLabel.Content = "_Password";
                OkButton.Content = "_Unlock";
                ConfirmPanel.Visibility = Visibility.Collapsed;
                WarningPanel.Visibility = Visibility.Collapsed;
                break;

            case PasswordPurpose.SetNew:
                Title = "Turn on encryption";
                HeadingText.Text = "Turn on encryption";
                IntroText.Text =
                    "Choose a password. From now on it will be needed every time " +
                    "acvayrecords starts.";
                FirstLabel.Content = "New _password";
                OkButton.Content = "Encrypt the _database";
                break;

            case PasswordPurpose.Confirm:
                Title = "Turn off encryption";
                HeadingText.Text = "Turn off encryption";
                IntroText.Text =
                    "Type the current password to confirm. The database will be left " +
                    "unencrypted, readable by anyone who can reach the file.";
                FirstLabel.Content = "Current _password";
                OkButton.Content = "Remove the _encryption";
                ConfirmPanel.Visibility = Visibility.Collapsed;
                WarningPanel.Visibility = Visibility.Collapsed;
                break;
        }

        AutomationName(FirstBox, (string)FirstLabel.Content);
    }

    private static void AutomationName(DependencyObject element, string label) =>
        System.Windows.Automation.AutomationProperties.SetName(
            element, label.Replace("_", string.Empty));

    /// <summary>
    /// Swaps between the masked boxes and plain text ones, carrying the text across so
    /// nothing is lost when the box is ticked mid-typing.
    /// </summary>
    private void SetVisible(bool visible)
    {
        if (visible)
        {
            VisibleFirstBox.Text = FirstBox.Password;
            VisibleSecondBox.Text = SecondBox.Password;
        }
        else
        {
            FirstBox.Password = VisibleFirstBox.Text;
            SecondBox.Password = VisibleSecondBox.Text;
        }

        var masked = visible ? Visibility.Collapsed : Visibility.Visible;
        var plain = visible ? Visibility.Visible : Visibility.Collapsed;

        FirstBox.Visibility = masked;
        VisibleFirstBox.Visibility = plain;

        var needsConfirm = _purpose == PasswordPurpose.SetNew;
        ConfirmPanel.Visibility = needsConfirm && !visible ? Visibility.Visible : Visibility.Collapsed;
        VisibleSecondBox.Visibility = needsConfirm && visible ? Visibility.Visible : Visibility.Collapsed;

        (visible ? (Control)VisibleFirstBox : FirstBox).Focus();
    }

    private string FirstValue =>
        ShowPasswordBox.IsChecked == true ? VisibleFirstBox.Text : FirstBox.Password;

    private string SecondValue =>
        ShowPasswordBox.IsChecked == true ? VisibleSecondBox.Text : SecondBox.Password;

    private void Fail(string message)
    {
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;

        // This window can run before the application's services exist, when it is
        // asking for the password that lets them start at all.
        AppServices.Current?.Announcer.SayError(message);
    }

    private void Submit()
    {
        var first = FirstValue;

        if (first.Length == 0)
        {
            Fail("Type the password first.");
            FirstBox.Focus();
            return;
        }

        switch (_purpose)
        {
            case PasswordPurpose.SetNew:
                if (first.Length < 6)
                {
                    Fail("Use at least six characters. A short password protects very little.");
                    return;
                }

                if (first != SecondValue)
                {
                    Fail("The two passwords are not the same. Type the same one in both boxes, " +
                         "or tick the show option to check what you have typed.");
                    return;
                }
                break;

            case PasswordPurpose.Unlock:
            case PasswordPurpose.Confirm:
                if (!Database.CanOpen(_databaseFile, first))
                {
                    Fail("That password does not open this database. Try again.");

                    if (ShowPasswordBox.IsChecked == true) VisibleFirstBox.SelectAll();
                    else FirstBox.SelectAll();

                    (ShowPasswordBox.IsChecked == true ? (Control)VisibleFirstBox : FirstBox).Focus();
                    return;
                }
                break;
        }

        Password = first;
        DialogResult = true;
    }
}
