"""Draws the Spark mark and writes it as PNG and ICO (`E8-T32`).

`src/Spark.UI/Assets/spark-logo.svg` is the editable source of truth; this is how that geometry
becomes the two raster files Windows and Avalonia actually want. Run it after editing the SVG, and
keep the two in step - a mark whose PNG has drifted from its SVG is worse than one with no SVG.

Deliberately dependency-free: a 4x supersampled point-in-shape fill, then zlib and struct for the
PNG container and a hand-built ICO directory over PNG payloads. Pillow would be three lines and a
dependency this repository does not otherwise have, on a script that runs once a year.

Usage: python scripts/make-logo.py [output directory]
"""
import io
import os
import struct
import sys
import zlib

OUT = (sys.argv[1] if len(sys.argv) > 1
       else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "Spark.UI", "Assets"))

SS = 4                      # supersampling factor
RADIUS = 0.226              # corner radius, as a fraction of the side
TOP = (0xB7, 0x9B, 0xFF)    # accent, lightened
BOTTOM = (0x6E, 0x4C, 0xD8)
INK = (0xFF, 0xFF, 0xFF)


def rounded_rect(x, y, n, r):
    """True when (x, y) is inside an n-by-n rounded square of corner radius r."""
    if x < 0 or y < 0 or x > n or y > n:
        return False
    cx = min(max(x, r), n - r)
    cy = min(max(y, r), n - r)
    dx = x - cx
    dy = y - cy
    return dx * dx + dy * dy <= r * r


def star(cx, cy, outer, inner, points=4, rotation=0.0):
    """A concave star as a list of vertices, alternating outer and inner radius."""
    import math
    verts = []
    for i in range(points * 2):
        angle = rotation + (math.pi * i / points)
        radius = outer if i % 2 == 0 else inner
        verts.append((cx + radius * math.sin(angle), cy - radius * math.cos(angle)))
    return verts


def inside(poly, x, y):
    """Even-odd point-in-polygon."""
    n = len(poly)
    result = False
    j = n - 1
    for i in range(n):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y):
            if x < (xj - xi) * (y - yi) / (yj - yi) + xi:
                result = not result
        j = i
    return result


def render(size):
    n = size * SS
    r = RADIUS * n

    # The big spark sits slightly left of centre with a small companion above right, which is
    # what stops the mark reading as a plain star and makes it legible at 16 px.
    big = star(n * 0.455, n * 0.545, n * 0.335, n * 0.096)
    small = star(n * 0.735, n * 0.275, n * 0.135, n * 0.040)

    rows = []
    for py in range(size):
        row = bytearray()
        for px in range(size):
            r_acc = g_acc = b_acc = a_acc = 0
            for sy in range(SS):
                for sx in range(SS):
                    x = px * SS + sx + 0.5
                    y = py * SS + sy + 0.5
                    if not rounded_rect(x, y, n, r):
                        continue

                    t = y / n
                    br = int(TOP[0] + (BOTTOM[0] - TOP[0]) * t)
                    bg = int(TOP[1] + (BOTTOM[1] - TOP[1]) * t)
                    bb = int(TOP[2] + (BOTTOM[2] - TOP[2]) * t)

                    if inside(big, x, y) or inside(small, x, y):
                        br, bg, bb = INK

                    r_acc += br
                    g_acc += bg
                    b_acc += bb
                    a_acc += 255

            total = SS * SS
            if a_acc == 0:
                row += bytes((0, 0, 0, 0))
            else:
                covered = a_acc // 255
                row += bytes((r_acc // covered, g_acc // covered, b_acc // covered, a_acc // total))
        rows.append(bytes(row))
    return rows


def png(rows, size):
    raw = b''.join(b'\x00' + row for row in rows)

    def chunk(tag, data):
        body = tag + data
        return struct.pack('>I', len(data)) + body + struct.pack('>I', zlib.crc32(body) & 0xFFFFFFFF)

    header = struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)
    return (b'\x89PNG\r\n\x1a\n'
            + chunk(b'IHDR', header)
            + chunk(b'IDAT', zlib.compress(raw, 9))
            + chunk(b'IEND', b''))


def ico(images):
    """An ICO whose entries are PNG payloads, which every Windows since Vista reads."""
    count = len(images)
    out = struct.pack('<HHH', 0, 1, count)
    offset = 6 + 16 * count
    for size, data in images:
        out += struct.pack('<BBBBHHII',
                           0 if size >= 256 else size,
                           0 if size >= 256 else size,
                           0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for _, data in images:
        out += data
    return out


os.makedirs(OUT, exist_ok=True)

images = []
for size in (16, 24, 32, 48, 64, 128, 256):
    data = png(render(size), size)
    images.append((size, data))
    if size == 256:
        io.open(os.path.join(OUT, 'spark-logo.png'), 'wb').write(data)

io.open(os.path.join(OUT, 'spark-logo.ico'), 'wb').write(ico(images))
print('wrote', OUT)
