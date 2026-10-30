using System.Windows;
using RecordKeeper.Core.Models;

namespace RecordKeeper.App.Views;

/// <summary>
/// Creates a category in one guided step.
///
/// The old route was to press "Add a type", which immediately wrote a row called
/// "New type" into the database and left the user to rename it. Abandoning halfway
/// left that junk behind. Nothing is written here until Create is pressed.
/// </summary>
public partial class NewCategoryWindow : Window
{
    /// <summary>An entry in the "start from" list; a null type means start empty.</summary>
    private sealed record CopySource(RecordType? Type, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly AppServices _services;

    public NewCategoryWindow(AppServices services)
    {
        InitializeComponent();

        _services = services;

        var sources = new List<CopySource> { new(null, "Start with no fields") };
        sources.AddRange(services.RecordTypes.GetAll()
            .Select(type => new CopySource(type, $"Copy the fields from {type.Name}")));

        CopyFromBox.ItemsSource = sources;
        CopyFromBox.SelectedIndex = 0;

        CopyFromBox.SelectionChanged += (_, _) => UpdateCopyHint();
        CreateButton.Click += (_, _) => Create();

        Loaded += (_, _) =>
        {
            UpdateCopyHint();
            NameBox.Focus();

            _services.Announcer.Say(
                "New category. Type a name, then choose what its identifying field is called. " +
                "You add its fields on the next screen.");
        };
    }

    /// <summary>The category that was created, or null if the window was cancelled.</summary>
    public RecordType? Created { get; private set; }

    private void UpdateCopyHint()
    {
        var source = CopyFromBox.SelectedItem as CopySource;

        CopyHintText.Text = source?.Type is null
            ? "The new category starts with no fields. You add them on the next screen."
            : $"{source.Type.Name} has {Describe(source.Type.Fields.Count)}. " +
              "They are copied in so you can change them. The original is not touched.";
    }

    private static string Describe(int count) => count switch
    {
        0 => "no fields",
        1 => "1 field",
        _ => $"{count} fields"
    };

    private void Create()
    {
        var announcer = _services.Announcer;

        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            announcer.SayError("The category needs a name.");
            NameBox.Focus();
            return;
        }

        if (_services.RecordTypes.NameExists(name))
        {
            var free = _services.RecordTypes.SuggestName(name);
            announcer.SayError(
                $"There is already a category called {name}. Try {free}, or choose another name.");

            NameBox.Text = free;
            NameBox.Focus();
            NameBox.SelectAll();
            return;
        }

        var source = (CopyFromBox.SelectedItem as CopySource)?.Type;

        // Copying clones the fields with their ids cleared, so the new category owns
        // its own copies and editing one cannot disturb the other.
        var category = source is not null
            ? source.CloneForNew(name)
            : new RecordType { Name = name };

        category.Description = DescriptionBox.Text.Trim();
        category.TitleLabel = TitleLabelBox.Text.Trim();
        category.SortOrder = _services.RecordTypes.GetAll(includeFieldsAndCounts: false).Count;

        try
        {
            _services.RecordTypes.Save(category);
        }
        catch (Exception exception)
        {
            App.Log("Creating a category failed", exception);
            announcer.SayError("The category could not be created. " + exception.Message);
            return;
        }

        Created = category;
        DialogResult = true;
    }
}
