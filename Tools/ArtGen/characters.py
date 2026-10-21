"""Customer portraits (600x800 RGBA, waist-up, standing at the machine glass)."""
import os
from PIL import Image, ImageDraw, ImageFilter
from artkit import Figure, plaid, apply_texture, SODIUM, EMERALD, VIOLET

CX = 300


def body(f, coat, shoulders=150, top=330, hip=125, arm=(0, 0, 0, 0), arm_color=None, long_coat=False):
    """Torso + arms. Returns nothing; draws on f."""
    arm_color = arm_color or coat
    sl, sr = CX - shoulders, CX + shoulders
    bottom = 820
    f.ellipse((sl - 10, top - 20, sl + 90, top + 70), coat)
    f.ellipse((sr - 90, top - 20, sr + 10, top + 70), coat)
    f.poly([(sl + 10, top), (sr - 10, top), (CX + hip + (30 if long_coat else 0), bottom), (CX - hip - (30 if long_coat else 0), bottom)], coat)
    f.poly([(sl, top + 25), (sl + 40, top + 15), (sl + 22, 700), (sl - 22, 700)], arm_color)
    f.poly([(sr, top + 25), (sr - 40, top + 15), (sr + 22, 700), (sr - 22 + 44, 700)], arm_color)


def head(f, skin, y=200, r=78, neck=34, jaw=0):
    f.rect((CX - neck, y + r - 20, CX + neck, y + r + 70), skin)
    f.ellipse((CX - r, y - r * 1.15, CX + r, y + r * 1.05 + jaw), skin)
    f.ellipse((CX - r - 8, y - 5, CX - r + 14, y + 35), skin)   # ears
    f.ellipse((CX + r - 14, y - 5, CX + r + 8, y + 35), skin)


def face(f, y=200, eye=(20, 22, 26), mouth=(60, 30, 30), smile=0, eyes_open=True, glow=None, blank=False):
    if blank:
        return
    ex = 30
    if eyes_open:
        for sx in (-1, 1):
            f.ellipse((CX + sx * ex - 13, y - 10, CX + sx * ex + 13, y + 5), (225, 220, 210))
            f.ellipse((CX + sx * ex - 7, y - 9, CX + sx * ex + 7, y + 4), glow or eye)
            f.ellipse((CX + sx * ex - 2 + 3, y - 7, CX + sx * ex + 2 + 3, y - 3), (250, 250, 250))
            f.line([(CX + sx * ex - 16, y - 22), (CX + sx * ex + 16, y - 24)], (40, 30, 25), 4)
    else:
        for sx in (-1, 1):
            f.line([(CX + sx * ex - 12, y - 2), (CX + sx * ex + 12, y - 2)], (40, 30, 25), 3)
    f.line([(CX + 2, y + 5), (CX - 6, y + 32), (CX + 6, y + 34)], (0, 0, 0, 70), 3)
    if smile <= 0:
        f.line([(CX - 18, y + 52), (CX + 18, y + 52)], mouth, 4)
    elif smile == 1:
        f.arc((CX - 26, y + 26, CX + 26, y + 58), 20, 160, mouth, 4)
    else:
        w = 22 + smile * 14
        f.poly([(CX - w, y + 44), (CX + w, y + 44), (CX + w * 0.55, y + 56 + smile * 6), (CX - w * 0.55, y + 56 + smile * 6)], (40, 10, 12))
        # teeth
        for i in range(-int(w // 9), int(w // 9) + 1):
            tx = CX + i * 9
            f.rect((tx - 3.5, y + 45, tx + 3.5, y + 52 + smile * 1.5), (232, 226, 205))
        f.arc((CX - w - 6, y + 20, CX + w + 6, y + 68 + smile * 4), 15, 165, (90, 40, 40), 3)


def cap(f, color, y=200, r=78, brim=(20, 20)):
    f.ellipse((CX - r - 4, y - r * 1.25, CX + r + 4, y - 10), color)
    f.rect((CX - r - 6, y - 40, CX + r + 6, y - 22), color)
    f.poly([(CX - r - 6, y - 30), (CX + r + 60, y - 30), (CX + r + 40, y - 12), (CX - r, y - 18)], tuple(max(0, c - 20) for c in color))


def hair(f, color, y=200, r=78, style="short"):
    if style == "short":
        f.ellipse((CX - r - 2, y - r * 1.2, CX + r + 2, y - 10), color)
    elif style == "long":
        f.ellipse((CX - r - 6, y - r * 1.22, CX + r + 6, y + 10), color)
        f.rect((CX - r - 10, y - 20, CX - r + 26, y + 150), color)
        f.rect((CX + r - 26, y - 20, CX + r + 10, y + 150), color)
    elif style == "curls":
        for i, (dx, dy) in enumerate([(-70, -40), (-40, -80), (0, -95), (40, -80), (70, -40), (-85, 10), (85, 10), (-80, 55), (80, 55)]):
            f.ellipse((CX + dx - 34, y + dy - 34, CX + dx + 34, y + dy + 34), color)
    elif style == "wet":
        f.ellipse((CX - r - 4, y - r * 1.2, CX + r + 4, y - 4), color)
        for dx in range(-r, r, 16):
            f.poly([(CX + dx, y - 20), (CX + dx + 14, y - 20), (CX + dx + 9, y + 60 + (dx % 3) * 10)], color)


def save(img, name, out, crop=True):
    """Waist-up crop, then let the lower body sink into darkness."""
    import numpy as np
    if crop:
        img = img.crop((60, 0, 540, 640)).resize((600, 800), Image.LANCZOS)
    a = np.asarray(img).astype(np.float32)
    h = a.shape[0]
    ys = np.linspace(0, 1, h)[:, None]
    fade = np.clip((1.0 - ys) / 0.32, 0, 1) ** 1.4          # alpha fade over the bottom third
    shade = np.clip(1.05 - ys * 0.75, 0.25, 1)               # body darkens away from the lamp
    a[:, :, :3] *= shade[:, :, None]
    a[:, :, 3] *= fade[:, 0][:, None] if fade.ndim == 2 else fade
    img = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA").filter(ImageFilter.GaussianBlur(0.6))
    img.quantize(colors=200, method=Image.Quantize.FASTOCTREE).save(os.path.join(out, name + ".png"), optimize=True)


def make_all(out):
    os.makedirs(out, exist_ok=True)
    skin = (196, 160, 132)
    pale = (160, 170, 168)
    dead = (128, 142, 140)

    # ---- Del Haskins: trucker, flannel, cap
    f = Figure()
    m = f.layer(); md = ImageDraw.Draw(m)
    body(f, (120, 40, 36), shoulders=165, hip=140)
    flannel = f.layer()
    body_f = Figure(); body(body_f, (255, 255, 255, 255), shoulders=165, hip=140)
    apply_texture(f, body_f.img, plaid((1200, 1600), (120, 36, 34, 255), (40, 20, 22, 255), (170, 150, 120, 255), 46))
    head(f, skin, r=82)
    face(f)
    f.ellipse((CX - 50, 245, CX + 50, 295), (110, 90, 70))  # beard shadow
    cap(f, (40, 70, 60))
    save(f.finish(seed=1), "cust_del", out)

    # ---- Del, dead: oil-black, wet, wrong side of face
    f = Figure()
    body(f, (30, 26, 24), shoulders=165, hip=140)
    head(f, dead, r=82)
    face(f, eye=(10, 10, 10), glow=(200, 210, 200))
    f.ellipse((CX + 5, 140, CX + 90, 280), (40, 30, 30, 220))
    cap(f, (20, 26, 24))
    save(f.finish(seed=2, wet=True, key_str=0.2, rim_str=1.0, desat=0.6), "cust_del_dead", out)

    # ---- College student: hoodie
    f = Figure()
    body(f, (60, 70, 120), shoulders=130, hip=115)
    head(f, (205, 168, 140), r=72)
    hair(f, (30, 22, 20), r=72, style="long")
    face(f)
    f.ellipse((CX - 120, 260, CX + 120, 380), (60, 70, 120))  # hood collar
    save(f.finish(seed=3), "cust_student", out)

    # ---- Nurse: teal scrubs, coat
    f = Figure()
    body(f, (50, 50, 58), shoulders=140, hip=120, long_coat=True)
    f.poly([(CX - 60, 330), (CX + 60, 330), (CX + 40, 820), (CX - 40, 820)], (40, 140, 140))
    head(f, (120, 84, 62), r=74)
    hair(f, (20, 16, 14), r=74, style="short")
    f.ellipse((CX - 60, 75, CX + 60, 150), (20, 16, 14))
    face(f)
    f.rect((CX + 70, 420, CX + 130, 452), (230, 230, 230), r=4)  # badge
    save(f.finish(seed=4), "cust_nurse", out)

    # ---- Hunter: orange vest
    f = Figure()
    body(f, (70, 60, 50), shoulders=150, hip=125)
    f.poly([(CX - 120, 330), (CX - 20, 330), (CX - 30, 820), (CX - 120, 820)], (230, 110, 20))
    f.poly([(CX + 20, 330), (CX + 120, 330), (CX + 120, 820), (CX + 30, 820)], (230, 110, 20))
    head(f, (180, 140, 110), r=76)
    face(f)
    cap(f, (200, 90, 20))
    f.rect((CX + 150, 520, CX + 185, 700), (30, 30, 30), r=8)  # flashlight
    save(f.finish(seed=5), "cust_hunter", out)

    # ---- Leash girl
    f = Figure()
    body(f, (150, 60, 80), shoulders=125, hip=110)
    for y in range(360, 820, 40):
        f.line([(CX - 120, y), (CX + 120, y)], (110, 40, 60), 4)
    head(f, (220, 180, 150), r=68)
    hair(f, (140, 90, 50), r=68, style="long")
    face(f, smile=1)
    f.line([(CX - 130, 640), (CX - 210, 760), (CX - 240, 800)], (200, 30, 30), 6)
    save(f.finish(seed=6), "cust_leash", out)

    # ---- Walter Pruitt: drenched bus driver
    for name, lag in (("cust_walter", False), ("cust_mimic", True)):
        f = Figure()
        body(f, (40, 58, 80), shoulders=160, hip=135)
        f.rect((CX - 140, 360, CX - 70, 395), (200, 170, 60), r=4)   # shoulder patch
        head(f, dead, r=80)
        hair(f, (60, 66, 70), r=80, style="wet")
        face(f, eye=(150, 160, 160), glow=(170, 185, 185) if not lag else (10, 10, 10), smile=0 if not lag else 3)
        f.rect((CX + 60, 420, CX + 140, 446), (220, 220, 210), r=3)  # name tag
        cap(f, (30, 40, 56))
        img = f.finish(seed=7, wet=True, key_str=0.3, rim_str=0.9, desat=0.5)
        save(img, name, out)

    # ---- Smiling woman: three stages
    for stage, name in ((1, "cust_smile"), (2, "cust_smile_2"), (3, "cust_smile_3")):
        f = Figure()
        coat = (110, 40, 70) if stage < 3 else (60, 24, 40)
        body(f, coat, shoulders=135 if stage < 3 else 120, hip=118, long_coat=True)
        f.poly([(CX - 30, 330), (CX + 30, 330), (CX, 420)], (230, 220, 200))  # blouse collar
        neck_len = 0 if stage < 3 else 40
        head(f, (215, 190, 170) if stage < 3 else (190, 185, 175), y=200 - neck_len, r=72, neck=26 + (0 if stage < 3 else -6))
        hair(f, (90, 50, 30), y=200 - neck_len, r=72, style="curls")
        face(f, y=200 - neck_len, smile=stage * 2 + 1, glow=(30, 20, 20) if stage < 3 else (5, 5, 5))
        img = f.finish(seed=8 + stage, rim=EMERALD if stage == 1 else VIOLET, rim_str=0.6 + stage * 0.2)
        save(img, name, out)

    # ---- Ellis Crane: dry and wet
    for wet, name in ((False, "cust_crane"), (True, "cust_crane_wet")):
        f = Figure()
        body(f, (92, 66, 44), shoulders=145, hip=125)
        f.poly([(CX - 26, 330), (CX + 26, 330), (CX + 10, 600), (CX - 10, 600)], (120, 40, 40))  # tie
        f.poly([(CX - 70, 330), (CX - 26, 330), (CX - 10, 520)], (220, 215, 200))
        f.poly([(CX + 70, 330), (CX + 26, 330), (CX + 10, 520)], (220, 215, 200))
        head(f, skin if not wet else pale, r=74)
        hair(f, (70, 60, 50) if not wet else (50, 50, 50), r=74, style="short" if not wet else "wet")
        face(f, eye=(30, 30, 30))
        for sx in (-1, 1):  # fogged glasses
            f.rect((CX + sx * 30 - 24, 180, CX + sx * 30 + 24, 212), (220, 230, 235, 150), r=8)
        f.line([(CX - 6, 196), (CX + 6, 196)], (40, 40, 40), 3)
        f.rect((CX + 120, 600, CX + 230, 700), (60, 40, 25), r=6)  # briefcase
        save(f.finish(seed=12 + wet, wet=wet, desat=0.3 + 0.3 * wet), name, out)

    # ---- The Collector: tall, pinstripe, wide hat, no face
    for name, true_form in (("cust_collector", False), ("cust_collector_true", True)):
        f = Figure()
        coat = (24, 24, 30)
        body(f, coat, shoulders=150, top=300, hip=150, long_coat=True)
        stripes = Figure()
        body(stripes, (255, 255, 255, 255), shoulders=150, top=300, hip=150, long_coat=True)
        tex = Image.new("RGBA", (1200, 1600), (24, 24, 30, 255))
        td = ImageDraw.Draw(tex)
        for x in range(0, 1200, 26):
            td.line([(x, 0), (x, 1600)], fill=(70, 70, 82, 255), width=2)
        apply_texture(f, stripes.img, tex)
        f.rect((CX - 30, 200, CX + 30, 320), (12, 12, 14))
        f.ellipse((CX - 70, 60, CX + 70, 240), (6, 6, 8))  # the face that isn't
        f.ellipse((CX - 170, 90, CX + 170, 130), (16, 16, 20))   # hat brim
        f.rect((CX - 80, 10, CX + 80, 110), (16, 16, 20), r=12)
        f.rect((CX - 80, 85, CX + 80, 100), (60, 20, 30))
        f.rect((CX - 170, 560, CX - 60, 700), (12, 10, 10), r=6)  # ledger
        f.rect((CX - 165, 565, CX - 160, 695), (180, 160, 120))
        if true_form:
            for i in range(9):
                f.line([(CX, 160), (CX + (i - 4) * 70, 160 + 40 * ((i % 3) + 1))], (6, 6, 8), 10)
        save(f.finish(seed=20 + true_form, key_str=0.25, rim=VIOLET, rim_str=1.1, desat=0.1), name, out)

    # ---- Lily: small, yellow raincoat
    f = Figure()
    s = 0.72
    f.ellipse((CX - 130, 400, CX + 130, 560), (230, 190, 30))
    f.poly([(CX - 120, 470), (CX + 120, 470), (CX + 150, 820), (CX - 150, 820)], (230, 190, 30))
    for y in (560, 640, 720):
        f.ellipse((CX - 8, y, CX + 8, y + 16), (60, 50, 20))
    head(f, (225, 195, 170), y=380, r=58, neck=20)
    f.ellipse((CX - 82, 280, CX + 82, 420), (230, 190, 30))   # hood
    f.ellipse((CX - 56, 318, CX + 56, 440), (225, 195, 170))
    hair(f, (120, 60, 30), y=380, r=58, style="short")
    f.ellipse((CX - 60, 305, CX + 60, 345), (230, 190, 30))
    face(f, y=385)
    save(f.finish(seed=30, wet=True, key_str=0.5, rim_str=0.7, desat=0.1), "cust_lily", out, crop=False)

    # ---- Mara Quill: uniform, badge, wet hair; and the mimic
    for name, mimic in (("cust_mara", False), ("cust_mara_mimic", True)):
        f = Figure()
        body(f, (34, 84, 74), shoulders=135, hip=115)
        f.poly([(CX - 40, 330), (CX + 40, 330), (CX, 400)], (200, 200, 190))
        head(f, (190, 170, 160), r=70)
        hair(f, (40, 30, 30), r=70, style="wet" if not mimic else "long")
        face(f, smile=0 if not mimic else 3, glow=(40, 80, 60) if not mimic else (5, 5, 5))
        f.rect((CX + 50, 400, CX + 130, 440), (230, 230, 220), r=4)  # badge
        f.ellipse((CX + 80, 405, CX + 108, 435), (30, 30, 30))      # "0"
        f.ellipse((CX + 86, 411, CX + 102, 429), (230, 230, 220))
        save(f.finish(seed=40 + mimic, wet=not mimic, rim=EMERALD if not mimic else VIOLET), name, out)

    # ---- Officer
    f = Figure()
    body(f, (30, 40, 60), shoulders=150, hip=125)
    f.rect((CX + 60, 380, CX + 110, 420), (200, 170, 60), r=6)
    head(f, (170, 125, 95), r=74)
    hair(f, (40, 30, 25), r=74, style="short")
    face(f)
    f.ellipse((CX - 110, 90, CX + 110, 135), (26, 30, 44))
    f.rect((CX - 76, 50, CX + 76, 120), (26, 30, 44), r=16)
    save(f.finish(seed=50), "cust_officer", out)

    # ---- Mr. Hale: gray windbreaker, healthy, too healthy
    f = Figure()
    body(f, (110, 112, 118), shoulders=150, hip=130)
    f.rect((CX - 140, 380, CX - 60, 410), (34, 84, 74), r=4)  # Meridian logo patch
    head(f, (226, 170, 140), r=76)
    hair(f, (150, 150, 150), r=76, style="short")
    face(f, smile=1)
    save(f.finish(seed=51, key_str=0.8, desat=0.0, dark=0.75), "cust_hale", out)

    # ---- The double: Meridian uniform, face never finished
    f = Figure()
    body(f, (34, 84, 74), shoulders=140, hip=118)
    head(f, (170, 160, 150), r=72)
    hair(f, (40, 32, 30), r=72, style="short")
    img = f.finish(seed=52)
    blur = img.crop((200, 100, 400, 300)).filter(ImageFilter.GaussianBlur(14))
    img.paste(blur, (200, 100), blur)
    save(img, "cust_double", out)

    # ---- Motorist, jogger
    f = Figure()
    body(f, (40, 70, 110), shoulders=140, hip=120)
    head(f, (200, 160, 130), r=74)
    hair(f, (60, 40, 30), r=74, style="short")
    face(f)
    save(f.finish(seed=53), "cust_motorist", out)

    f = Figure()
    body(f, (40, 40, 46), shoulders=128, hip=108)
    for x0 in (-90, 60):
        f.rect((CX + x0, 330, CX + x0 + 30, 820), (210, 230, 60))
    head(f, (190, 140, 110), r=68)
    hair(f, (30, 20, 15), r=68, style="long")
    face(f)
    save(f.finish(seed=54), "cust_jogger", out)

    # ---- The Thirteen: crowd of wet silhouettes
    f = Figure()
    for i, x in enumerate(range(40, 580, 42)):
        hgt = 260 + (i * 37) % 120
        y = 820 - hgt
        f.ellipse((x - 20, y - 46, x + 20, y), (30, 36, 44))
        f.poly([(x - 26, y), (x + 26, y), (x + 34, 820), (x - 34, 820)], (24, 30, 38))
    f.ellipse((290, 600, 320, 630), (230, 190, 30))
    save(f.finish(seed=60, wet=True, key_str=0.15, rim_str=0.5, desat=0.7), "cust_thirteen", out, crop=False)
