using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Mycelia.Model;

namespace Mycelia.Ui;

/// <summary>
/// The right-click menu for a person (TDD 15.2): the actions the person panel, the empty slots
/// and Main menu > Person already offer, gathered in one place.  It adds no behaviour of its own.
/// </summary>
public static class PersonMenu
{
    /// <summary>Selects the person and opens their menu at the mouse (the tree's cards and rows).</summary>
    public static void Show(AppState state, Person person, Action<Person> openPersonView)
    {
        // Selecting redraws the tree, so the menu has no placement target: it opens at the mouse.
        state.Select(person);
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        Fill(menu, state, person, openPersonView);
        menu.IsOpen = true;
    }

    /// <summary>Replaces the menu's items with the person's actions (the index list's own menu).</summary>
    public static void Fill(ContextMenu menu, AppState state, Person person, Action<Person> openPersonView)
    {
        // Undo replaces every object, so each item finds the person again by id when it is clicked.
        var id = person.Id;
        MenuItem Item(string header, Action<Person> action, string? gesture = null, bool enabled = true)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture ?? "", IsEnabled = enabled };
            item.Click += (_, _) => { if (state.Doc.GetPerson(id) is { } p) action(p); };
            return item;
        }
        MenuItem Sub(string header, params MenuItem[] items)
        {
            var sub = new MenuItem { Header = header };
            foreach (var i in items) sub.Items.Add(i);
            return sub;
        }

        var isHome = state.Doc.HomePerson == person;
        menu.Items.Clear();
        menu.Items.Add(Item("Open Person view", openPersonView));
        menu.Items.Add(Item("Centre the tree here", p => state.Select(p, focus: true)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(isHome ? "This is the home person" : "Make home person", p => PersonActions.SetHome(state, p), enabled: !isHome));
        menu.Items.Add(Item(state.IsBookmarked(person) ? "Remove bookmark" : "Add bookmark", state.ToggleBookmark, "Ctrl+Shift+B"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Sub("Add relative",
            Item("Father...", p => PersonActions.AddParent(state, p, Sex.Male)),
            Item("Mother...", p => PersonActions.AddParent(state, p, Sex.Female)),
            Item("Spouse or partner...", p => PersonActions.AddSpouse(state, p)),
            Item("Child...", p => PersonActions.AddChild(state, p)),
            Item("Sibling...", p => PersonActions.AddSibling(state, p))));
        menu.Items.Add(Sub("Link existing person",
            Item("As a parent...", p => PersonActions.LinkExistingParent(state, p)),
            Item("As a spouse or partner...", p => PersonActions.LinkExistingSpouse(state, p)),
            Item("As a child...", p => PersonActions.LinkExistingChild(state, p, null))));
        menu.Items.Add(Item("Set spouse order...", p => Dialogs.SpouseOrder(state, p)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Merge with another record...", p => PersonActions.Merge(state, p, null)));
        menu.Items.Add(Item($"Delete {person.DisplayName}...", p => PersonActions.Delete(state, p)));
    }
}
