using System.Text;
using System.Windows;
using System.Windows.Data;
using RecordKeeper.App.Commands;

namespace RecordKeeper.App.Views;

/// <summary>
/// The F1 reference. It is generated from the same command registry that drives the
/// menu and the palette, so it can never list a shortcut that no longer works.
/// </summary>
public partial class ShortcutHelpWindow : Window
{
    private readonly CommandRegistry _registry;

    public ShortcutHelpWindow(CommandRegistry registry)
    {
        InitializeComponent();

        _registry = registry;

        FilterBox.TextChanged += (_, _) => Populate();
        CloseButton.Click += (_, _) => Close();
        CopyButton.Click += (_, _) => CopyAll();

        Loaded += (_, _) =>
        {
            Populate();
            FilterBox.Focus();
        };
    }

    private void Populate()
    {
        var matches = _registry.Search(FilterBox.Text);

        var view = new CollectionViewSource { Source = matches };
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AppCommand.Group)));

        ShortcutList.ItemsSource = view.View;
    }

    /// <summary>
    /// Puts the whole reference on the clipboard as plain text, so it can be pasted
    /// into a document, enlarged, printed, or read in a preferred editor.
    /// </summary>
    private void CopyAll()
    {
        var text = new StringBuilder("acvayrecords keyboard shortcuts").AppendLine().AppendLine();

        foreach (var group in _registry.Grouped())
        {
            text.AppendLine(group.Key);
            text.AppendLine(new string('-', group.Key.Length));

            foreach (var command in group)
            {
                text.Append("  ").Append(command.Name);
                if (command.Shortcut.Length > 0) text.Append("  [").Append(command.Shortcut).Append(']');
                text.AppendLine();

                if (command.Description.Length > 0)
                    text.Append("      ").AppendLine(command.Description);
            }

            text.AppendLine();
        }

        try
        {
            Clipboard.SetText(text.ToString());
            AppServices.Current.Announcer.SaySuccess("The shortcut list was copied to the clipboard.");
        }
        catch (Exception exception)
        {
            App.Log("Copying the shortcut list failed", exception);
            AppServices.Current.Announcer.SayError("The list could not be copied to the clipboard.");
        }
    }
}
