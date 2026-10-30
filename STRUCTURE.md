# What is in acvayrecords, file by file

A map of the whole system: every folder and file in the repository, what each one is
for, what the application writes while it runs, and what the database holds.

`README.md` explains what the program *does*. This explains where everything *is*.

---

## 1. The shape of it

Three projects and a checking tool, plus the files a build produces.

```
acvayrecords\
│
├─ README.md                  what the program does, and how to use it
├─ STRUCTURE.md               this document
├─ .gitignore                 keeps build output and local data out of the repository
├─ RecordKeeper.sln           the solution: ties the four projects together
│
├─ src\
│   ├─ RecordKeeper.Core\     the records themselves. No screen, no windows.
│   ├─ RecordKeeper.App\      the application you see
│   └─ RecordKeeper.Setup\    the installer
│
├─ tools\
│   ├─ ThemeContrastCheck\    proves every colour pair can be read
│   └─ build-release.cmd      builds the application, then the installer round it
│
├─ dist\                      build output: the application
└─ release\                   build output: the installer to hand out
```

Every project folder also holds its own `.csproj`, which lists that project's settings
and what it depends on: `RecordKeeper.Core.csproj`, `RecordKeeper.App.csproj`,
`RecordKeeper.Setup.csproj` and `ThemeContrastCheck.csproj`.

`.gitignore` keeps the build output, the installer payload, any signing certificate
and any local database out of the repository.

**A note on the names.** The product is **acvayrecords**; the projects are still named
`RecordKeeper`, which is what it was called while it was being built. The namespaces,
the solution and the folders kept that name deliberately — renaming them changes every
file in the repository and buys nothing a reader cannot be told in one sentence.

The direction of dependency only ever goes one way:

```
RecordKeeper.App  ──uses──▶  RecordKeeper.Core
RecordKeeper.Setup           (uses nothing here, on purpose)
ThemeContrastCheck ──reads──▶ RecordKeeper.App's theme files
```

**Core knows nothing about windows.** It can be run, tested and reasoned about with no
screen attached, which is why every rule about what a record *is* lives there rather
than in a button's click handler.

**The installer depends on nothing in the solution.** It has to keep working on a
machine where the application cannot even start, so it shares no code with it. The
application reaches it as a file, already built.

---

## 2. `src\RecordKeeper.Core` — the records

No user interface. Models, the database, and the three services that turn records into
files. About 4,150 lines.

### `Data\` — where records are kept

| File | What it holds |
|---|---|
| `Database.cs` | Opens the SQLite file, creates the schema, runs the version upgrades, makes backups, and turns encryption on and off. The single gatekeeper for the file. |
| `AppPaths.cs` | Works out every location the program writes to, and the redirect file that lets records live anywhere. |
| `RecordRepository.cs` | Reading and writing records: the query that drives every list, the totals, the recycle bin, and re-applying formulas to records already saved. The largest file in Core. |
| `RecordTypeRepository.cs` | Categories and their fields: reading, saving, renaming, deleting, duplicating. |
| `CategoryGroupRepository.cs` | Groups, and the expense grand total across all the categories in one. |
| `AttachmentRepository.cs` | Files copied into the vault beside the database, and their links to records. |
| `SeedData.cs` | The three starter categories a new database is born with, and the marker in `meta` that stops them coming back after you delete them. |

### `Models\` — what a record is

| File | What it holds |
|---|---|
| `RecordItem.cs` | One record, plus `RecordQuery` (every way of narrowing a list), `RecordTotals` and `RecalculationResult`. |
| `RecordType.cs` | A category and its `FieldDef` fields, including the flags for expense and for totalling. |
| `FieldDataType.cs` | The seven kinds of field, and what can be said about each. |
| `FieldValueFormat.cs` | The three forms every value takes: what you type, what is stored, and what is shown or read aloud. Dates are stored `yyyy-MM-dd` so they sort as text. |
| `FormulaEvaluator.cs` | Arithmetic on field names in brackets. Four operators and brackets, nothing more. |
| `RecordCalculator.cs` | Applies a category's formulas to one record. Called on every save, so a stored total cannot be stale. |
| `RecordSummary.cs` | Adds up records already in hand, for the exports, so a report describes exactly the rows it printed. |
| `Money.cs` | The currency: rupees unless changed, written `Rs 1,250.50` and read aloud as "1,250.50 rupees". |
| `CategoryGroup.cs` | A group of categories, with its record count and expense total. |
| `AppSettings.cs` | Everything remembered between launches, and the reading and writing of `settings.json`. |
| `Brand.cs` | The brand in one place: names, colours, the "Powered by Haseeb" line. |

### `Services\` — records turned into files

| File | What it holds |
|---|---|
| `PdfExportService.cs` | Two reports: a list with a totals row, and a field-by-field detail report. Both carry the logo and the footer. |
| `ExportService.cs` | Plain text, which is the best format to read back with a screen reader, and CSV for a spreadsheet. |
| `BackupService.cs` | Dated zips of the database and the attachments, with the daily ones kept to a limit. |

---

## 3. `src\RecordKeeper.App` — the application

WPF, about 10,950 lines of code and markup. Views are windows, view models hold state,
and the view models open no dialogs and move no focus — those belong to the window,
which can do them accessibly.

### At the root

| File | What it holds |
|---|---|
| `App.xaml` / `App.xaml.cs` | Startup: the splash screen, unlocking an encrypted database, the first-run setup, and the log written when something goes wrong. |
| `AppServices.cs` | Everything built once at startup, wired by hand. Small enough that a container would be less clear. |
| `Converters.cs` | The handful of value converters the bindings need. |
| `VisualTreeHelpers.cs` | Finding things in the visual tree, and moving focus in a way a screen reader follows. |
| `AssemblyInfo.cs` | Theme and assembly attributes WPF requires. |

### `Views\` — one file pair per window

Every window below is two files: `Name.xaml` holds the markup, `Name.xaml.cs` the code
behind it. So `MainWindow` means `MainWindow.xaml` and `MainWindow.xaml.cs`.

| Window | What it is for |
|---|---|
| `MainWindow` | The whole application: categories on the left, records in the middle, and the record editor in the same space. 913 lines of markup, 1,827 of code. |
| `SplashWindow` | The logo, the animation, and the credit line while the application starts. Skipped when a screen reader is running. |
| `RecordTypeEditorWindow` | Categories and their fields: add, rename, reorder, duplicate, delete. |
| `NewCategoryWindow` | The short form for making a category, with a quick way to add its first fields. |
| `FieldEditWindow` | One field: its kind, whether it is required, its formula, and the two totalling ticks. |
| `ManageGroupsWindow` | Groups: add, rename, delete. Deleting one never deletes its categories. |
| `ChooseCategoryWindow` | Asks which category a new record belongs to, on a group or on All records. |
| `SettingsWindow` | Reading, colour, sound, startup, money, backups, encryption, and where the data lives. |
| `PasswordWindow` | Asks for the database password, with the option to show it while typing. |
| `CommandPaletteWindow` | Ctrl+K: type a command name instead of hunting through menus. |
| `ShortcutHelpWindow` | F1: every command and its shortcut, from the same list the menus are built from. |
| `AboutWindow` | Version, where the data is, and the credit. |

### `ViewModels\` — the state behind the windows

| File | What it holds |
|---|---|
| `MainViewModel.cs` | What is selected, what is listed, what it adds up to, the sorting and the period, and whether the editor is open. The largest file in the solution at 1,229 lines. |
| `RecordEditorViewModel.cs` | One record being edited: its fields, its live totals, and whether it has unsaved changes. |
| `FieldEditorViewModel.cs` | A category being reshaped in the category editor, and the list of fields on it. |
| `RecordRowViewModel.cs` | One row in the list, worded as a sentence for a screen reader. |
| `NavigationItem.cs` | One entry on the left: a category, a group, All records, Favourites or the recycle bin. |
| `ListOptions.cs` | The choices above the list: what to sort by, which period, which date, and one line of a total. |
| `Mvvm\ObservableObject.cs` | Change notification, hand-rolled. No framework. |

### `Themes\` — how it looks

| File | What it holds |
|---|---|
| `Common.xaml` | Every control style, rebuilt rather than inherited so each one honours the theme. The biggest markup file at 885 lines. |
| `Light.xaml` | White with Telenor blues. The default. |
| `Dark.xaml` | The same palette inverted. |
| `HighContrast.xaml` | Defers almost entirely to the colours Windows itself provides. |
| `Branding.xaml` | The logo as vector geometry, the accent rule and the brand bar. Everything else is produced from this. |

### `Branding\`, `Accessibility\`, `Commands\`

| File | What it holds |
|---|---|
| `Branding\acvayrecords.ico` | The application icon, ten sizes, rendered from `Branding.xaml`. |
| `Branding\LogoImage.cs` | Turns that vector logo into the PNG stamped on every PDF. |
| `Accessibility\Announcer.cs` | One way to say something: to the status bar as a live region, as a sound, and optionally aloud. |
| `Accessibility\SpeechOutput.cs` | The optional voice, separate so a failure there cannot stop the message reaching the screen. |
| `Accessibility\ThemeManager.cs` | Applies a theme, the text size and the focus outline, and follows Windows into high contrast. |
| `Commands\CommandRegistry.cs` | Every command registered once, so the menu, F1 and Ctrl+K cannot disagree. |

---

## 4. `src\RecordKeeper.Setup` — the installer

About 970 lines.

| File | What it holds |
|---|---|
| `Installer.cs` | Everything done to the machine, with no user interface: writing the program out, pointing it at the records folder, the shortcuts, the Add or remove programs listing, and the uninstall. |
| `SetupWindow.xaml` / `.cs` | The one page: where the records go, where the program goes, the shortcuts. |
| `UninstallWindow.xaml` / `.cs` | What Add or remove programs runs. Leads with "Your records are not removed". |
| `App.xaml` / `.cs` | Chooses between the two windows: `--uninstall` picks the second. |
| `Themes\SetupTheme.xaml` | The installer's own copy of the brand colours, so it depends on nothing. |
| `payload\acvayrecords.exe` | The application being installed, put here by the build. Not source. |

The same executable is both installer and uninstaller: it copies itself into the
install folder as `uninstall.exe`.

---

## 5. `tools\`

| File | What it holds |
|---|---|
| `build-release.cmd` | Build the application, put it inside the installer, build the installer. |
| `ThemeContrastCheck\Program.cs` | Measures all 54 foreground-on-background pairs the interface puts together against WCAG 2.1 and fails the build if one cannot be read. |

Themes are the one part of an accessibility-led application that is easy to break by
eye, which is why the colours are measured rather than eyeballed.

---

## 6. What a build produces

| Folder | What lands there |
|---|---|
| `dist\acvayrecords.exe` | The application. One self-contained file, about 77 MB, with the .NET runtime and the SQLCipher library inside it. |
| `dist\brand\` | The logo as PNGs at several sizes, the light and dark lockups, and a sample report. |
| `release\acvayrecords-setup.exe` | The installer, about 140 MB, with the application inside it. This is the file to hand to someone. |
| `dist\LatoFont\`, `*.pdb` | A folder the PDF library makes, and the debugging symbols. Neither is needed to run anything. |
| `bin\`, `obj\` | Intermediate build files, under each project. Disposable. |

Both folders can be deleted at any time; `tools\build-release.cmd` makes them again.

---

## 7. What the application writes while it runs

None of this is in the repository. It is created on the machine the program runs on.

### Finding the records

```
%LOCALAPPDATA%\RecordKeeper\datapath.txt
```

One line of text holding the path of the real data folder. This is the only location
the application knows in advance, and the installer writes it. Without it, the data
folder is `%LOCALAPPDATA%\RecordKeeper` itself.

The folder is still called `RecordKeeper`, from before the program was renamed.
Renaming it would strand every existing installation: the application would look in a
new, empty folder and quietly start again.

### The data folder

```
<wherever datapath.txt points>\
├─ records.db            the database: every record, category, group and tag
├─ records.db.before-v3  a copy kept before a schema upgrade
├─ records.db.before-v4  likewise
├─ records.db.previous   the file as it was before encryption was turned on or off
├─ settings.json         text size, colours, currency, backups, window size
├─ attachments\          files copied in and linked to records. Not encrypted.
├─ backups\
│   ├─ auto\             one a day on first launch, oldest deleted past the limit
│   └─ manual\           made with Ctrl+B. Never deleted automatically.
├─ exports\              PDFs, text and CSV written here unless told otherwise
└─ logs\                 written only when something goes wrong. Empty is good.
```

Copying this one folder moves everything to another computer.

---

## 8. What the database holds

SQLite, currently **schema version 4**, recorded in the `meta` table. Opening an older
file upgrades it in place, one step at a time, inside a transaction.

| Table | What a row is |
|---|---|
| `meta` | A named value. `schema_version`, and the marker saying the starter categories were made. |
| `category_group` | A group, such as "Online trading". |
| `record_type` | A category, such as "Purchased product", and which group it is in. |
| `field_def` | One field on a category: its kind, whether required, its formula, and whether it counts towards the group expense total or the totals under the list. |
| `record` | One record: its title, notes, whether starred, whether in the recycle bin, and when it was made and changed. |
| `field_value` | One field's value on one record. `value_text` is the canonical form; `value_number` mirrors it for numbers so sums and sorting work in the database. |
| `tag` / `record_tag` | Tags, and which records carry them. |
| `attachment` | A file in the vault and the record it belongs to. |

Three deletion rules are built into the schema itself, because they matter more than
any code that might forget them:

- Deleting a **group** sets its categories' `group_id` to null. Categories and records
  survive, ungrouped.
- Deleting a **category** deletes its fields, and the values of those fields.
- A **record type that still has records** cannot be deleted at all — `ON DELETE
  RESTRICT`. The database refuses, rather than taking the records with it.

Deleting a record from the application does not delete the row: `is_deleted` is set,
which is what the recycle bin is. Only emptying the bin removes it for good.

---

## 9. Where to start reading

| To understand | Open |
|---|---|
| What a record is | `Core\Models\RecordItem.cs`, then `RecordType.cs` |
| How a list is built | `Core\Data\RecordRepository.cs`, the `Query` method |
| What the main window does | `App\ViewModels\MainViewModel.cs` |
| How the screen reader is kept informed | `App\Accessibility\Announcer.cs` |
| How it looks the way it does | `App\Themes\Common.xaml` |
| What installing does | `Setup\Installer.cs` |
