"""Placeholder environment art: the lot (per weather), title, stockroom, camera feeds, products, scares.
Replace any file with final art of the same name and size; the game picks it up unchanged."""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
from artkit import vgradient, to_img, noise, radial, vignette, grain, SODIUM, EMERALD, VIOLET

W, H = 1600, 1000
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"

SKIES = {
    "clear": [(0, (6, 9, 18)), (0.55, (16, 22, 36)), (1, (12, 12, 14))],
    "fog": [(0, (22, 26, 30)), (0.55, (52, 58, 62)), (1, (24, 24, 26))],
    "rain": [(0, (8, 10, 16)), (0.55, (24, 30, 40)), (1, (12, 13, 16))],
    "storm": [(0, (4, 5, 10)), (0.5, (20, 22, 34)), (1, (10, 10, 12))],
    "margin": [(0, (24, 6, 30)), (0.55, (70, 22, 60)), (1, (16, 8, 14))],
    "flood": [(0, (10, 8, 20)), (0.5, (40, 20, 50)), (1, (8, 14, 18))],
}


def lot(weather, margin=False, seed=0):
    sky = vgradient(W, H, SKIES[weather])
    if margin:
        sky = sky * np.array([1.25, 0.8, 1.3])[None, None, :]
    img = to_img(sky)
    d = ImageDraw.Draw(img, "RGBA")
    horizon = int(H * 0.52)
    # stars
    r = np.random.RandomState(seed)
    if weather == "clear":
        for _ in range(220):
            x, y = r.randint(0, W), r.randint(0, horizon - 60)
            v = r.randint(80, 200)
            d.point((x, y), fill=(v, v, v + 20))
    # tree line
    pts = [(0, horizon)]
    for x in range(0, W + 20, 20):
        pts.append((x, horizon - 40 - int(noise(1, 1, 1, x + seed)[0, 0] * 0) - r.randint(10, 90)))
    pts.append((W, horizon + 10))
    d.polygon(pts, fill=(6, 8, 10))
    # reservoir strip
    d.rectangle((0, horizon, W, horizon + 60), fill=(14, 20, 28))
    for i in range(30):
        x = r.randint(0, W)
        d.line((x, horizon + r.randint(5, 55), x + r.randint(30, 140), horizon + r.randint(5, 55)), fill=(60, 80, 100, 60), width=1)
    # guard rail + road
    d.rectangle((0, horizon + 60, W, horizon + 140), fill=(20, 20, 22))
    for x in range(0, W, 90):
        d.rectangle((x, horizon + 52, x + 6, horizon + 78), fill=(70, 70, 70))
    d.line((0, horizon + 58, W, horizon + 58), fill=(120, 120, 120), width=4)
    d.line((0, horizon + 100, W, horizon + 100), fill=(160, 140, 60, 90), width=3)
    # parking lot
    lotc = (22, 22, 24) if weather in ("clear", "fog") else (16, 18, 22)
    d.rectangle((0, horizon + 140, W, H), fill=lotc)
    for x in range(-200, W, 260):
        d.line((x + 200, horizon + 150, x, H), fill=(140, 140, 120, 70), width=5)
    # bus stop sign (left)
    d.rectangle((210, horizon - 120, 218, horizon + 210), fill=(60, 60, 64))
    d.rectangle((168, horizon - 150, 262, horizon - 96), fill=(30, 70, 110))
    d.text((176, horizon - 142), "ROUTE 9\nNIGHT SVC", fill=(220, 220, 220), font=ImageFont.truetype(FONT, 14))
    d.rectangle((180, horizon + 40, 300, horizon + 52), fill=(50, 50, 50))  # bench
    # lamp post (right)
    d.rectangle((1240, horizon - 300, 1250, horizon + 220), fill=(40, 40, 44))
    d.rectangle((1180, horizon - 310, 1250, horizon - 296), fill=(40, 40, 44))
    arr = np.asarray(img).astype(np.float32)
    lampx, lampy = 1190, horizon - 296
    arr += radial(W, H, lampx, lampy, 520, SODIUM, 0.55, 1.6)
    arr += radial(W, H, lampx, horizon + 260, 600, SODIUM, 0.28, 1.4)
    # machine glow spilling from the right edge (the booth)
    arr += radial(W, H, W + 100, H * 0.75, 700, EMERALD, 0.22, 1.5)
    if weather in ("rain", "storm", "flood"):
        # wet lot reflections
        refl = arr[horizon - 200:horizon + 140][::-1] * 0.25
        hgt = min(refl.shape[0], H - (horizon + 140))
        arr[horizon + 140:horizon + 140 + hgt] += refl[:hgt]
    if weather == "fog" or margin:
        fogn = noise(W, H, 300, seed + 5)[:, :, None]
        arr = arr * (1 - fogn * 0.45) + fogn * np.array([120, 125, 130]) * 0.45 * (0.6 if margin else 1)
    if weather == "flood":
        wl = horizon + 260
        arr[wl:] = arr[wl:] * 0.5 + np.array([10, 30, 40]) * 0.5
    if margin:
        arr += radial(W, H, W / 2, horizon - 100, 900, VIOLET, 0.18, 1.2)
    arr = vignette(arr, 0.8)
    out = to_img(arr)
    return grain(out, 6, seed)


def stockroom():
    arr = vgradient(W, H, [(0, (26, 22, 18)), (1, (12, 10, 8))])
    img = to_img(arr)
    d = ImageDraw.Draw(img, "RGBA")
    def R(x, y, w, h, c, outline=None):
        d.rectangle((x * W, y * H, (x + w) * W, (y + h) * H), fill=c, outline=outline, width=3)
    R(0, 0.78, 1, 0.22, (20, 18, 16))                                  # floor
    R(0.06, 0.30, 0.14, 0.40, (70, 72, 70), (40, 40, 40))              # filing cabinet
    for i in range(3): R(0.075, 0.33 + i * 0.12, 0.11, 0.09, (85, 87, 84), (40, 40, 40))
    R(0.22, 0.62, 0.50, 0.03, (60, 44, 30))                            # shelf / desk
    R(0.24, 0.50, 0.12, 0.12, (40, 60, 90))                            # binder
    R(0.40, 0.55, 0.10, 0.07, (30, 30, 30))                            # recorder
    d.ellipse((0.415 * W, 0.565 * H, 0.44 * W, 0.6 * H), fill=(80, 80, 80))
    d.ellipse((0.455 * W, 0.565 * H, 0.48 * W, 0.6 * H), fill=(80, 80, 80))
    R(0.55, 0.60, 0.10, 0.03, (200, 190, 160))                          # napkins + notes
    R(0.70, 0.18, 0.16, 0.22, (120, 90, 60), (70, 50, 30))             # corkboard
    R(0.72, 0.21, 0.05, 0.07, (220, 210, 180)); R(0.79, 0.24, 0.05, 0.10, (200, 195, 170))
    R(0.88, 0.25, 0.10, 0.50, (54, 64, 70), (30, 34, 38))              # locker 0
    d.text((0.915 * W, 0.30 * H), "0", fill=(200, 200, 200), font=ImageFont.truetype(FONT, 40))
    R(0.36, 0.80, 0.18, 0.10, (34, 30, 26), (60, 50, 40))              # loose panel
    R(0.62, 0.28, 0.06, 0.14, (120, 130, 135), (60, 60, 60))           # mirror
    for x in (0.25, 0.30, 0.66): R(x, 0.66, 0.05, 0.12, (110, 90, 60))  # boxes
    arr = np.asarray(img).astype(np.float32)
    arr += radial(W, H, W * 0.45, -50, 900, (255, 230, 180), 0.35, 1.3)
    return grain(to_img(vignette(arr, 0.9)), 7, 3)


def cam(name):
    base = {"cam1": lot("clear", seed=11), "cam2": None, "cam3": stockroom(), "cam4": lot("fog", seed=12), "cam0": None}[name]
    if base is None:
        arr = vgradient(W, H, [(0, (20, 24, 22)), (1, (8, 10, 9))])
        img = to_img(arr)
        d = ImageDraw.Draw(img)
        if name == "cam2":   # looking back at the machine front
            d.rectangle((500, 120, 1100, 1000), fill=(30, 36, 34), outline=(70, 80, 76), width=6)
            for rr in range(4):
                for cc in range(4):
                    d.rectangle((540 + cc * 135, 170 + rr * 150, 650 + cc * 135, 290 + rr * 150), outline=(60, 90, 70), width=3)
        else:                # cam0: the booth from behind
            d.rectangle((0, 640, W, H), fill=(30, 28, 26))
            d.ellipse((700, 320, 900, 520), fill=(10, 10, 10))
            d.rectangle((640, 480, 960, 900), fill=(10, 10, 10))
        base = img
    img = base.convert("L").convert("RGB")
    return img.filter(ImageFilter.GaussianBlur(1.5))


PRODUCTS = {
    "prod_cola": ((150, 30, 30), "can", "COLA"), "prod_water": ((120, 170, 210), "bottle", ""),
    "prod_coffee": ((90, 60, 40), "can", "HOT"), "prod_energy": ((60, 200, 80), "can", "VOLT"),
    "prod_chips": ((210, 180, 60), "bag", ""), "prod_choco": ((110, 70, 40), "bar", ""),
    "prod_peanuts": ((190, 140, 80), "bag", ""), "prod_gum": ((240, 130, 170), "pack", ""),
    "prod_noodles": ((230, 230, 220), "cup", ""), "prod_battery": ((40, 40, 40), "battery", ""),
    "prod_aspirin": ((230, 230, 240), "box", "+"), "prod_matches": ((40, 90, 80), "pack", "M"),
    "prod_salt": ((200, 200, 200), "packet", "SALT"), "prod_mints": ((120, 200, 200), "box", ""),
    "prod_unlabeled": ((150, 150, 155), "can", ""), "prod_token": ((200, 160, 60), "coin", "313"),
    "prod_cassette": ((50, 50, 55), "cassette", "M.Q."), "prod_exact": ((180, 150, 80), "coins", "13"),
}


def product(color, shape, label):
    S = 256
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    dark = tuple(max(0, c - 50) for c in color)
    if shape == "can":
        d.rounded_rectangle((70, 30, 186, 226), 22, fill=color, outline=dark, width=6)
        d.rectangle((70, 60, 186, 72), fill=(220, 220, 220))
    elif shape == "bottle":
        d.rounded_rectangle((85, 70, 171, 230), 24, fill=color + (200,), outline=dark, width=5)
        d.rectangle((108, 26, 148, 72), fill=(40, 90, 160))
        d.rectangle((85, 120, 171, 170), fill=(240, 240, 250))
    elif shape in ("bag", "packet"):
        d.polygon([(60, 40), (196, 40), (210, 226), (46, 226)], fill=color, outline=dark)
        d.rectangle((60, 40, 196, 60), fill=dark)
    elif shape == "bar":
        d.rounded_rectangle((30, 90, 226, 166), 10, fill=color, outline=dark, width=5)
        d.rectangle((60, 90, 110, 166), fill=(230, 190, 60))
    elif shape in ("pack", "box"):
        d.rounded_rectangle((60, 60, 196, 200), 12, fill=color, outline=dark, width=5)
    elif shape == "cup":
        d.polygon([(70, 50), (186, 50), (170, 226), (86, 226)], fill=color, outline=(180, 40, 40))
        d.rectangle((70, 90, 186, 120), fill=(200, 40, 40))
    elif shape == "battery":
        for x in (70, 136):
            d.rounded_rectangle((x, 50, x + 50, 220), 10, fill=(40, 40, 40), outline=(200, 160, 40), width=5)
            d.rectangle((x + 17, 36, x + 33, 52), fill=(180, 180, 180))
    elif shape == "coin":
        d.ellipse((48, 48, 208, 208), fill=color, outline=dark, width=8)
    elif shape == "coins":
        for i in range(4):
            d.ellipse((50 + i * 22, 90 - i * 10, 170 + i * 22, 210 - i * 10), fill=color, outline=dark, width=5)
    elif shape == "cassette":
        d.rounded_rectangle((30, 70, 226, 186), 10, fill=color, outline=(90, 90, 90), width=5)
        d.rectangle((60, 90, 196, 130), fill=(220, 210, 180))
        d.ellipse((80, 140, 110, 170), fill=(20, 20, 20)); d.ellipse((146, 140, 176, 170), fill=(20, 20, 20))
    if label:
        f = ImageFont.truetype(FONT, 36 if len(label) <= 4 else 28)
        bb = d.textbbox((0, 0), label, font=f)
        d.text(((S - bb[2]) / 2, 120 - bb[3] / 2), label, fill=(255, 255, 255), font=f)
    return img.resize((128, 128), Image.LANCZOS)


def scare(name):
    """Full-screen jumpscare plates: a face filling the frame, high contrast."""
    S = 1000
    arr = np.zeros((S, S, 3), np.float32)
    tint = {"js_collector": VIOLET, "js_final": VIOLET, "js_mara": EMERALD}.get(name, (200, 210, 220))
    img = to_img(arr)
    d = ImageDraw.Draw(img)
    d.ellipse((180, 80, 820, 980), fill=(int(tint[0] * .35), int(tint[1] * .35), int(tint[2] * .35)))
    for sx in (-1, 1):
        d.ellipse((500 + sx * 150 - 70, 380, 500 + sx * 150 + 70, 470), fill=(0, 0, 0))
        d.ellipse((500 + sx * 150 - 12, 410, 500 + sx * 150 + 12, 438), fill=(240, 240, 240))
    if name in ("js_smile", "js_hollow", "js_mimic", "js_child_behind"):
        d.polygon([(220, 620), (780, 620), (700, 760), (300, 760)], fill=(20, 0, 0))
        for x in range(240, 770, 26):
            d.rectangle((x, 620, x + 16, 662), fill=(235, 230, 210))
            d.rectangle((x + 8, 720, x + 22, 760), fill=(220, 214, 196))
    else:
        d.ellipse((410, 650, 590, 820), fill=(0, 0, 0))
    out = np.asarray(img.filter(ImageFilter.GaussianBlur(3))).astype(np.float32)
    out = vignette(out, 1.2)
    return grain(to_img(out), 18, hash(name) % 1000)


def make_all(root):
    for sub in ("bg", "cams", "Products", "Scares"):
        os.makedirs(os.path.join(root, sub), exist_ok=True)
    for i, w in enumerate(SKIES):
        lot(w, seed=i).save(f"{root}/bg/{w}.jpg", quality=84)
        lot(w, margin=True, seed=i).save(f"{root}/bg/{w}_margin.jpg", quality=84)
    lot("fog", margin=False, seed=99).save(f"{root}/bg/title.jpg", quality=84)
    stockroom().save(f"{root}/bg/stockroom.jpg", quality=84)
    for c in ("cam0", "cam1", "cam2", "cam3", "cam4"):
        cam(c).resize((960, 600)).save(f"{root}/cams/{c}.jpg", quality=80)
    for name, (color, shape, label) in PRODUCTS.items():
        product(color, shape, label).save(f"{root}/Products/{name}.png", optimize=True)
    for s in ("js_drowned", "js_smile", "js_collector", "js_mara", "js_child_behind", "js_hollow", "js_final", "js_mimic", "js_crowd", "face_glass", "hands_glass"):
        scare(s).save(f"{root}/Scares/{s}.jpg", quality=82)


if __name__ == "__main__":
    import sys
    import characters
    root = sys.argv[1]
    make_all(root)
    characters.make_all(os.path.join(root, "Customers"))
