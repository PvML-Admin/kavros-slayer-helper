# Kavros Slayer Helper

A Companion module for the Kavros client. It reads your slayer task from chat, counts the remaining kills, and shows where to go, what to use, and your streak and points.

Players only install Kavros Companion. Companion downloads this app from the [Releases](https://github.com/PvML-Admin/kavros-slayer-helper/releases) on this repository and keeps it up to date.

## Status

The look of the panel is being reviewed. There is no release yet.

## What it will do

- Read assignment messages from chat, such as "You're assigned to kill X; only N more to go", and the usual RS3 slayer master messages
- Count remaining kills from chat or kill messages
- Show the monster, the count left, its best locations, recommended combat style or gear, weaknesses, and whether a special item is needed (for example a mirror shield or earmuffs)
- Track the task streak and points
- A compact mode for a smaller panel

It only reads the screen. It does not use game memory or packets.

## Data

Monster lists, locations, weaknesses, special items and point rewards start from the standard RS3 lists on the [RuneScape Wiki](https://runescape.wiki/). Kavros is a private server, so spawns, masters or task lists may differ. The data is kept in a simple, editable file so any Kavros-specific location or task can be overridden later without changing the rest of the app.

## For Companion

This repository is public so Companion can check releases without signing in. When a release is published, the portable zip will follow the same convention as other linked apps.
