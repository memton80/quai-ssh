"""Génère l'icône de Quai (bitte d'amarrage stylisée + invite de commande) en .ico et .png."""
import sys
from PIL import Image, ImageDraw

def render(size: int) -> Image.Image:
    scale = 4
    s = size * scale
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # Fond : carré arrondi, dégradé vertical bleu nuit -> bleu canard.
    top, bottom = (15, 76, 129), (11, 132, 148)
    grad = Image.new("RGBA", (s, s))
    gd = ImageDraw.Draw(grad)
    for y in range(s):
        t = y / max(1, s - 1)
        gd.line([(0, y), (s, y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,))
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.22), fill=255)
    img.paste(grad, (0, 0), mask)
    # Chevron d'invite ">".
    w = max(1, int(s * 0.085))
    white = (255, 255, 255, 255)
    x0, x1 = s * 0.24, s * 0.46
    y0, ym, y1 = s * 0.30, s * 0.47, s * 0.64
    d.line([(x0, y0), (x1, ym), (x0, y1)], fill=white, width=w, joint="curve")
    r = w / 2
    for (cx, cy) in ((x0, y0), (x1, ym), (x0, y1)):
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=white)
    # Curseur "_".
    d.rounded_rectangle([s * 0.52, s * 0.60, s * 0.76, s * 0.60 + w], radius=r, fill=white)
    # Ligne de quai (eau).
    d.rounded_rectangle([s * 0.20, s * 0.77, s * 0.80, s * 0.77 + w * 0.6], radius=r, fill=(255, 255, 255, 150))
    return img.resize((size, size), Image.LANCZOS)

out_ico, out_png = sys.argv[1], sys.argv[2]
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
big = render(256)
big.save(out_ico, format="ICO", sizes=[(n, n) for n in sizes], append_images=[render(n) for n in sizes[:-1]])
render(64).save(out_png, format="PNG")
