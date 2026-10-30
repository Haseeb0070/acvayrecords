namespace RecordKeeper.App.Commands;

/// <summary>One named action, reachable from the menu, a shortcut and the command palette.</summary>
public sealed class AppCommand
{
    public required string Name { get; init; }

    /// <summary>Heading this command sits under in the shortcut list.</summary>
    public required string Group { get; init; }

    /// <summary>The key combination as the user should read it, for example "Ctrl + N".</summary>
    public string Shortcut { get; init; } = string.Empty;

    /// <summary>A sentence saying what the command does. Spoken in the palette and the help window.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Extra words the palette will match on, for users who know a different term.</summary>
    public string Keywords { get; init; } = string.Empty;

    public required Action Execute { get; init; }

    public Func<bool>? CanExecute { get; init; }

    public bool IsAvailable => CanExecute?.Invoke() ?? true;

    /// <summary>The single line a screen reader reads for this command.</summary>
    public string AccessibleName =>
        Shortcut.Length > 0
            ? $"{Name}. Shortcut {Shortcut}. {Description}"
            : $"{Name}. {Description}";

    public override string ToString() => Name;
}

/// <summary>
/// The catalogue of everything the application can do.
///
/// This exists so that there is exactly one list of commands feeding the menu, the
/// F1 shortcut reference and the Ctrl+K palette. A keyboard-only user is badly served
/// by a help screen that has drifted out of step with the real shortcuts, and a single
/// source makes that drift impossible.
/// </summary>
public sealed class CommandRegistry
{
    private readonly List<AppCommand> _commands = new();

    public IReadOnlyList<AppCommand> All => _commands;

    public void Add(AppCommand command) => _commands.Add(command);

    public void Add(string group, string name, string shortcut, string description,
                    Action execute, Func<bool>? canExecute = null, string keywords = "")
    {
        _commands.Add(new AppCommand
        {
            Group = group,
            Name = name,
            Shortcut = shortcut,
            Description = description,
            Keywords = keywords,
            Execute = execute,
            CanExecute = canExecute
        });
    }

    /// <summary>
    /// Finds commands matching what the user has typed. Every word must appear
    /// somewhere, so "exp pdf" finds "Export to PDF" without needing exact wording.
    /// </summary>
    public List<AppCommand> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return _commands.ToList();

        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return _commands
            .Select(command => new
            {
                Command = command,
                Haystack = $"{command.Name} {command.Group} {command.Description} {command.Keywords}"
            })
            .Where(entry => words.All(word =>
                entry.Haystack.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(entry =>
                entry.Command.Name.StartsWith(words[0], StringComparison.OrdinalIgnoreCase))
            .ThenBy(entry => entry.Command.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(entry => entry.Command)
            .ToList();
    }

    /// <summary>Commands grouped for the help window, in the order they were registered.</summary>
    public List<IGrouping<string, AppCommand>> Grouped() =>
        _commands.GroupBy(command => command.Group).ToList();
}
