using RecordKeeper.Core.Models;

namespace RecordKeeper.Core.Data;

/// <summary>What <see cref="SeedData.EnsureStarterTypes"/> actually did.</summary>
public sealed class SeedOutcome
{
    public List<string> Added { get; } = new();
    public List<string> Removed { get; } = new();
    public List<string> KeptBecauseInUse { get; } = new();

    public bool AnythingHappened => Added.Count > 0 || Removed.Count > 0;

    /// <summary>A sentence for the status bar, or empty when nothing changed.</summary>
    public string Describe()
    {
        if (!AnythingHappened) return string.Empty;

        var parts = new List<string>();

        if (Added.Count > 0)
            parts.Add($"Added the record {(Added.Count == 1 ? "type" : "types")} {Join(Added)}.");

        if (Removed.Count > 0)
            parts.Add($"Removed the unused example {(Removed.Count == 1 ? "type" : "types")} {Join(Removed)}.");

        if (KeptBecauseInUse.Count > 0)
            parts.Add($"Kept {Join(KeptBecauseInUse)}, because {(KeptBecauseInUse.Count == 1 ? "it has" : "they have")} records in them.");

        return string.Join(" ", parts);
    }

    private static string Join(List<string> names) => names.Count switch
    {
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1]
    };
}

/// <summary>
/// Installs the record types the app opens with.
///
/// This runs on every start but does its work once, tracked by a marker in the meta
/// table. That lets the starter set be revised later without wiping anything the user
/// has since built: a type is only ever removed when it still holds no records at all.
/// </summary>
public static class SeedData
{
    private const string MarkerKey = "starter_set";

    /// <summary>Bump this when the starter set below changes.</summary>
    private const string MarkerValue = "2";

    /// <summary>
    /// Types from the first version of the app. They are examples, so they are cleared
    /// away when the starter set is revised, but only while they are empty.
    /// </summary>
    private static readonly string[] SupersededExampleTypes =
        { "Contact", "Expense", "Document", "Appointment" };

    public static SeedOutcome EnsureStarterTypes(Database database, RecordTypeRepository repository)
    {
        var outcome = new SeedOutcome();

        if (database.GetMeta(MarkerKey) == MarkerValue) return outcome;

        var existing = repository.GetAll();

        foreach (var wanted in BuildStarterTypes())
        {
            if (existing.Any(t => t.Name.Equals(wanted.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            repository.Save(wanted);
            outcome.Added.Add(wanted.Name);
        }

        foreach (var name in SupersededExampleTypes)
        {
            var stale = existing.FirstOrDefault(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (stale is null) continue;

            // Never destroy a type the user has actually put records into, even if it
            // started life as one of our examples.
            if (repository.TryDelete(stale.Id, out _)) outcome.Removed.Add(stale.Name);
            else outcome.KeptBecauseInUse.Add(stale.Name);
        }

        database.SetMeta(MarkerKey, MarkerValue);
        return outcome;
    }

    public static List<RecordType> BuildStarterTypes() => new()
    {
        // ================================================= Purchased product
        new RecordType
        {
            Name = "Purchased product",
            TitleLabel = "Item",
            Description = "Something you bought: how many, what each one cost, and when.",
            SortOrder = 0,
            Fields =
            {
                Field("Quantity", FieldDataType.Number, required: true,
                      help: "How many pieces were bought."),

                Field("Price per piece", FieldDataType.Money, required: true,
                      help: "What a single piece cost."),

                // Worked out, never typed.
                Calculated("Total price", "[Quantity] * [Price per piece]",
                           help: "Quantity multiplied by the price of one piece."),

                Field("Bought on", FieldDataType.Date, required: true, defaultValue: "today",
                      help: "The day the purchase was made.")
            }
        },

        // ================================================= Worker
        new RecordType
        {
            Name = "Worker",
            TitleLabel = "Worker name",
            Description = "Work given to a worker, what is owed for it, and what has been paid.",
            SortOrder = 1,
            Fields =
            {
                Field("Product weight", FieldDataType.Number, required: true,
                      help: "The weight of product given to this worker."),

                Field("Total amount", FieldDataType.Money, required: true,
                      help: "The full amount owed for this work.", totalInList: true),

                Field("Work given at", FieldDataType.Date, defaultValue: "today",
                      help: "The day the work was handed over."),

                Field("Paid amount", FieldDataType.Money, defaultValue: "0",
                      help: "How much has been paid to this worker so far.", totalInList: true),

                Field("Amount given at", FieldDataType.Date, showInList: false,
                      help: "The day the payment was handed over. Leave this empty until you pay."),

                // Worked out, never typed.
                Calculated("Remaining amount", "[Total amount] - [Paid amount]",
                           help: "The total amount less whatever has already been paid.")
            }
        },

        // ================================================= Order
        new RecordType
        {
            Name = "Order",
            TitleLabel = "Customer name",
            Description = "An order from a customer, with what it earns and what it costs.",
            SortOrder = 2,
            Fields =
            {
                Field("Product name", FieldDataType.Text, required: true,
                      help: "What the customer ordered."),

                Field("Price", FieldDataType.Money, required: true,
                      help: "What the customer is paying.", totalInList: true),

                Field("Expense", FieldDataType.Money,
                      help: "What it cost you to fulfil this order.", totalInList: true),

                Field("Ordered at", FieldDataType.Date, required: true, defaultValue: "today",
                      help: "The day the order was placed.")
            }
        }
    };

    private static FieldDef Field(
        string name,
        FieldDataType dataType,
        bool required = false,
        string choices = "",
        string defaultValue = "",
        string help = "",
        bool showInList = true,
        bool totalInList = false) => new()
    {
        Name = name,
        DataType = dataType,
        IsRequired = required,
        Choices = choices,
        DefaultValue = defaultValue,
        HelpText = help,
        ShowInList = showInList,
        TotalInList = totalInList
    };

    /// <summary>
    /// A money field the app works out from the others on the record.
    ///
    /// A worked-out total is the one a column of money is almost always added up for,
    /// so these start ticked for the running total under the list.
    /// </summary>
    private static FieldDef Calculated(
        string name,
        string formula,
        string help = "",
        bool showInList = true,
        bool totalInList = true) => new()
    {
        Name = name,
        DataType = FieldDataType.Money,
        Formula = formula,
        HelpText = help,
        ShowInList = showInList,
        TotalInList = totalInList,
        IsRequired = false
    };
}
