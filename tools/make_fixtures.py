#!/usr/bin/env python3
"""Draws synthetic game frames for the detector tests (tests/fixtures/*.png): a slayer counter
(yellow number / task / points / streak on a dark translucent box) and a slayer master's dialogue box
(parchment, gold speaker name, brown text, portrait on the left) over noisy terrain.
They approximate the RS3 widgets; the user's real screenshots go in tests/fixtures/real/."""
import os, random
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tests", "fixtures")
SANS = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
SANS_B = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
SERIF_B = "/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf"
YELLOW = (255, 214, 0)

def terrain(w, h, seed):
    rnd = random.Random(seed)
    img = Image.new("RGB", (w, h), (60, 80, 45))
    d = ImageDraw.Draw(img)
    for _ in range(900):
        x, y = rnd.randrange(w), rnd.randrange(h)
        r = rnd.randrange(6, 40)
        c = rnd.choice([(52, 74, 40), (70, 92, 52), (88, 82, 64), (46, 58, 70), (95, 100, 105), (40, 48, 36)])
        d.ellipse([x - r, y - r, x + r, y + r], fill=c)
    d.rectangle([0, 0, w, 90], fill=(110, 140, 175))   # sky
    # A distractor: one row of yellow item text on a dark strip (not a stack).
    d.rectangle([40, 640, 300, 662], fill=(15, 15, 15))
    d.text((48, 643), "Dragon bones", font=ImageFont.truetype(SANS, 14), fill=YELLOW)
    return img

def counter(img, x, y, left, task="Dragons", points="6,257", streak="291"):
    w, h = 170, 112
    over = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(over)
    d.rectangle([x, y, x + w, y + h], fill=(12, 12, 14, 205))
    big, mid, small = ImageFont.truetype(SANS_B, 28), ImageFont.truetype(SANS_B, 15), ImageFont.truetype(SANS, 13)
    tw = d.textlength(str(left), font=big)
    d.text((x + (w - tw) / 2, y + 4), str(left), font=big, fill=YELLOW)
    tw = d.textlength(task, font=mid)
    d.text((x + (w - tw) / 2, y + 40), task, font=mid, fill=YELLOW)
    d.text((x + 8, y + 66), "Slayer points:", font=small, fill=(235, 235, 235))
    d.text((x + w - 8 - d.textlength(points, font=small), y + 66), points, font=small, fill=YELLOW)
    d.text((x + 8, y + 86), "Task streak:", font=small, fill=(235, 235, 235))
    d.text((x + w - 8 - d.textlength(streak, font=small), y + 86), streak, font=small, fill=YELLOW)
    img.paste(Image.alpha_composite(img.convert("RGBA"), over).convert("RGB"))

def dialogue(img, x, y):
    w, h = 560, 140
    d = ImageDraw.Draw(img)
    d.rectangle([x - 3, y - 3, x + w + 3, y + h + 3], fill=(70, 50, 30))
    d.rectangle([x, y, x + w, y + h], fill=(214, 196, 152))
    rnd = random.Random(7)
    for _ in range(400):   # parchment grain
        px, py = x + rnd.randrange(w), y + rnd.randrange(h)
        d.point((px, py), fill=(200, 182, 140))
    d.rectangle([x + 12, y + 14, x + 108, y + 126], fill=(92, 64, 48))   # portrait
    d.ellipse([x + 30, y + 30, x + 90, y + 100], fill=(170, 120, 90))
    gold = (176, 128, 32)
    title = ImageFont.truetype(SERIF_B, 17)
    tw = d.textlength("LANIAKEA", font=title)
    d.text((x + 120 + (w - 130 - tw) / 2, y + 10), "LANIAKEA", font=title, fill=gold)
    body = ImageFont.truetype(SANS, 15)
    brown = (58, 38, 20)
    d.text((x + 128, y + 44), "You're doing okay, I suppose. Your new task is to", font=body, fill=brown)
    d.text((x + 128, y + 66), "kill 125 Dragons.", font=body, fill=brown)
    d.rectangle([x + w - 130, y + h - 30, x + w - 20, y + h - 8], fill=(150, 110, 30))
    d.text((x + w - 118, y + h - 28), "Continue", font=ImageFont.truetype(SANS_B, 13), fill=(250, 240, 210))

def distractors(img, seed):
    """Other RS3-ish yellow-on-dark interface text: an XP tracker box (left-aligned rows), ability-bar
    cooldown numbers, a buff bar with timers."""
    rnd = random.Random(seed)
    d = ImageDraw.Draw(img)
    f = ImageFont.truetype(SANS, 12)
    x, y = rnd.randrange(30, 300), rnd.randrange(120, 300)
    d.rectangle([x, y, x + 150, y + 70], fill=(18, 22, 28))
    for i, t in enumerate(["Slayer 92", "Attack 99", "Total XP 1,204,331"]):
        d.text((x + 6, y + 6 + i * 20), t, font=f, fill=YELLOW)
    bx, by = rnd.randrange(300, 700), 690
    for i in range(10):
        d.rectangle([bx + i * 36, by - 4, bx + i * 36 + 32, by + 26], fill=(22, 22, 22))
        d.text((bx + i * 36 + 8, by + 4), str(rnd.randrange(1, 60)), font=f, fill=YELLOW)
    ux, uy = rnd.randrange(700, 1000), rnd.randrange(100, 200)
    for i in range(6):
        d.rectangle([ux + i * 30, uy, ux + i * 30 + 26, uy + 26], fill=(30, 26, 20))
        d.text((ux + i * 30 + 4, uy + 14), "%dm" % rnd.randrange(1, 30), font=ImageFont.truetype(SANS, 9), fill=YELLOW)

def real_frames():
    """The user's real counter crop pasted into busy frames at a few positions and UI scales."""
    src = os.path.join(ROOT, "real", "counter.png")
    if not os.path.exists(src):
        return
    crop = Image.open(src).convert("RGB")
    cases = [(1060, 110, 1.0), (40, 400, 1.0), (620, 30, 1.25), (900, 460, 1.5)]
    for n, (x, y, k) in enumerate(cases, 1):
        img = terrain(1280, 720, 10 + n)
        distractors(img, n)
        c = crop if k == 1.0 else crop.resize((int(crop.width * k), int(crop.height * k)), Image.BICUBIC)
        img.paste(c, (x, y))
        img.save(os.path.join(ROOT, "real-pasted-%d.png" % n))
        with open(os.path.join(ROOT, "real-pasted-%d.txt" % n), "w") as fh:
            fh.write("%d %d %d %d\n" % (x, y, c.width, c.height))

def minimised_frames():
    """The user's real minimised counter (just "177" and the skull) pasted into busy frames."""
    src = os.path.join(ROOT, "real", "counter-minimised.png")
    if not os.path.exists(src):
        return
    crop = Image.open(src).convert("RGB")
    cases = [(1100, 120, 1.0), (60, 420, 1.0), (700, 40, 1.5)]
    for n, (x, y, k) in enumerate(cases, 1):
        img = terrain(1280, 720, 30 + n)
        distractors(img, 20 + n)
        c = crop if k == 1.0 else crop.resize((int(crop.width * k), int(crop.height * k)), Image.BICUBIC)
        img.paste(c, (x, y))
        img.save(os.path.join(ROOT, "real-minimised-%d.png" % n))
        with open(os.path.join(ROOT, "real-minimised-%d.txt" % n), "w") as fh:
            fh.write("%d %d %d %d\n" % (x, y, c.width, c.height))

def main():
    os.makedirs(ROOT, exist_ok=True)
    a = terrain(1280, 720, 1); counter(a, 1060, 120, 135); dialogue(a, 360, 520)
    a.save(os.path.join(ROOT, "frame-counter-dialogue.png"))
    b = terrain(1280, 720, 2); counter(b, 1060, 120, 131)
    b.save(os.path.join(ROOT, "frame-counter-131.png"))
    c = terrain(1280, 720, 3); counter(c, 1060, 120, 134)
    c.save(os.path.join(ROOT, "frame-counter-134.png"))
    e = terrain(1280, 720, 4)
    e.save(os.path.join(ROOT, "frame-empty.png"))
    real_frames()
    minimised_frames()
    print("fixtures in", ROOT)

if __name__ == "__main__":
    main()
