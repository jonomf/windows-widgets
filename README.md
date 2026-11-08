# Jono's Widgets (Windows)

Windows 11 versions of three of the Android widgets, for the **Widgets board** (Win+W):

- **Todoist**: tasks from one project, saved filter or custom query, most overdue first (or priority
  first). Pin two: work and personal. Tap ○ to complete a task, tap a task to open it in Todoist.
  Overdue count in red, then the total.
- **Repos**: your GitHub repos, most recently pushed first, with each one's latest CI run
  (✓ passed, ✗ failed, ● running) and commit. Tap a repo to open it, tap its mark to open the run.
- **Now**: say what you're doing now with one tap, logged to **Toggl Track** (the Android Now widget
  keeps its log on the phone; Toggl is where the two can meet). Same rules as on the phone:
  tap a different activity to switch; tap the right one within a minute to fix a mistake;
  tap the current one again to move its start back 15 minutes. Large size adds today's totals.

Every widget comes in small, medium and large (the Widgets board's only sizes), and bigger sizes
show more. Each widget's top row has ⟳ (refresh) and ⚙ (settings), plus ↑ when a newer build is
out (tap it to download).

## Install

1. Download **JonoWidgets.zip** from [the latest release](https://github.com/jonomf/windows-widgets/releases/latest)
   and extract it.
2. Double-click **Install.cmd**. The first time, Windows asks for admin permission: that trusts the
   certificate the app is signed with (`jono-widgets.cer`), which Windows requires for apps from
   outside the Store. It also installs the Windows App Runtime the app needs.
3. Press **Win+W** → **+** → **Jono's Widgets** → pin Todoist, Repos and/or Now.
4. Each widget asks for its token the first time:
   - Todoist: Settings → Integrations → Developer → API token.
   - GitHub: a fine-grained token, all repositories, read-only **Contents** and **Actions**
     (include `windows-widgets` so the ↑ update symbol works).
   - Toggl: Profile settings → API Token.

Tokens are stored encrypted for your Windows account (DPAPI) in the app's own data folder and only
sent to their own service.

**Updating:** download **JonoWidgets.msix** from the latest release and double-click it → Update.

## Notes
- Toggl's free plan allows about 30 API calls an hour, so Now only calls Toggl when you tap and every
  ~20 minutes while the board is open; elapsed time is counted locally.
- The board fetches data while it's open: Todoist every 10 min, Repos every 5 (every minute while a
  build is running), and when you open the board if the data is a couple of minutes old.
- Log for debugging: `%LOCALAPPDATA%\Packages\Jono.Widgets_*\LocalState\log.txt`.

## How it works
- `src/JonoWidgets` is one packaged (MSIX) C# app, .NET 8 + Windows App SDK. The Widgets board
  starts it as a COM server (`-RegisterProcessAsComServer`) and calls `WidgetProvider`.
- `Hub` keeps the pinned widgets; each widget (`Widgets/*.cs`) builds its whole Adaptive Card in code
  (`AC.cs`) for its current size and keeps its settings in the board's per-widget custom state.
- `Services/` has the Todoist (API v1), GitHub and Toggl (API v9) calls, settings, the log and the
  update check.
- `tools/make_assets.py` draws the icons and widget-picker screenshots.
- Every push to `main` builds a signed MSIX on GitHub Actions and publishes release `build-N`
  (package version 1.0.N.0). `signing/` holds the self-signed certificate (subject `CN=Jono Widgets`,
  which must match the manifest's Publisher). The private key `signing/jono-widgets.pfx` is only committed while the repo is private; without it CI just compiles.
