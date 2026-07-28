#!/usr/bin/env python3
"""
Procedural isometric building texture generator.
Produces a 3D isometric box sprite matching the C&C2 tile map perspective.

C&C2 direction convention:
  - gx+1 → upper-right (East),  gy+1 → upper-left (North)
  - hz+1 → straight up on screen (-Y)

Projection math (matches FNARTS.Core.CoordUtil):
  wx = (gx - gy) * HALF_TILE_W
  wy = -(gx + gy) * HALF_TILE_H - hz * TILE_HEIGHT

Usage:
  python tools/build_building_tex.py --east 3 --north 2 --height 1
  python tools/build_building_tex.py --east 3 --north 2 --height 2 -o my_building.png
"""

import argparse
import math
from PIL import Image

# ── Constants (match CoordUtil.cs) ────────────────────────────────────
HALF_TILE_W = 32
HALF_TILE_H = 16
TILE_HEIGHT  = 32


# ── Geometry ──────────────────────────────────────────────────────────

def world_to_grid_ground(wx, wy):
    """Inverse of the ground-level (hz=0) isometric projection.
    Returns (gx_ground, gy_ground) — the continuous grid coords
    of the point on the ground plane that projects to (wx, wy)."""
    gx = (wx / HALF_TILE_W - wy / HALF_TILE_H) / 2.0
    gy = (-wy / HALF_TILE_H - wx / HALF_TILE_W) / 2.0
    return gx, gy


class BuildingGeometry:
    """Pre-computes texture layout and provides per-pixel face tests."""

    def __init__(self, E, N, H):
        self.E = E  # tiles along East  (gx axis, upper-right on screen)
        self.N = N  # tiles along North (gy axis, upper-left on screen)
        self.H = H  # tile-units tall

        # Anchor = footprint centre in continuous grid space (E/2, N/2, 0)
        centre_gx = E / 2.0
        centre_gy = N / 2.0
        self.anchor_wx = (centre_gx - centre_gy) * HALF_TILE_W
        self.anchor_wy = -(centre_gx + centre_gy) * HALF_TILE_H  # hz=0

        # Texture dimensions (anchor-centred)
        self.tex_w = int((E + N) * HALF_TILE_W)
        self.tex_h = int((E + N) * HALF_TILE_H + H * (2 * TILE_HEIGHT))

    # ── Per-face tests ────────────────────────────────────────────────

    def test_top_face(self, gx_ground, gy_ground):
        """Roof: gx in [0,E], gy in [0,N], hz=H.
        Height shifts apparent grid position by +H in both axes."""
        gx = gx_ground - self.H
        gy = gy_ground - self.H
        return 0 <= gx <= self.E and 0 <= gy <= self.N

    def test_south_wall(self, gx_ground, gy_ground):
        """South wall (lower-right face): gy=0, gx in [0,E], hz in [0,H]."""
        hz = gy_ground
        gx = gx_ground - hz
        return 0 <= hz <= self.H and 0 <= gx <= self.E

    def test_west_wall(self, gx_ground, gy_ground):
        """West wall (lower-left face): gx=0, gy in [0,N], hz in [0,H]."""
        hz = gx_ground
        gy = gy_ground - hz
        return 0 <= hz <= self.H and 0 <= gy <= self.N

    # ── Convenience ───────────────────────────────────────────────────

    def pixel_to_ground(self, px, py):
        """Convert texture pixel to ground-level grid coords."""
        wx = self.anchor_wx + (px - self.tex_w / 2.0)
        wy = self.anchor_wy + (py - self.tex_h / 2.0)
        return world_to_grid_ground(wx, wy)

    def classify_pixel(self, px, py):
        """Return face name and hz for a pixel, or (None, 0) if transparent."""
        gx, gy = self.pixel_to_ground(px, py)
        if self.test_top_face(gx, gy):
            return 'top', self.H
        if self.test_south_wall(gx, gy):
            hz = gy
            return 'south', hz
        if self.test_west_wall(gx, gy):
            hz = gx
            return 'west', hz
        return None, 0

    def get_vertices(self):
        """Return (top_verts, south_verts, west_verts) — each a list of
        (wx, wy) world-coordinate tuples for the face's corners."""
        E, N, H = self.E, self.N, self.H

        top = [
            (0,            -(0 + 0) * HALF_TILE_H     - H * TILE_HEIGHT),  # (0,0,H)
            ((E - 0) * 32, -(E + 0) * HALF_TILE_H     - H * TILE_HEIGHT),  # (E,0,H)
            ((E - N) * 32, -(E + N) * HALF_TILE_H     - H * TILE_HEIGHT),  # (E,N,H)
            ((0 - N) * 32, -(0 + N) * HALF_TILE_H     - H * TILE_HEIGHT),  # (0,N,H)
        ]
        south = [
            (0,            0),                                                 # (0,0,0)
            ((E - 0) * 32, -(E + 0) * HALF_TILE_H),                           # (E,0,0)
            ((E - 0) * 32, -(E + 0) * HALF_TILE_H     - H * TILE_HEIGHT),     # (E,0,H)
            (0,            -H * TILE_HEIGHT),                                  # (0,0,H)
        ]
        west = [
            (0,            0),                                                 # (0,0,0)
            (0,            -H * TILE_HEIGHT),                                  # (0,0,H)
            ((0 - N) * 32, -(0 + N) * HALF_TILE_H     - H * TILE_HEIGHT),     # (0,N,H)
            ((0 - N) * 32, -(0 + N) * HALF_TILE_H),                           # (0,N,0)
        ]
        return top, south, west

    def world_to_texel(self, wx, wy):
        """Convert world coords to texture pixel coords."""
        px = wx - self.anchor_wx + self.tex_w / 2.0
        py = wy - self.anchor_wy + self.tex_h / 2.0
        return px, py


# ── Colour helpers ────────────────────────────────────────────────────

def hex_to_rgba(hex_str):
    """#RRGGBB → (R, G, B, 255)."""
    h = hex_str.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255)


def shade(colour, factor):
    """Multiply RGB channels by factor, clamp to [0,255]."""
    r, g, b, a = colour
    return (
        max(0, min(255, int(r * factor))),
        max(0, min(255, int(g * factor))),
        max(0, min(255, int(b * factor))),
        a,
    )


# ── Renderer ──────────────────────────────────────────────────────────

class BuildingRenderer:
    def __init__(self, geom, roof_color, wall_color, edge_color, draw_edges=True):
        self.geom = geom
        self.roof_color = roof_color
        self.wall_color = wall_color
        self.edge_color = edge_color
        self.draw_edges = draw_edges

    def render(self):
        w, h = self.geom.tex_w, self.geom.tex_h
        img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
        px = img.load()

        # Phase 1: fill faces
        for y in range(h):
            for x in range(w):
                face, _ = self.geom.classify_pixel(x, y)
                if face == 'top':
                    px[x, y] = self.roof_color
                elif face == 'south':
                    px[x, y] = shade(self.wall_color, 0.60)
                elif face == 'west':
                    px[x, y] = shade(self.wall_color, 0.40)

        # Phase 2: edge outlines
        if self.draw_edges:
            top_v, south_v, west_v = self.geom.get_vertices()
            all_faces = [top_v, south_v, west_v]
            for face_verts in all_faces:
                n = len(face_verts)
                for i in range(n):
                    wx0, wy0 = face_verts[i]
                    wx1, wy1 = face_verts[(i + 1) % n]
                    tx0, ty0 = self.geom.world_to_texel(wx0, wy0)
                    tx1, ty1 = self.geom.world_to_texel(wx1, wy1)
                    self._draw_line(px, w, h, tx0, ty0, tx1, ty1)

        return img

    def _draw_line(self, pixels, img_w, img_h, x0, y0, x1, y1):
        """Bresenham line in texture space, clipped to image bounds."""
        x0, y0 = int(round(x0)), int(round(y0))
        x1, y1 = int(round(x1)), int(round(y1))
        dx = abs(x1 - x0)
        dy = -abs(y1 - y0)
        sx = 1 if x0 < x1 else -1
        sy = 1 if y0 < y1 else -1
        err = dx + dy

        while True:
            if 0 <= x0 < img_w and 0 <= y0 < img_h:
                pixels[x0, y0] = self.edge_color
            if x0 == x1 and y0 == y1:
                break
            e2 = 2 * err
            if e2 >= dy:
                err += dy
                x0 += sx
            if e2 <= dx:
                err += dx
                y0 += sy


# ── CLI ───────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(
        description='Generate a procedural isometric building texture.')
    parser.add_argument('--east', type=int, required=True,
                        help='Building width in tiles along East (gx) axis')
    parser.add_argument('--north', type=int, required=True,
                        help='Building depth in tiles along North (gy) axis')
    parser.add_argument('--height', type=int, required=True,
                        help='Building height in tile units')
    parser.add_argument('--output', '-o', type=str, default=None,
                        help='Output PNG path (default: building_E{N}_N{N}_H{H}.png)')
    parser.add_argument('--roof-color', type=str, default='#A0A0B0',
                        help='Roof face colour (hex, default: #A0A0B0)')
    parser.add_argument('--wall-color', type=str, default='#8C8CA0',
                        help='Wall base colour (hex, default: #8C8CA0)')
    parser.add_argument('--edge-color', type=str, default='#404050',
                        help='Edge outline colour (hex, default: #404050)')
    parser.add_argument('--no-edges', action='store_true',
                        help='Disable edge outline rendering')
    args = parser.parse_args()

    if args.east < 1 or args.north < 1 or args.height < 1:
        parser.error('E, N, H must be >= 1')

    geom = BuildingGeometry(args.east, args.north, args.height)
    renderer = BuildingRenderer(
        geom,
        roof_color=hex_to_rgba(args.roof_color),
        wall_color=hex_to_rgba(args.wall_color),
        edge_color=hex_to_rgba(args.edge_color),
        draw_edges=not args.no_edges,
    )

    img = renderer.render()

    out_path = args.output or f'building_E{args.east}_N{args.north}_H{args.height}.png'
    img.save(out_path)
    print(f'Saved {geom.tex_w}x{geom.tex_h} texture → {out_path}')
    print(f'  Anchor (world): ({geom.anchor_wx:.0f}, {geom.anchor_wy:.0f})')
    print(f'  Anchor (texel): ({geom.tex_w/2:.0f}, {geom.tex_h/2:.0f})')


if __name__ == '__main__':
    main()
