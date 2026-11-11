# Contributing to Mycelia

Thank you for helping.  Bug reports, ideas and pull requests are all welcome.

## Reporting a bug or asking for a feature

Use the issue forms under **Issues > New issue**.  For a bug, say which version you ran (Options ›
Updates shows it), what you did, what you expected and what happened.  If it involves
GEDCOM, attach the smallest file that shows it, with invented people or private details removed.

Never post a real person's details, an API key or a tree file with living people in an issue.
Security problems go through a private report instead (see `SECURITY.md`).

## Before writing code

For anything larger than a small fix, open an issue first and describe what you intend to change.
That avoids work on something that won't fit.

Read `Docs/Design/TDD_Mycelia_v0_3_0.md`, the design record, before changing anything structural.
`CLAUDE.md` lists the parts of the code that are easy to get wrong (undo replaces every object,
dates are kept as text, theme resources are looked up by name at run time, and more); it is
written for Claude Code but reads as a guide for anyone.

## Building and checking

```
dotnet build Mycelia.sln -c Debug -v minimal --nologo
dotnet run --project familyTree/Mycelia.csproj
```

The build must stay at 0 warnings and 0 errors.  There is no test project: check a change by
running the program.  For GEDCOM, import a file, export it again and compare.  `--screenshot
<file.png>` saves a picture of the window with the sample tree, and `--workspace <name>` opens a
workspace; `CLAUDE.md` lists the other switches.

## House rules

- **.NET 10 and WPF only.**  No NuGet packages; raise it in an issue if one looks necessary.
- **British English, no em-dashes.**  In code, comments and the program's text.  Plain, direct
  wording.
- **Keep comments.**  Don't remove a comment unless the code it describes goes too.
- **Own graphics only.**  Icons are line drawings in `Ui/Theme.xaml`.  Don't add artwork, logos or
  brand names from other programs.
- **Invented people only.**  The sample tree and any test data must not describe real people.
- **Documentation with the code.**  A change to the file format, the model or something people
  see updates the design record in the same pull request, and adds a line to
  `Docs/CHANGELOG_Mycelia.md`.
- **Help with the code.**  A change people can see updates its page in `Docs/Help/`.  If a screen
  looks different, run `tools/help-shots.ps1` to make its pictures again.  Fixes to the help on
  their own are welcome too: a clearer sentence or a missing step is a good first contribution.
- **Conventions** (date formats, relationship wording, data-check limits, sorting) are in
  `Docs/Design/Design_Considerations.md`, and the code cites their section.  People rely on them
  feeling familiar: change one only with a reason, and update that document with it.

## Pull requests

Keep each pull request to one change, describe what it does and how you checked it, and fill in
the template.  By contributing you agree that your contribution is licensed under the GNU General
Public License v3.0, as the rest of the code is.

Please follow the [Code of Conduct](CODE_OF_CONDUCT.md).
