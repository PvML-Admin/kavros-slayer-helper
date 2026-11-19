# Kavros Slayer Helper

A Companion module for the Kavros client, created by **Meleeman112**. It docks a small panel over the
top-left of `kavrosclient.exe`, reads your Slayer task from the chatbox and shows the kills left, who
assigned it, where to go, how to fight it, and your streak and points.

Players only install Kavros Companion. Companion lists **Slayer helper** in its hub, downloads this app
from this repository's [Releases](https://github.com/PvML-Admin/kavros-slayer-helper/releases)
(`KavrosSlayerHelper-win-Portable.zip` → `current/KavrosSlayerHelper.exe`) and keeps it up to date.

It only reads the screen (like Alt1 and Zenity's Clue Helper). It never reads game memory, packets or
sends input to the game.

## The panel

- **Full** and **compact** modes (the *Compact* / *Expand* button) and a minimise button that folds the
  panel to its header. The mode is remembered.
- Shown only while the Kavros client (or the panel itself) is the foreground window; it follows the client
  when it moves or resizes. It never takes focus from the game, except while you type a count (below).
- On screen from startup. No tray icon. One copy at a time (mutex `Local\KavrosSlayerHelper`).
- **Rescan** re-reads everything visible in the chatbox now (and finds the chatbox again).
  **Clear** forgets the current task (the streak and points stay).

### Setting the count by hand

Kavros may not print how many kills are left, so the count can always be set or nudged:

- Click the number to type a new count, then Enter (Esc cancels; Up/Down step it). Focus goes back to the
  game when you finish.
- Hover the task to show small − / + buttons, or use the mouse wheel over it (one kill per notch).
- If the assignment message has no amount, the panel says *left · click to set* until you set it.

## Reading chat

The chat reader captures the game window, finds the chatbox (the bottom-left area first, then the whole
window, anchored on Slayer lines or on timestamped lines), cleans the pixels and runs Windows' built-in text
recognition (`Windows.Media.Ocr`, Windows 10 or 11, English language pack). This is the approach Zenity's
Clue Helper uses (MIT; see `THIRD_PARTY.txt`). Each read is compared with the last so a message is only
counted once, even when it stays on screen; text inside the panel itself is ignored.

Recognised out of the box (RS3 wording, from the wiki's Slayer master transcripts):

| Kind | Example |
|---|---|
| Assignment | `Your new task is to kill 187 abyssal demons.` (any master's lead-in), `We'll start you off hunting cows. You'll need to kill 18 of them.`, `You need to kill 22 crawling hands.`, and `Your new task is to kill abyssal demons.` with no amount |
| Progress | `You're currently assigned to kill abyssal demons; only 142 more to go.` (enchanted gem / master), `You still need to kill 120 monsters to complete your current Slayer assignment` (combat bracelet), `You're still hunting abyssal demons; ...` |
| Complete | `You've completed 47 tasks in a row and gain 18 points.`, `...received 270 slayer points ... total of 1,510 points`, the Raptor's special assignment, `You need something new to hunt.` |
| Kill | none by default (see below) |

A `Name:` speaker at the start of the line sets the master. Otherwise the master is only filled in when just
one master assigns that monster in that amount. Points not shown in chat come from the master's table
(every 10th and 50th task included).

**Counting down.** The count is the assigned amount, then any progress line or configured kill line updates
it, and you can always correct it by hand. Kavros' own kill or task messages are not known yet: add their
wording to `chat.kill` in the override file (below) and each match counts one kill down, or sets the count if
the pattern captures `(?<left>…)`.

### Limits

- Needs Windows 10/11 text recognition with English installed. Without it the panel still works, by hand.
- OCR reads what is on screen: a chat tab that is filtered, scrolled up, collapsed or covered by another
  interface is not read. Very small or transparent chat text reads worse; a solid chat background helps.
- Two identical kill lines in a row with nothing between them can look like one line that did not move.
  Turning on chat timestamps in the client makes every line unique.
- The panel cannot know about kills made while it was closed, so check the count after a restart.
- Patterns are matched against OCR text, which can drop or swap a character; the cleaner fixes common
  digit mix-ups (O/0, l/1).

## Files

Everything lives in `%AppData%\KavrosSlayerHelper`:

| File | What |
|---|---|
| `state.json` | the current task, count, master, streak, points |
| `settings.json` | `compact`, `minimised`, `offsetX`/`offsetY` (panel offset in the game area, default 12), `scanMs` (chat read interval, default 1000), `chatRegion` (`[x, y, w, h]` as fractions of the game area, to fix where the chatbox is), `processName` (default `kavrosclient`), `windowTitle`, `titleBarHeight`, `hideFromCapture` |
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
dotnet run -c Release --project tests/UnitTests     # unit tests (chat parsing, merge, countdown)
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
