# Mycelia - Changelog

Each released version gets an entry here, newest first.  Entries are written for someone who has to
understand what changed without reading the diff.

## 0.2.0 - unreleased (in progress, 10 November 2026)

The Assistant, built in steps: the conversation and the connection to MCP servers, then the tools
that read and change the tree, then those tools for other programs, then research: web addresses
on sources and citations and downloading records come first, finding and reading archives next.

### New

- **A new name: Mycelia.**  The program was called Family Tree.  A mycelium is the underground
  network that links trees and feeds them, as the archives and MCP servers do for a family tree.
  Your settings, keys and conversations are carried over the first time Mycelia starts; trees and
  backups keep their `.ftree` and `.ftbackup` files.  If Claude Desktop or Claude Code was set up to
  use Family Tree, set it up again from Options › Other programs.
- **A new look.**  The Mycelia mark and icon: three people above the line, joined underground to
  their sources.  A dark forest-green top bar, the brand green on buttons, links, check boxes and
  sliders throughout (they no longer pick up the Windows accent colour), Inter for the interface
  and Space Grotesk for headings and the "mycelia" wordmark.  Home carries the tagline "Family
  trees, connected to their sources."  Printed charts use the same fonts and colours.
- **The Assistant.**  An Assistant button in the top bar opens a pane on the right of every
  workspace, which can be dragged wider or narrower.  Type a request and the AI model answers,
  using Mycelia's own tools and the tools of any MCP servers you have set up.  Replies are
  formatted (bold, lists, tables, links) and each can be copied for an email or document.  Every
  tool it uses is listed and can be opened to see what was sent and what came back.  Tools that
  change something ask first, with Allow, Always allow and Decline.  Stop ends a request at any
  point.
- **The Assistant works on your tree.**  It can search people, read anyone's facts, families,
  ancestors, descendants, relationships and timeline, look at places, sources and media, check
  for data errors and duplicates, and work out dates and ages.  It can add people (as someone's
  parent, child, spouse or sibling), add and change facts with their sources, link and unlink
  relatives, merge duplicates, tidy places, add sources and repositories, attach pictures, and
  save, export, merge or back up the tree.  It can go to a person, workspace or chart to show you
  what it means.  Every change asks first, says plainly what it will do, and is one step you can
  undo; when a request has changed the tree, a line at its end offers Revert, which puts everything
  back as it was before that request.
- **Claude Desktop, Claude Code and other programs can use Mycelia.**  Options › Other
  programs (off until you switch it on) lets programs on this computer that speak MCP use the same
  tools as the Assistant on the tree you have open.  Copy for Claude Desktop and Copy for Claude
  Code give you the setting to paste into each.  Their changes always ask you in Mycelia first,
  in the Assistant panel ("Claude Desktop asks: allow this?"), and living people are withheld from
  them as from the Assistant.  Nothing outside this computer can connect, and every request needs
  the token shown in Options.  The status bar says when a program is connected.
- **Living people stay private by default.**  Options › Assistant › "Living people sent to the AI
  model": names and relationships only (the default), full details, or not at all.  People and
  facts you've marked Private are withheld the same way unless you choose to include them.
- **One conversation per tree.**  The conversation about a tree is kept until you clear it (the
  bin button or `/clear`), encrypted for your Windows account, and comes back when the tree is
  opened again.  A tree that has never been saved keeps its conversation only while it is open.
- **Your choice of AI model.**  Options › AI model: Anthropic (Claude), OpenAI, or any service
  with the OpenAI interface, including Ollama or LM Studio on your own computer, which needs no key
  and keeps everything on the computer.  Test the model checks the settings.  Keys are kept
  encrypted.
- **MCP servers.**  Options › MCP servers adds servers that run on this computer or at a web
  address, or imports the ones you set up for Claude Desktop, Claude Code, Cursor or VS Code.  Test
  connection lists a server's tools, and each tool can be allowed, asked about or switched off.
  Options › Activity log shows what the servers said.  Servers that sign in through the browser
  are not supported yet; servers that take a key in a header are.
- **Panels fold away.**  Every part of the People, Places, Sources, Media and Charts workspaces,
  and the Assistant, is a panel with a fold button in its header.  A folded panel becomes a narrow
  strip with its name written downwards; click it to open it again.  The other panels take the
  room.  Which panels are folded, and the widths you drag them to, are remembered.
- **Everything lines up.**  The panels share one header height, the same corners, borders and
  12 px gaps, and the Assistant's top and bottom line up with the workspace's panels.
- **Nothing is cut off.**  When the window or the Assistant leaves too little room, panels first
  give up width down to their least, then fold from the left.  Before, the Person panel could be
  cut off on the right.  In a narrow window the top bar shows icons only on the Assistant and Save
  buttons, and then on the workspace tabs, instead of overlapping.
- **An installer and software updates.**  Mycelia now has a setup program that installs it for
  you alone, with no administrator rights, and its own icon.  It checks for a newer version when it
  starts (Options › Updates can make that manual, or include beta releases).  When one is found, an
  "Update available" button appears in the top bar; Download and Install fetches it, checks it is
  the genuine release signed by the publisher, asks to save your tree, then updates and starts
  again.  Tools > Check for updates... checks at any time.
- **Web addresses on sources and citations.**  A source has a web address, with an Open button in
  the Sources workspace.  A citation can hold the record's own web address, the date you looked at
  it (Today fills it in) and an extract or transcription of the record.  They show under each fact's
  sources as a link you can click; click a citation to change it.  GEDCOM files carry them, and
  web addresses written by other programs are read.  Find and replace searches extracts.
- **The Assistant can download records.**  Asked to, it saves a register page, certificate or
  census image from a web address into a folder named after the tree, beside the tree file
  ("Ashworth media"), and adds it as a media item linked to the people it is about.  It asks
  first, never overwrites a file, and tells you when the address gave a sign-in page instead of the
  record.  The tree must have been saved once.
- **Gather media into one folder.**  Gather media... (at the foot of the Media list, and in Main
  menu > Import and export) copies every file the tree links to into one folder, the tree's media
  folder unless you choose another, and points the tree at the copies.  It is for a tree whose
  pictures are spread across several folders, such as one imported from GEDCOM, so they can be kept
  and backed up in one place.  The originals are not moved or deleted, nothing in the folder is
  overwritten (a second "census.jpg" becomes "census (2).jpg"), files that can't be found are
  listed and left as they were, and Undo points the tree back at the originals.
- **The Assistant knows where to look.**  A list of over a hundred genealogy websites and
  archives worldwide is built in.  The Assistant uses it to suggest where to search for a country
  and a kind of record, with each site's cost and whether it can be searched automatically.
- **The Assistant can search open archives.**  WikiTree, Wikidata, Open Archives (Netherlands and
  Belgium), The National Archives' Discovery catalogue, Trove, DigitalNZ with Papers Past,
  Europeana, GeoNames and the Internet Archive can be searched without asking you each time.
  Before reading any other web address it asks, showing the address.
- **MCP servers that sign in through the browser.**  Options › MCP servers › Browser sign-in:
  press Sign in, approve Mycelia on the service's own page, and Mycelia keeps the sign-in
  (encrypted) and renews it before it runs out.  Services that let apps register themselves need
  nothing more; for others, enter your own app's client ID and secret.
- **The MCP Helper.**  Options › MCP servers › MCP Helper... sets up a server for you: pick Open
  Archives (the Netherlands and Belgium) or Riksarkivet (Sweden), run by the archives themselves,
  or name any server, give its address or paste its configuration.  It checks how the server
  signs in, asks for what it needs and fills in the settings; with an AI model set up, it also
  explains the steps.  Other genealogy servers are listed with the reason they aren't offered,
  FamilySearch among them.
- **The Assistant researches carefully.**  It looks up where to search first, searches records
  only for people who have died, cites every record with its web address, the date and an
  extract, and never invents a record.  It doesn't offer to search sites it can't reach, such as
  subscription sites, gives you the search steps for sites that need a form filled in, and calls
  people, sources and places by name rather than by their ids.  It keeps questions and
  suggestions for you in its reply, out of the tree's notes.
- **The Assistant shows whom you're asking about.**  A line above the message box names the
  selected person ("About: Josiah Ashworth, 1798-1871"), or the whole tree when nobody is selected, and
  follows the selection.
- **A fact's own note can be read and edited.**  The fact form, in the Person panel and the Person
  view, has a Note box under Description.  Until now a fact's note, from a GEDCOM file or written
  by the Assistant, showed only as "1" in the Person view's Notes column.
- **Moving up and down the pedigree.**  The arrow on a card in the last column is now a button:
  one click moves the tree up to that person, so you can walk back through the generations.  A
  new button on the left of the first person's card moves the tree down to a child, choosing from
  a list when there are several.  Double-clicking a person on the tree now centres the tree on
  them and stays in the Tree view; until now it opened their Person view, so every step up meant
  switching back to Tree.  The Person view opens with F3, the right-click menu, the Person switch
  or a double-click in the index.
- **A right-click menu for people.**  Right-click a person on the tree, in the family strip or in
  the index (or press Shift+F10 or the Menu key there) to open their Person view, centre the tree
  on them, make them the home person, bookmark them, add or link a relative, set the spouse order,
  merge or delete them.  Until now right-clicking did nothing, and the home person could only be
  changed with the star in the Person panel's header.  Main menu > Person has Make home person
  too.
- **Screen readers can reach the Person panel's tabs.**  The contents of the Facts, Family, Notes
  and Media tabs, and of the Options window's tabs, were hidden from screen readers and other
  accessibility tools.
- **"Always allow this site".**  When the Assistant asks to read a web address, you can allow that
  site for good; other sites still ask.  Options › Research lists the sites you've allowed, each
  with Remove.
- **Options › Research.**  Trove and Europeana need a free key, and GeoNames a free user name.
  Enter them here; they are kept encrypted, and Mycelia adds them to those archives' requests
  itself, so the AI model never sees them.
- **Mycelia is open source.**  The code is on GitHub at paulfilkin/mycelia under the GNU General
  Public License v3.0, with a README, issue forms for bugs and ideas, and private reporting for
  security problems.  The name and artwork stay reserved.
- **Help.**  F1 opens the help page for the screen you're on: the workspace, the Person view, the
  Assistant, each tab of the Options window, or the MCP Helper.  The main menu has a Help submenu
  with all the help, the keyboard shortcuts, and a way to report a problem or suggest an idea.  The
  pages are on GitHub, so they can be read once the repository is public.

### Internal

- Releases (TDD 20): each version is published on GitHub's Releases page as well as through the
  updater, with the same signed installer, so testers who are not developers can download it.
  The README has a Download section, and the help's Getting started says where to download and
  what to do about the Windows SmartScreen warning.
- Help (TDD 28): the pages in `Docs/Help/`, their pictures made from the sample tree by
  `tools/help-shots.ps1`, and `Ui/Help.cs` with the pages' address, F1 and the Help submenu.
- The public repository (TDD 27): `LICENSE`, `TRADEMARKS.md`, `README.md`, `CONTRIBUTING.md`,
  `SECURITY.md`, `CODE_OF_CONDUCT.md`, `.github/` (issue forms, pull request template, build
  check); `Docs/Design/Design_Considerations.md`, with how Mycelia feels familiar, how it
  differs and the conventions it follows, cited by the code; a README banner from the splash
  artwork; local paths and a real-person example removed.
- `Ai/Mcp/McpRegistry.cs`, `McpSetup.cs` and `Ui/McpHelperWindow.cs`, ported from
  multifariousStudio; `Ai/Mcp/GenealogyServers.cs`, the Helper's genealogy list; `ChatText` gains a
  settable `Markdown`.  `--options "MCP Helper"` photographs the Helper.  The research rules are in
  the system prompt.  TDD 22.9 and 25.5.
- `Ai/Mcp/McpOAuth.cs` and `McpOAuthSettings` (on `McpServerConfig.OAuth`), ported from
  multifariousStudio: MCP browser sign-in with PKCE, automatic registration and token renewal; the
  web transport sends and renews the token; `MainWindow` saves renewed tokens;
  `OptionsDialog.SignIn.cs`.  The callback is `http://127.0.0.1:47822/callback`.
- Renamed: `Mycelia.exe`, the `Mycelia.*` namespaces, `Mycelia.csproj`, `Mycelia.sln`, `Mycelia.iss`
  (`MyceliaSetup-<version>.exe`), the update slug `mycelia`, the MCP server name `mycelia` and
  `MYCELIA_MCP_TOKEN`, `SOUR MYCELIA` in GEDCOM, and the settings folders, copied once from the old
  ones by `AppSettings.Load`.  The project folder stays `familyTree/`.  TDD section 26.
- The artwork: `Ui/Theme.xaml` holds the palette, the Fluent control brush overrides and the mark
  and icon drawings made from `Docs/Design/Artwork/`; `tools/make-icon.ps1` builds the icon from the
  small form up to 40 px and the full one from 48; `Resources/Fonts/` holds the two variable fonts
  (resources) and their OFL licences (copied to the output).  TDD section 26.1.
- The MCP client, model services, tool limit handling and conversation store are ported from
  multifariousStudio into `familyTree/Ai/`, without third-party packages.
- `TDD_Mycelia_v0_2_0.md` adds section 22.  `AppSettings` gains `Assistant`, `McpServers` and
  `ReadOnly` (screenshot mode now never writes settings).
- `--assistant` and `--assistant-demo` open the pane at start-up, the second with a sample
  exchange for window pictures.  With `--screenshot`, `--size WxH` sets the window's size and
  `--fold` folds named panels.
- `Ai/Server/` holds the MCP server (Streamable HTTP on the loopback address, bearer token, Host
  and Origin checks) and the `--mcp-relay` bridge; `OptionsDialog.Serving.cs` the Other programs
  tab.  The Assistant never connects to Mycelia itself.  `--options "<tab>"` (with
  `--screenshot`) photographs the Options window.
- `Updates/` holds the update client, flow and offer, ported from multifariousStudio; the product is
  `mycelia` on the multifariousUpdates plugin, signed with key `ft-2026-1`.  `Mycelia.iss`
  builds `Releases\MyceliaSetup-<version>.exe`; `tools/make-icon.ps1` makes the icon from the logo
  drawing.  `--noautoupdate` skips the start-up check.
- `Ui/FoldPanel.cs` holds `FoldPanel` and `FoldLayout`; `AppSettings` gains `FoldedPanels` and
  `PanelWidths`, and the Assistant's settings `PaneFolded`.
- `Citation` gains `Url`, `Accessed` and `Text`, `Source` gains `Url` (empty by default, so the
  file format stays version 1).  GEDCOM writes `WWW`, `_ACCESSED` and `DATA` `TEXT`, and reads
  `WWW`, `_LINK` and `_URL`.  `TreeDocument.MediaFolder`; `Ai/Assistant/Downloads.cs`, ported from
  multifariousStudio; `download_file` (60 tools), served to programs with `openWorldHint`.  TDD
  section 25.
- `Ai/Research/` holds the listed archive APIs with their notes and keys, `fetch_url`'s requests
  and the reader of the resources list, which the csproj embeds from
  `Docs/Resources/genealogy-resources.md`.  `AssistantTools.Research.cs` adds `suggest_resources`,
  `api_notes` and `fetch_url` (63 tools).  `AssistantTool` gains `DefaultPolicy`, `AllowWithoutAsking`
  (a call that may run without asking under Ask) and `ReachesWeb` (`openWorldHint`).
  `AssistantSettings.ResearchKeys`; `OptionsDialog.Research.cs`.

## 0.1.0 - unreleased (first build, 10 November 2026)

The first build.  Nothing is released yet.

### New

- **A tree file of its own.**  Trees are saved as `.ftree`, a readable JSON file holding people,
  families, facts, places, sources, repositories, media links and the history of people viewed.
  Save writes a temporary file and swaps it in.
- **People workspace.**  An index sorted by surname, given name, birth, marriage or death date with
  a search box and a History list of the people looked at; a tree panel
  with pedigree (four generations, the spouse under the focus person), family and descendants
  views, zoomable and fitted to the panel; a family strip under the pedigree with the couple, their
  parents, the marriage and a children table; and a person panel with facts, family, notes and
  media tabs, including live data-error warnings as you type.
- **Facts** with 66 fact types, dates typed freely ("12 Mar 1890", "Abt. 1850",
  "Bet. 1850 and 1855", "1720/21", "44 BC") and shown in the chosen style, places, descriptions,
  preferred and private marks, and source citations.
- **Places, Sources and Media workspaces** with searchable lists and editors: events at a place,
  combine places, show on a map in the browser; sources with repositories and the facts that cite
  them; media with previews, portraits and the people in each item.
- **Charts:** pedigree, descendant, fan and family group sheet, drawn as vectors, zoomable, saved
  as a 300 dpi PNG or printed to fit the page.
- **Tools:** a relationship calculator with the path between two people (including half, step
  and in-law relations), a date calculator with three modes, a Soundex lookup, and Find data
  errors.
- **GEDCOM 5.5.1** import and export, including custom fact tags, child relationship natures,
  sources, repositories, media and notes.
- **Home page** with the tree's statistics, the home person, surname counts, recent trees and the
  date preferences.
- **Undo and redo** (60 steps) for every change, with typing grouped per field.
- **Hi-DPI** per-monitor rendering, own icons and logo, Fluent light theme in brand green.
- **A sample tree** (the invented Ashworth family) to explore the program with.

### Added later on 10 November 2026, still unreleased

- **Person view.**  Double-clicking anyone on the tree, in the index or in the family strip opens
  a Person view: relatives across the top, every fact in
  one table grouped as personal information, individual facts and facts shared with each spouse,
  the selected fact's form beside it, notes below, and a Tree/Person switch above the workspace.
  The table lists facts by date with undated last and ties by a fixed type
  order, couples by marriage date, sortable columns, and "Preferred" only on the preferred fact
  of each type.  The person panel lists facts in the same order.
- **Merge two people.**  The two records side by side, one kept; facts, sources, relatives, notes
  and media move across as one undo step.  From the main menu, the person panel or a duplicate pair.
- **Find duplicate people** in Tools: likely pairs from sound-alike surnames, matching given names
  (including old abbreviations such as Thos and Wm) and births within two years, each with a
  Compare and merge button.
- **Options window** from the main menu and the Home page: start-up workspace, reopen the last
  tree, date display format, date input order, surnames in capitals, and the unlikely-date alerts.
  The Home page's preferences became a summary card with an Options button.
- **Main menu** gains Find duplicate people, Merge two people, Delete and Options.
- **Charts** start at 4 generations for every chart.
- **Merge another tree into this one** from a `.ftree` or GEDCOM file, as one undo step.  Places
  and sources with the same names are shared; afterwards the program offers to look for people
  who are now in the tree twice.
- **Back up with media and restore.**  A backup is one `.ftbackup` file holding the tree and a
  copy of every linked picture or document; restoring unpacks it into a chosen folder with the
  media beside the tree, so a tree can move to another computer with its pictures.
- **Export options.**  Export the whole tree or a branch (ancestors, descendants or both of one
  person, with or without spouses).  Living people can be privatised (named "Living", with no
  dates or places).  Living means no death recorded, born within 120 years and married within 100.  People and facts marked
  Private are now left out unless you tick them in, as the fact editor always said.
- **Find and replace** (Ctrl+F) across names, notes, fact descriptions, citations, places,
  sources and media captions, with every match listed and untickable before replacing.
- **Sort all children by birth** and **Convert names** (to "First Middle Surname" or "First
  Middle SURNAME", keeping McDonald, MacKenzie and O'Brien) in the menu.
- **Set spouse order** for people with more than one spouse; the first is the preferred spouse
  and is shown first everywhere.
- **Your own fact types** (Manage fact types), kept in the tree and written to GEDCOM as named
  events.  A GEDCOM file with event types this program does not know now creates them rather than
  losing the name.
- **Relationships**: every couple in one list with type, status, marriage and children, to find
  and go to.
- **Timeline** in the Person view: the person's life in date order with births and deaths of
  close family and historical events, each with the age at the time.  **Historical events** can
  be edited, and a starter list (mostly Britain and Ireland, 1801 to 1969) added in one click; the
  sample tree has it already.
- **Navigation:** Back and Forward arrows in the top bar (Alt+Left, Alt+Right), bookmarks
  (Ctrl+Shift+B) with a Bookmarks list in the index, Ctrl+Home for the home person, Ctrl+1 to
  Ctrl+7 for the workspaces, F3 for the Person view and Ctrl+D for Tools.
- **The main menu** is grouped into Import and export, Edit, Person and Tools.

### Internal

- .NET 10 WPF, no third-party packages.
- `--screenshot <png>` renders the window for automated checks; `--workspace <name>` opens a tab.
