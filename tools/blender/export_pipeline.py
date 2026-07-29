"""
Full export pipeline: orchestrates scene setup, model import,
camera placement, NPR material application, edge outlines, and
final render to a PNG texture matching the game's isometric format.

Main entry point: export_building(config) → renders one building.
"""

from __future__ import annotations
import os
import sys
from typing import Dict, Optional, Tuple

try:
    import bpy
    HAS_BPY = True
except ImportError:
    HAS_BPY = False

# Ensure tools/ is importable when running from blender_export.py
_script_dir = os.path.dirname(os.path.abspath(__file__))
_tools_dir = os.path.dirname(_script_dir)
if _tools_dir not in sys.path:
    sys.path.insert(0, _tools_dir)

from blender import camera_setup
from blender import npr_materials
from blender import outlines


def _ensure_bpy():
    if not HAS_BPY:
        raise RuntimeError("export_pipeline requires Blender's bpy module")


# ── Default export config ──────────────────────────────────────────────

DEFAULT_CONFIG: dict = {
    'east': 3,
    'north': 2,
    'height': 2,
    'model_path': '',
    'collection': '',
    'output_path': '',
    'roof_color': (0.627, 0.627, 0.690, 1.0),   # #A0A0B0
    'wall_color': (0.549, 0.549, 0.627, 1.0),   # #8C8CA0
    'edge_color': (0.251, 0.251, 0.314, 1.0),   # #404050
    'edge_thickness': 1.5,
    'no_edges': False,
    'outline_method': 'freestyle',               # 'freestyle' | 'compositor' | 'none'
    'z_scale': camera_setup.Z_SCALE_CORRECTION,
    'verify': False,
}


# ── Pipeline ────────────────────────────────────────────────────────────

def export_building(config: dict) -> str:
    """Run the full export pipeline for one building.

    Args:
        config: Dict with keys matching DEFAULT_CONFIG.
                Required: east, north, height, model_path, output_path.

    Returns:
        Absolute path to the rendered PNG.
    """
    _ensure_bpy()

    # Merge with defaults
    cfg = {**DEFAULT_CONFIG, **config}

    _validate_config(cfg)

    east = cfg['east']
    north = cfg['north']
    height_tiles = cfg['height']
    model_path = cfg['model_path']
    output_path = cfg['output_path']

    tex_w, tex_h = camera_setup.tex_dims(east, north, height_tiles)

    print(f"\n{'='*60}")
    print(f"  Export: {east}×{north}×{height_tiles} tiles  →  {tex_w}×{tex_h} px")
    print(f"  Model:  {model_path}")
    print(f"  Output: {output_path}")
    print(f"{'='*60}")

    # ── 1. Fresh scene ─────────────────────────────────────────────
    _fresh_scene()

    # ── 2. Import model ────────────────────────────────────────────
    building_root = _import_model(model_path, cfg.get('collection', ''))

    # ── 3. Validate & align to game axes ───────────────────────────
    _validate_model_bounds(building_root, east, north, height_tiles)

    # ── 4. Apply Z-scale correction ────────────────────────────────
    building_root.scale.z *= cfg['z_scale']

    # ── 5. NPR materials ───────────────────────────────────────────
    materials = npr_materials.create_building_materials(
        roof_color=cfg['roof_color'],
        wall_color=cfg['wall_color'],
    )
    _apply_materials_recursive(building_root, materials)
    npr_materials.setup_npr_lighting()

    # ── 6. Camera ──────────────────────────────────────────────────
    cam_obj, cam_data, (tw, th) = camera_setup.setup_dimetric_camera(
        east, north, height_tiles)
    camera_setup.print_camera_info(cam_obj, tw, th, east, north, height_tiles)

    # ── 7. Render settings ─────────────────────────────────────────
    npr_materials.configure_eevee_npr()
    _configure_render_output(output_path, tw, th)

    # ── 8. Edge outlines ───────────────────────────────────────────
    if not cfg['no_edges']:
        _setup_outlines(cfg)

    # ── 9. Render ──────────────────────────────────────────────────
    print(f"\n  Rendering...")
    bpy.ops.render.render(write_still=True)
    print(f"  Rendered → {output_path}")

    # ── 10. Verify (optional) ──────────────────────────────────────
    if cfg.get('verify', False):
        _verify_output(output_path, east, north, height_tiles)

    print(f"\n  Done: {output_path}\n")
    return os.path.abspath(output_path)


# ── Helpers ────────────────────────────────────────────────────────────

def _validate_config(cfg: dict):
    """Check that required keys are present and sensible."""
    for key in ('east', 'north', 'height'):
        if cfg.get(key, 0) < 1:
            raise ValueError(f"'{key}' must be >= 1, got {cfg.get(key)}")

    if not cfg.get('model_path'):
        raise ValueError("'model_path' is required")

    if not cfg.get('output_path'):
        # Auto-generate
        e, n, h = cfg['east'], cfg['north'], cfg['height']
        cfg['output_path'] = f"building_E{e}_N{n}_H{h}.png"


def _fresh_scene():
    """Reset the scene to factory defaults."""
    bpy.ops.wm.read_factory_settings(use_empty=True)


def _import_model(model_path: str, collection_name: str = '') -> "bpy.types.Object":
    """Import or link a building model and return its root object.

    Supports .blend (append from collection), .fbx, .obj, .gltf/.glb.
    """
    ext = os.path.splitext(model_path)[1].lower()
    print(f"\n── Import ───────────────────────────────")

    if ext == '.blend':
        return _import_blend(model_path, collection_name)
    elif ext == '.fbx':
        bpy.ops.import_scene.fbx(filepath=model_path)
    elif ext == '.obj':
        bpy.ops.import_scene.obj(filepath=model_path)
    elif ext in ('.gltf', '.glb'):
        bpy.ops.import_scene.gltf(filepath=model_path)
    else:
        raise ValueError(f"Unsupported model format: {ext}")

    # After import, collect all mesh objects under a common parent
    return _group_imported_meshes(collection_name or 'BuildingRoot')


def _import_blend(blend_path: str, collection_name: str = '') -> "bpy.types.Object":
    """Append all objects from a named collection in another .blend file.

    If collection_name is empty, use the first collection found.
    """
    if not collection_name:
        # Discover collections in the source file
        with bpy.data.libraries.load(blend_path, link=False) as (data_from, data_to):
            if data_from.collections:
                collection_name = data_from.collections[0]
                print(f"  Auto-selected collection: '{collection_name}'")
            else:
                raise ValueError(f"No collections found in {blend_path}")

    # Append the collection
    col = None
    for src_col in bpy.data.collections:
        if src_col.name == collection_name and src_col.library:
            # Already linked? Remove and re-append
            pass

    # Append via the bpy.data.libraries API
    with bpy.data.libraries.load(blend_path, link=False) as (data_from, data_to):
        data_to.collections = [collection_name]

    new_col = data_to.collections[0] if data_to.collections else None
    if new_col is None:
        raise RuntimeError(f"Failed to append collection '{collection_name}' from {blend_path}")

    # Link collection into scene
    bpy.context.scene.collection.children.link(new_col)

    # Create a parent empty for transform manipulation
    return _group_imported_meshes(collection_name)


def _group_imported_meshes(name: str) -> "bpy.types.Object":
    """Create an empty parent for all mesh objects in the scene.

    This lets us apply Z-scale and other transforms to the entire
    building as a unit.
    """
    parent = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(parent)
    parent.empty_display_type = 'PLAIN_AXES'

    mesh_objects = [obj for obj in bpy.context.scene.objects
                    if obj.type == 'MESH' and obj.parent is None and obj != parent]

    for obj in mesh_objects:
        obj.parent = parent

    if not mesh_objects:
        print(f"  ⚠ No mesh objects found after import")

    print(f"  Grouped {len(mesh_objects)} mesh(es) under '{name}'")
    return parent


def _validate_model_bounds(root_obj, east: int, north: int, height_tiles: int):
    """Check that the model sits in the first quadrant with SW at origin.

    Issues a warning if the model extends significantly beyond the
    expected footprint — this helps catch coordinate convention errors.
    """
    _ensure_bpy()

    # Collect all mesh vertices in world space
    mesh_objs = [obj for obj in root_obj.children_recursive
                 if obj.type == 'MESH'] + ([root_obj] if root_obj.type == 'MESH' else [])

    if not mesh_objs:
        print(f"  [validate] No mesh objects to validate")
        return

    min_x = min_y = min_z = float('inf')
    max_x = max_y = max_z = float('-inf')

    for obj in mesh_objs:
        for v in obj.data.vertices:
            w = obj.matrix_world @ v.co
            min_x = min(min_x, w.x)
            max_x = max(max_x, w.x)
            min_y = min(min_y, w.y)
            max_y = max(max_y, w.y)
            min_z = min(min_z, w.z)
            max_z = max(max_z, w.z)

    print(f"  [validate] Model bounds: "
          f"X[{min_x:.1f}…{max_x:.1f}]  "
          f"Y[{min_y:.1f}…{max_y:.1f}]  "
          f"Z[{min_z:.1f}…{max_z:.1f}]")

    expected_x = east
    expected_y = north
    expected_z = height_tiles

    if min_x < -0.1:
        print(f"  ⚠ Model extends into negative X (min={min_x:.1f}). "
              f"Expected: SW corner at origin, first quadrant.")
    if min_y < -0.1:
        print(f"  ⚠ Model extends into negative Y (min={min_y:.1f}). "
              f"Expected: SW corner at origin, first quadrant.")
    if min_z < -0.1:
        print(f"  ⚠ Model extends below Z=0 (min={min_z:.1f}).")

    if max_x > expected_x * 1.3 or max_y > expected_y * 1.3 or max_z > expected_z * 1.3:
        print(f"  ⚠ Model extends beyond expected {expected_x}×{expected_y}×{expected_z}. "
              f"Texture may clip.")


def _apply_materials_recursive(obj, materials: dict):
    """Walk object tree and apply NPR materials to mesh objects.

    Assigns materials based on face orientation or material slot index:
      - Slot 0 / upward-facing  → roof
      - Slot 1 / Y-facing       → south wall
      - Slot 2 / X-facing       → west wall
    """
    for child in [obj] + list(obj.children_recursive):
        if child.type == 'MESH':
            npr_materials.apply_materials_to_object(child, materials)


def _configure_render_output(output_path: str, tex_w: int, tex_h: int):
    """Set render output path, format, and transparency."""
    scene = bpy.context.scene
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.image_settings.color_depth = '8'
    scene.render.image_settings.compression = 15
    scene.render.filepath = output_path


def _setup_outlines(cfg: dict):
    """Configure edge outlines based on config."""
    method = cfg.get('outline_method', 'freestyle')

    if method == 'freestyle':
        outlines.setup_freestyle_outlines(
            edge_color=cfg['edge_color'],
            thickness=cfg['edge_thickness'],
        )
    elif method == 'compositor':
        outlines.setup_compositor_outlines(
            edge_color=cfg['edge_color'],
            thickness=int(cfg['edge_thickness']),
        )
    else:
        outlines.disable_freestyle()
        outlines.clear_compositor_nodes()


def _verify_output(output_path: str, east: int, north: int, height_tiles: int):
    """Pixel-level verification against procedural reference.

    Requires the build_building_tex.py reference tool and Pillow.
    """
    try:
        from PIL import Image
        import subprocess
        import tempfile
    except ImportError:
        print("  [verify] Pillow not available; skipping verification")
        return

    print(f"\n── Verify ───────────────────────────────")

    try:
        # Generate reference texture
        tools_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        tex_tool = os.path.join(tools_dir, 'build_building_tex.py')

        with tempfile.NamedTemporaryFile(suffix='.png', delete=False) as tmp:
            ref_path = tmp.name

        subprocess.run([
            sys.executable, tex_tool,
            '--east', str(east),
            '--north', str(north),
            '--height', str(height_tiles),
            '--no-edges',
            '-o', ref_path,
        ], check=True, capture_output=True)

        ref = Image.open(ref_path)
        out = Image.open(output_path)

        if ref.size != out.size:
            print(f"  ✗ Size mismatch: ref {ref.size} vs output {out.size}")
            os.unlink(ref_path)
            return

        # Compare coverage (non-transparent pixel masks)
        import numpy as np
        ref_arr = np.array(ref)
        out_arr = np.array(out)

        ref_mask = ref_arr[:, :, 3] > 0
        out_mask = out_arr[:, :, 3] > 0

        intersection = np.sum(ref_mask & out_mask)
        union = np.sum(ref_mask | out_mask)
        iou = intersection / union if union > 0 else 0

        print(f"  Coverage IoU:   {iou:.3f}  ({'✓' if iou > 0.90 else '✗'})")

        os.unlink(ref_path)

    except Exception as e:
        print(f"  [verify] Error: {e}")
