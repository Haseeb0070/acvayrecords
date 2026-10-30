# acvayrecords

**A record keeper for Windows that works properly with a screen reader.**

A general-purpose record manager by acvaysoft, built for someone who works with
Narrator, Magnifier and the keyboard. Everything is stored on the machine it runs on:
no server, no account, no database engine to install, and no internet connection at
any point.

You define your own categories and the fields they hold. It adds up what you ask it
to, filters by date, exports to PDF, text and CSV, and can encrypt the database behind
a password.

| | |
|---|---|
| **Platform** | Windows 10 and 11, 64-bit |
| **Built with** | .NET 8 and WPF, SQLite, QuestPDF |
| **Installing** | One self-contained file. No .NET needed, no administrator rights |
| **Data** | One folder you choose. Copy it to move everything to another machine |
| **Powered by** | Haseeb |

**Contents** · [Installing](#installing-it-on-a-new-computer) ·
[What it stores](#what-it-stores) · [Accessibility](#accessibility) ·
[Keyboard shortcuts](#keyboard-shortcuts) · [Building from source](#building-from-source) ·
[Known limitations](#known-limitations)

**[STRUCTURE.md](STRUCTURE.md)** is the companion to this file: every folder and file,
what the application writes while it runs, and what the database holds.

---

## Installing it on a new computer

Hand someone **`release\acvayrecords-setup.exe`** — one file, about 140 MB. Nothing
has to be installed first: no .NET, no database, no administrator password.

It asks two questions:

1. **Where should your records be kept?** The one folder that holds the database, the
   attached files, the backups and the exports. Put it somewhere that gets backed up,
   or on a drive of its own. Pointing it at a folder that already has records opens
   those records as they are — which is how you move to a new computer: copy the
   folder across, install, and point the installer at it.
2. **Where should the program be installed?** Suggested under your own Local AppData,
   which is why no administrator password is needed.

Then it makes the Start menu and desktop shortcuts and lists itself in **Add or remove
programs**. Removing it takes away the program and the shortcuts and **leaves your
records exactly where they are** — the uninstaller says so before it does anything.

### Building the installer

```bash
tools\build-release.cmd
```

Three steps: build the application, put it inside the installer, build the installer.
The installer references no other project in this solution — it has to keep working
when the application it installs cannot even start — so the application reaches it as
a payload file rather than as code.

### Signing it, so Windows stops warning about it

Nothing here is code-signed, so a new machine shows **"Windows protected your PC"**
the first time the installer runs. The way past it is *More info* then *Run anyway*.
Removing the warning means buying a certificate and signing with it.

Prices and eligibility rules change. Check before paying for anything.

**A certificate from a certificate authority — Sectigo, DigiCert, SSL.com**

| | Cost a year | Warning on first download |
|---|---|---|
| OV, organisation validated | about $200 to $400 | Still shown, and fades as installs build reputation |
| EV, extended validation | about $400 to $700 | **Gone from the first download** |

Both are sold worldwide. Since 2023 the private key has to live on a hardware token
or a cloud HSM, so there is no `.pfx` file to copy about: the token is the key, and
losing it means buying another certificate.

**Microsoft Artifact Signing** — renamed from Trusted Signing in 2026 — is far
cheaper at about $10 a month, needs no hardware token, and Windows trusts it
immediately. The catch is where you live: individuals are limited to the USA and
Canada, organisations to the USA, Canada, the European Union and the United Kingdom.
From anywhere else, a certificate authority is the route.

**A certificate you make yourself** costs nothing and removes the warning completely
on machines where the certificate is installed. For a handful of local customers,
handed over in person, that is a real answer:

```powershell
$cert = New-SelfSignedCertificate -Type CodeSigningCert `
            -Subject "CN=Haseeb, O=acvaysoft" `
            -CertStoreLocation Cert:\CurrentUser\My `
            -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(5)

Export-PfxCertificate -Cert $cert -FilePath acvaysoft.pfx -Password (Read-Host -AsSecureString)
Export-Certificate   -Cert $cert -FilePath acvaysoft.cer
```

Then once on each machine, as administrator:

```powershell
Import-Certificate -FilePath acvaysoft.cer -CertStoreLocation Cert:\LocalMachine\Root
```

Be straight about what that buys. It proves the file came from whoever holds the key,
and it silences the warning **only where the certificate was installed**. A stranger
downloading the installer still sees the warning. Keep the `.pfx` and its password off
the repository and out of any backup that leaves your hands; anyone holding both can
sign anything in your name.

**Doing the signing.** `signtool.exe` comes with the Windows SDK — only the
"Windows SDK Signing Tools" component is needed, which is a small install.

```bash
signtool sign /f acvaysoft.pfx /p %PFX_PASSWORD% /fd SHA256 ^
         /tr http://timestamp.digicert.com /td SHA256 "dist\acvayrecords.exe"
```

Two things that are easy to get wrong:

**Always timestamp** (`/tr`). Without it, every copy ever shipped stops validating on
the day the certificate expires. With it, the signature stays good afterwards.

**Sign at two points, not one.** The installer carries the application inside it, so
signing the installer alone leaves an unsigned program within it, which some security
tools flag after it is installed. The order is:

1. Build the application, then sign `dist\acvayrecords.exe`
2. Copy it into `src\RecordKeeper.Setup\payload\`
3. Build the installer, then sign `release\acvayrecords-setup.exe`

`tools\build-release.cmd` does steps 1 to 3 without the signing. The two signing
commands go after the first and third steps, reading the password from an environment
variable rather than sitting in the file.

## Running it

`dist\acvayrecords.exe` is a single self-contained file. Copy it anywhere and
double-click it — the .NET runtime is inside it, so nothing needs installing.

## Where your data lives

One folder holds everything:

```
%LOCALAPPDATA%\RecordKeeper\
  records.db                        the database (SQLite)
  attachments\2026\10\<record-id>\  files you attached, original names kept
  backups\auto\                     the daily backup zips
  backups\manual\                   backups you made yourself (Ctrl+B)
  exports\                          PDF, text and CSV output
  logs\                             plain-text error log, written only on a fault
  settings.json                     your preferences, readable and hand-editable
  datapath.txt                      only present if you moved the data folder
```

Copying that one folder is how you back everything up or move to another computer.
**Settings → Move the data folder** copies it somewhere else (Documents, a USB drive)
and points future launches at it; the original is left behind as a safety net.

## What it stores

Records are grouped into **record types**. The app opens with three, and you can
rename, reshape or delete any of them in **Manage record types** (Ctrl+Shift+T).

**Purchased product** — something you bought.

| Field | Kind |
|---|---|
| Item | the identifying field |
| Quantity | number, required |
| Price per piece | money, required |
| **Total price** | **worked out:** `[Quantity] * [Price per piece]` |
| Bought on | date, required, defaults to today |

**Worker** — work given out, what is owed, what has been paid.

| Field | Kind |
|---|---|
| Worker name | the identifying field |
| Product weight | number, required |
| Total amount | money, required |
| Work given at | date, defaults to today |
| Paid amount | money, starts at 0 |
| Amount given at | date, left empty until you pay |
| **Remaining amount** | **worked out:** `[Total amount] - [Paid amount]` |

**Order** — an order from a customer.

| Field | Kind |
|---|---|
| Customer name | the identifying field |
| Product name | text, required |
| Price | money, required |
| Expense | money |
| Ordered at | date, required, defaults to today |

### Field kinds

| Kind | Notes |
|---|---|
| Short text | one line |
| Long text | many lines |
| Number | checked on save |
| Amount of money | shown in your regional currency format |
| Date | accepts `27/10/2026`, or just the word `today` / `tomorrow` / `yesterday` |
| Yes or no | a checkbox |
| Choice from a list | options you supply, one per line |
| **Worked out automatically** | a number or money field with a formula — see below |

Every record also has one required identifying field (which each type names for
itself — "Item", "Worker name", "Customer name"), free-form notes,
comma-separated tags, a favourite flag and any number of attached files.

### Adding a category

**Ctrl+Shift+N** opens a short form: the category's name, what its identifying field
should be called, an optional description, and an option to **start from an existing
category** — which copies that category's fields so you can change them rather than
retype them. The original is untouched; the copy owns its own fields.

Nothing is written until you press Create. The old route wrote a row called "New type"
into the database the moment you clicked, and abandoning halfway left it behind.

It then takes you straight to the field editor with the cursor in the **quick-add**
box: type a field name, choose what it holds, press Enter, and the box is ready for the
next one. Eight fields is eight lines of typing, not eight dialogs. Anything beyond a
name and a kind — required, options, a formula, the spoken explanation — is set with
**Edit field** afterwards.

**Ctrl+Shift+T** opens the same window on its own, where you can also duplicate or
delete a category. A category holding records cannot be deleted.

### Groups, and the expense grand total

Categories can be gathered into **groups** — "Online trading", "Offline trading" — from
**Categories and their fields**: pick a category, choose its group, or press **Manage
groups** to add, rename and delete them. The sidebar then shows each group as a heading
with its categories stepped in underneath.

Any **number or money field** can be ticked as *"Count this towards the group expense
total"* in **Edit field**. Every ticked field, in **any** category of the same group, is
added into one grand total shown against that group — in the sidebar, and as a panel
when the group is selected. A calculated field can be an expense too, so a worked-out
Total price can feed the group total directly.

The panel names the fields the total is made of, because a total nobody can check is a
total nobody can trust. A group with nothing marked says so, rather than showing a
misleading zero.

Deleting a group **never** deletes categories or records. They simply become ungrouped.

### What the records add up to

Every list is totalled. Above the records sits a panel adding up each number and money
field across **exactly the records shown** — the same query builds the list and the
sums, so the two cannot disagree. The totals are read out with the record count, so
the answer arrives without having to go looking for it.

A field nothing was entered in is **not** counted as a zero. Where a total came from
fewer records than are listed, the panel says so: *"from 97 of 100 records"*.

**You choose which fields are added up.** In **Edit field**, a number or money field
has a tick: *"Add this field up under the record list"*. Not every column of numbers
is worth a sum — adding up a *price per piece* gives the sum of the prices, which is a
real number and almost never the one anyone wanted — and only you know which of your
fields is which.

The starter categories come ready: *Total price*, *Total amount*, *Paid amount*,
*Remaining amount*, *Price* and *Expense* are totalled; *Price per piece* and
*Product weight* are not. An existing database is upgraded on the same principle — a
field that feeds a formula is left untotalled, because it is usually a rate or an
input, and everything else numeric is totalled. Where a category has numbers but
nothing ticked, the page says so rather than showing an empty space.

### The three buttons in the corner

Above the list, beside each other, sit the three things that change what the page
shows: **Sort and period**, **Show totals** and **Table view**. Each says what pressing
it will do, and each remembers the answer for next time.

Sorting and period start **hidden** — they are controls rather than content, and four
lists across the top of every category is a lot to tab past for someone who sets an
order once. The totals start **shown**, because a total is an answer.

Hiding the controls does not undo them. An order stays in force, and a period that is
narrowing the list keeps its line of explanation on screen even with the controls put
away — otherwise records would be missing with nothing to say why. The totals are read
out with the record count whether the panel is showing or not.

### Sorting and periods

**Sort by** covers the identifying field, **every one of your own fields**, and the
dates a record was added or changed. The order button shows the order the list is in
now — *Largest first*, *Oldest first*, *A to Z*, worded for the kind of value — and
pressing it reverses it. Records with nothing in the field being sorted go last either
way round: "not filled in" is not a small amount and not an early date.

**Show** narrows the list to *Today*, *This month*, *Last month*, *This year*, or
between two dates you type. A category with more than one date field also gets a **By
date** list, so you choose whether a period means *Bought on* or *Amount given at*.
With no date field, the date the record was added is used, and the line under the
controls always says which date is being measured.

The totals, the PDF and the plain-text export all follow the period. A period that
narrowed the list but not the total would be worse than no period at all.

### Money

Amounts are in **rupees** unless you change it, in **Settings → Money**: `Rs`, `₨`,
`₹`, dollars, pounds, euros, dirhams, or whatever Windows is set to. The setting shows
a real amount in the chosen currency before you commit to it.

This is deliberately **not** taken from the Windows regional settings. Your records are
in one currency whatever the machine is set to, and amounts that changed symbol after a
reinstall would be quietly wrong.

A screen reader spells "Rs" out letter by letter, so wherever the app speaks rather
than prints, an amount is read as *"1,250.50 rupees"*.

### Working out every total again

**Tools → Work out every total again.** A record works out its totals when it is saved,
so a formula you change afterwards does not reach the records already stored. This
goes back over them.

It takes a backup first, since it writes to many records on the strength of one
keystroke, and it leaves records whose totals are already right exactly as they are —
including their modified date. It says how many totals it changed, in which categories,
and out of how many records checked. Running it twice does nothing the second time.

### Leaving a record you have not saved

A half-written record is never thrown away quietly. Clicking another category, jumping
to one with Alt and a number, starting a second record, opening Settings or the
category list, running the totals again, or closing the application all stop and ask
first — one question, worded the same way everywhere:

> **Yes** saves it before going on. **No** throws the changes away. **Cancel** stays
> here and keeps editing.

Cancel really does stay: the category list puts its own highlight back, and the record
stays open with the cursor returned to it. If saving fails — a required field left
empty — nothing moves, because moving on would lose the record you just asked to keep.

A record you have opened but not changed is closed without a question. There is
nothing to lose, and a dialog that asks about nothing teaches people to dismiss
dialogs without reading them.

### Where a new record goes

**New record** only ever appears where it can work:

| Selected on the left | What New record does |
| --- | --- |
| A category | Adds a record straight into it |
| A group | Asks which of that group's categories it belongs to |
| All records | Asks which category it belongs to |
| A group with no categories | Hidden — there would be no fields to fill in |
| Favourites | Hidden — a new record has not been starred yet |
| Recycle bin | Hidden — a new record has not been deleted |

Where the button is hidden, **Ctrl+N** still says why, and what to do instead. A button
that answers a click by explaining why it cannot work is still a button that did
nothing, and for a keyboard user it is worse: it sits in the tab order making a promise
it never keeps. The same rule applies to **Open**, which is shown only when a record is
selected.

### Encryption

**Off by default**, in Settings → Encryption. Turning it on asks for a password and
converts the database in place using SQLCipher, which encrypts the whole file page by
page. From then on the password is needed every time the application starts.

Three things it is important to be clear about:

- **A forgotten password cannot be recovered**, by anyone, including this application.
  If it is lost, the records are gone.
- **Attached files are not encrypted.** Only the database is. Anything attached stays
  readable in the `attachments` folder.
- The password is **never stored anywhere** — only a flag saying the database is
  encrypted. And the flag is not trusted: what decides whether you are asked is whether
  the file actually opens without a key, so copying a database and a settings file
  apart cannot lock you out or quietly leave you unprotected.

A backup is taken before converting, the conversion is written to a new file and only
swapped in once complete, and the previous database is kept beside it as
`records.db.previous` until you delete it. The application closes afterwards, because
every open connection was made with the old key.

### Calculated fields

Give a number or money field a **formula** and the app fills it in for you. Formulas
name other fields in square brackets and take `+`, `-`, `*` (or `x`), `/` and
brackets:

```
[Quantity] * [Price per piece]
[Total amount] - [Paid amount]
([Price] - [Expense]) * 0.9
```

A calculated field is read-only, and recalculates as you type into the fields it
depends on. It is still a focusable text box rather than a label, so the keyboard
can land on it and the screen reader can read it back; it announces itself as
"worked out automatically, read only" along with the current value. The new total is
spoken once you leave the field that changed it, rather than on every keystroke.

If something it needs is still empty, the field stays **blank rather than showing
zero** — zero is a real amount and would be misleading. It says why instead: *"Price
per piece is empty, so this cannot be worked out yet."*

The recalculation is applied by the data layer on every save, not just by the editor,
so a total can never be stored out of step with its inputs.

You can add your own calculated fields, or change these formulas, in **Manage record
types → Edit field**. A misspelled field name, a formula that refers to itself, or a
syntax error is caught and explained as you type it, not left to fail silently.

## Branding

The **acvaysoft** mark is a geometric lower-case "a": a deep blue tile, the bowl and
stem in white, and a cyan bar beneath carrying the brand colour. The wordmark sets
"acvay" bold in deep blue and "soft" light in mid blue.

It is defined once, as vector geometry, in `src/RecordKeeper.App/Themes/Branding.xaml`.
Everything else is produced from that one definition — the window icon, the startup
screen, the application icon at ten sizes, the About box and the PNG stamped on every
PDF — so the printed logo cannot drift out of step with the one on screen. Being
vector, it also stays sharp at any magnification.

The `.exe` icon is `src/RecordKeeper.App/Branding/acvayrecords.ico`, rendered from
that same geometry at 16, 20, 24, 32, 40, 48, 64, 96, 128 and 256 pixels. One image
per size, because a 16-pixel icon scaled down from a 256-pixel one is a smudge.

**acvaysoft** is the maker; **acvayrecords** is this program. The application, the
startup screen, the About box and every report carry the line **Powered by Haseeb**.

Ready-made files are written to `dist/brand/`:

| File | For |
|---|---|
| `acvaysoft-mark-512…16.png` | the mark alone, at six sizes down to a 16-pixel icon |
| `acvaysoft-lockup-light.png` | mark and wordmark, for a light background |
| `acvaysoft-lockup-dark.png` | mark and wordmark, for a dark background |
| `acvaysoft-sample-report.pdf` | a real report, showing the letterhead |

**On every PDF**, each page carries the mark and the acvaysoft wordmark across the top,
with the report title under it and a cyan rule below that. The footer reads "Produced
by acvayrecords, powered by Haseeb" with the date and the page number. The header repeats on
every page on purpose: a page separated from page one should still say where it came
from. The plain-text export carries the same name as its first line.

**The startup screen** shows the mark, then the wordmark rising into place, then a cyan
line drawing itself across — about a second and a half in total. Three rules govern it,
all of them because the person using this may not be able to see it:

- **Any key or click dismisses it at once.**
- It is **skipped entirely when a screen reader is running.** Windows is asked directly,
  and Narrator sets that flag. A splash screen tells a screen-reader user nothing and
  delays everything.
- It shows **without motion** when Windows has animations turned off, and is off
  entirely if you clear the box in Settings → Starting up.

If it ever fails it is swallowed and logged. Decoration must never be the reason
somebody cannot reach their records.

## Look and feel

The default theme is a **white page with a Telenor-style blue palette**. Each colour
has a job, so colour reinforces the structure rather than just decorating it:

| | Colour | Used for | Contrast on white |
|---|---|---|---|
| near-black navy | `#10243A` | the data itself, body text | 15.7 : 1 |
| deep blue | `#00507A` | headings, column headers, selected rows | 8.7 : 1 |
| mid blue | `#00648F` | field labels, counts, help text | 6.5 : 1 |
| brand cyan | `#00A9E0` | the main button, marker bars | fill only |
| pale blue | `#EAF6FB` | panels and group headers | — |

The brand cyan is only ever a **fill**, never text, and never a boundary on its own.
It reaches just 2.7 : 1 against white, so text in it would be unreadable and a button
outlined in it would be hard to find. The main button therefore has a cyan fill, dark
navy text and a deep blue outline.

The focus outline is **orange** by default, not blue. It has to be a different hue from
everything around it, or a low-vision user cannot tell the control they are on from a
row that happens to be selected. Settings offers yellow, blue, green, magenta, white
and black if another colour suits your eyes better.

A black theme with white, yellow and light-blue text is also included, and Settings has
light, dark, high contrast, and "follow Windows". Changing the text size, theme or
focus outline there previews live, so you can judge it while you choose, and Cancel
puts it all back.

Other things that make it easier on a new user: the main action on each screen is a
filled button rather than one of a row of identical ones; an empty category shows a
panel explaining what to do instead of a blank box; and the selected row is marked by a
coloured bar as well as a fill, so it survives a colour-blind reader.

## Accessibility

This is the part the design is built around, not a layer added afterwards.

**One thing at a time.** The main area shows either the record list *or* the record
being edited, never both side by side. Side-by-side panes fall apart at 300% zoom and
force a screen-reader user to work out which pane they are in.

**List view reads as a sentence.** Each record is a single list item whose spoken name
packs in the useful fields: *"Electricity bill. Amount £132.00. Date 27 October 2026.
Category Utilities. Tagged 2026, utilities."* You can arrow down a list and hear what
each record is without opening any of them. Table view (Ctrl+Shift+V) is there for
comparing records by eye.

**Nothing is mouse-only.** Every command has a shortcut, every menu has an Alt
mnemonic, and F1 lists the lot. Ctrl+K opens a palette where you type a command name
instead of hunting through menus.

**F6 announces the region.** Tab tells you about individual controls; F6 tells you
which part of the window you are in, which is how you keep a mental map of it.

**The status bar is a live region.** Confirmations and errors are read out by Narrator
without focus moving, and nothing ever auto-dismisses — a message you missed is still
there to read with F6.

**Errors move the focus.** A validation failure jumps the keyboard to the offending
field and says why. Required fields and current errors are part of each control's
accessible name, because a red border conveys nothing to someone who cannot see it.

**Windows settings win.** High contrast mode is honoured by taking every colour from
the system palette rather than overriding it, and it overrides whatever theme is
otherwise set. Text scales from 11 to 48 point with Ctrl+Plus and Ctrl+Minus,
independently of the Windows setting, and the app is PerMonitorV2 DPI-aware so
Magnifier stays crisp.

**The focus outline is tunable.** Thickness 1–8 pixels and a choice of colours, because
a caret is invisible to someone looking at another part of the screen at 300%.

**Optional speech of its own.** Confirmations can be spoken in the app's own voice for
users who do not keep Narrator running, plus three distinct sounds for save, error and
delete. Both are off-by-default-friendly and switchable in Settings.

**No animations, no timeouts, no auto-dismissing toasts.**

## Keyboard shortcuts

F1 inside the app is the authoritative list — it is generated from the same command
registry that drives the menus, so it cannot drift out of date.

| | |
|---|---|
| Ctrl+N | New record |
| Enter | Open the selected record |
| Ctrl+S | Save |
| Escape | Back to the list / clear the search |
| Delete | Move to the recycle bin |
| Ctrl+R | Restore from the recycle bin |
| Shift+Delete | Delete permanently (recycle bin only) |
| Ctrl+D | Mark as favourite |
| Ctrl+F | Search |
| Ctrl+Shift+C | Clear the search |
| Ctrl+K | Find a command |
| F6 / Shift+F6 | Next / previous pane |
| Alt+1 … Alt+9 | Jump to a category |
| Ctrl+L | Show or hide the category list |
| Ctrl+Shift+V | Switch list / table view |
| Alt+O / Alt+H | Sort by / Show which dates |
| Alt+B | Which date a period uses |
| Ctrl+Plus / Minus / 0 | Bigger / smaller / reset text |
| Ctrl+Shift+W | Say where I am |
| Ctrl+E | Export list to PDF |
| Ctrl+T | Export to plain text |
| Ctrl+B | Back up now |
| Ctrl+Shift+A | Attach a file |
| Ctrl+Shift+N | New category |
| Ctrl+Shift+T | Categories and their fields |
| Ctrl+, | Settings |
| F5 | Refresh |
| F1 | Keyboard shortcuts |

## Safety

Deleting is two-stage: Delete moves records to a recycle bin they can be restored
from, and only Shift+Delete *inside* the bin destroys anything. Both ask first, and
the confirmation says what will happen in words.

A backup zip of the database and every attached file is written once a day on first
launch, keeping the last 14 by default. Ctrl+B makes one on demand.

Attached files are **copied** into the data folder, never linked, so moving or deleting
the original cannot break a record.

## Building from source

Needs the .NET 8 SDK.

```bash
dotnet build RecordKeeper.sln
```

```bash
dotnet publish src/RecordKeeper.App/RecordKeeper.App.csproj -c Release -o dist
```

| Project | What it holds |
|---|---|
| `src/RecordKeeper.Core` | models, SQLite data access, export, backup. No UI. |
| `src/RecordKeeper.App` | the WPF application |
| `src/RecordKeeper.Setup` | the installer. References nothing else here, on purpose |
| `tools/ThemeContrastCheck` | checks every theme colour pair for readability |
| `tools/build-release.cmd` | builds the application, then the installer around it |

The themes are the one part of an accessibility-led app that is easy to break by eye,
so the colour pairs are checked rather than eyeballed. Run it after touching a theme:

```bash
dotnet run --project tools/ThemeContrastCheck
```

It measures every foreground-on-background pair the UI actually puts together against
WCAG 2.1 — 4.5 : 1 for text, 3 : 1 for a boundary — and exits non-zero if one fails.
The high-contrast theme is skipped, because all of its colours come from the user's own
Windows palette.

Three dependencies: `Microsoft.Data.Sqlite`, `QuestPDF` (PDF output) and
`System.Speech` (the optional spoken confirmations).

## Known limitations

- **The PDF export is not a tagged PDF.** A screen reader will read it, but without
  heading structure. This is why plain-text export (Ctrl+T) is offered alongside it
  everywhere and is the better choice for reading back.
- **Attachments are not encrypted**, even when the database is. Encryption covers
  `records.db`; a receipt in the attachments folder stays a readable file.
- **No version history.** Editing a record overwrites the previous values. The recycle
  bin covers accidental deletion, and the daily backups cover the rest.
- **Backups cannot be restored from inside the app.** Ctrl+B makes one and Settings
  lists them, but using one means opening the zip and replacing the database by hand.
- **No import.** Records go out as CSV, text and PDF; nothing comes back in.
- **Formulas do arithmetic only** — the four operators and brackets. No conditions, no
  functions like sum or average, and no referring to other records.
- **Exports cover one record type at a time**, since the columns come from that type's
  fields.
- **The installer is not code-signed**, so Windows shows "Windows protected your PC"
  the first time it runs on a new machine. Choose *More info* then *Run anyway*.
  See **Signing it, so Windows stops warning about it** above for what it would take
  to remove that, including the free option for machines you hand it to yourself.
- **The data folder is still called `RecordKeeper`** inside Local AppData, from before
  the program was named acvayrecords. The one file there is what points at wherever
  the records really are, so renaming it would strand every existing installation.
