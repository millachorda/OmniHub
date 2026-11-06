# Genera el logo y el icono de OmniHub (ejecutar: python Assets/make_icon.py)
import math, os
from PIL import Image, ImageDraw, ImageFilter

here = os.path.dirname(os.path.abspath(__file__))
S = 512

def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
# fondo redondeado oscuro
bg = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(bg).rounded_rectangle([16, 16, S - 16, S - 16], radius=110, fill=(16, 18, 26, 255))
img.alpha_composite(bg)

# anillo con degradado cian -> violeta
ring = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(ring)
c1, c2 = (0, 229, 255), (140, 82, 255)
cx = cy = S / 2
for i in range(360):
    t = (math.sin(math.radians(i)) + 1) / 2
    col = lerp(c1, c2, t) + (255,)
    d.arc([96, 96, S - 96, S - 96], i, i + 2, fill=col, width=46)
glow = ring.filter(ImageFilter.GaussianBlur(18))
img.alpha_composite(glow)
img.alpha_composite(ring)

# punto central
dot = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(dot).ellipse([cx - 52, cy - 52, cx + 52, cy + 52], fill=(232, 236, 243, 255))
img.alpha_composite(dot)

img.save(os.path.join(here, "omnihub.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
# Logo para la interfaz: solo el anillo, sin fondo (queda bien al girar en el launcher)
logo = Image.new("RGBA", (S, S), (0, 0, 0, 0))
logo.alpha_composite(glow)
logo.alpha_composite(ring)
logo.alpha_composite(dot)
logo.save(os.path.join(here, "logo.png"))
print("ok")
