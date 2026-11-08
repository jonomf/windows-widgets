"""Draws the app's logos, widget icons and widget-picker screenshots into src/JonoWidgets/Assets.

Run from the repo root: python3 tools/make_assets.py  (needs Pillow)
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "JonoWidgets" / "Assets"
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"

BG = (32, 33, 38)
CARD = (44, 46, 53)
TEXT = (235, 236, 240)
SUBTLE = (150, 153, 163)
BLUE = (91, 141, 239)
RED = (229, 83, 75)
ORANGE = (230, 160, 60)
GREEN = (77, 179, 126)
PURPLE = (155, 111, 216)


def font(size, bold=False):
    return ImageFont.truetype(FONT_BOLD if bold else FONT, size)


def logo(size, pad_frac=0.12, bg=True):
    """Three tiles: the widgets. Transparent around a rounded dark square."""
    s = size * 4  # supersample
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    pad = int(s * pad_frac)
    if bg:
        d.rounded_rectangle([pad, pad, s - pad, s - pad], radius=int(s * 0.18), fill=BG)
    inner = pad + int(s * 0.12)
    gap = int(s * 0.05)
    w = s - 2 * inner
    half = (w - gap) // 2
    d.rounded_rectangle([inner, inner, inner + half, inner + w], radius=int(s * 0.06), fill=RED)
    d.rounded_rectangle([inner + half + gap, inner, inner + w, inner + half], radius=int(s * 0.06), fill=BLUE)
    d.rounded_rectangle([inner + half + gap, inner + half + gap, inner + w, inner + w], radius=int(s * 0.06), fill=GREEN)
    return im.resize((size, size), Image.LANCZOS)


def wide(w, h):
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    l = logo(int(h * 0.7), pad_frac=0.0)
    im.paste(l, ((w - l.width) // 2, (h - l.height) // 2), l)
    return im


def glyph_icon(color, draw_fn, size=64):
    s = size * 4
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.22), fill=color)
    draw_fn(d, s)
    return im.resize((size, size), Image.LANCZOS)


def todoist_glyph(d, s):
    for i in range(3):
        y = int(s * (0.28 + i * 0.22))
        d.ellipse([int(s * 0.18), y - int(s * 0.06), int(s * 0.30), y + int(s * 0.06)], outline="white", width=int(s * 0.03))
        d.line([int(s * 0.38), y, int(s * 0.82), y], fill="white", width=int(s * 0.05))


def repos_glyph(d, s):
    w = int(s * 0.05)
    d.line([int(s * 0.35), int(s * 0.22), int(s * 0.35), int(s * 0.78)], fill="white", width=w)
    d.line([int(s * 0.35), int(s * 0.55), int(s * 0.65), int(s * 0.38)], fill="white", width=w)
    for x, y in [(0.35, 0.22), (0.35, 0.78), (0.65, 0.35)]:
        r = int(s * 0.08)
        cx, cy = int(s * x), int(s * y)
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill="white")


def now_glyph(d, s):
    c = s // 2
    r = int(s * 0.3)
    w = int(s * 0.05)
    d.ellipse([c - r, c - r, c + r, c + r], outline="white", width=w)
    d.line([c, c, c, c - int(r * 0.7)], fill="white", width=w)
    d.line([c, c, c + int(r * 0.55), c], fill="white", width=w)


def card(w=300, h=304):
    im = Image.new("RGB", (w, h), CARD)
    return im, ImageDraw.Draw(im)


def todoist_shot():
    im, d = card()
    d.text((16, 14), "Work", font=font(15, True), fill=TEXT)
    d.text((228, 14), "3", font=font(14, True), fill=RED)
    d.text((248, 14), "12", font=font(14), fill=SUBTLE)
    rows = [
        (RED, "Send the Q4 plan", "2d", RED),
        (ORANGE, "Review Sam's PR", "1d", RED),
        (BLUE, "Book flights", "Today", GREEN),
        (SUBTLE, "Call the dentist", "3pm", GREEN),
        (SUBTLE, "Expense report", "Tmrw", ORANGE),
        (BLUE, "Plan offsite agenda", "Fri", BLUE),
        (SUBTLE, "Update resume", "Nov 20", SUBTLE),
    ]
    y = 46
    for pc, t, due, dc in rows:
        d.ellipse([16, y + 3, 28, y + 15], outline=pc, width=2)
        d.text((38, y), t, font=font(14), fill=TEXT)
        tw = d.textlength(due, font=font(12))
        d.text((284 - tw, y + 2), due, font=font(12), fill=dc)
        y += 34
    return im


def repos_shot():
    im, d = card()
    d.text((16, 14), "jonomf", font=font(15, True), fill=TEXT)
    rows = [
        ("✓", GREEN, "windows-widgets", "5m", "Repos widget: CI status"),
        ("●", ORANGE, "todoist-widget", "12m", "Sort by priority option"),
        ("✓", GREEN, "now-widget", "2h", "Lock screen buttons"),
        ("✗", RED, "audiobook-widget", "1d", "Music profile"),
        ("✓", GREEN, "github-widget", "3d", "Update chips"),
    ]
    y = 46
    for mark, mc, name, ago, msg in rows:
        d.text((16, y), mark, font=font(14, True), fill=mc)
        d.text((36, y), name, font=font(14), fill=TEXT)
        tw = d.textlength(ago, font=font(12))
        d.text((284 - tw, y + 2), ago, font=font(12), fill=SUBTLE)
        d.text((36, y + 20), msg, font=font(12), fill=SUBTLE)
        y += 48
    return im


def now_shot():
    im, d = card()
    d.text((16, 14), "Work", font=font(15, True), fill=TEXT)
    d.text((196, 16), "9am · 2h 5m", font=font(12), fill=SUBTLE)
    names = ["Sleep", "Eat", "Move", "Rest", "Work", "Family", "Friends", "Hobby", "Read", "Games", "Watch", "Chores"]
    colors = [BLUE, RED, GREEN, ORANGE, PURPLE, (63, 182, 198), (227, 111, 165), (138, 166, 60),
              (201, 138, 75), (108, 123, 196), (79, 157, 154), (154, 154, 154)]
    for i, (n, c) in enumerate(zip(names, colors)):
        col, row = i % 4, i // 4
        x0, y0 = 16 + col * 68, 48 + row * 80
        if n == "Work":
            d.rounded_rectangle([x0 - 2, y0 - 4, x0 + 64, y0 + 66], radius=8, fill=(52, 72, 120))
        d.ellipse([x0 + 18, y0 + 4, x0 + 44, y0 + 30], fill=c)
        tw = d.textlength(n, font=font(12))
        d.text((x0 + 31 - tw / 2, y0 + 40), n, font=font(12), fill=TEXT if n == "Work" else SUBTLE)
    return im


def main():
    (ASSETS / "Widgets").mkdir(parents=True, exist_ok=True)
    logo(50).save(ASSETS / "StoreLogo.png")
    logo(300).save(ASSETS / "Square150x150Logo.scale-200.png")
    logo(88).save(ASSETS / "Square44x44Logo.scale-200.png")
    logo(24, pad_frac=0.0).save(ASSETS / "Square44x44Logo.targetsize-24_altform-unplated.png")
    wide(620, 300).save(ASSETS / "Wide310x150Logo.scale-200.png")
    wide(1240, 600).save(ASSETS / "SplashScreen.scale-200.png")
    glyph_icon(RED, todoist_glyph).save(ASSETS / "Widgets" / "Todoist.png")
    glyph_icon((60, 64, 72), repos_glyph).save(ASSETS / "Widgets" / "Repos.png")
    glyph_icon(PURPLE, now_glyph).save(ASSETS / "Widgets" / "Now.png")
    todoist_shot().save(ASSETS / "Widgets" / "Todoist_Screenshot.png")
    repos_shot().save(ASSETS / "Widgets" / "Repos_Screenshot.png")
    now_shot().save(ASSETS / "Widgets" / "Now_Screenshot.png")


if __name__ == "__main__":
    main()
