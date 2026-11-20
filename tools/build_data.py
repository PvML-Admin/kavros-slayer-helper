#!/usr/bin/env python3
"""Builds data/slayer.json from the RuneScape Wiki (RS3), using its API politely.

    python3 tools/build_data.py [--cache DIR]

Sources (all read through https://runescape.wiki/api.php):
  * Module:Slayer assignments/<Master>   task lists: amount range, weight, unlock requirements
  * Template:Slayer masters/table        master requirements and points per task / 10th / 50th
  * Bucket infobox_monster               eligible monsters, Slayer level, combat level, weakness
  * <monster> pages, ==Locations==       {{LocLine}} rows: location, requirements, spawn count
Responses are cached in --cache (default .wiki-cache) so re-runs do not hit the wiki again.
Special items, styles and gear suggestions come from the tables at the bottom of this file,
written from the wiki's "Slayer equipment" page.
"""
import json, os, re, sys, time, urllib.parse, urllib.request
from collections import Counter, OrderedDict

API = "https://runescape.wiki/api.php"
UA = "KavrosSlayerHelperDataBuilder/1.0 (https://github.com/PvML-Admin/kavros-slayer-helper)"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CACHE = os.path.join(ROOT, ".wiki-cache")
if "--cache" in sys.argv:
    CACHE = sys.argv[sys.argv.index("--cache") + 1]
os.makedirs(CACHE, exist_ok=True)
_last = [0.0]


def api(params):
    key = re.sub(r"[^A-Za-z0-9._-]+", "_", "_".join("%s=%s" % kv for kv in sorted(params.items())))[:180]
    path = os.path.join(CACHE, key + ".json")
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    wait = 0.6 - (time.time() - _last[0])          # at most ~2 requests a second
    if wait > 0:
        time.sleep(wait)
    url = API + "?" + urllib.parse.urlencode(dict(params, format="json", formatversion="2"))
    for attempt in range(4):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=40) as r:
                data = json.loads(r.read().decode("utf-8"))
            break
        except Exception as e:  # noqa: BLE001
            if attempt == 3:
                raise
            time.sleep(3 * (attempt + 1))
    _last[0] = time.time()
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f)
    return data


def wikitext(title):
    d = api({"action": "parse", "page": title, "prop": "wikitext", "redirects": "1"})
    return (d.get("parse") or {}).get("wikitext", "")


# ---------------------------------------------------------------- wikitext cleanup
def strip_wiki(s):
    if not s:
        return ""
    s = re.sub(r"<ref[^>]*/>", "", s)
    s = re.sub(r"<ref[^>]*>.*?</ref>", "", s, flags=re.S)
    s = re.sub(r"<[^>]+>", "", s)
    s = re.sub(r"\[\[File:[^\]]*\]\]", "", s)
    # {{Skillreq|Agility|71}} / {{scm|Agility|71}} -> 71 Agility
    s = re.sub(r"\{\{\s*(?:Skillreq|scm|SCP|Scm)\s*\|\s*([^|}]+?)\s*\|\s*(\d+)[^}]*\}\}", r"\2 \1", s, flags=re.I)
    s = re.sub(r"\{\{\s*(?:plink|plinkp|l)\s*\|\s*([^|}]+)[^}]*\}\}", r"\1", s, flags=re.I)
    s = re.sub(r"\{\{\s*fairycode\s*\|\s*([^|}]+)\}\}", r"Fairy ring \1", s, flags=re.I)
    s = re.sub(r"\{\{\s*(?:sic|NA|\*)\s*\}\}", "", s, flags=re.I)
    for _ in range(3):
        s = re.sub(r"\{\{[^{}]*\}\}", "", s)
    s = re.sub(r"\[\[[^\]|]*\|([^\]]*)\]\]", r"\1", s)
    s = re.sub(r"\[\[([^\]]*)\]\]", r"\1", s)
    s = re.sub(r"\[https?://\S+ ([^\]]*)\]", r"\1", s)
    s = s.replace("'''", "").replace("''", "")
    s = re.sub(r"\s+", " ", s).strip(" ,;")
    return s


# ---------------------------------------------------------------- Lua module parsing
def lua_table(src):
    """Parses the `return { ["X"] = { min = 1, ... }, ... }` modules into Python dicts."""
    tok = re.compile(r"\[\s*\"(?P<key>(?:[^\"\\]|\\.)*)\"\s*\]|'(?P<sq>(?:[^'\\]|\\.)*)'|\"(?P<dq>(?:[^\"\\]|\\.)*)\"|(?P<num>-?\d+(?:\.\d+)?)|(?P<ident>[A-Za-z_][A-Za-z0-9_]*)|(?P<p>[{}=,])|(?P<c>--[^\n]*)")
    items = []
    for m in tok.finditer(src):
        g = m.lastgroup
        v = m.group(g)
        if g == "c":
            continue
        if g in ("key", "sq", "dq"):
            v = v.replace("\\'", "'").replace('\\"', '"')
            items.append(("key" if g == "key" else "str", v))
        elif g == "num":
            items.append(("num", float(v) if "." in v else int(v)))
        elif g == "ident":
            items.append(("ident", v))
        else:
            items.append(("p", v))
    pos = [0]

    def peek():
        return items[pos[0]] if pos[0] < len(items) else ("eof", None)

    def take():
        t = peek(); pos[0] += 1; return t

    def value():
        t = take()
        if t == ("p", "{"):
            obj = OrderedDict(); arr = []
            while peek() != ("p", "}"):
                k = peek()
                if k[0] in ("key", "ident") and pos[0] + 1 < len(items) and items[pos[0] + 1] == ("p", "="):
                    take(); take(); obj[k[1]] = value()
                else:
                    arr.append(value())
                if peek() == ("p", ","):
                    take()
            take()
            return obj if obj or not arr else arr
        if t[0] in ("str", "num"):
            return t[1]
        if t[0] == "ident":
            return {"true": True, "false": False, "nil": None}.get(t[1], t[1])
        raise ValueError("unexpected %r" % (t,))

    while peek() != ("eof", None) and peek() != ("p", "{"):
        take()
    return value()


# ---------------------------------------------------------------- masters
MASTERS = [  # id, module name, display name, aliases (disciples who give the same tasks)
    ("turael", "Turael", "Turael", ["Spria"]),
    ("jacquelyn", "Jacquelyn", "Jacquelyn", []),
    ("vannaka", "Vannaka", "Vannaka", []),
    ("the-raptor", "The Raptor", "The Raptor", ["Raptor"]),
    ("mazchna", "Mazchna", "Mazchna", ["Achtryn"]),
    ("chaeldar", "Chaeldar", "Chaeldar", []),
    ("sumona", "Sumona", "Sumona", []),
    ("duradel", "Duradel", "Duradel", ["Lapalok"]),
    ("kuradal", "Kuradal", "Kuradal", []),
    ("morvran", "Morvran", "Morvran", []),
    ("laniakea", "Laniakea", "Laniakea", []),
    ("mandrith", "Mandrith", "Mandrith", []),
]


def master_table():
    src = wikitext("Template:Slayer masters/table")
    rows = {}
    for block in src.split("\n|-")[1:]:
        lines = [l for l in block.split("\n") if l.startswith("|") and not l.startswith("|}")]
        if not lines:
            continue
        names = re.findall(r"\[\[([^\]|]+)\]\]", lines[0])
        if not names:
            continue
        cells = []
        for l in lines[1:]:
            c = l[1:]
            while re.match(r'^\s*[a-z-]+="[^"]*"\s*\|', c):
                c = re.sub(r'^\s*[a-z-]+="[^"]*"\s*\|', "", c, count=1)
            cells.append(c.strip())
        cells = [c for c in cells if "LeagueRegion" not in c]
        if len(cells) < 7:
            continue
        loc, cb, sl, quest, p1, p10, p50 = cells[-7:]

        def num(c):
            m = re.findall(r"(\d+)", strip_wiki(re.sub(r"<ref.*", "", c, flags=re.S)))
            return int(m[-1]) if m else 0
        mloc = re.search(r"text=([^|}]+)", loc)
        rows[names[0]] = {
            "location": mloc.group(1).strip() if mloc else strip_wiki(loc),
            "combat": num(cb) if "Combat" in cb else 0,
            "slayer": num(sl) if "Slayer" in sl else 0,
            "quest": strip_wiki(re.sub(r"<ref.*", "", quest, flags=re.S)),
            "points": {"task": num(p1), "every10": num(p10), "every50": num(p50)},
        }
    return rows


# ---------------------------------------------------------------- tables written from the wiki
# Special items (Slayer equipment page and the monsters' own pages). Category -> text.
SPECIAL = {
    "Aberrant spectres": "Nose peg (or slayer helmet)",
    "Banshees": "Earmuffs (or slayer helmet)",
    "Mighty banshees": "Earmuffs (or slayer helmet)",
    "Basilisks": "Mirror shield",
    "Cockatrice": "Mirror shield",
    "Dust devils": "Face mask (or slayer helmet)",
    "Smoke devils": "Face mask (or slayer helmet)",
    "Gargoyles": "Rock hammer to finish them",
    "Rockslugs": "Bag of salt to finish them",
    "Desert lizards": "Ice cooler to finish them",
    "Lizards": "Ice cooler to finish them",
    "Zygomites": "Fungicide spray to finish them",
    "Mutated zygomites": "Fungicide spray to finish them",
    "Wall beasts": "Spiny helmet (or slayer helmet)",
    "Cave crawlers": "",
    "Turoth": "Leaf-bladed weapon, broad arrows/bolts or Magic Dart",
    "Kurask": "Leaf-bladed weapon, broad arrows/bolts or Magic Dart",
    "Killerwatts": "Insulated boots",
    "Harpie bug swarms": "Lit bug lantern",
    "Molanisks": "Slayer bell to lure them",
    "Cave horrors": "Witchwood icon",
    "Mogres": "Fishing explosives to lure them",
    "Vampyres": "Ivandis flail, blisterwood weapon or stake",
    "Terror dogs": "",
    "Crystal shapeshifters": "Crystal chime",
    "Skeletal wyverns": "Elemental, mind or dragonfire shield",
    "Ice strykewyrms": "Fire cape (or TokHaar-Kal) recommended",
    "Desert strykewyrms": "",
    "Jungle strykewyrms": "",
    "Dark beasts": "",
    "Cave slimes": "Light source",
    "Cave bugs": "Light source",
    "Lava strykewyrms": "",
    "Black dragons": "Dragonfire protection",
    "Blue dragons": "Dragonfire protection",
    "Green dragons": "Dragonfire protection",
    "Red dragons": "Dragonfire protection",
    "Bronze dragons": "Dragonfire protection",
    "Iron dragons": "Dragonfire protection",
    "Steel dragons": "Dragonfire protection",
    "Mithril dragons": "Dragonfire protection",
    "Adamant dragons": "Dragonfire protection",
    "Rune dragons": "Dragonfire protection",
    "Gemstone dragons": "Dragonfire protection",
    "Living wyverns": "Dragonfire protection",
    "Celestial dragons": "Dragonfire protection",
    "Dragons": "Dragonfire protection",
    "Aquanites": "",
    "Grifolapines": "",
    "Grifolaroos": "",
    "Ganodermic creatures": "",
    "Hellhounds": "",
}

STYLE = {
    "stab": "Melee (stab)", "slash": "Melee (slash)", "crush": "Melee (crush)",
    "arrow": "Ranged (arrows)", "bolt": "Ranged (bolts)", "thrown": "Ranged (thrown)",
    "air": "Magic (air spells)", "water": "Magic (water spells)", "earth": "Magic (earth spells)", "fire": "Magic (fire spells)",
    "melee": "Melee", "ranged": "Ranged", "magic": "Magic",
}
# Categories whose monster pages have no location table: written from the wiki's own text
# ("Muspah": "...heading to The Cradle, where three Muspah will continually respawn"; "...a Slayer level of 76";
#  "Volcanic creatures": "...inside the Fight Cave as a special challenge").
MANUAL_LOCATIONS = {
    "Muspah": [{"name": "The Cradle (Freneskae)", "requirements": "Fate of the Gods",
                "notes": "three muspah respawn; the Shard of Zaros keeps them tolerant", "spawns": 3}],
    "Volcanic creatures": [{"name": "Fight Cave (TzHaar City)", "requirements": "Kuradal special challenge",
                            "notes": "the Fight Cave waves count", "spawns": 0}],
}
MANUAL_SLAYER = {"Muspah": 76}

# Generic best-in-slot suggestions per combat class (not from the wiki; editable in the override file).
GEAR = {
    "melee": "Noxious scythe or Dragon Rider lance",
    "ranged": "Noxious longbow or Ascension crossbows",
    "magic": "Noxious staff or Fractured Staff of Armadyl",
    "any": "Any combat style",
}
CLASS = {"stab": "melee", "slash": "melee", "crush": "melee", "melee": "melee",
         "arrow": "ranged", "bolt": "ranged", "thrown": "ranged", "ranged": "ranged",
         "air": "magic", "water": "magic", "earth": "magic", "fire": "magic", "magic": "magic"}
WEAKNESS_FIX = {"slashing": "slash", "stabbing": "stab", "crushing": "crush", "arrows": "arrow", "bolts": "bolt",
                "throwing": "thrown", "thrown weapons": "thrown", "none": "", "nothing": "", "n/a": ""}


def weak_norm(w):
    w = (w or "").strip().lower()
    return WEAKNESS_FIX.get(w, w)


# ---------------------------------------------------------------- locations
def location_section(src):
    """All text under headings that mention locations, up to the next level-2 heading that does not."""
    out, take = [], False
    for line in src.split("\n"):
        h = re.match(r"^(=+)\s*(.*?)\s*=+\s*$", line)
        if h:
            level, title = len(h.group(1)), h.group(2).lower()
            if "location" in title:
                take = True
                continue
            if level <= 2:
                take = False
        if take:
            out.append(line)
    return "\n".join(out)


def loclines(page):
    src = wikitext(page)
    sec = location_section(src)
    if not sec.strip():
        return []
    out = []
    i = 0
    while True:
        j = sec.find("{{LocLine", i)
        if j < 0:
            break
        depth, k = 0, j
        while k < len(sec):
            if sec.startswith("{{", k):
                depth += 1; k += 2; continue
            if sec.startswith("}}", k):
                depth -= 1; k += 2
                if depth == 0:
                    break
                continue
            k += 1
        body = sec[j + 2:k - 2]
        i = k
        fields, spawns = {}, 0
        depth, cur, parts = 0, "", []
        for ch_i, ch in enumerate(body):
            if body.startswith("{{", ch_i) or body.startswith("[[", ch_i):
                depth += 1
            if body.startswith("}}", ch_i) or body.startswith("]]", ch_i):
                depth -= 1
            if ch == "|" and depth <= 0:
                parts.append(cur); cur = ""
            else:
                cur += ch
        parts.append(cur)
        for p in parts[1:]:
            if "=" in p and not p.strip().startswith("npcid:") and not re.match(r"\s*x:", p):
                k2, v = p.split("=", 1)
                fields[k2.strip().lower()] = v.strip()
            elif "x:" in p:
                spawns += 1
        name = strip_wiki(fields.get("loc", fields.get("location", "")))
        if not name:
            continue
        out.append({
            "name": name,
            "requirements": strip_wiki(fields.get("requirements", fields.get("requirement", ""))),
            "notes": strip_wiki(fields.get("notes", fields.get("note", ""))),
            "spawns": spawns,
            "members": fields.get("mem", "").lower() == "yes",
        })
    if not out:
        for h in re.findall(r"^===+\s*(.*?)\s*===+\s*$", sec, flags=re.M):
            name = strip_wiki(h)
            if name and "location" not in name.lower():
                out.append({"name": name, "requirements": "", "notes": "", "spawns": 0, "members": True})
    return out


def main():
    table = master_table()
    masters = OrderedDict()
    categories = OrderedDict()
    for mid, module, name, aliases in MASTERS:
        tasks = lua_table(wikitext("Module:Slayer assignments/" + module))
        meta = table.get(name) or table.get(module) or {}
        mt = OrderedDict()
        for cat, t in tasks.items():
            reqs = t.get("reqs") or {}
            mt[cat] = OrderedDict([("min", t.get("min")), ("max", t.get("max")), ("weight", t.get("weight")),
                                   ("slayer", reqs.get("slayer", 0) or 0), ("combat", reqs.get("combat", 0) or 0),
                                   ("unlock", strip_wiki(reqs.get("other", "")))])
            categories.setdefault(cat, mt[cat]["slayer"])
            categories[cat] = max(categories[cat], mt[cat]["slayer"])
        masters[mid] = OrderedDict([
            ("name", name), ("aliases", aliases),
            ("combat", meta.get("combat", 0)), ("slayer", meta.get("slayer", 0)),
            ("quest", meta.get("quest", "")), ("location", meta.get("location", "")),
            ("points", meta.get("points", {"task": 0, "every10": 0, "every50": 0})),
            ("tasks", mt)])

    rows = api({"action": "bucket", "query": "bucket('infobox_monster').select('page_name','page_name_sub','slayer_category','slayer_level','weakness_text','combat_level','location_restriction').where('slayer_category','!=',bucket.Null()).limit(5000).run()"})["bucket"]
    by_cat = {}
    for r in rows:
        if (r.get("location_restriction") or "") == "removed":
            continue
        cats = r.get("slayer_category") or []
        for idx, c in enumerate(cats):
            by_cat.setdefault(c.lower(), []).append((idx, r))

    monsters = OrderedDict()
    for cat in sorted(categories, key=str.lower):
        found = by_cat.get(cat.lower(), [])
        # Rows of this category outside Dungeoneering and other instances; the category's own pages
        # (listed first) win, but any eligible monster counts (the wiki's category order is not reliable).
        normal = [(idx, r) for idx, r in found if not (r.get("location_restriction") or "")] or found
        primary = [r for idx, r in sorted(normal, key=lambda x: x[0])]
        # Canonical page: the one whose name matches the category (e.g. "Abyssal demon"); else a page that
        # contains it, the weakest first (task monsters rather than bosses: Kalphite worker, not Kalphite King).
        counts = Counter(r["page_name"] for r in primary)
        lowcb = {}
        for r in primary:
            cb = r.get("combat_level") or 0
            lowcb[r["page_name"]] = min(lowcb.get(r["page_name"], 10**6), cb if cb else 10**6)
        singular = re.sub(r"(ies)$", "y", cat.lower())
        singular = re.sub(r"(?<!s)s$", "", singular)

        def score(p):
            pl = p.lower()
            return (pl == singular or pl == cat.lower(), singular in pl, -lowcb.get(p, 10**6), counts[p])
        pages = sorted(counts, key=score, reverse=True)
        canon = pages[0] if pages else None
        crow = next((r for r in primary if r["page_name"] == canon), None) if canon else None
        weak = Counter()
        for r in primary:
            for w in r.get("weakness_text") or []:
                if weak_norm(w):
                    weak[weak_norm(w)] += 1
        wk = weak.most_common(1)[0][0] if weak else ""
        cls = CLASS.get(wk, "any")
        locs, seen = [], set()
        used = 0
        for p in pages[:6]:
            if used >= 3:
                break
            got = loclines(p)
            if got:
                used += 1
            for l in got:
                key = l["name"].lower()
                if key in seen:
                    continue
                seen.add(key)
                l["monster"] = p if p != canon else ""
                locs.append(l)
        if not locs and cat in MANUAL_LOCATIONS:
            locs = [dict(l) for l in MANUAL_LOCATIONS[cat]]
        sl = min([r.get("slayer_level") or 1 for r in primary] or [1])
        sl = max(sl, MANUAL_SLAYER.get(cat, 0))
        monsters[cat] = OrderedDict([
            ("slayer", max(sl, categories[cat]) if categories[cat] else sl),
            ("combat", lowcb.get(canon, 0) if canon and lowcb.get(canon, 10**6) < 10**6 else 0),
            ("weakness", wk.capitalize() if wk else "None"),
            ("style", STYLE.get(wk, "Any style")),
            ("gear", GEAR[cls]),
            ("special", SPECIAL.get(cat, "")),
            ("aliases", sorted({singular} | {r["page_name"].lower() for r in primary} - {cat.lower()})),
            ("eligible", sorted({r["page_name"] for idx, r in found})),
            ("locations", [OrderedDict([(k, v) for k, v in [("name", l["name"]), ("requirements", l["requirements"]),
                                        ("notes", l["notes"]), ("spawns", l["spawns"]), ("monster", l.get("monster", ""))] if v != "" or k in ("name",)]) for l in locs]),
        ])
        print("%-36s %3d monsters %2d locations  weak=%s" % (cat, len(monsters[cat]["eligible"]), len(locs), wk), file=sys.stderr)

    out = OrderedDict([
        ("version", 1),
        ("source", "RuneScape Wiki (runescape.wiki), RS3. Text under CC BY-NC-SA 3.0."),
        ("generated", time.strftime("%Y-%m-%d")),
        ("masters", masters),
        ("monsters", monsters),
        ("chat", chat_patterns()),
    ])
    os.makedirs(os.path.join(ROOT, "data"), exist_ok=True)
    with open(os.path.join(ROOT, "data", "slayer.json"), "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print("masters=%d monsters=%d tasks=%d" % (len(masters), len(monsters), sum(len(m["tasks"]) for m in masters.values())))


def chat_patterns():
    """Default chat patterns (.NET regex, case-insensitive). Named groups: monster, count (assigned
    amount), left (kills left), streak, points, total. From the RS3 transcripts on the wiki."""
    return OrderedDict([
        ("assign", [
            r"(?:new )?task is to kill (?<count>\d[\d,.]*) (?!more )(?<monster>[^.;!?]+)",
            r"start you off hunting (?<monster>[^.;!?]+?)[.;,]? ?you'?ll need to kill (?<count>\d[\d,.]*)",
            r"you need to kill (?<count>\d[\d,.]*) (?!more )(?<monster>[^.;!?]+)",
            r"go and kill (?<count>\d[\d,.]*) (?<monster>[^.;!?]+)",
            r"(?:new )?task is to kill (?<monster>[a-z][^.;!?\d]+)",
        ]),
        ("progress", [
            r"assigned to kill (?<monster>[^;:]+?)\s*[;:,.]\s*only (?<left>\d[\d,.]*) more to go",
            r"kill (?<left>\d[\d,.]*) more (?<monster>[^.;!?]+)",
            r"you still need to kill (?<left>\d[\d,.]*) (?:monsters? )?to complete your (?:current )?slayer",
            r"(?<left>\d[\d,.]*) (?:more )?(?:kills? )?(?:left|remaining) (?:on|for|in) your (?:slayer )?task",
            r"slayer task[^.]*?(?<left>\d[\d,.]*) (?:kills? )?(?:left|remaining)",
            r"still hunting (?<monster>[^;.!]+)",
        ]),
        ("complete", [
            r"completed (?<streak>\d[\d,.]*) tasks? in a row and (?:gain(?:ed)?|receive[ds]?|earn(?:ed)?) (?<points>\d[\d,.]*) (?:slayer )?points?(?:[^\d]{0,40}total of (?<total>\d[\d,.]*))?",
            r"completed your special assignment[^.]*?(?<points>\d[\d,.]*) slayer points",
            r"you(?:'ve| have) completed your (?:slayer )?(?:task|assignment)",
            r"need something new to hunt",
        ]),
        ("kill", []),
    ])


if __name__ == "__main__":
    main()
