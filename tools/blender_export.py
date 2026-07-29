#!/usr/bin/env python3
"""
Blender script for exporting isometric RTS building textures.

Produces 2:1 dimetric sprites matching the FNA_RTS game projection
using 三渲二 (NPR cel-shading) techniques — flat toon shading,
hard light transitions, and crisp edge outlines.

Usage
-----

  # Single building (batch mode)
  blender --background --python tools/blender_export.py -- \\
      --model models/barracks.blend \\
      --east 3 --north 2 --height 2 \\
      --output data/textures/buildings/barracks.png

  # Batch from JSON manifest
  blender --background --python tools/blender_export.py -- \\
      --from-json tools/buildings_manifest.json --batch

  # Interactive (opens Blender, sets up scene for manual tweaking)
  blender --python tools/blender_export.py

  # Inside Blender's Scripting workspace:
  import blender_export
  blender_export.export_building(
      model_path="models/barracks.blend",
      east=3, north=2, height=2,
      output_path="barracks.png",
  )

JSON Manifest Format
--------------------
[
  {
    "id": "barracks",
    "east": 3, "north": 2, "height": 2,
    "model": "models/barracks.blend",
    "output": "data/textures/buildings/barracks.png",
    "roof_color": "#C0A060",
    "wall_color": "#8B7355",
    "outline_method": "freestyle"
  }
]
"""

from __future__ import annotations
import json
import os
import sys
from typing import Optional

# Ensure tools/ is importable
_tools_dir = os.path.dirname(os.path.abspath(__file__))
if _tools_dir not in sys.path:
    sys.path.insert(0, _tools_dir)

try:
    import bpy
    HAS_BPY = True
except ImportError:
    HAS_BPY = False

from blender import export_pipeline
from blender import camera_setup
from blender import npr_materials


# ── Public API (usable from Blender's Python console) ───────────────────

def export_building(
    model_path: str,
    east: int,
    north: int,
    height: int,
    output_path: str = "",
    roof_color: str = "#A0A0B0",
    wall_color: str = "#8C8CA0",
    edge_color: str = "#404050",
    edge_thickness: float = 1.5,
    no_edges: bool = False,
    outline_method: str = "freestyle",
    collection: str = "",
    verify: bool = False,
) -> str:
    """Export a single building model to an isometric sprite texture.

    Args:
        model_path: Path to .blend, .fbx, .obj, .gltf/.glb file.
        east: Building width in tiles (East / gx axis).
        north: Building depth in tiles (North / gy axis).
        height: Building height in tile units.
        output_path: Output PNG path. Auto-generated if empty.
        roof_color: Hex colour for roof face.
        wall_color: Hex colour for wall faces.
        edge_color: Hex colour for edge outlines.
        edge_thickness: Outline width in pixels.
        no_edges: Disable edge outlines entirely.
        outline_method: 'freestyle', 'compositor', or 'none'.
        collection: Collection name (for .blend files). Auto-detected if empty.
        verify: Run pixel-level verification after export.

    Returns:
        Absolute path to the rendered PNG.
    """
    config = {
        'east': east,
        'north': north,
        'height': height,
        'model_path': model_path,
        'collection': collection,
        'output_path': output_path,
        'roof_color': npr_materials.hex_to_rgba(roof_color),
        'wall_color': npr_materials.hex_to_rgba(wall_color),
        'edge_color': npr_materials.hex_to_rgba(edge_color),
        'edge_thickness': edge_thickness,
        'no_edges': no_edges,
        'outline_method': outline_method,
        'verify': verify,
    }
    return export_pipeline.export_building(config)


def export_batch(manifest_path: str) -> list:
    """Export multiple buildings from a JSON manifest file.

    Args:
        manifest_path: Path to JSON file with array of building specs.

    Returns:
        List of absolute output paths.
    """
    with open(manifest_path, 'r', encoding='utf-8') as f:
        entries = json.load(f)

    results = []
    for i, entry in enumerate(entries):
        print(f"\n{'#'*60}")
        print(f"  Batch [{i+1}/{len(entries)}]: {entry.get('id', 'unnamed')}")
        print(f"{'#'*60}")

        out = export_building(
            model_path=entry['model'],
            east=entry['east'],
            north=entry['north'],
            height=entry['height'],
            output_path=entry.get('output', ''),
            roof_color=entry.get('roof_color', '#A0A0B0'),
            wall_color=entry.get('wall_color', '#8C8CA0'),
            edge_color=entry.get('edge_color', '#404050'),
            edge_thickness=entry.get('edge_thickness', 1.5),
            no_edges=entry.get('no_edges', False),
            outline_method=entry.get('outline_method', 'freestyle'),
            collection=entry.get('collection', ''),
            verify=entry.get('verify', False),
        )
        results.append(out)

    return results


# ── CLI (argparse) ──────────────────────────────────────────────────────

def _parse_args(argv: list):
    """Parse command-line arguments after the '--' separator.

    Blender eats everything before '--' as its own args.
    Everything after '--' is forwarded to the Python script.
    """
    import argparse

    parser = argparse.ArgumentParser(
        description='Export isometric RTS building texture from Blender model.',
    )

    # Building dimensions
    parser.add_argument('--east', type=int, default=None,
                        help='Building width in tiles (East / gx axis)')
    parser.add_argument('--north', type=int, default=None,
                        help='Building depth in tiles (North / gy axis)')
    parser.add_argument('--height', type=int, default=None,
                        help='Building height in tile units')

    # Model input
    parser.add_argument('--model', type=str, default=None,
                        help='Path to .blend, .fbx, .obj, or .gltf/.glb file')
    parser.add_argument('--collection', type=str, default='',
                        help='Collection name within .blend file (default: auto-detect)')

    # Output
    parser.add_argument('--output', '-o', type=str, default='',
                        help='Output PNG path (default: building_E{N}_N{N}_H{H}.png)')

    # Colours
    parser.add_argument('--roof-color', type=str, default='#A0A0B0')
    parser.add_argument('--wall-color', type=str, default='#8C8CA0')
    parser.add_argument('--edge-color', type=str, default='#404050')
    parser.add_argument('--edge-thickness', type=float, default=1.5)
    parser.add_argument('--no-edges', action='store_true')
    parser.add_argument('--outline-method', type=str,
                        choices=['freestyle', 'compositor', 'none'],
                        default='freestyle')

    # Batch
    parser.add_argument('--from-json', type=str, default=None,
                        help='JSON manifest file with array of building specs')
    parser.add_argument('--batch', action='store_true', default=False,
                        help='Auto-quit after export (for --background mode)')

    # Verify
    parser.add_argument('--verify', action='store_true', default=False,
                        help='Verify output against procedural reference')

    args = parser.parse_args(argv)
    return args


def _find_script_argv():
    """Find the '--' separator in sys.argv and return everything after it.

    Returns the script's argv list, or falls back to empty list.
    """
    try:
        idx = sys.argv.index('--')
        return sys.argv[idx + 1:]
    except ValueError:
        return []


# ── Main ────────────────────────────────────────────────────────────────

def main():
    """Dispatch: if running in Blender, parse CLI args and execute."""
    cli_args = _find_script_argv()
    args = _parse_args(cli_args)

    if args.from_json:
        # Batch mode from JSON manifest
        export_batch(args.from_json)
        if args.batch and HAS_BPY:
            bpy.ops.wm.quit_blender()
        return

    # Single export
    if args.east is None or args.north is None or args.height is None:
        print("Error: --east, --north, and --height are required for single export.")
        print("Use --from-json for batch mode, or provide all three dimensions.")
        sys.exit(1)

    if args.model is None:
        print("Error: --model is required for single export.")
        sys.exit(1)

    export_building(
        model_path=args.model,
        east=args.east,
        north=args.north,
        height=args.height,
        output_path=args.output,
        roof_color=args.roof_color,
        wall_color=args.wall_color,
        edge_color=args.edge_color,
        edge_thickness=args.edge_thickness,
        no_edges=args.no_edges,
        outline_method=args.outline_method,
        collection=args.collection,
        verify=args.verify,
    )

    if args.batch and HAS_BPY:
        bpy.ops.wm.quit_blender()


if __name__ == '__main__':
    main()
