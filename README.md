# Kavros Slayer Helper

A Companion module for the Kavros client, created by **Meleeman112**. It docks a small panel over the
top-left of `kavrosclient.exe`, reads your Slayer task from the RS3 slayer counter and the slayer master's
dialogue, and shows the kills left, who assigned it, where to go, how to fight it, and your streak and points.

Players only install Kavros Companion. Companion lists **Slayer helper** in its hub, downloads this app
from this repository's [Releases](https://github.com/PvML-Admin/kavros-slayer-helper/releases)
(`KavrosSlayerHelper-win-Portable.zip` → `current/KavrosSlayerHelper.exe`) and keeps it up to date.

It only reads the screen (like Alt1 and Zenity's Clue Helper). It never reads game memory, packets or
sends input to the game.

## The panel

- **Full** and **compact** modes (the *Compact* / *Expand* button) and a minimise button (`–`, which turns
  into `+` to expand again) that folds the panel to its header. The mode is remembered.
- **Drag it** by the header or any empty part of the panel. The position is remembered (as an offset inside
  the game, so it moves with the client) and always kept inside the game area on screen.
- Visible whenever the Kavros client is on screen, focused or not (clicking Companion, AFK Warden, Clue
  Helper or another app doesn't hide it); it sits directly above the client in the window order, so other
  apps you bring to the front cover it, and it is never "always on top" over them. Hidden only while the
  client is minimised or closed. It never takes focus from the game, except while you type a count (below).
- On screen from startup. No tray icon. One copy at a time (mutex `Local\KavrosSlayerHelper`). Switching it
  off in Companion (WM_CLOSE) hides it immediately and exits.
- **Rescan** looks for the slayer counter again and re-reads an open master dialogue.
  **Clear** forgets the current task (the streak and points stay).
- Right-click the header for **Save debug capture**: the last full capture, the counter crop and a report
  of what was detected go to `%AppData%\KavrosSlayerHelper\debug` (send these if it misreads).

### Setting the count by hand

Kavros may not print how many kills are left, so the count can always be set or nudged:

- Click the number to type a new count, then Enter (Esc cancels; Up/Down step it). Focus goes back to the
  game when you finish.
- Hover the task to show small − / + buttons, or use the mouse wheel over it (one kill per notch).
- If the assignment message has no amount, the panel says *left · click to set* until you set it.

## Reading the counter and the master's dialogue

RS3 doesn't put the task in the chatbox. The helper captures the game window and runs Windows' built-in text
recognition (`Windows.Media.Ocr`, Windows 10 or 11, English language pack) on two widgets, the approach
Zenity's Clue Helper uses (MIT; see `THIRD_PARTY.txt`):

- **The slayer counter** (the dark box with the yellow kills-left number, the task name, *Slayer points* and
  *Task streak*). It is the source of truth for the count, the points and the streak whenever it is on
  screen, even when it shows more than was assigned. It is found anywhere in the client by colour (stacked
  yellow text on a dark box), confirmed by OCR, then only that small region is captured each second. OCR
  runs only when its pixels change, a changed reading must be read the same way twice before it is shown,
  and kills are usually read by matching the big digits against templates learned from earlier reads (no
  OCR at all). If it disappears for a few seconds the last values stay and it is looked for again.
  The number is read by digit shapes taken from real counter screenshots (`src/Core/DigitFont.cs`); a
  digit they don't know is OCR'd on its own and must agree with the whole-box read. A changed count is only
  shown after two frames in a row agree, and only if it went down by a small step (up to 20) or a new task
  appeared; anything else (a misread 99, a jump up) must hold for 8 frames before it replaces the count.
  The panel never counts down by itself while the counter is on screen. The RS3 clock box (`12:34`) right
  above the counter is found and masked out before anything is read, a time is never taken as a number,
  and only the rows from the count down are OCR'd. Points and streak are read by their own digit shapes
  and may only go up (a drop must hold for 8 frames). The total comes only from a new task (the master's
  popup or the first counter read of a task), is never raised past the count by more than a few, and
  never above the data's maximum for the task plus half again; a bogus saved total is repaired on start.
  A changed count is confirmed by a second capture about 0.1 s later, so kills show within about a second. Task names are matched to the
  wiki data with OCR look-alikes allowed (O/D, 0/o, l/1, rn/m); a name that can't be placed is never shown.
  **Minimised**, the counter is just a small dark box with the yellow kills-left number and the slayer skull
  at its top-right; that form is found too (the skull is required, so ability-bar and buff timers aren't
  mistaken for it) and the kills left keep counting down from it, with the last task, points and streak kept.
  A bare number is only used once a task is known (from the expanded counter or a master's dialogue).
- **The slayer master's dialogue** (parchment box, master's name in gold at the top, portrait on the left).
  It is found by its parchment colour every other second and read once per appearance, line wraps undone:
  `Your new task is to kill 125 Dragons.`, `Your task is to kill …`, `… kill 40 more Dragons` (progress), the
  wiki's other assignment wordings, and a known master's name sets the master. The patterns in the data's
  `chat` section (overridable, below) are matched against the dialogue text.

Without the counter, the count comes from the dialogue and your own corrections (below). The task name
(`Dragons`, `DRAGON`) is matched to the wiki categories, singular or plural. The panel itself is never read.

### Limits

- Needs Windows 10/11 text recognition with English installed. Without it the panel still works, by hand.
- A counter covered by another interface (or the panel) isn't read until it's visible again.
- The colour cues were tuned on screenshots; unusual interface scaling or transparency may need the
  `counterRegion` setting to pin where the counter is.
- The panel cannot know about kills made while it was closed, so check the count after a restart.

## Files

Everything lives in `%AppData%\KavrosSlayerHelper`:

| File | What |
|---|---|
| `state.json` | the current task, count, master, streak, points |
| `settings.json` | `compact`, `minimised`, `offsetX`/`offsetY` (panel offset in the game area, default 12, set by dragging), `freeX`/`freeY` (position when no client is open), `scanMs` (read interval, default 1000), `counterRegion` (`[x, y, w, h]` as fractions of the game area, to pin where the slayer counter is), `processName` (default `kavrosclient`), `windowTitle`, `titleBarHeight`, `hideFromCapture` |
| `slayer.override.json` | your changes to the data (below); reloaded as soon as it is saved |
| `slayer-helper.log` | a short log |

## Data

`data/slayer.json` is built into the exe. It is generated by `tools/build_data.py` from the
[RuneScape Wiki](https://runescape.wiki/) (RS3) through its API, politely (cached, one request every
0.6 s, with a descriptive User-Agent): `Module:Slayer assignments/<master>` for every master's task list,
`Template:Slayer masters/table` for requirements and points, the `infobox_monster` bucket for each
category's monsters, Slayer level, combat level and weakness, and each monster page's location table.
Wiki text is CC BY-NC-SA 3.0.

Coverage: **12 masters** (Turael/Spria, Jacquelyn, Vannaka, The Raptor, Mazchna/Achtryn, Chaeldar,
Sumona, Duradel/Lapalok, Kuradal, Morvran, Laniakea, Mandrith), **427 task entries**, **162 Slayer
categories** with **646 locations**.

Kavros is a private server, so spawns, masters and task lists may differ. Fix them in the override file;
the rest of the data stays as shipped.

### Format

```jsonc
{
  "version": 1,
  "masters": {
    "kuradal": {
      "name": "Kuradal", "aliases": [],
      "combat": 110, "slayer": 75, "quest": "Barbarian Training", "location": "Ancient Cavern",
      "points": { "task": 18, "every10": 90, "every50": 270 },
      "tasks": {
        "Abyssal demons": { "min": 150, "max": 250, "weight": 10, "slayer": 85, "combat": 0, "unlock": "Morytania access" }
      }
    }
  },
  "monsters": {
    "Abyssal demons": {
      "slayer": 85, "combat": 98,
      "weakness": "Slash", "style": "Melee (slash)", "gear": "Noxious scythe or Dragon Rider lance",
      "special": "",                       // a required item, e.g. "Mirror shield"; "" shows "None needed"
      "aliases": ["abyssal demon"],        // other names chat may use
      "eligible": ["Abyssal demon", "Abyssal lord"],
      "locations": [
        { "name": "Kuradal's Dungeon", "requirements": "", "notes": "", "spawns": 10,
          "monster": "",                   // set when the spawns are another eligible monster
          "recommended": true,             // optional: show first, tagged "Recommended"
          "tag": "Fairy ring ALR" }        // optional: the text on the right
      ]
    }
  },
  "chat": { "assign": ["..."], "progress": ["..."], "complete": ["..."], "kill": [] }
}
```

Without `recommended`, the first location of the task's own monster with no requirement and outside the
Wilderness is shown first. `gear` is a generic suggestion per combat style, not wiki data. The shown
locations are the first three.

### The override file

`%AppData%\KavrosSlayerHelper\slayer.override.json` has the same shape and is merged over the built-in
data. Comments and trailing commas are allowed.

- Objects merge key by key (names are case-insensitive); anything you leave out keeps the shipped value.
- `null` deletes a key: `"masters": { "turael": null }`.
- Arrays replace the shipped array; a key ending in `+` puts your items **in front** instead:
  `"locations+": [ ... ]`, `"kill+": [ ... ]`.

```jsonc
{
  "masters": { "kuradal": { "points": { "task": 20 } } },
  "monsters": {
    "Abyssal demons": {
      "locations+": [ { "name": "Kavros Abyss", "recommended": true, "tag": "Teleport tab" } ]
    }
  },
  "chat": {
    // .NET regular expressions, case-insensitive. Named groups: monster, count, left, streak, points, total.
    "kill+":   [ "you have slain an? (?<monster>[a-z ]+)" ],
    "assign+": [ "your slayer task: (?<count>\\d+) x (?<monster>[^.]+)" ]
  }
}
```

A pattern that does not compile is skipped and logged; the others keep working.

## Building

The exe is .NET Framework 4.8 WinForms (C# 7.3) and builds with the .NET SDK on Windows or Linux:

```sh
dotnet run -c Release --project tests/UnitTests     # unit tests (dialogue/counter parsing, detection on tests/fixtures, merge, countdown)
tools/release.sh                                     # out/KavrosSlayerHelper.exe + dist/KavrosSlayerHelper-win-Portable.zip
python3 tools/build_data.py                          # regenerate data/slayer.json from the wiki
```

Renders of the panel are in `docs/screenshots/`.

`KavrosSlayerHelper.exe --render panel.png [--mode full|compact|minimised|empty] [--scale 2] [--hover]`
draws the panel with an example task into a PNG; `--attach "<title>"` docks over any window (for testing),
`--demo` shows an example task.

Pushing a `v*` tag runs `.github/workflows/release.yml`, which tests, builds and attaches
`KavrosSlayerHelper-win-Portable.zip` (plus its SHA-256) to that tag's release.

## Companion

`companion.module.json` describes the module (id `slayer-helper`). Companion's built-in linked-apps list has
the matching entry (owner `PvML-Admin`, repo `kavros-slayer-helper`, asset
`KavrosSlayerHelper-win-Portable.zip`, exe `current/KavrosSlayerHelper.exe`).
