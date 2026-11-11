# Mycelia - project instructions

Mycelia is a Windows desktop application for recording a family tree: people, families, facts,
places, sources, media and charts, with GEDCOM 5.5.1 import and export, and an Assistant that
connects the tree to archives and their records through MCP ("Mycelia Connects Pedigrees").  It
was called Family Tree until 11 November 2026, and its project folder `familyTree/` keeps the old
name.  It will feel familiar to people used to Family Tree Maker and other desktop genealogy
programs; the conventions it follows (date formats, relationship wording, data-check limits,
index sorting, history) and how it differs are in `Docs/Design/Design_Considerations.md`.  Its
code, data model, file format and graphics are its own.

Read `Docs/Design/TDD_Mycelia_v0_3_0.md` before changing anything structural.  It is the
design record: the data model, the file format, undo, GEDCOM, kinship, the data checks and every
workspace.  A `CLAUDE.local.md` beside this file, where there is one, adds instructions for that
computer.

## Repository layout

```
/
├─ Mycelia.sln
├─ familyTree/                    the application (.NET 10, WPF)
│  ├─ Model/                      Entities, FactTypes, TreeDocument, GenDate, Gedcom, Kinship,
│  │                              DataChecker (and Soundex), IndexSort, FactOrder, PersonMerge,
│  │                              TreeImport, TreeTools, Living, Timeline, SampleTree
│  ├─ Ai/                         the Assistant: SecretStore; Mcp/ (client, hub, server config,
│  │                              browser sign-in, registry, setup plans, the genealogy list);
│  │                              Assistant/ (session, chat items, providers, tool budget,
│  │                              conversation store, settings, downloads); Research/ (the
│  │                              listed archive APIs, fetch_url's requests, the resources list);
│  │                              Tools/AssistantTools (five partial files: core and privacy,
│  │                              Read, Change, Files, Research);
│  │                              Server/ (McpServer for other programs, McpRelay)
│  ├─ Updates/                    UpdateClient, UpdateService (UpdateConfig: endpoint, slug,
│  │                              public keys), UpdateWindow
│  ├─ Resources/Mycelia.ico       the logo as an icon (tools/make-icon.ps1)
│  ├─ Mycelia.iss                 Inno Setup installer script
│  ├─ Ui/                         AppState and AppSettings, UiKit, Dialogs and ManageDialogs
│  │                              (one partial class), PersonActions, PersonMenu, OptionsDialog (with
│  │                              OptionsDialog.Ai, .SignIn, .Research, .Serving, .Updates),
│  │                              McpHelperWindow, AssistantPane, ChatText, FoldPanel
│  │                              (with FoldLayout), Converters,
│  │                              ScreenshotRunner, Theme.xaml
│  ├─ Ui/Views/                   Home, People (XAML) with PersonEditor and PersonView (sharing
│  │                              FactForm), Places, Sources, Media (MasterDetail), Charts, Tools
│  ├─ Ui/Charts/ChartCanvas.cs    vector drawing of the four printable charts
│  ├─ App.xaml / MainWindow.xaml  start-up, top bar, workspaces, status bar
│  ├─ app.manifest                per-monitor V2 DPI, long paths, Windows 10/11
│  └─ Mycelia.csproj              version, Fluent theme, manifest
├─ Docs/                          documentation (see the map below); Images/ for the README;
│                                 Help/ the user help, with its pictures in Help/images/
├─ tools/make-icon.ps1            makes the icon from the logo drawing (Windows PowerShell 5.1)
├─ tools/help-shots.ps1           makes the help pictures from the sample tree (TDD 28)
├─ .github/                       issue forms, pull request template, the build check
├─ README.md, LICENSE (GPL-3.0), TRADEMARKS.md, CONTRIBUTING.md, SECURITY.md, CODE_OF_CONDUCT.md
└─ Releases/                      installers built from Mycelia.iss (untracked)
```

The repository is on GitHub at paulfilkin/mycelia, branch main.

## Build

.NET 10 (`net10.0-windows`), WPF with the Fluent theme, AnyCPU.  No third-party NuGet packages.
From the repository root:

```
dotnet build Mycelia.sln -c Debug -v minimal --nologo
```

The build is clean: 0 warnings, 0 errors.  Keep it that way.  The Fluent theme's experimental
warning (WPF0001) is suppressed in the csproj.

To run it: `dotnet run --project familyTree/Mycelia.csproj`.  Command-line arguments: a file
path to open (`.ftree` or `.ged`), `--workspace <Home|People|Person|Places|Sources|Media|Charts|Tools>`
to start on that tab (`Person` is the People workspace's Person view), and `--screenshot <png>` to open the sample tree behind other windows,
draw 120 frames, save the window to the PNG and exit (settings are neither read nor written in
this mode).  `--assistant` opens the Assistant pane; `--assistant-demo` opens it with a sample
exchange that sends nothing.  With `--screenshot`, `--size 1080x680` sets the window's size and
`--fold People.Index,Assistant` folds panels by their settings key, and `--options "Other programs"`
photographs the Options window at that tab.  `--mcp-relay` opens no window: it is the stdio bridge
Claude Desktop starts (TDD 24).  Without a path it opens the most recent tree, or the sample tree.

There is no test project.  Verification means running the application, and for GEDCOM, importing
a file, exporting it again and comparing.

The version is the `<Version>` line in `familyTree/Mycelia.csproj`, currently 0.3.0 (0.2.0 was
the first release; 0.1.0 was never released); it is also on four lines of
`familyTree/Mycelia.iss`.  The installer is built with:

```
dotnet publish familyTree/Mycelia.csproj -c Release -r win-x64 --self-contained true
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" familyTree\Mycelia.iss
```

Versions are released with multifariousRelease, the publisher's release program, which bumps the
version lines, commits, builds, signs, publishes to the WordPress plugin and GitHub, and tags.
Before it runs, the changelog needs a `## <version> - unreleased` section and every other change
must be committed (TDD section 20); how the program updates itself is section 23.

## Documentation map

| Document | What it is authoritative for |
| --- | --- |
| `Docs/Design/TDD_Mycelia_v0_3_0.md` | The design record: model, file format, undo, dates, GEDCOM, kinship, data checks, every workspace, the Assistant and MCP client, key decisions, what is not built |
| `Docs/Design/Design_Considerations.md` | What Mycelia is for, where it feels familiar and where it differs, and the conventions it follows (dates, names, relationship wording, data checks, sorting, shortcuts), by the section names the code cites |
| `Docs/Help/` | The user help: one page per subject, read on GitHub and opened by F1 (TDD 28) |
| `Docs/CHANGELOG_Mycelia.md` | What changed in each version, user-visible and internal |
| `README.md` and the community files | What the public sees first: what Mycelia is, building, contributing, security, licence |
| `Docs/Resources/` | Reference material: `genealogy-resources.md`, a worldwide list of research sites with cost, API and MCP availability (compiled 10 November 2026) |

The TDD file name carries the version it describes.  When the version moves, copy the TDD to the
new version's name, edit it, and update the pointers in this file.

## Working rules for this project

- Agree a plan before writing code, and do not deviate from it once agreed.
- Documentation is updated with the code, not after it.  A change that alters the file format, a
  model, a convention or a user-visible behaviour updates the TDD (and for a convention,
  `Design_Considerations.md`) in the same slice.
- A change people can see updates its help page in `Docs/Help/` in the same slice, and its
  pictures with `tools/help-shots.ps1` when the screen looks different.  Keep the section headings
  F1 links to (`Ui/Help.cs`, TDD 28), or change the link with the heading.
- Every released version gets a changelog entry, written for someone who has to understand what
  changed without reading the diff.
- Never commit or push without being asked.  Propose the commit message and wait for approval.
- British English, no em-dashes.  Prose is double-spaced between sentences; code, code comments,
  commit messages and UI strings are single-spaced.
- No third-party NuGet packages.  Raise it first if one looks necessary.
- Own graphics only: icons are the line geometries in `Ui/Theme.xaml` and the logo is a drawing
  there.  No artwork, logos or brand names from other programs.  The Mycelia mark, icon, palette
  and type are the person's own artwork in `Docs/Design/Artwork/` (SVGs and a sheet); drawings in
  the theme are made from those SVGs.
- Mycelia follows familiar conventions; it is never described as a copy or rebuild of another
  program, and nothing names another program's internal parts.
- The sample tree (`Model/SampleTree.cs`) is invented.  Keep it that way: no real people, in the
  sample, in examples or in test data.

## Things that will catch you out

- **Undo replaces every object.**  `TreeDocument` keeps undo as whole-tree JSON snapshots (60
  deep) and `Restore` deserialises one, so after any change or undo every `Person`, `Family` and
  `Fact` instance is new.  Never hold a model reference across a change; find it again by id
  (`AppState.OnStructureChanged` does this for the selection and focus).
- **Two ways to change the tree.**  Anything that is not typing goes through
  `TreeDocument.Change(action, structural)` as one undo step.  Typing into a field uses
  `BeginEdit(token)`, `MarkEdited()` and `EndEdit()` (see `UiKit.Field`): the first keystroke after
  the box gains focus starts an undo step and later keystrokes join it.  `structural: false` skips
  `StructureChanged`, so the open editor keeps its state; use it for edits that change no links.
- **Dates are text.**  A fact's `Date` is kept exactly as typed; `GenDate.Parse` reads it (cached)
  and `Display` formats it in the preference style.  `GenDate.Style` and `GenDate.DayFirst` are
  static and set from `AppSettings` at start-up.  `DataChecker` compares dates only to the
  precision both share (Design considerations, Comparing two dates).
- **Preferred facts.**  `Person.Birth`, `Death` and `Family.Marriage` return the preferred fact of
  that type, or the first.  `AddFact` marks a fact preferred when it is the first of its type.
- **Partner 1 is the man where known** (`AddFamily` swaps the pair), matching GEDCOM `HUSB`.
  `Father` and `Mother` pick by sex first and fall back to the slot.
- **Theme resources are looked up by key at run time** (`UiKit.Res`, `UiKit.Brush`,
  `UiKit.Icon("Name")` for `I.Name`).  A missing key throws when the screen is built, not at
  compile time.
- **The Fluent theme ignores the accent keys for its controls.**  It copies the Windows accent into
  each control's own brushes when it loads, so `Ui/Theme.xaml` overrides those brushes by name
  (`CheckBoxCheckBackgroundFillChecked`, `SliderThumbBackground`, `TextControlBorderBrushFocused`
  and the rest, TDD 16).  A control that turns blue needs its brush added there.
- **Fonts are Inter and Space Grotesk, built in** (`Resources/Fonts`, `UiFont` and `DisplayFont`).
  WPF renders only a variable font's named weights; Space Grotesk has no SemiBold, so headings use
  Bold.  Setting `FontWeight="SemiBold"` on `DisplayFont` text gives a synthesised, widened bold.
- **The icon is two drawings.**  After changing `IconImage` or `IconSmallImage`, run
  `tools/make-icon.ps1` again (TDD 20).
- **Most screens are built in code.**  Only `MainWindow.xaml` and `Ui/Views/PeopleView.xaml` are
  XAML; the other views, the person editor, the dialogs and the charts are C#.  XAML pages are
  picked up by the SDK defaults; the csproj lists none.
- **The conventions are cited.**  `DataChecker` limits, `IndexSort.HistoryLimit`, the kinship
  wording and the rest each name their section of `Design_Considerations.md`.  Change one only with
  a reason, and update that document with it.
- **GEDCOM export has options.**  `Gedcom.Export(doc, path)` with no options writes everything;
  the File menu always passes `ExportOptions` from its window, where Private people and facts are
  left out unless ticked, living people can be privatised (`Living.IsLiving`, Design
  considerations, Living people) and a branch can be chosen (TDD section 11).
- **Custom fact types have ids from 1000** and live in the tree (`TreeData.CustomFactTypes`).
  `FactTypes.SetCustom` is called from `RebuildIndex`, so `FactTypes.Get`, `PersonTypes` and
  `CoupleTypes` change with the open tree.
- **Spouse order** is `Person.SpouseOrder`; `TreeDocument.SpouseFamilies` follows it.  The person
  editor and Person view fall back to marriage-date order (`FactOrder.ByMarriage`) when it is empty.
- **Bookmarks are not undo steps.**  `AppState.ToggleBookmark` marks the tree edited and raises
  `BookmarksChanged`, and `TreeDocument.Restore` keeps the current bookmark list over the
  snapshot's, so Undo and Redo neither lose nor bring back a bookmark.  Back and Forward live only in `AppState` and are cleared by `SetDocument`.
- **Media are absolute file paths.**  Nothing is copied into a tree folder; a tree opened on
  another computer shows "File not found" until "Find file..." relinks each item.  Only
  `download_file` writes into the tree's media folder (`TreeDocument.MediaFolder`, `<tree name>
  media` beside the tree file, TDD 25), and never deletes or overwrites there.
- **The old name is still in some places on purpose.**  The project folder `familyTree/` and the
  `.ftree` and `.ftbackup` extensions come from the program's first name, Family Tree.  Leave them:
  renaming the folder breaks the project paths, and new extensions would stop existing trees and
  backups from opening.
- **Settings live in `%AppData%\Mycelia\settings.json`:** recent files (8), date style, day
  first, surnames in capitals, start-up workspace, reopen last tree, data warnings.  They are
  changed in the Options window (`OptionsDialog`), whose `ApplyToApp` pushes them into statics
  (`GenDate.Style`, `GenDate.DayFirst`, `Person.SurnamesInCapitals`, `PersonEditor.ShowWarnings`).
  A missing or unreadable file means defaults, and a failed save is ignored.  The Assistant's
  settings and MCP servers are in the same file (`Assistant`, `McpServers`); API keys, header and
  environment values are DPAPI-encrypted (`Ai/SecretStore`).  In screenshot mode
  `AppSettings.ReadOnly` makes `Save` do nothing.
- **The Assistant runs on the UI thread.**  `AssistantSession.SendAsync` is started from the pane
  and its awaits resume there, so tools may read and change the tree directly; never run a tool
  with `ConfigureAwait(false)` or from a background thread.  A tool must find people by id on
  each call (undo replaces every object).
- **A new Assistant tool:** read tools use `Read(...)`, change tools `Write(...)` with a `Describe`
  sentence for the approval card.  Look everything up and check it before calling `Change(...)`, so
  a refused call leaves no empty undo step and the request's Revert count stays right.  Every person
  in a result goes through `Ref`, `Vitals` or a `Withheld` check, so living and Private people are
  never given to the model beyond what Options › Assistant allows (TDD 22.4).
- **Browser sign-in tokens live in working copies too.**  The hub and the Options window hold
  clones of each `McpServerConfig`; a renewed token raises `McpOAuth.TokensChanged`, and
  `MainWindow` copies it into the saved server (by id and address) and saves.  Never save a clone
  over the settings without `TakeTokensFrom` the saved one (Options' `KeepSavedTokens`), or a
  renewed token is lost (TDD 22.5).
- **Other programs call the same tools** through `Ai/Server/McpServer` (off by default, TDD 24).
  Its calls arrive on socket threads and are run on the UI thread by `MainWindow`'s
  `IMcpServerHost.RunAsync`; never touch the tree from the server's own threads.  A new tool is
  served automatically; add it to `McpServer.Destructive` if it deletes or overwrites, and set
  `ReachesWeb` on the tool if it sends requests to the web.
- **The MCP Helper's genealogy list offers only archives' own servers** (`Ai/Mcp/GenealogyServers.cs`,
  TDD 22.9).  A third-party server goes in as not offered, with the reason; offering one is a
  decision for the person, and its package must name an exact version.  Check each entry live
  before adding it, and never run a third-party server program to check it without asking.
- **`fetch_url` runs unasked only for the listed archive APIs** (`Ai/Research/ResearchApis.cs`,
  TDD 25.4) and the sites the person chose "Always allow this site" for
  (`AssistantSettings.AllowedSites`).  Adding a host to the list lets the model send whatever it
  likes to that host without the person seeing it: add only an archive's own API host, checked live,
  with a note.  Its "always" must stay per site (`AllowAlwaysCall`), never the whole tool.  Keys are
  added by `WebFetch` and redacted from everything returned; never put a key in a note or a result.
- **The resources list is embedded** from `Docs/Resources/genealogy-resources.md` (the csproj links
  it).  `ResourceList` reads its tables by column position, so keep the eight columns in order.
- **Conversations are per tree file**, DPAPI-encrypted in `%LocalAppData%\Mycelia\Assistant\`
  under a hash of the tree's path (TDD 22.6).  `MainWindow` calls `OpenTree` on `DocumentReplaced`
  and `TreeSaved` on every `EditStateChanged`; a tree with no file keeps its conversation in memory.
- **The Assistant's code is ported from multifariousStudio** (`Core/Mcp`, `Core/Assistant`,
  `Controls/ChatText`).  Fixes there may apply here and the other way round; TDD section 22 says
  what was left out.
- **Side-by-side parts are `FoldPanel`s in a `FoldLayout`** (TDD 16.1): People's Tree view
  (XAML), `MasterDetail` and `ChartsView` (code).  A new workspace with panels should use them, so
  it folds, lines up and never cuts a panel off.  Panel controls go in `HeaderTools`; the header is
  48 px for every panel.  The Assistant is a `FoldPanel` in `MainWindow`'s own column, aligned to
  the visible layout's top by `AlignAssistant`.
- **Updates are signed, and the slug is fixed.**  `UpdateConfig.ProductSlug` (`mycelia`) is part
  of the signed message, so it must match the plugin's product exactly.  The private key is kept
  on the publisher's computer: never read any private key into the conversation, commit it or
  upload it.  Only the public key (`ft-2026-1`) is in the code; a new
  key is added beside it, never in its place, before the plugin switches (TDD 23).
- **New files carry no byte-order mark**, as the rest of the repository; PowerShell's
  `WriteAllText` with a UTF-8 encoding adds one.
- **Merging is this app's own design.**  `PersonMerge` rules are recorded in TDD section 7.1.
- **The History list is saved with the tree** (`TreeData.History`, 400 ids) but viewing a person
  does not mark the tree dirty, so it is only written with the next save.
- **`familyTree/shots/`** holds window pictures made with screenshot mode for checks; the csproj
  excludes it from the build and `.gitignore` from the repository.
