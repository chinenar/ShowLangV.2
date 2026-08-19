# Recovered development history

The repository was created after the application had already gone through several local iterations. The available source folders were converted into chronological Git commits and lightweight tags so the earlier states can be inspected without keeping duplicate project folders in the working tree.

| Tag | Recovered state |
| --- | --- |
| `legacy-ahk` | Original AutoHotkey implementation and startup backup |
| `snapshot-before-latency-fix` | Partial native prototype preserved before the latency/caret work |
| `snapshot-before-pause` | Full native project before tray Pause/Resume controls |
| `snapshot-before-search-position` | Native build with Pause/Resume before Windows Search placement changes |
| `snapshot-before-appearance` | Build before adjustable size and transparency controls |
| `snapshot-before-box-alpha` | Build before box-only per-pixel transparency |
| `v2-current` | Current organized source, tools, scripts, and documentation |

## Inspecting an older state

```powershell
git show snapshot-before-pause
```

To browse an older version without changing the current branch:

```powershell
git worktree add ..\ShowLang-old snapshot-before-pause
```

Remove the temporary worktree afterward with:

```powershell
git worktree remove ..\ShowLang-old
```
