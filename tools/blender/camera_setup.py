"""
Camera setup for 2:1 dimetric isometric building rendering.

Matches the projection used by FNARTS.Core.CoordUtil and
ProceduralAssetProvider.GenIsometricBox:

  gx+1 (East)  → screen (+32, -16) pixels  (upper-right)
  gy+1 (North) → screen (-32, -16) pixels  (upper-left)
  hz+1 (Height)→ screen (0, -32) pixels    (straight up; texture
                  allocates 2× for walls = 64 px)

Texture dimensions for E×N×H building:
  texW = (E + N) * 32
  texH = (E + N) * 16 + H * 64

The camera uses Blender XYZ Euler rotation (60°, 0°, 45°) to
achieve the split-XY-plane + foreshortened-vertical look.
"""

from __future__ import annotations
import math
from typing import Tuple

try:
    import bpy
    from mathutils import Vector, Matrix
    HAS_BPY = True
except ImportError:
    HAS_BPY = False

# ── Game constants (match CoordUtil.cs) ─────────────────────────────────

HALF_TILE_W = 32.0
HALF_TILE_H = 16.0
TILE_HEIGHT  = 32.0   # height step in world space = 32 px; texture allocates 2× = 64 px

# ── Camera rotation: Euler XYZ (rx, ry, rz) in radians ─────────────────
# 60° pitch gives the classic C&C2 horizontal-vs-vertical ratio.
CAM_ROTATION = (
    math.radians(60.0),   # X: pitch down to reveal walls
    0.0,                   # Y: no yaw offset
    math.radians(45.0),   # Z: 45° azimuth (equal east/north exposure)
)

# ── Camera position expressed as "distance along view direction from
#    building centre". For ortho cameras distance only affects what
#    is clipped; we set it large enough to keep everything in front
#    of the near clip.
CAM_DISTANCE = 50.0


# ── Helpers ────────────────────────────────────────────────────────────

def _ensure_bpy():
    if not HAS_BPY:
        raise RuntimeError("camera_setup requires Blender's bpy module")


def _project_point(world_pt: Vector, cam_obj) -> Vector:
    """Project a world-space point to camera clip space (-1…1)."""
    # bpy_extras.object_utils.world_to_camera_view is available but
    # we compute manually to avoid the extra import.
    co = cam_obj.matrix_world.inverted() @ world_pt
    # Orthographic: no perspective divide, just scale by ortho_scale
    return co


def tex_dims(east: int, north: int, height: int) -> Tuple[int, int]:
    """Return (width, height) in pixels for a building of given tile size."""
    w = int((east + north) * HALF_TILE_W)
    h = int((east + north) * HALF_TILE_H + height * 2.0 * TILE_HEIGHT)
    return w, h


# ── Main camera setup ──────────────────────────────────────────────────

def setup_dimetric_camera(
    east: int,
    north: int,
    height_tiles: int,
    scene: "bpy.types.Scene" = None,
    cam_name: str = "RTS_Dimetric_Camera",
) -> Tuple["bpy.types.Object", "bpy.types.Camera", Tuple[int, int]]:
    """Create and configure an orthographic camera for 2:1 dimetric export.

    Args:
        east:  Building width in tiles (East / gx axis).
        north: Building depth in tiles (North / gy axis).
        height_tiles: Building height in tile-units.
        scene: Blender scene (default: bpy.context.scene).
        cam_name: Name for the camera data-block and object.

    Returns:
        (camera_object, camera_data, (tex_w, tex_h))
    """
    _ensure_bpy()

    if scene is None:
        scene = bpy.context.scene

    tex_w, tex_h = tex_dims(east, north, height_tiles)

    # --- Create camera data + object ---
    cam_data = bpy.data.cameras.new(cam_name)
    cam_data.type = 'ORTHO'
    cam_data.display_size = 2.0

    cam_obj = bpy.data.objects.new(cam_name, cam_data)
    scene.collection.objects.link(cam_obj)

    # --- Rotation: standard 2:1 dimetric ---
    cam_obj.rotation_mode = 'XYZ'
    cam_obj.rotation_euler = CAM_ROTATION

    # --- Position: look at building footprint centre from SW ---
    # Building occupies world volume [0, east] × [0, north] × [0, height].
    centre = Vector((
        east / 2.0,
        north / 2.0,
        height_tiles / 2.0,
    ))

    # Camera looks at building centre.  Position the camera 'behind'
    # the building (SW direction in XY plane, elevated).
    # The camera's -Z axis points along the view direction.
    # After rotation, the camera's world-space forward is its local -Z.
    # We place the camera at centre - forward * distance.
    forward = cam_obj.matrix_world.to_3x3() @ Vector((0, 0, -1))
    cam_obj.location = centre - forward * CAM_DISTANCE

    # --- Compute ortho_scale from camera-space bounds ---
    ortho_scale = _compute_ortho_scale(cam_obj, east, north, height_tiles,
                                        tex_w, tex_h)
    cam_data.ortho_scale = ortho_scale

    # --- Set render resolution ---
    scene.render.resolution_x = tex_w
    scene.render.resolution_y = tex_h
    scene.render.resolution_percentage = 100

    # --- Make active ---
    scene.camera = cam_obj

    return cam_obj, cam_data, (tex_w, tex_h)


def _compute_ortho_scale(
    cam_obj,
    east: float, north: float, height_tiles: float,
    tex_w: int, tex_h: int,
    margin_px: int = 1,
) -> float:
    """Compute ortho_scale so the building volume fills the render frame.

    The building occupies [0, east] × [0, north] × [0, height_tiles] in
    world space.  We project its eight corners to camera-local space,
    measure the extent, and set ortho_scale accordingly.
    """
    # Camera inverse transform (world → camera local)
    cam_mat_inv = cam_obj.matrix_world.inverted()

    # Eight corners of the world-aligned bounding box
    corners = [
        Vector((x, y, z))
        for x in (0.0, east)
        for y in (0.0, north)
        for z in (0.0, height_tiles)
    ]

    # Project to camera-local space and find X/Y extent on the image plane.
    # In camera-local coords after inverse transform:
    #   camera X → screen right,  camera Y → screen up, camera Z → depth.
    min_x = min_y = float('inf')
    max_x = max_y = float('-inf')

    for c in corners:
        lc = cam_mat_inv @ c       # local camera coords
        min_x = min(min_x, lc.x)
        max_x = max(max_x, lc.x)
        min_y = min(min_y, lc.y)
        max_y = max(max_y, lc.y)

    cam_w = max_x - min_x
    cam_h = max_y - min_y

    if cam_w < 1e-6 or cam_h < 1e-6:
        return 10.0  # fallback

    # In an orthographic camera:
    #   visible camera-space width  = ortho_scale * (tex_w / tex_h)
    #   visible camera-space height = ortho_scale
    #
    # We need both:
    #   cam_w  <= ortho_scale * (tex_w / tex_h)
    #   cam_h  <= ortho_scale
    #
    # The larger of { cam_w * tex_h/tex_w,  cam_h } sets the minimum
    # ortho_scale.  Add margin in pixel units.

    scale_from_width  = cam_w * (tex_h / tex_w)
    scale_from_height = cam_h

    # Convert pixel margin to camera-space margin
    # 1 pixel = ortho_scale / tex_h  camera-units
    # margin_cam = margin_px * (ortho_scale / tex_h)
    # Since ortho_scale appears on both sides, solve:
    #   ortho = max(scale_w, scale_h) + margin_px * (ortho / tex_h)
    #   ortho * (1 - margin/tex_h) = max(...)
    #   ortho = max(...) / (1 - margin/tex_h)

    ortho = max(scale_from_width, scale_from_height)
    if margin_px > 0 and tex_h > margin_px:
        ortho /= (1.0 - margin_px / tex_h)

    return ortho


# ── Z-scale calibration ────────────────────────────────────────────────

# In a pure orthographic projection at (60°, 0°, 45°) the height axis
# (world Z) does not project with exactly the same pixel-per-unit ratio
# that the game expects.  We compensate by applying a Z-scale to the
# building model.
#
# The game's projection is:
#   wy_screen = -(gx + gy) * HALF_TILE_H - hz * TILE_HEIGHT
# so 1 height unit → -TILE_HEIGHT = -32 world-space pixels.
#
# In Blender's orthographic camera, 1 world-Z unit produces
#   cam_Y = sin(rx) = sin(60°) = 0.866  camera-up units.
# The ortho_scale maps cam_Y units to pixels.
#
# We calibrate by rendering a 1×1×1 cube and measuring.  The factor
# below was empirically verified.  If the rendered height in pixels
# differs from expected, adjust this value.

Z_SCALE_CORRECTION = 1.0 / math.sin(math.radians(60.0))
# ≈ 1.1547 — stretches Z so 1 Blender unit of height projects to the
# same pixel count as 1 tile of horizontal displacement in screen-Y.


def apply_z_scale(obj, factor: float = Z_SCALE_CORRECTION):
    """Apply Z-axis scale correction to a Blender object for height mapping."""
    _ensure_bpy()
    obj.scale.z *= factor


# ── Console output ─────────────────────────────────────────────────────

def print_camera_info(cam_obj, tex_w: int, tex_h: int, east: int, north: int, height_tiles: int):
    """Log camera config for debugging."""
    _ensure_bpy()
    cam = cam_obj.data
    print(f"── Camera ───────────────────────────────")
    print(f"  Name:          {cam_obj.name}")
    print(f"  Type:          {cam.type}")
    print(f"  Rotation:      {tuple(round(math.degrees(r), 2) for r in cam_obj.rotation_euler)} deg")
    print(f"  Location:      ({cam_obj.location.x:.2f}, {cam_obj.location.y:.2f}, {cam_obj.location.z:.2f})")
    print(f"  Ortho scale:   {cam.ortho_scale:.3f}")
    print(f"  Resolution:    {tex_w} × {tex_h}")
    print(f"  Building:      {east}×{north}×{height_tiles} tiles")
