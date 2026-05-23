# pwsh

PowerShell equivalent of [knownexus/zsh](https://github.com/knownexus/zsh) — same structure, same aliases, same logic, ported to PowerShell 5.1+ / PowerShell 7+.

## Setup

### 1. Allow scripts to run

PowerShell blocks scripts by default. Run this once in an elevated (Admin) or regular PowerShell window:

```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### 2. Clone the repo

```powershell
git clone https://github.com/<your-username>/pwsh "C:\repos\pwsh"
```

### 3. Hook into your profile

Run this for **both** Windows PowerShell 5.1 and PowerShell 7 (run once in each):

```powershell
New-Item -ItemType File -Force $PROFILE
Add-Content $PROFILE '. "C:\repos\pwsh\Profile.ps1"'
```

> If you cloned to a different location, replace `C:\repos\pwsh` with your actual path.

### 4. Activate

Either open a new PowerShell window, or reload your current session:

```powershell
. $PROFILE
```

---

### 4. Personalise

Open `profile.d\11-aliases.ps1` and update the navigation shortcuts to match your own machine:

```powershell
function global:rp  { Set-Location 'C:\your\repos' }
function global:rpa { Set-Location 'C:\your\repos\SomeProject' }
```

---

## What's included

```
Profile.ps1          — entry point (sourced from $PROFILE)
profile.d/
  00-window-title.ps1  — terminal title stack (Push-Title / Pop-Title / Invoke-Entitled)
  10-ls.ps1            — l / ll / la / lla
  11-aliases.ps1       — g / g1 / g2 search, cd - (back), cdh, rp, v, rmr, md, remspace, tre
  12-git.ps1           — full git alias set (see below)
  20-ssh.ps1           — ssh / ssx wrappers with title integration
  30-settings.ps1      — PSReadLine history, emacs bindings, incremental save
  40-prompt.ps1        — VCS-aware two-line prompt (git / svn, shows ?ADRM flags)
  50-completions.ps1   — menu-complete, history prediction, posh-git (if installed)
  60-locals.ps1        — PATH additions (~/bin, cargo, cabal, go, volta, ...)
  70-variables.ps1     — VISUAL / EDITOR defaults
  80-functions.ps1     — mcd, odx, fch, fchs, fr, New-GitProject / make-project
  81-key-bindings.ps1  — Ctrl+left/right word nav, up/down history search, smart Ctrl+D
```

---

## Git aliases

| Alias | Command |
|-------|---------|
| `gstat` / `g!` | `git status` |
| `ga` / `gita` | `git add` |
| `gc` | `git commit` |
| `gitcm` | `git commit -m` |
| `gitca` | `git commit --amend` |
| `uncommit` | `git reset --soft HEAD^` |
| `gdf` | `git diff` |
| `gdfs` | `git diff --staged` |
| `gdf1` | `git diff HEAD~1` |
| `gl` / `gitl` | `git log` |
| `gitlg` | `git log --graph` (pretty) |
| `gs` / `gits` | `git show` |
| `gitsn` | `git show --name-only` |
| `gbl` | `git blame` |
| `gitb` | `git branch` |
| `gitc` | `git checkout` |
| `gitcb` | `git checkout -b` |
| `gitcd` | `git checkout develop` |
| `gres` / `gitres` | `git checkout --` |
| `gress` | `git restore --staged` |
| `gp` | `git stash pop` |
| `gst` | `git stash push` |
| `stash` | `git stash save` |
| `gpf` / `gitfp` | `git push --force` |
| `gp1` | push current branch and set upstream |
| `pushdr <branch>` | push current branch to named remote branch |
| `gru` | `git remote update` |
| `grpo` | `git remote prune origin` |
| `giturl` | print remote origin URL |
| `remote!` | git reset --hard origin/{branch} | hard reset to remote HEAD |
| `grd` | `git rebase develop` |
| `grm` | `git rebase master` |
| `gro` | `git rebase origin` |
| `grod` | `git rebase origin/develop` |
| `gri <n>` | interactive rebase last n commits |
| `grim <n>` | interactive rebase (preserve merges) last n commits |
| `groot` | interactive rebase from root |
| `gitra` | `git rebase --abort` |
| `gitrc` | `git rebase --continue` |
| `gup` | `git reset HEAD~1` |
| `gitrmc` | `git rm --cached` |
| `gaw` | apply whitespace-ignoring diff to index |
| `ignoreme` | `git update-index --assume-unchanged` |
| `dontignoreme` | `git update-index --no-assume-unchanged` |
| `update` | remote update + rebase origin |
| `updated` | remote update + rebase origin/develop |
| `rs` | clear screen + `git status` |

---

## Key differences from the zsh version

| zsh | PowerShell | Notes |
|-----|-----------|-------|
| `g!` / `git!` | `gstat` | `!` is not a valid function-name character in PS |
| `remote!` | `gitremote-reset` | same reason |
| `alias foo='...'` | `function` / `Set-Alias` | PS aliases cannot carry arguments |
| zsh-syntax-highlighting | PSReadLine built-in syntax colouring | no extra module needed |
| Right-side prompt (`RPS1`) | VCS info printed on the line above the prompt | PowerShell has no native right-prompt |
| `cd -` | `cd -` | implemented via `$OLDPWD` tracking |

---

## Optional dependencies

- **PSReadLine** — ships with PowerShell 5.1+ and PS7; unlocks history dedup, key bindings, and predictions.
- **posh-git** — `Install-Module posh-git` — rich git tab completion, auto-loaded by `50-completions.ps1` if present.
