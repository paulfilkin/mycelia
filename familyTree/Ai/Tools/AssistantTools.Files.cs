using System.Text.Json.Nodes;
using Mycelia.Ai.Assistant;
using Mycelia.Model;
using Mycelia.Ui.Charts;

namespace Mycelia.Ai.Tools;

/// <summary>The tools that move around the program (Allow by default) and work with files (Ask by default), TDD 22.3.</summary>
public sealed partial class AssistantTools
{
    private static readonly string[] Workspaces = ["Home", "People", "Person", "Places", "Sources", "Media", "Charts", "Tools"];

    private IEnumerable<AssistantTool> MoveTools() =>
    [
        Read("select_person", "Go to a person",
            "Selects a person and centres the tree on them, as clicking them does, optionally opening their Person view, so the person sees who you mean.",
            Schema.Object(
                ("person_id", Schema.String("Who."), true),
                ("open_person_view", Schema.Boolean("Open the People workspace's Person view (default false: the Tree view)."), false)),
            args =>
            {
                var p = PersonArg(args);
                _state.Select(p, focus: true);
                _host.Navigate(args.Bool("open_person_view", false) ? "Person" : "People");
                return new JsonObject { ["selected"] = Ref(p) };
            }, "Selected"),

        Read("show_workspace", "Show a workspace",
            "Shows one of the program's workspaces: Home, People (the tree), Person (the selected person's Person view), Places, Sources, Media, Charts or Tools.",
            Schema.Object(("workspace", Schema.String("Which.", Workspaces), true)),
            args =>
            {
                var name = args.Str("workspace") ?? "";
                var ws = Workspaces.FirstOrDefault(w => w.Equals(name, StringComparison.OrdinalIgnoreCase))
                         ?? throw new ArgumentException($"workspace must be one of {string.Join(", ", Workspaces)}.");
                _host.Navigate(ws);
                return new JsonObject { ["showing"] = ws };
            }, "Shown"),

        Read("show_chart", "Show a chart",
            "Opens the Charts workspace on a chart for a person: Pedigree, Descendants, Fan or FamilyGroupSheet, with a number of generations (2 to 7). The person can then save it as a picture or print it.",
            Schema.Object(
                ("kind", Schema.String("The chart.", EnumNames<ChartKind>()), true),
                ("person_id", Schema.String("Whose chart; the selected person if left out."), false),
                ("generations", Schema.Integer("Generations, 2 to 7 (not for a family group sheet)."), false)),
            args =>
            {
                var kind = EnumArg(args, "kind", ChartKind.Pedigree);
                if (args.Str("person_id") is not null) _state.Select(PersonArg(args), focus: true);
                int? generations = args["generations"] is null ? null : Math.Clamp(args.Int("generations", 4), 2, 7);
                _host.ShowChart(kind, generations);
                return new JsonObject { ["showing"] = kind.ToString(), ["person"] = RefOrNull(_state.Selected) };
            }, "Shown"),
    ];

    private IEnumerable<AssistantTool> FileTools() =>
    [
        Write("save_tree", "Save the tree",
            "Saves the tree to its file. A tree never saved needs a path (a full path ending in .ftree).",
            Schema.Object(("path", Schema.String("Where to save it, for a tree never saved or to save a copy under a new name."), false)),
            args =>
            {
                var path = args.Str("path");
                if (path is not null) path = FullPath(path, TreeDocument.FileExtension);
                else if (Doc.FilePath is null) throw new ArgumentException("This tree has never been saved: give a path ending in .ftree.");
                return _host.SaveTree(path) is { } problem ? throw new InvalidOperationException(problem) : $"Saved the tree to {Doc.FilePath}.";
            },
            args => args.Str("path") is { } p ? $"Save the tree as {p}." : $"Save the tree to {Doc.FilePath ?? "a new file"}."),

        Write("export_gedcom", "Export GEDCOM",
            "Writes the whole tree, or a branch, to a GEDCOM 5.5.1 file for other programs and websites. People and facts marked Private are left out unless included; living people can be privatised (name and family links only).",
            Schema.Object(
                ("path", Schema.String("The file to write: a full path ending in .ged."), true),
                ("include_private", Schema.Boolean("Include people and facts marked Private (default false)."), false),
                ("privatise_living", Schema.Boolean("Write living people as \"Living\" with no details (default true)."), false),
                ("branch_person_id", Schema.String("Export only a branch around this person."), false),
                ("ancestors", Schema.Boolean("For a branch: their ancestors (default true)."), false),
                ("descendants", Schema.Boolean("For a branch: their descendants (default false)."), false),
                ("spouses", Schema.Boolean("For a branch: the spouses of everyone in it (default true)."), false)),
            args =>
            {
                var path = FullPath(args.Str("path") ?? throw new ArgumentException("path is required."), ".ged");
                HashSet<string>? branch = null;
                if (args.Str("branch_person_id") is not null)
                {
                    var root = PersonArg(args, "branch_person_id");
                    branch = TreeTools.Branch(Doc, root, args.Bool("ancestors", true), args.Bool("descendants", false), args.Bool("spouses", true));
                }
                Gedcom.Export(Doc, path, new Gedcom.ExportOptions
                {
                    PersonIds = branch,
                    IncludePrivate = args.Bool("include_private", false),
                    PrivatiseLiving = args.Bool("privatise_living", true),
                });
                return $"Exported {(branch is null ? "the tree" : TreeTools.Describe(Doc, branch))} to {path}.";
            },
            args => $"Write {(args.Str("branch_person_id") is not null ? $"a branch around {NameOf(args, "branch_person_id")}" : "the whole tree")} to the GEDCOM file {args.Str("path")}"
                    + (args.Bool("privatise_living", true) ? ", with living people privatised." : ", with living people in full.")),

        Write("merge_tree_file", "Merge in another tree",
            "Adds every person from another tree file (.ftree or GEDCOM .ged) to this tree, as one undo step. Places and sources with the same names are shared; people are not matched, so find_duplicates afterwards.",
            Schema.Object(("path", Schema.String("The file's full path."), true)),
            args =>
            {
                var path = args.Str("path") ?? throw new ArgumentException("path is required.");
                if (!File.Exists(path)) throw new ArgumentException($"There is no file at {path}.");
                var other = TreeImport.Read(Doc, path);
                if (other.People.Count == 0) return "That tree has nobody in it; nothing was changed.";
                return Change(() => $"Added {TreeImport.MergeInto(Doc, other).Count} people from {Path.GetFileName(path)}.  People who were in both trees now appear twice: find_duplicates finds them.");
            },
            args => $"Add everyone in {Path.GetFileName(args.Str("path") ?? "")} to this tree."),

        new AssistantTool
        {
            Name = "download_file",
            Title = "Download a record",
            Description = "Downloads a record image or document (a register page, a certificate, a census image, a newspaper clipping) from a web address into the tree's media folder beside the tree file, and by default adds it as a media item linked to people. Never overwrites. The tree must have been saved once, so it has a folder.",
            Schema = Schema.Object(
                ("url", Schema.String("The file's web address (https:// or http://)."), true),
                ("file_name", Schema.String("A file name to save it as (the server's name if left out)."), false),
                ("add_as_media", Schema.Boolean("Add it to the tree as a media item (default true)."), false),
                ("caption", Schema.String("The media item's caption."), false),
                ("date", Schema.String("The record's date."), false),
                ("category", Schema.String("Category (Certificate, Census, Register, Newspaper, Photo...)."), false),
                ("description", Schema.String("Description, such as where it came from."), false),
                ("person_ids", Schema.Array(Schema.String("A person's id."), "People the record is about."), false)),
            Changes = true,
            ReachesWeb = true,
            Run = DownloadFileAsync,
            Describe = args => $"Download {args.Str("url")} into the tree's media folder"
                               + (args.Bool("add_as_media", true) ? " and add it as a media item" : "")
                               + (args.Strings("person_ids").Count > 0 ? $" linked to {string.Join(", ", args.Strings("person_ids").Select(id => Doc.GetPerson(id)?.DisplayName ?? id))}." : "."),
        },

        Write("backup_tree", "Back up the tree",
            "Writes a backup: one .ftbackup file holding the tree and copies of its media files.",
            Schema.Object(("path", Schema.String("The backup file: a full path ending in .ftbackup."), true)),
            args =>
            {
                var path = FullPath(args.Str("path") ?? throw new ArgumentException("path is required."), TreeImport.BackupExtension);
                var missing = TreeImport.Backup(Doc, path);
                return $"Backed up the tree to {path}." + (missing.Count > 0 ? $"  {missing.Count} media files couldn't be found and were left out." : "");
            },
            args => $"Back up the tree with its media to {args.Str("path")}."),
    ];

    /// <summary>
    /// download_file: into <see cref="TreeDocument.MediaFolder"/>, then (by default) one undo step adding the media item.
    /// Everything is checked first; a web page where a file was expected (a sign-in or error page) is kept but not added.
    /// </summary>
    private async Task<ToolOutcome> DownloadFileAsync(JsonObject args, CancellationToken cancel)
    {
        var url = args.Str("url") ?? throw new ArgumentException("url is required.");
        var folder = Doc.MediaFolder ?? throw new ArgumentException("This tree has never been saved, so it has no media folder yet: save it first (save_tree with a path).");
        var people = args.Strings("person_ids").Select(id => Doc.GetPerson(id) ?? throw new ArgumentException($"There is no person with id {id}.")).ToList();
        var doc = Doc;
        var result = await Downloads.DownloadAsync(Downloads.Http, url, folder, args.Str("file_name"), cancel);
        var size = result.Bytes >= 1048576 ? $"{result.Bytes / 1048576.0:F1} MB" : $"{Math.Max(1, result.Bytes / 1024)} KB";
        var text = $"Saved {result.FileName} ({size}) in {folder}.";
        if (result.LooksLikeWebPage)
            return new ToolOutcome(text + "  It is a web page, not a document or image (perhaps a sign-in or error page), so it wasn't added to the tree.", true, "A web page, not added");
        if (!args.Bool("add_as_media", true))
            return new ToolOutcome(text, false, $"Saved {result.FileName}");
        // The tree may have changed while the file came down; the people are found again by id.
        if (!ReferenceEquals(doc, Doc))
            return new ToolOutcome(text + "  Another tree was opened meanwhile, so it wasn't added.", true);
        MediaItem? made = null;
        Change(() =>
        {
            made = new MediaItem
            {
                FilePath = result.Path,
                Caption = args.Str("caption") ?? Path.GetFileNameWithoutExtension(result.FileName),
                Date = args.Str("date") ?? "",
                Category = args.Str("category") ?? "",
                Description = args.Str("description") ?? $"Downloaded from {url}",
            };
            foreach (var p in people.Select(p => Doc.GetPerson(p.Id)).OfType<Person>()) made.PersonIds.Add(p.Id);
            Doc.Data.Media.Add(made);
            return "";
        });
        return new ToolOutcome(text + $"  Added it as the media item \"{made!.Caption}\" ({made.Id}).", false, $"Saved and added {result.FileName}");
    }

    /// <summary>A full path with the expected extension added when missing; a relative path is refused.</summary>
    private static string FullPath(string path, string extension)
    {
        if (!Path.IsPathRooted(path)) throw new ArgumentException($"Give a full path, such as C:\\Users\\...\\Documents\\tree{extension}.");
        if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) path += extension;
        var folder = Path.GetDirectoryName(path);
        if (folder is null || !Directory.Exists(folder)) throw new ArgumentException($"The folder {folder} doesn't exist.");
        return path;
    }
}
