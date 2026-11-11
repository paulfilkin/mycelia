[Help home](README.md)

# GEDCOM, merging and backups

This page covers the files Mycelia reads and writes: its own tree files, GEDCOM files for moving a tree to and from other programs, merging another tree into the open one, and backups that include your media.

Everything here is in the main menu (the menu button at the right of the top bar), under **Import and export**.

## Tree files and backup files

- **.ftree** is a Mycelia tree.  It holds everyone and everything in the tree: people, families, facts, places, sources, citations, links to media, bookmarks, your own fact types, historical events and the history list.  It is a plain text file in JSON format.  Save, Save as... and Open... in the main menu work with these files.
- **.ftbackup** is a backup: one zip file holding the tree and a copy of every media file it links to.  It is made by **Back up with media...** and opened by **Restore a backup...**.

Saving a tree replaces the file.  Mycelia does not keep older copies, so make a backup from time to time.

## Importing a GEDCOM file

GEDCOM is the common file format for family trees.  Most genealogy programs and websites can write one.

1. Main menu > Import and export > Import GEDCOM...
2. If the open tree has unsaved changes, you are asked whether to save them first.
3. Choose the .ged file and click **Open**.

The GEDCOM file becomes a new tree, which replaces the open one.  Mycelia tells you how many people and families it read.  The new tree has not been saved yet: save it with Ctrl+S to keep it as a .ftree file.

You can also open a .ged file with Main menu > Open..., or with **Import GEDCOM** on the Home workspace.

What is read:

- people with their names, sex, notes and facts; extra names become "Also Known As" facts;
- families with both partners, children (with adopted, foster and other links) and the couple's facts;
- places, sources, repositories and citations, including web addresses, accessed dates and extracts;
- links to media files, with the first picture for each person as their portrait;
- events of a type Mycelia does not know, which become fact types of your own with the name given in the file.

The first person in the file becomes the home person.  Files in UTF-8, UTF-16 and older single-byte character sets are read.

## Exporting a GEDCOM file

1. Main menu > Import and export > Export GEDCOM...
2. Choose what to export in the **Export GEDCOM** window (see below) and click **Export...**.
3. Choose where to save the .ged file and click **Save**.

The file is GEDCOM 5.5.1 in UTF-8, which most family tree programs can read.  Media are written as links to the files' paths; the files themselves are not included.  Your own fact types are written as events with their names.

### What to export

- **The whole tree**, or
- **A branch**: everyone connected to one person in one direction.  **Starting with** is the person selected in the People workspace (or the home person); click it to choose someone else.  Then choose **Ancestors**, **Descendants** or **Ancestors and descendants**, and tick **Include spouses** to add the husbands and wives of everyone in the branch.  The window shows how many people the branch holds and the years they were born, such as "42 people, born 1798-1946".

Main menu > Import and export > Export a branch... opens the same window, titled **Export branch**, with **A branch** chosen.

In a branch, only families with at least one member in the branch are written, links to people outside it are dropped, and only the sources and repositories its facts cite are included.

### Private and living people

- **Privatise living people**: when ticked, each living person is written with the given name "Living", their surname and sex, their links to their family, and nothing else: no facts, notes or media.  Use this before you share a file.
- **Include facts and people marked Private**: unticked at first.  While it is unticked, facts marked Private are left out, and people marked Private are written in the same reduced form as living people.

When a person in a couple is written in the reduced form, the couple's facts and notes are left out too.

### Who counts as living

Mycelia decides whether someone is living from what the tree records, in this order:

1. Anyone with a death date or death place is not living.
2. Anyone born more than 120 years ago, or whose marriage was more than 100 years ago, is not living.
3. Anyone with a burial, cremation, funeral or cause of death is not living.
4. Anyone with a birth date within the last 120 years is living.
5. Anyone with no birth date is living if their preferred spouse or either parent is living by the same rules.

In the sample tree, Peter Ashworth (born 1942, no death recorded) counts as living.

## Merging another tree into this one

To bring the people from another tree into the open one:

1. Main menu > Import and export > Merge another tree into this one...
2. Choose a Mycelia tree (.ftree) or a GEDCOM file (.ged).
3. Mycelia says how many people and families it will add.  Click **OK**.

Everything in the other tree is added.  Places and repositories with the same name, sources with the same title and author, your own fact types with the same name, media with the same file path and historical events with the same title and date are shared rather than doubled.

People are never matched, so anyone who was in both trees now appears twice.  Mycelia asks whether to look for duplicates now: **Yes** opens the Tools workspace and runs Find duplicates, where you can compare each pair and merge them.  See [Merging two people](tools.md#merging-two-people).

The merge is one step: Undo (Ctrl+Z) removes everything it added.

## Backing up with media

Main menu > Import and export > Back up with media... saves the tree as it is open, including changes not yet saved, together with a copy of every media file it links to, in one .ftbackup file.  The file name starts as the tree's name and today's date.

Mycelia tells you how many people and media files it backed up.  Media files that cannot be found are left out, and listed so you can find them.

A backup is also the way to move a tree and its pictures to another computer, since a tree file alone holds only the paths of its media.

## Restoring a backup

1. Main menu > Import and export > Restore a backup...
2. If the open tree has unsaved changes, you are asked whether to save them first.
3. Choose the .ftbackup file.
4. Choose a folder to restore into.

Mycelia saves the tree in that folder as a .ftree file named after the tree, puts the media files in a folder called Media beside it, points the tree's media at those copies, and opens the tree.

Nothing in the folder is overwritten.  If a tree file with that name is already there, the restored one is saved as "name (2)", and so on; media files with names already in the Media folder are given the next free name in the same way.

## Related pages

- [Getting started](getting-started.md)
- [Media](media.md)
- [Tools](tools.md)
