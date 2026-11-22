# mycelia: Working with Git

A step-by-step guide to getting set up and making a change, for someone new to this repository.  `CONTRIBUTING.md` in the repository root explains the rules; this guide gives the commands.  Designated Driver, the desktop tool that set this repository up, has a button for each of these steps; the commands are what it runs.

All commands are for PowerShell on Windows.  Unless a step says otherwise, run them from the repository folder (the `mycelia` folder the clone creates).

## 1. Before you start

You need:

- A GitHub account with access to `paulfilkin/mycelia`.  For a private repository, ask @paulfilkin for the **Write** role.
- Permission to install software on your PC.

## 2. One-off setup

Do this once on each PC you work on.

**Install Git and the GitHub CLI**, and the .NET tools if you don't have them.

```powershell
winget install Git.Git GitHub.cli
```

Close and reopen PowerShell afterwards so the new commands are found.

**Sign in to GitHub.**  Choose GitHub.com, HTTPS, and "Login with a web browser".  You enter a one-time code in the browser.

```powershell
gh auth login
```

**Let Git use that sign-in**, so pushes and pulls don't ask for a password:

```powershell
gh auth setup-git
```

Only if you will change the automatic checks in `.github\workflows`, add the permission for that too:

```powershell
gh auth refresh -h github.com -s workflow
```

**Set the name and email on your commits.**  Use your GitHub no-reply address so your own email isn't published in the history.  The first command prints the address; paste it into the third.

```powershell
gh api user --jq '"\(.id)+\(.login)@users.noreply.github.com"'
```

```powershell
git config --global user.name "Your Name"
```

```powershell
git config --global user.email "PASTE-THE-ADDRESS-HERE"
```

**Two safety settings.**  The first stops `git pull` making unexpected merge commits; the second tidies away branches that were deleted on GitHub.

```powershell
git config --global pull.ff only
```

```powershell
git config --global fetch.prune true
```

**Get the code.**  Go to the folder where you keep projects, then clone and enter the repository:

```powershell
gh repo clone paulfilkin/mycelia
```

```powershell
cd mycelia
```

**Turn on the repository's hooks.**  They check your commit messages and stop a push to `main` by mistake.  This setting is per clone.

```powershell
git config core.hooksPath .githooks
```

**Check it works.**  Run the checks; they should pass.

```powershell
dotnet build "familyTree\Mycelia.csproj" -warnaserror
```

## 3. Before you start each piece of work

1. Choose the issue you'll work on.  Every change starts from an issue; see `CONTRIBUTING.md` section 2 for when one is ready.  To list the open issues, or open one in the browser:

   ```powershell
   gh issue list
   ```

   ```powershell
   gh issue view 42 --web
   ```

   Assign it to yourself so nobody else starts the same work (42 is the issue number):

   ```powershell
   gh issue edit 42 --add-assignee "@me"
   ```

   Then move its card to **In Progress** on the [project board](https://github.com/users/paulfilkin/projects/9).

   If the work has no issue yet, open one and choose the **Feature** or **Bug** form:

   ```powershell
   gh issue create --web
   ```

2. Check where you are:

   ```powershell
   git status
   ```

   It should say "On branch main" and "nothing to commit, working tree clean".  If it doesn't, see section 8.

3. Get the latest code from GitHub:

   ```powershell
   git switch main
   ```

   ```powershell
   git pull
   ```

4. Make a branch for the issue: `feature/...` for something new, `fix/...` for a bug, `docs/...` for documentation only, `chore/...` for tooling and maintenance, followed by the issue number and a short description.

   ```powershell
   git switch -c feature/42-short-description
   ```

You can't push to `main`: every change goes through a branch and a pull request.

## 4. While you work

| To | Run |
|---|---|
| Run the checks | `dotnet build "familyTree\Mycelia.csproj" -warnaserror` |
| See which files you changed | `git status` |
| See the changes themselves | `git diff` |
| Save a step as a commit | `git add -A`, then `git commit -m "Add feedback export"` |
| Send the branch to GitHub the first time | `git push -u origin feature/42-short-description` |
| Send later commits | `git push` |

Commit in small steps and push whenever you stop work, so your work isn't only on your PC.  Commit messages start with a short summary in the imperative ("Add", "Fix", "Update"); see `CONTRIBUTING.md` section 6.  The `commit-msg` hook tells you when a message breaks the rules.

Before you open a pull request, run the checks.

## 5. Opening a pull request

1. Push your latest commits:

   ```powershell
   git push
   ```

2. Open the pull request in the browser, where the template's checklist appears:

   ```powershell
   gh pr create --web
   ```

   Under **Issue**, complete `Closes #` with your issue number, for example `Closes #42`, so the merge closes the issue.  Fill in what changed and why, how you tested it, tick the checklist, and say whether you used an AI assistant.  Then click **Create pull request**.

3. The automatic checks, **Build and test** and **Guard**, start by themselves.  To watch them from PowerShell:

   ```powershell
   gh pr checks --watch
   ```

   Or open the pull request on GitHub and look at the **Checks** tab.

## 6. Review and merge

- If a check fails, open its log (the **Checks** tab, or **Details** next to the check), fix the problem on your branch, commit and push.  The checks run again by themselves.
- The pull request then waits for approval.  "Review required" is normal until then.
- Answer every review comment, with a change or a reply, and click **Resolve conversation**.
- Once it's approved and the checks are green, the reviewer merges it with **Squash and merge**.  Your branch is deleted on GitHub automatically, the issue is closed and its card moves to **Done**.

To see the state of your pull request at any time:

```powershell
gh pr view --web
```

## 7. After your pull request is merged

Go back to `main`, get the merged result, and delete your local branch:

```powershell
git switch main
```

```powershell
git pull
```

```powershell
git branch -D feature/42-short-description
```

The capital `-D` is needed because squash merging makes Git think the branch wasn't merged.  Start your next piece of work from section 3.

## 8. Common situations

| Situation | What to do |
|---|---|
| You made changes while on `main` and want to keep them | `git switch -c feature/name`.  The changes move to the new branch. |
| You want to throw away changes to a file | `git restore <file>`, or `git restore .` for everything.  This can't be undone. |
| You want to undo your last commit (not yet pushed) but keep the changes | `git reset --soft HEAD~1` |
| GitHub says your branch is out of date with `main` | `gh pr update-branch`, then `git pull` on your branch. |
| Updating brings a conflict | `git status` lists the files.  In each, keep the right lines and remove the `<<<<<<<`, `=======` and `>>>>>>>` markers.  Then `git add -A`, `git commit` and `git push`. |
| The `pre-push` hook refuses your push | You are on `main`.  Make a branch (first row) and push that. |
| The `commit-msg` hook refuses your message | It says which rule was broken.  `git commit` again with a better message. |
| A push is refused about `workflow` scope | You changed `.github\workflows`.  Run the `gh auth refresh` command from section 2. |
| You work on two PCs | Push your branch before you leave one.  On the other: `git fetch`, `git switch feature/name`, `git pull`. |
| You want to try someone else's pull request | `gh pr list`, then `gh pr checkout <number>`.  Go back with `git switch main`. |

## 9. Working with Claude Code

Claude Code can run these steps for you.  Tell it to follow `CONTRIBUTING.md`, and to propose each commit message and wait for your approval.  Some prompts that work:

- Starting: "Read issue 42, check git status, pull main, then create a branch for it."
- Finishing: "Run the checks, propose a commit message, and once I approve it, push the branch and open a pull request with the template filled in and Closes #42."
- After a merge: "Switch to main, pull, and delete my local branch."

You are still responsible for every line it writes: read the changes before you commit them.
