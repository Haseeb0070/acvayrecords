using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RecordKeeper.App.Accessibility;
using RecordKeeper.Core.Models;

namespace RecordKeeper.App.Views;

/// <summary>
/// Asks which category a new record belongs to.
///
/// Needed because a group, and All records, cover several categories at once. Rather
/// than leave the New record button dead on those views, the question the app cannot
/// answer by itself is put to the user, once, in a window that can be driven entirely
/// from the keyboard.
/// </summary>
public partial class ChooseCategoryWindow : Window
{
    /// <summary>A category dressed for the list, with its counts already worded.</summary>
    private sealed class CategoryRow
    {
        public required RecordType Type { get; init; }

        public string Name => Type.Name;

        public string Summary
        {
            get
            {
                var fields = Describe(Type.Fields.Count, "field", "fields");
                var records = Describe(Type.RecordCount, "record", "records");
                return $"{fields}, {records}. Starts with the {Type.EffectiveTitleLabel.ToLowerInvariant()}.";
            }
        }

        public string AccessibleName => $"{Name}. {Summary}";

        private static string Describe(int count, string one, string many) =>
            count == 1 ? $"1 {one}" : $"{count} {many}";
    }

    private readonly Announcer _announcer;

    /// <summary>The category the user picked, or null when they cancelled.</summary>
    public RecordType? Chosen { get; private set; }

    public ChooseCategoryWindow(AppServices services, IReadOnlyList<RecordType> choices, string whereFrom)
    {
        InitializeComponent();

        _announcer = services.Announcer;

        HeadingText.Text = "Which category?";
        IntroText.Text =
            $"{whereFrom} covers {choices.Count} categories, so the new record needs to be " +
            "told which one it belongs to. Its fields come from the category you choose.";

        var rows = choices.Select(type => new CategoryRow { Type = type }).ToList();
        CategoryList.ItemsSource = rows;
        CategoryList.SelectedIndex = 0;

        CategoryList.SelectionChanged += (_, _) =>
        {
            if (Selected is { } row) _announcer.Say(row.AccessibleName);
        };

        // Enter on the list, and a double-click, mean the same as Continue.
        CategoryList.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Accept();
            e.Handled = true;
        };
        // Only a double-click on a row counts. Double-clicking the empty space below
        // the last row would otherwise start a record in whatever happened to be
        // selected, which is not what anyone meant by it.
        CategoryList.MouseDoubleClick += (_, e) =>
        {
            if (!IsOnARow(e.OriginalSource as DependencyObject)) return;
            Accept();
        };

        ContinueButton.Click += (_, _) => Accept();
        CancelButton.Click += (_, _) => Close();

        Loaded += (_, _) =>
        {
            VisualTreeHelpers.FocusAccessibly(CategoryList);
            _announcer.Say(
                $"Which category? {choices.Count} to choose from. " +
                "Use the up and down arrow keys, then press Enter. " +
                (Selected is { } row ? row.AccessibleName : string.Empty));
        };
    }

    private CategoryRow? Selected => CategoryList.SelectedItem as CategoryRow;

    /// <summary>True when the click landed on a row rather than the space below them.</summary>
    private static bool IsOnARow(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ListBoxItem) return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void Accept()
    {
        if (Selected is not { } row)
        {
            _announcer.Say("Choose a category first.", AnnouncementKind.Warning);
            return;
        }

        Chosen = row.Type;
        DialogResult = true;
    }
}
