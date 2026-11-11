[Help home](README.md)

# Questions and troubleshooting

Answers to common questions, and what to do when something doesn't work as you expect.

## My changes weren't saved

Mycelia keeps your changes in memory as you work, and writes them to the tree file only when you
save: press **Ctrl+S** or click **Save** in the top bar.  There is no automatic saving.  The top
bar shows "unsaved changes" until you save, and Mycelia asks before it closes or opens another
tree with changes unsaved.

Moving from one person to another never loses anything.  If a change seems to have gone, check
whether **Undo** was used: Undo takes back the last change wherever it was made, so clicking it
after moving to someone else undoes your typing on the person you left.  **Redo** (Ctrl+Y) brings
it back.

## Where is everything kept?

- **Your tree:** the `.ftree` file you saved, wherever you chose to put it.
- **Pictures and documents:** where they already were on your computer.  Mycelia links to them
  and doesn't copy them, except records the Assistant downloads, which go into a folder beside the
  tree file, named after it (for `Ashworth.ftree`, the folder `Ashworth media`).
- **Settings:** `%AppData%\Mycelia\settings.json`.  API keys and sign-in details in it are
  encrypted for your Windows account.
- **Assistant conversations:** `%LocalAppData%\Mycelia\Assistant\`, one per tree, encrypted for
  your Windows account.

Nothing is stored online.  Mycelia has no account and no cloud copy of your tree.

## Pictures show "File not found"

A picture or document is a link to a file on your computer.  If the file has moved, or the tree
was opened on another computer, select the item in the [Media](media.md) workspace and use **Find
file...** to point to it again.  To move a tree to another computer with its pictures, use Main
menu > Import and export > **Back up with media...** and restore the backup there (see
[GEDCOM, merging and backups](gedcom-and-backups.md)).

## A date is shown in orange

Mycelia couldn't read it as a date.  It is kept exactly as you typed it, but it can't be used for
sorting, ages or checks.  The hint under the date box shows how a date is read; see
[Facts, dates and sources](facts-and-sources.md) for the forms Mycelia understands.

## The person panel shows warnings I don't want

The warnings box lists unlikely dates, such as a mother under 13 at a birth or a death before a
birth, and the details still to complete.  Turn it off in Options > Warnings.  Tools > **Find
data errors** checks the whole tree whichever way it is set.

## The Assistant asks me to choose an AI model

The Assistant needs an AI service to work.  Click **Options** in the Assistant pane, or open
Options > AI model, choose a provider and model, and add your API key.  See
[the Assistant](assistant.md#choosing-an-ai-model).

## The Assistant asks before every change

That's deliberate: every change it wants to make waits for **Allow**, **Always allow** or
**Decline**.  **Always allow** stops the questions for that kind of change.  For web addresses,
"always" applies to one site at a time.  Every change is an undo step, and the line at the end of
a request offers **Revert** to put everything back as it was before that request.

## The Assistant can't search a website

The Assistant reads open archive services and the sites you allow.  It can't sign in to
subscription sites or fill in search forms; for those it gives you the steps to search yourself.
To connect more archives, see [MCP servers](mcp-servers.md).

## An MCP server doesn't connect

Open Options > MCP servers, select the server and check that it is on and that its address or
command is right.  If it needs a sign-in, sign in again from there.  Options > Activity log shows
what the servers said, including their errors.  See [MCP servers](mcp-servers.md).

## An update is refused

Mycelia installs only updates signed with the publisher's key.  If an update says it is signed
with a key this version doesn't know, download the new version from the publisher's website
instead.  See [Options and updates](options-and-updates.md#updates).

## Reporting a problem or suggesting an idea

Use Main menu > Help > **Report a problem or suggest an idea...**, which opens the forms on
GitHub.  Please don't include real people's details, API keys or a tree with living people.  Report
security problems privately, as the project's
[security page](https://github.com/paulfilkin/mycelia/security/policy) describes.
