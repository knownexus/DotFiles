# dotfiles

One repo for both shell setups — zsh (Linux/Mac) and pwsh (Windows) — merged
from the formerly-separate [knownexus/zsh](https://github.com/knownexus/zsh)
and [knownexus/pwsh](https://github.com/knownexus/pwsh) repos (full commit
history preserved via `git subtree`).

```
dotfiles/
  bootstrap.sh      # deploy the zsh side (Linux/Mac)
  bootstrap.ps1     # deploy the pwsh side (Windows)
  shared/
    commands.yaml   # single source of truth for the git-aliases cheatsheet,
                     # read by both sides at runtime via yq — no more hand-
                     # copying a table between two repos every time it changes
  zsh/              # zsh config — see zsh/GUIDE.md
  pwsh/             # pwsh config — see pwsh/GUIDE.md
```

Each shell's *implementation* stays separate — a zsh function and its pwsh
equivalent are genuinely different code, and porting one into the other
wouldn't make either simpler. What's shared is the *data* that used to drift
out of sync between hand-maintained copies: the git-aliases cheatsheet table.

## Setup

Pick the one that matches this machine:

```sh
./bootstrap.sh          # Linux/Mac — symlinks ~/.resources, wires ~/.zshrc, installs all dependencies
```

```powershell
.\bootstrap.ps1         # Windows — wires $PROFILE, installs all dependencies
```

Both are safe to re-run, and both install everything the config uses —
required (`git`, `yq` — mikefarah/yq, same binary and query language on both
platforms) and optional (`vim`, `less`, `ripgrep`, `fzf`, `bat`, `glow`,
`zoxide`, `direnv`, and more) — via each platform's package manager
(dnf/apt/brew on Linux/Mac, winget on Windows), so nothing has to be
installed by hand afterward.

## Why one repo

Keeping zsh and pwsh in separate repos meant every change made on one side
(a bugfix, a new alias, a QoL improvement) had to be manually re-applied on
the other, entry by entry — which is exactly how several of the bugs fixed
in these configs' history got introduced in the first place: the two copies
of the git-aliases table, and the two copies of each fix, drifted. Moving the
actual duplicated *data* into `shared/commands.yaml` removes that class of
bug at the source, without forcing either shell into an unnatural one-size-
fits-all implementation.
