#!/usr/bin/env python3
"""
Blender script for exporting RTS tank models to GLB with part split.

Exports a .blend tank model to a single GLB file that preserves the
part split (Hull / Turret / Barrel / Track* / Wheel*) required by the
game's dynamic tank rendering (hull yaw, turret yaw, wheel spin).

The script strips everything the game does not need — lights, cameras,
animations, armatures, shape keys — and validates the model against the
naming and pivot conventions described in docs/BLENDER_TANK_GUIDE.md.

Usage
-----

  # Single model (batch mode, auto-quit)
  blender --background --python tools/export_tank.py -- \
      --model models/tank.blend \
      --output data/models/tank.glb

  # Validate only (no export)
  blender --background --python tools/export_tank.py -- \
      --model models/tank.blend --check-only

  # Batch from JSON manifest
  blender --background --python tools/export_tank.py -- \
      --from-json tools/tanks_manifest.json --batch

  # Strict mode: validation warnings become errors
  blender --background --python tools/export_tank.py -- \
      --model models/tank.blend --strict

  # Inside Blender's Scripting workspace:
  import export_tank
  export_tank.export_tank(
      blend_path="models/tank.blend",
      output_path="data/models/tank.glb",
  )

JSON Manifest Format
--------------------
[
  {
    "id": "tank",
    "model": "models/tank.blend",
    "output": "data/models/tank.glb",
    "collection": ""           # optional collection filter
  }
]
"""

from __future__ import annotations

import argparse
import json
import os
import sys

try:
    import bpy
    from mathutils import Vector
    HAS_BPY = True
except ImportError:
    HAS_BPY = False


# ── Part naming conventions (see docs/BLENDER_TANK_GUIDE.md) ─────────

REQUIRED_PARTS = ('hull', 'turret')

PART_LABELS = {
    'hull':   'Hull (body) — required',
    'turret': 'Turret — required',
    'barrel': 'Barrel (optional, child of Turret)',
    'track':  'Track (static, future UV scroll)',
    'wheel':  'Wheel (spins around local X axis)',
    'static': 'Static detail (merged into hull by the game)',
}

# Triangle budgets per part and total
BUDGETS = {
    'hull':   2000,
    'turret': 1500,
    'barrel': 800,
    'track':  800,
    'wheel':  300,
    'static': 1000,
}
TOTAL_BUDGET = 5000


def classify_part(name: str) -> str:
    """Map a mesh object name to its part role via prefix matching."""
    n = name.lower()
    for prefix, part in (('hull', 'hull'), ('turret', 'turret'),
                         ('barrel', 'barrel'), ('track', 'track'),
                         ('wheel', 'wheel')):
        if n.startswith(prefix):
            return part
    return 'static'


# ── Helpers ────────────────────────────────────────────────────────────

def _world_bbox(obj):
    """World-space axis-aligned bounds of an object: (mins, maxs)."""
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    return ([min(c[i] for c in corners) for i in range(3)],
            [max(c[i] for c in corners) for i in range(3)])


def _mesh_tri_count(obj, depsgraph) -> int:
    """Evaluate the mesh (modifiers applied) and count triangles."""
    eval_obj = obj.evaluated_get(depsgraph)
    mesh = eval_obj.to_mesh()
    try:
        mesh.calc_loop_triangles()
        return len(mesh.loop_triangles)
    finally:
        eval_obj.to_mesh_clear()


def _has_vertex_colors(mesh) -> bool:
    if hasattr(mesh, 'color_attributes') and any(
            a.domain == 'CORNER' and a.data_type in ('FLOAT_COLOR', 'BYTE_COLOR')
            for a in mesh.color_attributes):
        return True
    return hasattr(mesh, 'vertex_colors') and len(mesh.vertex_colors) > 0


def _material_count(mesh) -> int:
    return len([m for m in mesh.materials if m is not None])


# ── Scene preparation ─────────────────────────────────────────────────

def _strip_non_model_data():
    """Remove lights/cameras/armatures/actions etc. Keep only MESH + EMPTY."""
    for obj in list(bpy.data.objects):
        if obj.type not in ('MESH', 'EMPTY'):
            bpy.data.objects.remove(obj, do_unlink=True)

    for obj in bpy.data.objects:
        obj.animation_data_clear()
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)

    # Remove orphaned datablocks of non-model types
    for attr in ('cameras', 'lights', 'speakers', 'lattices', 'curves',
                 'metaballs', 'armatures', 'movieclips', 'sounds',
                 'grease_pencils', 'grease_pencils_v3'):
        coll = getattr(bpy.data, attr, None)
        if coll is None:
            continue
        for block in list(coll):
            if block.users == 0:
                coll.remove(block)


def _collection_objects(col) -> set:
    """All objects in a collection, including nested children."""
    objs = set(col.objects)
    for child in col.children:
        objs |= _collection_objects(child)
    return objs


def _filter_collection(collection_name: str):
    """Remove everything outside the named collection."""
    col = bpy.data.collections.get(collection_name)
    if col is None:
        raise ValueError(f"Collection '{collection_name}' not found in file")
    keep = _collection_objects(col)
    for obj in list(bpy.data.objects):
        if obj not in keep:
            bpy.data.objects.remove(obj, do_unlink=True)
    mesh_count = len([o for o in bpy.data.objects if o.type == 'MESH'])
    print(f"  Filtered to collection '{collection_name}': {mesh_count} mesh(es)")


# ── Validation ────────────────────────────────────────────────────────

def _validate_model():
    """Validate naming / pivots / budgets.

    Returns (errors, warnings, report) where report is a list of
    (name, part, tri_count, material_count) tuples for the summary.
    """
    errors, warnings, info = [], [], []
    report = []

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    if not meshes:
        errors.append("No mesh objects found in the file")
        return errors, warnings, report

    parts = {}
    for obj in meshes:
        parts.setdefault(classify_part(obj.name), []).append(obj)

    # ── Required parts ────────────────────────────────────────────
    missing = [p for p in REQUIRED_PARTS if p not in parts]
    if missing:
        names = ', '.join(PART_LABELS[m].split(' — ')[0] for m in missing)
        errors.append(f"Missing required part(s): {names} "
                      "(name your meshes 'Hull*' and 'Turret*')")

    # Unrecognised names (possible typos)
    for obj in parts.get('static', []):
        warnings.append(f"'{obj.name}' does not match any part prefix — "
                        "treated as static detail (typo? see "
                        "docs/BLENDER_TANK_GUIDE.md)")

    depsgraph = bpy.context.evaluated_depsgraph_get()

    # ── Hull pivot checks (reference for turret checks) ───────────
    hull_bbox = None
    if 'hull' in parts:
        h = parts['hull'][0]
        hull_bbox = _world_bbox(h)
        ox, oy, oz = h.matrix_world.translation
        (minx, miny, minz), (maxx, maxy, maxz) = hull_bbox

        if not (minx - 0.1 <= ox <= maxx + 0.1 and
                miny - 0.1 <= oy <= maxy + 0.1):
            warnings.append(f"'Hull' origin ({ox:.1f}, {oy:.1f}) is outside the "
                            "hull footprint — body yaw will orbit off-centre")
        if abs(oz - minz) > 0.5:
            warnings.append(f"'Hull' origin is {oz - minz:.1f} above the ground "
                            "plane (should be at the hull bottom)")
        cx = (minx + maxx) / 2.0
        cy = (miny + maxy) / 2.0
        w = maxx - minx
        d = maxy - miny
        if w > 0 and abs(cx - ox) > 0.3 * w:
            warnings.append(f"'Hull' origin is far from the footprint centre "
                            f"(offset {cx - ox:.1f} on X)")
        if d > 0 and abs(cy - oy) > 0.3 * d:
            warnings.append(f"'Hull' origin is far from the footprint centre "
                            f"(offset {cy - oy:.1f} on Y)")

    # ── Per-part checks ───────────────────────────────────────────
    for part in ('hull', 'turret', 'barrel', 'track', 'wheel', 'static'):
        for obj in parts.get(part, []):
            name = obj.name
            tris = _mesh_tri_count(obj, depsgraph)
            origin = obj.matrix_world.translation
            (minx, miny, minz), (maxx, maxy, maxz) = _world_bbox(obj)
            mats = _material_count(obj.data)
            flags = []

            if part == 'turret' and hull_bbox:
                hull_minz = hull_bbox[0][2]
                hull_maxz = hull_bbox[1][2]
                if origin.z < hull_minz + 0.5:
                    flags.append("origin at ground level — pivot likely missing "
                                 "(must be at the turret ring)")
                elif origin.z > hull_maxz + 3.0:
                    flags.append(f"origin {origin.z - hull_maxz:.1f} above the "
                                 "hull top — check turret ring position")

            if part == 'wheel':
                if not (minx - 0.2 <= origin.x <= maxx + 0.2 and
                        miny - 0.2 <= origin.y <= maxy + 0.2 and
                        minz - 0.2 <= origin.z <= maxz + 0.2):
                    flags.append("origin not at the wheel axle centre")

            if any(abs(c - 1.0) > 0.01 for c in obj.scale):
                flags.append(f"scale {tuple(round(c, 2) for c in obj.scale)} != 1 "
                             "(apply transforms with Ctrl+A)")
            if any(abs(r) > 0.01 for r in obj.rotation_euler):
                flags.append("object is rotated — apply transforms with Ctrl+A")
            if obj.data.shape_keys is not None:
                flags.append("has shape keys (ignored at export)")
            if obj.modifiers:
                info.append(f"'{name}': {len(obj.modifiers)} modifier(s) — "
                            "applied at export")
            if mats == 0 and not _has_vertex_colors(obj.data):
                flags.append("no material and no vertex colors — renders black")
            if tris > BUDGETS[part]:
                flags.append(f"{tris:,} tris exceeds {BUDGETS[part]:,} budget")

            for f in flags:
                warnings.append(f"'{name}': {f}")

            report.append((name, part, tris, mats))

    total = sum(r[2] for r in report)
    if total > TOTAL_BUDGET:
        warnings.append(f"Total {total:,} tris exceeds {TOTAL_BUDGET:,} budget")

    return errors, warnings + info, report


# ── Export ────────────────────────────────────────────────────────────

def _gltf_export_params() -> dict:
    """Build GLB export params compatible with the running Blender version.

    The glTF exporter API changed across releases:
      - export_materials: bool (<= 4.x) → enum (5.x, 'EXPORT')
      - export_colors:    bool (<= 4.x) → removed (5.x, replaced by
        export_all_vertex_colors / export_vertex_color)
    """
    rna = bpy.ops.export_scene.gltf.get_rna_type().properties

    params = {
        'export_format': 'GLB',
        'export_texcoords': True,
        'export_normals': True,
        'export_apply': True,             # apply modifiers into the mesh
        'export_yup': True,               # glTF standard: Y-up
        'export_animations': False,       # game logic drives animation
        'export_skins': False,
        'export_morph': False,
        'export_lights': False,
        'export_cameras': False,
        'export_image_format': 'AUTO',    # embed textures into the GLB
    }

    if 'export_materials' in rna:
        prop = rna['export_materials']
        params['export_materials'] = ('EXPORT' if prop.type == 'ENUM' else True)

    if 'export_colors' in rna:
        params['export_colors'] = True
    if 'export_all_vertex_colors' in rna:
        params['export_all_vertex_colors'] = True

    return params


def _export_glb(output_path: str):
    """Export the scene to a single GLB (meshes + materials + textures only)."""
    params = _gltf_export_params()
    bpy.ops.export_scene.gltf(filepath=output_path, **params)


# ── Public API ────────────────────────────────────────────────────────

def export_tank(blend_path: str, output_path: str = '', collection: str = '',
                strict: bool = False, check_only: bool = False,
                open_file: bool = True) -> str:
    """Extract the tank model from a .blend file and export it as GLB.

    Args:
        blend_path: Path to the source .blend file.
        output_path: Output .glb path. Auto-generated if empty.
        collection: Optional collection name filter.
        strict: Fail on validation warnings (errors always fail).
        check_only: Validate and report without exporting.
        open_file: Open blend_path (False when the file is already open,
                   e.g. interactive use inside Blender).

    Returns:
        Absolute path to the exported GLB ('' in check-only mode).

    Raises:
        ValueError: On validation failure or missing file.
    """
    if not HAS_BPY:
        raise RuntimeError("export_tank requires Blender's bpy module")
    if not os.path.isfile(blend_path):
        raise ValueError(f"Model file not found: {blend_path}")

    if open_file:
        print(f"\nOpening: {blend_path}")
        bpy.ops.wm.open_mainfile(filepath=os.path.abspath(blend_path))

    # 1. Strip everything the game does not need
    _strip_non_model_data()

    # 2. Optional collection filter
    if collection:
        _filter_collection(collection)

    # 3. Validate
    errors, warnings, report = _validate_model()

    print("\n── Model Report ───────────────────────────────")
    for name, part, tris, mats in report:
        print(f"  {name:<16} {PART_LABELS[part]:<40} {tris:>6,} tris  {mats} mat(s)")
    total = sum(r[2] for r in report)
    print(f"  {'TOTAL':<16} {'':<40} {total:>6,} tris")

    print("\n── Validation ─────────────────────────────────")
    for msg in errors:
        print(f"  ✗ ERROR: {msg}")
    for msg in warnings:
        print(f"  ✗ WARN:  {msg}")
    if not errors and not warnings:
        print("  ✓ All checks passed")

    if errors:
        raise ValueError("Validation failed:\n  " + "\n  ".join(errors))
    if strict and warnings:
        raise ValueError("Strict mode — validation warnings:\n  "
                         + "\n  ".join(warnings))
    if check_only:
        return ""

    # 4. Export
    if not output_path:
        output_path = os.path.splitext(blend_path)[0] + '.glb'
    output_path = os.path.abspath(output_path)
    os.makedirs(os.path.dirname(output_path) or '.', exist_ok=True)

    print(f"\n── Export ─────────────────────────────────────")
    _export_glb(output_path)

    size_kb = os.path.getsize(output_path) / 1024
    print(f"  Done → {output_path}  ({size_kb:.1f} KB)")
    return output_path


# ── CLI ───────────────────────────────────────────────────────────────

def _find_script_argv():
    """Return argv after the '--' separator (Blender eats the rest)."""
    try:
        idx = sys.argv.index('--')
        return sys.argv[idx + 1:]
    except ValueError:
        return []


def _parse_args(argv):
    parser = argparse.ArgumentParser(
        description='Export RTS tank model from .blend to GLB.')
    parser.add_argument('--model', type=str, default=None,
                        help='Path to source .blend file')
    parser.add_argument('--output', '-o', type=str, default='',
                        help='Output .glb path (default: <model>.glb)')
    parser.add_argument('--collection', type=str, default='',
                        help='Only export this collection')
    parser.add_argument('--strict', action='store_true',
                        help='Fail on validation warnings')
    parser.add_argument('--check-only', action='store_true',
                        help='Validate and report without exporting')
    parser.add_argument('--from-json', type=str, default=None,
                        help='JSON manifest for batch export')
    parser.add_argument('--batch', action='store_true', default=False,
                        help='Auto-quit Blender after export')
    return parser.parse_args(argv)


def main():
    args = _parse_args(_find_script_argv())
    failed = False

    if args.from_json:
        try:
            with open(args.from_json, 'r', encoding='utf-8') as f:
                entries = json.load(f)
        except (OSError, json.JSONDecodeError) as e:
            print(f"Error reading manifest {args.from_json}: {e}")
            return 1

        for i, entry in enumerate(entries):
            print(f"\n{'#'*60}\n"
                  f"  Batch [{i+1}/{len(entries)}]: {entry.get('id', 'unnamed')}\n"
                  f"{'#'*60}")
            try:
                export_tank(
                    blend_path=entry['model'],
                    output_path=entry.get('output', ''),
                    collection=entry.get('collection', ''),
                    strict=args.strict,
                    check_only=args.check_only,
                )
            except Exception as e:
                print(f"  FAILED: {e}")
                failed = True
    else:
        if not args.model:
            print("Error: --model is required "
                  "(or use --from-json for batch mode)")
            return 2
        try:
            export_tank(
                blend_path=args.model,
                output_path=args.output,
                collection=args.collection,
                strict=args.strict,
                check_only=args.check_only,
            )
        except Exception as e:
            print(f"FAILED: {e}")
            failed = True

    if args.batch and HAS_BPY:
        bpy.ops.wm.quit_blender()
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
