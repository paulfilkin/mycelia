using System.IO.Compression;
using System.Text.Json;

namespace Mycelia.Model;

/// <summary>
/// File > Merge another tree, and backups.  These are this app's own design (TDD, Merging another
/// tree; Backup and restore).
/// </summary>
public static class TreeImport
{
    /// <summary>
    /// Adds everything in <paramref name="other"/> to the open tree, under new ids so nothing can clash.
    /// Places, sources and repositories with the same name as existing ones are shared rather than
    /// doubled.  Run inside <see cref="TreeDocument.Change"/>.  People are not matched here: Find
    /// duplicate people does that afterwards.  Returns the ids of the people added.
    /// </summary>
    public static List<string> MergeInto(TreeDocument doc, TreeData other)
    {
        var data = doc.Data;
        var cmp = StringComparer.CurrentCultureIgnoreCase;

        // Shared things first, matched by name.
        var placeIds = new Dictionary<string, string>();
        foreach (var pl in other.Places)
            placeIds[pl.Id] = doc.GetOrCreatePlace(pl.Name)?.Id ?? pl.Id;
        var repoIds = new Dictionary<string, string>();
        foreach (var r in other.Repositories)
        {
            var same = data.Repositories.FirstOrDefault(x => cmp.Equals(x.Name, r.Name));
            if (same is null)
            {
                same = new Repository { Name = r.Name, Address = r.Address, Phone = r.Phone, Email = r.Email };
                data.Repositories.Add(same);
            }
            repoIds[r.Id] = same.Id;
        }
        var sourceIds = new Dictionary<string, string>();
        foreach (var s in other.Sources)
        {
            var same = data.Sources.FirstOrDefault(x => cmp.Equals(x.Title, s.Title) && cmp.Equals(x.Author, s.Author));
            if (same is null)
            {
                same = new Source
                {
                    Title = s.Title, Author = s.Author, Publisher = s.Publisher, PublisherLocation = s.PublisherLocation,
                    PublishDate = s.PublishDate, CallNumber = s.CallNumber, Comments = s.Comments,
                    RepositoryId = s.RepositoryId is null ? null : repoIds.GetValueOrDefault(s.RepositoryId),
                };
                data.Sources.Add(same);
            }
            sourceIds[s.Id] = same.Id;
        }

        void Remap(IEnumerable<Fact> facts)
        {
            foreach (var f in facts)
            {
                f.Id = Ids.New('E');
                f.PlaceId = f.PlaceId is null ? null : placeIds.GetValueOrDefault(f.PlaceId);
                foreach (var c in f.Citations) c.SourceId = sourceIds.GetValueOrDefault(c.SourceId, c.SourceId);
            }
        }

        // Custom fact types: same name and scope shares the type; otherwise a new id.
        var typeIds = new Dictionary<int, int>();
        foreach (var t in other.CustomFactTypes)
        {
            var same = data.CustomFactTypes.FirstOrDefault(x => x.Scope == t.Scope && cmp.Equals(x.Name, t.Name));
            if (same is null)
            {
                same = new CustomFactType { Id = NextCustomId(data), Name = t.Name, Scope = t.Scope, HasDate = t.HasDate, HasPlace = t.HasPlace };
                data.CustomFactTypes.Add(same);
            }
            typeIds[t.Id] = same.Id;
        }
        void RemapTypes(IEnumerable<Fact> facts)
        {
            foreach (var f in facts)
                if (typeIds.TryGetValue(f.TypeId, out var id)) f.TypeId = id;
        }

        var personIds = new Dictionary<string, string>();
        foreach (var p in other.People)
        {
            var newId = Ids.New('I');
            personIds[p.Id] = newId;
            p.Id = newId;
        }
        var familyIds = new Dictionary<string, string>();
        foreach (var f in other.Families)
        {
            var newId = Ids.New('F');
            familyIds[f.Id] = newId;
            f.Id = newId;
        }
        var mediaIds = new Dictionary<string, string>();
        foreach (var m in other.Media)
        {
            var same = data.Media.FirstOrDefault(x => string.Equals(x.FilePath, m.FilePath, StringComparison.OrdinalIgnoreCase));
            mediaIds[m.Id] = same?.Id ?? Ids.New('M');
        }

        foreach (var p in other.People)
        {
            Remap(p.Facts);
            RemapTypes(p.Facts);
            p.PhotoMediaId = p.PhotoMediaId is null ? null : mediaIds.GetValueOrDefault(p.PhotoMediaId);
            p.SpouseOrder = p.SpouseOrder.Select(id => familyIds.GetValueOrDefault(id)).OfType<string>().ToList();
            data.People.Add(p);
        }
        foreach (var f in other.Families)
        {
            f.Partner1Id = f.Partner1Id is null ? null : personIds.GetValueOrDefault(f.Partner1Id);
            f.Partner2Id = f.Partner2Id is null ? null : personIds.GetValueOrDefault(f.Partner2Id);
            foreach (var c in f.Children) c.PersonId = personIds.GetValueOrDefault(c.PersonId, c.PersonId);
            Remap(f.Facts);
            RemapTypes(f.Facts);
            data.Families.Add(f);
        }
        foreach (var m in other.Media)
        {
            var people = m.PersonIds.Select(id => personIds.GetValueOrDefault(id)).OfType<string>().ToList();
            var existing = data.Media.FirstOrDefault(x => x.Id == mediaIds[m.Id]);
            if (existing is not null)
            {
                foreach (var id in people) if (!existing.PersonIds.Contains(id)) existing.PersonIds.Add(id);
                continue;
            }
            m.Id = mediaIds[m.Id];
            m.PersonIds = people;
            data.Media.Add(m);
        }
        foreach (var e in other.HistoricalEvents)
            if (!data.HistoricalEvents.Any(x => cmp.Equals(x.Title, e.Title) && x.Date == e.Date))
                data.HistoricalEvents.Add(e);

        doc.RebuildIndex();
        return personIds.Values.ToList();
    }

    public static int NextCustomId(TreeData data) =>
        Math.Max(FactTypes.FirstCustomId, data.CustomFactTypes.Count == 0 ? 0 : data.CustomFactTypes.Max(t => t.Id) + 1);

    /// <summary>
    /// Reads a tree to merge: a .ftree file or a GEDCOM file.  Reading builds a document for the other
    /// tree, which loads its fact types in place of the open tree's (<see cref="FactTypes.SetCustom"/>
    /// is global), so the open tree's are put back afterwards, whether or not the merge goes ahead.
    /// </summary>
    public static TreeData Read(TreeDocument open, string path)
    {
        try
        {
            return Path.GetExtension(path).Equals(".ged", StringComparison.OrdinalIgnoreCase)
                ? Gedcom.Import(path)
                : TreeDocument.Load(path).Data;
        }
        finally
        {
            FactTypes.SetCustom(open.Data.CustomFactTypes);
        }
    }

    // ---------------------------------------------------------------- backup and restore

    public const string BackupExtension = ".ftbackup";
    private const string TreeEntry = "tree.ftree";
    private const string MediaFolder = "media/";

    /// <summary>
    /// Writes a backup: one zip file holding the tree and a copy of every media file it links to
    /// (files that cannot be found are listed in the result).  Media paths inside the backup are
    /// relative ("media/0001 photo.jpg").  Returns the media files that were missing.
    /// </summary>
    public static List<string> Backup(TreeDocument doc, string zipPath)
    {
        var copy = JsonSerializer.Deserialize<TreeData>(doc.Serialize(), TreeDocument.JsonOptionsForCopy)!;
        var missing = new List<string>();
        var tmp = zipPath + ".tmp";
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            var n = 0;
            foreach (var m in copy.Media)
            {
                if (!File.Exists(m.FilePath)) { missing.Add(m.FilePath); continue; }
                var entry = $"{MediaFolder}{++n:0000} {Path.GetFileName(m.FilePath)}";
                zip.CreateEntryFromFile(m.FilePath, entry, CompressionLevel.Optimal);
                m.FilePath = entry;
            }
            var treeEntry = zip.CreateEntry(TreeEntry, CompressionLevel.Optimal);
            using var w = new StreamWriter(treeEntry.Open());
            w.Write(JsonSerializer.Serialize(copy, TreeDocument.JsonOptionsForCopy));
        }
        File.Move(tmp, zipPath, overwrite: true);
        return missing;
    }

    /// <summary>
    /// Restores a backup into <paramref name="folder"/>: the tree as "&lt;name&gt;.ftree" and its media in a
    /// "Media" folder beside it, with the tree's media paths pointing there.  Files already in that
    /// folder are left alone and the restored ones take names not in use.  Returns the tree's path.
    /// </summary>
    public static string Restore(string zipPath, string folder)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var treeEntry = zip.GetEntry(TreeEntry) ?? throw new InvalidDataException("This is not a Mycelia backup: it has no tree in it.");
        TreeData data;
        using (var r = new StreamReader(treeEntry.Open()))
            data = JsonSerializer.Deserialize<TreeData>(r.ReadToEnd(), TreeDocument.JsonOptionsForCopy) ?? new TreeData();

        var mediaDir = Path.Combine(folder, "Media");
        var root = Path.GetFullPath(mediaDir) + Path.DirectorySeparatorChar;
        foreach (var m in data.Media)
        {
            if (!m.FilePath.StartsWith(MediaFolder, StringComparison.Ordinal)) continue;
            var entry = zip.GetEntry(m.FilePath);
            if (entry is null) continue;
            var target = Path.GetFullPath(Path.Combine(mediaDir, m.FilePath[MediaFolder.Length..]));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // never write outside the folder
            Directory.CreateDirectory(mediaDir);
            // A file already there (an earlier restore, say) may be a different picture, so never reuse it.
            target = UnusedPath(target);
            entry.ExtractToFile(target);
            m.FilePath = target;
        }

        var name = string.Concat(data.Name.Split(Path.GetInvalidFileNameChars())).Trim();
        if (name.Length == 0) name = "Restored tree";
        var path = Path.Combine(folder, name + TreeDocument.FileExtension);
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{name} ({i}){TreeDocument.FileExtension}");
        // Written directly rather than through a TreeDocument, which would load this tree's fact
        // types in place of the open tree's before the restored tree is opened.
        data.Modified = DateTime.Now;
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, TreeDocument.JsonOptionsForCopy));
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    /// <summary>The path itself when nothing is there, else "name (2).ext", "name (3).ext" and so on.</summary>
    private static string UnusedPath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
