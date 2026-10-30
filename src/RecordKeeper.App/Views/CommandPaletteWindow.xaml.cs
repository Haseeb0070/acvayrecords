using System.Windows;
using System.Windows.Input;
using RecordKeeper.App.Accessibility;
using RecordKeeper.App.Commands;

namespace RecordKeeper.App.Views;

/// <summary>
/// A searchable list of every command.
///
/// Menus are a poor fit for a keyboard-only user who knows what they want: they mean
/// remembering which menu holds which item. Typing "pdf" and pressing Enter is both
/// faster and needs nothing memorised.
/// </summary>
public partial class CommandPaletteWindow : Window
{
    private readonly CommandRegistry _registry;
    private readonly Announcer _announcer;

    public CommandPaletteWindow(CommandRegistry registry, Announcer announcer)
    {
        InitializeComponent();

        _registry = registry;
        _announcer = announcer;

        QueryBox.TextChanged += (_, _) => Filter();
        QueryBox.PreviewKeyDown += OnQueryKeyDown;
        ResultList.MouseDoubleClick += (_, _) => Run();
        ResultList.PreviewKeyDown += OnListKeyDown;

        RunButton.Click += (_, _) => Run();
        CloseButton.Click += (_, _) => DialogResult = false;

        Loaded += (_, _) =>
        {
            Filter();
            QueryBox.Focus();
        };
    }

    /// <summary>The command the user picked, or null if they closed the dialog.</summary>
    public AppCommand? ChosenCommand { get; private set; }

    private void Filter()
    {
        var matches = _registry.Search(QueryBox.Text);

        ResultList.ItemsSource = matches;
        if (matches.Count > 0) ResultList.SelectedIndex = 0;

        ResultCountText.Text = matches.Count switch
        {
            0 => "No commands match. Try fewer words.",
            1 => "1 command matches. Press Enter to run it.",
            _ => $"{matches.Count} commands match. Press the down arrow to reach them."
        };
    }

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        // The down arrow hands over to the results without losing the typed text.
        if (e.Key is Key.Down && ResultList.Items.Count > 0)
        {
            ResultList.Focus();
            if (ResultList.SelectedIndex < 0) ResultList.SelectedIndex = 0;
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Run();
            e.Handled = true;
            return;
        }

        // Typing a letter in the list sends the user back to the search box, so they
        // never end up stuck typing into a list that ignores them.
        if (e.Key is >= Key.A and <= Key.Z && Keyboard.Modifiers == ModifierKeys.None)
        {
            QueryBox.Focus();
            QueryBox.CaretIndex = QueryBox.Text.Length;
        }
    }

    private void Run()
    {
        if (ResultList.SelectedItem is not AppCommand command)
        {
            _announcer.Say("No command is selected.", AnnouncementKind.Warning);
            return;
        }

        ChosenCommand = command;
        DialogResult = true;
    }
}
