using System.Text.Json;
using Mycelia.Model;

namespace Mycelia.Ui;

/// <summary>What every workspace shares: the open tree, the selected person and the person the tree is centred on.</summary>
public sealed class AppState
{
    private TreeDocument _doc;

    public AppState(TreeDocument doc)
    {
        _doc = doc;
        Hook(doc);
        Focus = doc.HomePerson;
        Selected = Focus;
    }

    public TreeDocument Doc => _doc;
    public Person? Selected { get; private set; }
    public Person? Focus { get; private set; }

    public event Action? DocumentReplaced;
    public event Action? SelectionChanged;
    public event Action? TreeChanged;
    public event Action? EditStateChanged;
    public event Action<string>? NavigateRequested;

    public void SetDocument(TreeDocument doc)
    {
        Unhook(_doc);
        _doc = doc;
        Hook(doc);
        Focus = doc.HomePerson;
        Selected = Focus;
        _back.Clear();
        _forward.Clear();
        DocumentReplaced?.Invoke();
        NavigationChanged?.Invoke();
        SelectionChanged?.Invoke();
        EditStateChanged?.Invoke();
    }

    // ---- Back and Forward (View > Back, Alt+Left; Forward, Alt+Right): the people selected, by id ----

    private readonly List<string> _back = [];
    private readonly List<string> _forward = [];
    private bool _navigating;

    /// <summary>Most steps kept for Back.</summary>
    public const int BackLimit = 100;

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public event Action? NavigationChanged;

    public void GoBack() => Step(_back, _forward);
    public void GoForward() => Step(_forward, _back);

    private void Step(List<string> from, List<string> to)
    {
        while (from.Count > 0)
        {
            var id = from[^1];
            from.RemoveAt(from.Count - 1);
            var p = _doc.GetPerson(id);
            if (p is null || p == Selected) continue;
            if (Selected is not null) to.Add(Selected.Id);
            _navigating = true;
            try { Select(p, focus: true); }
            finally { _navigating = false; }
            break;
        }
        NavigationChanged?.Invoke();
    }

    public void Select(Person? p, bool focus = false)
    {
        if (p is null) return;
        if (!_navigating && Selected is not null && Selected != p)
        {
            _back.Add(Selected.Id);
            if (_back.Count > BackLimit) _back.RemoveAt(0);
            _forward.Clear();
            NavigationChanged?.Invoke();
        }
        var changed = Selected != p || (focus && Focus != p);
        Selected = p;
        if (focus) Focus = p;
        if (changed) SelectionChanged?.Invoke();
    }

    public void Navigate(string workspace) => NavigateRequested?.Invoke(workspace);

    // ---- bookmarks (Person > Add Bookmark, Ctrl+Shift+B), kept with the tree ----

    public bool IsBookmarked(Person p) => _doc.Data.Bookmarks.Contains(p.Id);

    /// <summary>Adds or removes a bookmark.  Not an undo step, but it marks the tree changed so it is saved.</summary>
    public void ToggleBookmark(Person p)
    {
        if (!_doc.Data.Bookmarks.Remove(p.Id)) _doc.Data.Bookmarks.Add(p.Id);
        _doc.MarkEdited();
        BookmarksChanged?.Invoke();
    }

    public event Action? BookmarksChanged;

    /// <summary>Redraw everything without changing the tree (after a display preference changes).</summary>
    public void Refresh()
    {
        TreeChanged?.Invoke();
        SelectionChanged?.Invoke();
    }

    private void Hook(TreeDocument doc)
    {
        doc.StructureChanged += OnStructureChanged;
        doc.Edited += OnEdited;
    }

    private void Unhook(TreeDocument doc)
    {
        doc.StructureChanged -= OnStructureChanged;
        doc.Edited -= OnEdited;
    }

    private void OnStructureChanged()
    {
        // Undo replaces every object, so find the same people again by id.
        Selected = _doc.GetPerson(Selected?.Id) ?? _doc.HomePerson;
        Focus = _doc.GetPerson(Focus?.Id) ?? Selected;
        TreeChanged?.Invoke();
        SelectionChanged?.Invoke();
    }

    private void OnEdited() => EditStateChanged?.Invoke();
}

/// <summary>Preferences and recent files, kept in the user's application data folder.</summary>
public sealed class AppSettings
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mycelia");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    public List<string> RecentFiles { get; set; } = [];
    public DateStyle DateStyle { get; set; } = DateStyle.DMY;
    public bool DayFirst { get; set; } = true;
    public bool SurnamesInCapitals { get; set; }

    /// <summary>Workspace shown at start-up.</summary>
    public string StartWorkspace { get; set; } = "People";

    /// <summary>Open the most recent tree at start-up rather than the sample tree.</summary>
    public bool ReopenLastTree { get; set; } = true;

    /// <summary>Show the data-error box in the person panel (alerts for unlikely birth, death and marriage dates).</summary>
    public bool ShowDataWarnings { get; set; } = true;

    /// <summary>Options › AI model and Assistant (TDD 22.7).</summary>
    public Ai.Assistant.AssistantSettings Assistant { get; set; } = new();

    /// <summary>Panels the person folded, as "Workspace.Panel" (FoldLayout, TDD 16.1).</summary>
    public List<string> FoldedPanels { get; set; } = [];

    /// <summary>Widths panels were dragged to, by "Workspace.Panel" (FoldLayout, TDD 16.1).</summary>
    public Dictionary<string, double> PanelWidths { get; set; } = [];

    /// <summary>Options › MCP servers: the servers the Assistant connects to (TDD 22.5).</summary>
    public List<Ai.Mcp.McpServerConfig> McpServers { get; set; } = [];

    /// <summary>Options › Other programs: Mycelia's tools served over MCP (TDD 24).</summary>
    public Ai.Server.McpServerSettings McpServer { get; set; } = new();

    /// <summary>Options › Updates and the last check's result (TDD 23).</summary>
    public Mycelia.Updates.UpdateSettings Updates { get; set; } = new();

    /// <summary>Screenshot mode: settings are neither read nor written.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ReadOnly { get; set; }

    /// <summary>
    /// The program was called Family Tree until 11 November 2026, with its settings in %AppData%\FamilyTree and the
    /// Assistant's conversations in %LocalAppData%\FamilyTree.  Each is copied once into the Mycelia folder that doesn't
    /// exist yet; the old folders are left as they were.  DPAPI values decrypt the same, being tied to the Windows user.
    /// </summary>
    private static void CopyFromOldName()
    {
        foreach (var root in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            var parent = Environment.GetFolderPath(root);
            string old = Path.Combine(parent, "FamilyTree"), current = Path.Combine(parent, "Mycelia");
            if (!Directory.Exists(old) || Directory.Exists(current))
                continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(old, "*", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(current, Path.GetRelativePath(old, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A copy that fails part-way leaves defaults for what is missing; the old folder is untouched.
            }
        }
    }

    public static AppSettings Load()
    {
        CopyFromOldName();
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // A damaged settings file is replaced with defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        if (ReadOnly) return;
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Preferences are a convenience; failing to save them must not stop the app.
        }
    }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 8) RecentFiles.RemoveRange(8, RecentFiles.Count - 8);
        Save();
    }
}
