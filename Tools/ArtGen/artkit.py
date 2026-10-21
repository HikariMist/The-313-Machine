"""Shared helpers for the procedural art generator (numpy + Pillow).

Everything is drawn at 2x and downsampled for anti-aliasing. Lighting model: a cold ambient
fill, a warm sodium-vapor key light from the upper left (the lot lamp) and an emerald rim from
the right (the vending machine glass). That pairing is the game's visual signature.
"""
import math
import random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageChops

SODIUM = (233, 176, 76)
EMERALD = (52, 211, 145)
VIOLET = (155, 109, 255)
NIGHT = (8, 10, 15)


def rng(seed):
    return random.Random(seed)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def vgradient(w, h, stops):
    """stops: list of (pos 0..1, (r,g,b))."""
    arr = np.zeros((h, w, 3), np.float32)
    ys = np.linspace(0, 1, h)
    for c in range(3):
        xp = [s[0] for s in stops]
        fp = [s[1][c] for s in stops]
        arr[:, :, c] = np.interp(ys, xp, fp)[:, None]
    return arr


def to_img(arr, mode="RGB"):
    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), mode)


def noise(w, h, scale, seed, octaves=4):
    """Cheap value noise via upscaled random grids."""
    r = np.random.RandomState(seed)
    out = np.zeros((h, w), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        gw, gh = max(2, int(w / scale) + 2), max(2, int(h / scale) + 2)
        grid = r.rand(gh, gw).astype(np.float32)
        img = Image.fromarray((grid * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
        out += np.asarray(img, np.float32) / 255 * amp
        total += amp
        amp *= 0.5
        scale /= 2
    return out / total


def grain(img, amount=10, seed=0):
    arr = np.asarray(img).astype(np.float32)
    r = np.random.RandomState(seed)
    n = r.normal(0, amount, arr.shape[:2])[:, :, None]
    if arr.shape[2] == 4:
        arr[:, :, :3] += n
    else:
        arr += n
    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), img.mode)


def radial(w, h, cx, cy, radius, color, strength=1.0, falloff=2.0):
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    d = np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2) / radius
    a = np.clip(1 - d, 0, 1) ** falloff * strength
    return a[:, :, None] * np.array(color, np.float32)[None, None, :]


def vignette(arr, strength=0.65):
    h, w = arr.shape[:2]
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    d = np.sqrt(((xs - w / 2) / (w / 2)) ** 2 + ((ys - h / 2) / (h / 2)) ** 2)
    v = 1 - np.clip(d - 0.5, 0, 1) * strength
    return arr * v[:, :, None]


class Figure:
    """A layered RGBA figure drawn at 2x. Shapes go onto a color layer; a mask tracks coverage
    so lighting can be applied consistently at the end."""

    def __init__(self, w=600, h=800, ss=2):
        self.ss = ss
        self.w, self.h = w * ss, h * ss
        self.img = Image.new("RGBA", (self.w, self.h), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)

    def S(self, *v):
        return [x * self.ss for x in v]

    def ellipse(self, box, fill):
        self.d.ellipse(self.S(*box), fill=fill)

    def poly(self, pts, fill):
        self.d.polygon([(x * self.ss, y * self.ss) for x, y in pts], fill=fill)

    def rect(self, box, fill, r=0):
        if r:
            self.d.rounded_rectangle(self.S(*box), radius=r * self.ss, fill=fill)
        else:
            self.d.rectangle(self.S(*box), fill=fill)

    def line(self, pts, fill, width):
        self.d.line([(x * self.ss, y * self.ss) for x, y in pts], fill=fill, width=int(width * self.ss), joint="curve")

    def arc(self, box, start, end, fill, width):
        self.d.arc(self.S(*box), start, end, fill=fill, width=int(width * self.ss))

    def overlay(self, other):
        self.img = Image.alpha_composite(self.img, other)
        self.d = ImageDraw.Draw(self.img)

    def layer(self):
        return Image.new("RGBA", (self.w, self.h), (0, 0, 0, 0))

    def finish(self, key=SODIUM, rim=EMERALD, key_str=0.55, rim_str=0.75, wet=False, seed=0, desat=0.25, dark=0.62):
        arr = np.asarray(self.img).astype(np.float32)
        rgb, a = arr[:, :, :3], arr[:, :, 3:4] / 255.0
        h, w = rgb.shape[:2]
        # Night grading: darken and desaturate the base colors.
        lum = rgb.mean(axis=2, keepdims=True)
        rgb = (rgb * (1 - desat) + lum * desat) * dark
        # Key light: warm, from upper left, falling off down the body.
        ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
        keymap = np.clip(1.15 - (xs / w) * 1.1 - (ys / h) * 0.7, 0, 1)[:, :, None] ** 1.3
        rgb = rgb * (0.35 + 0.8 * keymap) + np.array(key, np.float32) * keymap * key_str * 0.25
        # Rim light from the right: edge of the alpha mask, offset left.
        mask = Image.fromarray((a[:, :, 0] * 255).astype(np.uint8))
        shifted = ImageChops.offset(mask, -int(10 * self.ss), 0)
        edge = ImageChops.subtract(mask, shifted).filter(ImageFilter.GaussianBlur(4 * self.ss))
        e = np.asarray(edge).astype(np.float32)[:, :, None] / 255.0
        rgb = rgb + e * np.array(rim, np.float32) * rim_str
        if wet:
            r = np.random.RandomState(seed)
            streaks = np.zeros((h, w), np.float32)
            for _ in range(140):
                x = r.randint(0, w)
                y0 = r.randint(0, h)
                ln = r.randint(20, 160) * self.ss
                streaks[y0:y0 + ln, x:x + self.ss] = r.uniform(0.25, 0.8)
            streaks = np.asarray(Image.fromarray((streaks * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2 * self.ss))).astype(np.float32) / 255
            rgb = rgb + streaks[:, :, None] * np.array([150, 190, 210], np.float32) * a
        out = np.concatenate([np.clip(rgb, 0, 255), a * 255], axis=2).astype(np.uint8)
        img = Image.fromarray(out, "RGBA")
        img = img.resize((self.w // self.ss, self.h // self.ss), Image.LANCZOS)
        return grain(img, 5, seed)


def plaid(size, base, line1, line2, step=18):
    tile = Image.new("RGBA", size, base)
    d = ImageDraw.Draw(tile)
    for x in range(0, size[0], step):
        d.line([(x, 0), (x, size[1])], fill=line1, width=max(2, step // 4))
    for y in range(0, size[1], step):
        d.line([(0, y), (size[0], y)], fill=line2, width=max(2, step // 5))
    return tile


def apply_texture(fig, mask_layer, texture):
    """Fills the coverage of mask_layer with a texture image and composites onto the figure."""
    m = mask_layer.split()[3]
    tex = texture.resize(mask_layer.size)
    tex.putalpha(m)
    fig.overlay(tex)
