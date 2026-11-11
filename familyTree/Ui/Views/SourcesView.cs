using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mycelia.Model;

namespace Mycelia.Ui.Views;

/// <summary>Sources (records, books, registers), where they are kept, and which facts cite them.</summary>
public sealed class SourcesView : MasterDetail
{
    public SourcesView() : base("Sources", "Book", "Source")
    {
        FooterButtons.Children.Add(UiKit.Button("SecondaryButton", "Add source", (_, _) => AddSource(), "Plus"));
    }

    private TreeDocument Doc => State!.Doc;

    private List<(object Owner, Fact Fact, Citation Citation)> CitationsOf(Source s) =>
        Doc.AllFacts().SelectMany(x => x.Fact.Citations.Where(c => c.SourceId == s.Id).Select(c => (x.Owner, x.Fact, c))).ToList();

    protected override IEnumerable<Entry> Items(string query)
    {
        var counts = Doc.AllFacts().SelectMany(x => x.Fact.Citations).GroupBy(c => c.SourceId).ToDictionary(g => g.Key, g => g.Count());
        return Doc.Data.Sources
            .Where(s => query.Length == 0 || s.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) || s.Author.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(s =>
            {
                var n = counts.GetValueOrDefault(s.Id);
                return new Entry(s, Row(s.ToString(), s.Author, n == 1 ? "1 citation" : $"{n} citations"));
            });
    }

    protected override string? KeyOf(object? model) => (model as Source)?.Id;

    protected override void ShowDetail(object? item)
    {
        if (item is not Source source)
        {
            Detail.Content = UiKit.EmptyState("Book", "No sources yet", "Sources record where each fact came from. Add one here or from a fact in People.");
            return;
        }
        var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var del = UiKit.Button("GhostButton", "Delete source", (_, _) => Delete(source), "Trash");
        del.Foreground = UiKit.Brush("Danger");
        del.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(del, Dock.Right);
        head.Children.Add(del);
        var titles = new StackPanel();
        titles.Children.Add(UiKit.Text("SOURCE", "SectionLabel"));
        var title = UiKit.Text(source.ToString(), "H1");
        title.TextWrapping = TextWrapping.Wrap;
        titles.Children.Add(title);
        head.Children.Add(titles);
        sp.Children.Add(head);

        sp.Children.Add(UiKit.Field(Doc, "Title", () => source.Title, v => { source.Title = v; title.Text = source.ToString(); }));
        sp.Children.Add(UiKit.Field(Doc, "Author or originator", () => source.Author, v => source.Author = v));
        sp.Children.Add(Pair(
            UiKit.Field(Doc, "Publisher", () => source.Publisher, v => source.Publisher = v),
            UiKit.Field(Doc, "Publisher location", () => source.PublisherLocation, v => source.PublisherLocation = v)));
        sp.Children.Add(Pair(
            UiKit.Field(Doc, "Publish date", () => source.PublishDate, v => source.PublishDate = v),
            UiKit.Field(Doc, "Call number", () => source.CallNumber, v => source.CallNumber = v)));

        // The source online: a database, catalogue entry or digitised book (TDD 25), with Open beside it.
        var webField = UiKit.Field(Doc, "Web address", () => source.Url, v => source.Url = v.Trim());
        var open = UiKit.Button("GhostButton", "Open", (_, _) => Dialogs.OpenWebAddress(source.Url), "Link", "Open the web address in the browser");
        open.VerticalAlignment = VerticalAlignment.Bottom;
        open.Margin = new Thickness(8, 0, 0, 12);
        var webRow = new DockPanel();
        DockPanel.SetDock(open, Dock.Right);
        webRow.Children.Add(open);
        webRow.Children.Add(webField);
        sp.Children.Add(webRow);

        // Repository
        sp.Children.Add(new TextBlock { Text = "Repository (where the source is kept)", Style = UiKit.Res<Style>("FieldLabel") });
        var repoBox = new ComboBox
        {
            IsEditable = true,
            ItemsSource = Doc.Data.Repositories.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            Text = Doc.GetRepository(source.RepositoryId)?.Name ?? "",
            Margin = new Thickness(0, 0, 0, 12),
        };
        var repoDetail = new StackPanel();
        void ShowRepo()
        {
            repoDetail.Children.Clear();
            var repo = Doc.GetRepository(source.RepositoryId);
            if (repo is null) return;
            var card = new StackPanel();
            card.Children.Add(UiKit.Field(Doc, "Repository address", () => repo.Address, v => repo.Address = v));
            card.Children.Add(Pair(
                UiKit.Field(Doc, "Phone", () => repo.Phone, v => repo.Phone = v),
                UiKit.Field(Doc, "Email", () => repo.Email, v => repo.Email = v)));
            var border = new Border { Style = UiKit.Res<Style>("Card"), Background = UiKit.Brush("SurfaceAlt"), Padding = new Thickness(14, 12, 14, 0), Margin = new Thickness(0, 0, 0, 12), Child = card };
            repoDetail.Children.Add(border);
        }
        void CommitRepo()
        {
            var name = repoBox.Text.Trim();
            if (name == (Doc.GetRepository(source.RepositoryId)?.Name ?? "")) return;
            Doc.Change(() =>
            {
                if (name.Length == 0) { source.RepositoryId = null; return; }
                var repo = Doc.Data.Repositories.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.CurrentCultureIgnoreCase));
                if (repo is null)
                {
                    repo = new Repository { Name = name };
                    Doc.Data.Repositories.Add(repo);
                }
                source.RepositoryId = repo.Id;
            }, structural: false);
            ShowRepo();
        }
        repoBox.LostKeyboardFocus += (_, _) => { if (!repoBox.IsKeyboardFocusWithin) CommitRepo(); };
        repoBox.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(new Action(CommitRepo));
        sp.Children.Add(repoBox);
        sp.Children.Add(repoDetail);
        ShowRepo();

        sp.Children.Add(UiKit.Field(Doc, "Comments", () => source.Comments, v => source.Comments = v, multiline: true));

        var cites = CitationsOf(source);
        sp.Children.Add(UiKit.Text(cites.Count == 0 ? "CITATIONS" : $"CITATIONS ({cites.Count})", "SectionLabel"));
        if (cites.Count == 0) sp.Children.Add(UiKit.Text("No facts cite this source yet. Add a citation from a fact in People.", "Caption"));
        foreach (var (owner, fact, citation) in cites.OrderBy(c => Doc.OwnerName(c.Owner), StringComparer.CurrentCultureIgnoreCase))
        {
            var row = new Grid { Margin = new Thickness(-8, 0, -8, 2), Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };
            left.Children.Add(UiKit.Text(Doc.OwnerName(owner), weight: FontWeights.Medium));
            left.Children.Add(UiKit.Text($"{fact.Type.Name}  {fact.ParsedDate.Display}".Trim(), "Caption"));
            row.Children.Add(left);
            var detail = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            if (citation.Detail.Length > 0) detail.Children.Add(UiKit.Text(citation.Detail, "Caption"));
            if (citation.Url.Length > 0) detail.Children.Add(Dialogs.WebLink(citation.Url));
            Grid.SetColumn(detail, 1);
            row.Children.Add(detail);
            row.MouseEnter += (_, _) => row.Background = UiKit.Brush("Hover");
            row.MouseLeave += (_, _) => row.Background = System.Windows.Media.Brushes.Transparent;
            row.MouseLeftButtonUp += (_, _) =>
            {
                var person = owner as Person ?? Doc.GetPerson((owner as Family)?.Partner1Id) ?? Doc.GetPerson((owner as Family)?.Partner2Id);
                if (person is null) return;
                State!.Select(person, focus: true);
                State.Navigate("People");
            };
            sp.Children.Add(row);
        }
        Detail.Content = Page(sp);
    }

    private void AddSource()
    {
        var title = Dialogs.Prompt("Add source", "Title (for example \"1881 Census\" or \"Parish register of St Mary's\")", "");
        if (title is null) return;
        var source = new Source { Title = title };
        Doc.Change(() => Doc.Data.Sources.Add(source));
        SelectItem(source);
    }

    private void Delete(Source source)
    {
        var cites = CitationsOf(source);
        var msg = cites.Count == 0
            ? $"Delete \"{source}\"?"
            : $"Delete \"{source}\"?  {cites.Count} citations to it will be removed from facts.";
        if (!Dialogs.Confirm("Delete source", msg, "Delete")) return;
        Doc.Change(() =>
        {
            foreach (var (_, fact, _) in cites) fact.Citations.RemoveAll(c => c.SourceId == source.Id);
            Doc.Data.Sources.Remove(source);
        });
    }
}
