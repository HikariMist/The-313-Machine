"""Turns the studio's approved art in Art/Source into game-ready images.

    python3 import_user_art.py <repo root>

- title_keyart.png        -> Resources/Art/bg/title.jpg (menu side cropped off; the game draws its own menu)
- drenched_man_sheet.png  -> Customers/cust_walter.png, cust_mimic.png (keyed out of the dark background)
- smiling_woman_sheet.png -> Customers/cust_smile.png, cust_smile_2.png, cust_smile_3.png (expression busts)
Re-run after replacing a source sheet. Crop boxes are in source-pixel coordinates.
"""
import os
import sys
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

PORTRAIT = (600, 800)


def key_out(img, bg=None, thresh=11, span_fill=False):
    """Alpha from distance to the flat background colour, with holes filled and soft edges."""
    a = np.asarray(img.convert("RGB")).astype(np.float32)
    if bg is None:
        bg = np.median(np.concatenate([a[:4].reshape(-1, 3), a[:, :4].reshape(-1, 3), a[:, -4:].reshape(-1, 3)]), axis=0)
    dist = np.sqrt(((a - bg) ** 2).sum(axis=2))
    mask = dist > thresh
    mask = ndimage.binary_opening(mask, iterations=1)
    labels, n = ndimage.label(mask)
    if n:
        sizes = ndimage.sum(mask, labels, range(1, n + 1))
        mask = labels == (np.argmax(sizes) + 1)          # keep the figure, drop stray rain streaks
    mask = ndimage.binary_fill_holes(mask)
    if span_fill:
        # Dark clothing can match the background; fill each row between the figure's outer edges.
        filled = np.zeros_like(mask)
        for y in range(mask.shape[0]):
            xs = np.nonzero(mask[y])[0]
            if len(xs) > 1: filled[y, xs[0]:xs[-1] + 1] = True
        mask = ndimage.binary_opening(filled, structure=np.ones((1, 9)))
    alpha = Image.fromarray((mask * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.6))
    out = img.convert("RGBA")
    out.putalpha(alpha)
    return out


def fit_portrait(img, bottom_fade=0.28):
    """Scales a cut-out to the 600x800 portrait frame, anchored at the bottom, fading the lower edge."""
    w, h = img.size
    scale = min(PORTRAIT[0] / w, PORTRAIT[1] / h)
    img = img.resize((int(w * scale), int(h * scale)), Image.LANCZOS)
    canvas = Image.new("RGBA", PORTRAIT, (0, 0, 0, 0))
    canvas.paste(img, ((PORTRAIT[0] - img.width) // 2, PORTRAIT[1] - img.height), img)
    a = np.asarray(canvas).astype(np.float32)
    ys = np.linspace(0, 1, PORTRAIT[1])
    a[:, :, 3] *= np.clip((1 - ys) / bottom_fade, 0, 1)[:, None]
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def tint(img, rgb, amount):
    a = np.asarray(img).astype(np.float32)
    a[:, :, :3] = a[:, :, :3] * (1 - amount) + np.array(rgb, np.float32) * amount * (a[:, :, :3].mean(axis=2, keepdims=True) / 128)
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def main(root):
    src = os.path.join(root, "Art", "Source")
    art = os.path.join(root, "Assets", "_Project", "Resources", "Art")
    cust = os.path.join(art, "Customers")

    p = os.path.join(src, "title_keyart.png")
    if os.path.exists(p):
        im = Image.open(p).convert("RGB")
        im = im.crop((560, 0, im.width, im.height))
        # The crop leaves the tail of the mockup tagline ("...ervice.") at the left edge; cover it with
        # the sky strip directly above so the pole and wires stay continuous.
        im.paste(im.crop((0, 446, 95, 486)), (0, 488))
        im.save(os.path.join(art, "bg", "title.jpg"), quality=88)

    p = os.path.join(src, "drenched_man_sheet.png")
    if os.path.exists(p):
        sheet = Image.open(p).convert("RGB")
        walter = fit_portrait(key_out(sheet.crop((50, 150, 400, 617)), thresh=8, span_fill=True))
        walter.save(os.path.join(cust, "cust_walter.png"), optimize=True)
        # The mimic wears Walter's face from the wrong side of the glass.
        mimic = tint(walter.transpose(Image.FLIP_LEFT_RIGHT), (150, 110, 255), 0.22)
        mimic.save(os.path.join(cust, "cust_mimic.png"), optimize=True)

    p = os.path.join(src, "smiling_woman_sheet.png")
    if os.path.exists(p):
        sheet = Image.open(p).convert("RGB")
        busts = {"cust_smile": (425, 140, 625, 355), "cust_smile_2": (655, 410, 855, 625), "cust_smile_3": (885, 410, 1085, 625)}
        for name, box in busts.items():
            fit_portrait(key_out(sheet.crop(box)), bottom_fade=0.2).save(os.path.join(cust, name + ".png"), optimize=True)


# Character sheets share one layout: a 3x2 "head expressions" grid at the top right.
COL_CENTERS = (515, 748, 980)
ROWS = ((135, 358), (402, 628))


def bust(sheet, col, row, half_w=108):
    cx = COL_CENTERS[col]
    y0, y1 = ROWS[row]
    return sheet.crop((cx - half_w, y0, cx + half_w, y1))


SHEET_BUSTS = {
    # sheet file: [(portrait name, col, row, span_fill)]
    "hat_stranger_sheet.png": [("cust_collector", 0, 0, True), ("cust_collector_true", 1, 1, True)],
    "child_sheet.png": [("cust_lily", 0, 0, False)],
    "corrupted_worker_sheet.png": [("cust_mara", 0, 0, False), ("cust_mara_mimic", 2, 1, True)],
    "faceless_passenger_sheet.png": [("cust_double", 2, 1, False)],
}


def import_busts(root):
    src = os.path.join(root, "Art", "Source")
    cust = os.path.join(root, "Assets", "_Project", "Resources", "Art", "Customers")
    for sheet_name, items in SHEET_BUSTS.items():
        p = os.path.join(src, sheet_name)
        if not os.path.exists(p):
            continue
        sheet = Image.open(p).convert("RGB")
        for name, col, row, span in items:
            fit_portrait(key_out(bust(sheet, col, row), thresh=8 if span else 11, span_fill=span), bottom_fade=0.2).save(os.path.join(cust, name + ".png"), optimize=True)


if __name__ == "__main__":
    root = sys.argv[1] if len(sys.argv) > 1 else os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    main(root)
    import_busts(root)
