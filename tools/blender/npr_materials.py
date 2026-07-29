"""
NPR (Non-Photorealistic Rendering) materials for 三渲二 cel-shaded look.

Creates flat toon-shaded materials using EEVEE's Shader-to-RGB node.
Three materials are provided for the visible isometric faces:
  - M_Roof     (lightest, fully lit)
  - M_SouthWall (mid-tone, 60% brightness)
  - M_WestWall  (darkest, 40% brightness)

The node graph per material:
  Geometry(Normal) + SunDirection → DotProduct → ColorRamp(CONSTANT)
    → DiffuseBSDF → MaterialOutput

Colors default to the ProceduralAssetProvider palette (#A0A0B0 roof,
#8C8CA0 walls, #404050 edges) and are overridable via function
arguments and CLI flags.
"""

from __future__ import annotations
from typing import Tuple

try:
    import bpy
    HAS_BPY = True
except ImportError:
    HAS_BPY = False


# ── Default colour palette (matches ProceduralAssetProvider.cs) ────────

DEFAULT_ROOF  = (0.627, 0.627, 0.690, 1.0)   # #A0A0B0
DEFAULT_WALL  = (0.549, 0.549, 0.627, 1.0)   # #8C8CA0
DEFAULT_EDGE  = (0.251, 0.251, 0.314, 1.0)   # #404050

# Brightness multipliers for wall faces
SOUTH_BRIGHTNESS = 0.60   # lower-right face, moderately lit
WEST_BRIGHTNESS  = 0.40   # lower-left face, darkest


def _ensure_bpy():
    if not HAS_BPY:
        raise RuntimeError("npr_materials requires Blender's bpy module")


def hex_to_rgba(hex_str: str) -> Tuple[float, float, float, float]:
    """#RRGGBB or #RRGGBBAA → (R, G, B, A) in 0…1 range."""
    h = hex_str.lstrip('#')
    r = int(h[0:2], 16) / 255.0
    g = int(h[2:4], 16) / 255.0
    b = int(h[4:6], 16) / 255.0
    a = int(h[6:8], 16) / 255.0 if len(h) >= 8 else 1.0
    return (r, g, b, a)


def _create_toon_material(name: str, base_color: Tuple[float, float, float, float],
                          shadow_levels: int = 2) -> "bpy.types.Material":
    """Build an EEVEE toon material with discrete shading bands.

    Args:
        name: Material name.
        base_color: (r, g, b, a) base / fully-lit colour.
        shadow_levels: Number of discrete shade bands (2 = lit + shadow).

    Returns:
        A new Blender Material with the toon node graph.
    """
    _ensure_bpy()

    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    nodes.clear()

    # ── Output ──────────────────────────────────────────────────────
    output = nodes.new('ShaderNodeOutputMaterial')
    output.location = (800, 0)

    # ── Emission shader (pure flat colour, no 3D lighting) ──────────
    emission = nodes.new('ShaderNodeEmission')
    emission.location = (600, 0)
    links.new(emission.outputs['Emission'], output.inputs['Surface'])

    # ── ColorRamp for discrete toon bands ───────────────────────────
    ramp = nodes.new('ShaderNodeValToRGB')
    ramp.location = (200, 100)
    ramp.color_ramp.interpolation = 'CONSTANT'

    # Remove default stops; create lit + shadow stops
    stops = ramp.color_ramp.elements
    # We need exactly shadow_levels stops.  Start fresh.
    while len(stops) > 2:
        stops.remove(stops[1])
    stops[0].position = 0.0
    stops[0].color = _scale_color(base_color, 0.55)   # shadow
    stops[1].position = 0.35
    stops[1].color = base_color                         # lit

    # Additional highlight band if requested
    if shadow_levels >= 3:
        highlight = stops.new(0.85)
        r, g, b, a = base_color
        hl = (min(r * 1.2, 1.0), min(g * 1.2, 1.0), min(b * 1.2, 1.0), a)
        highlight.color = hl

    links.new(ramp.outputs['Color'], emission.inputs['Color'])

    # ── Diffuse-like lighting ───────────────────────────────────────
    # We use Geometry normal dotted with a hard-coded sun direction
    # to get a scalar "lit-ness", then feed through the ColorRamp.
    geom = nodes.new('ShaderNodeNewGeometry')
    geom.location = (-400, 100)

    dot = nodes.new('ShaderNodeVectorMath')
    dot.operation = 'DOT_PRODUCT'
    dot.location = (-200, 100)
    # Sun from upper-left (matching isometric lighting convention)
    dot.inputs[1].default_value = (0.577, 0.577, 0.577)  # normalized (1, 1, 1)

    links.new(geom.outputs['Normal'], dot.inputs[0])

    # Map dot-product (-1…1) to (0…1) for the ramp
    map_range = nodes.new('ShaderNodeMapRange')
    map_range.location = (0, 100)
    map_range.inputs['From Min'].default_value = -1.0
    map_range.inputs['From Max'].default_value = 1.0
    map_range.inputs['To Min'].default_value = 0.0
    map_range.inputs['To Max'].default_value = 1.0

    links.new(dot.outputs['Value'], map_range.inputs['Value'])
    links.new(map_range.outputs['Result'], ramp.inputs['Fac'])

    return mat


def _scale_color(color: Tuple[float, float, float, float],
                 factor: float) -> Tuple[float, float, float, float]:
    """Multiply RGB channels by factor, keep alpha."""
    return (
        max(0.0, min(1.0, color[0] * factor)),
        max(0.0, min(1.0, color[1] * factor)),
        max(0.0, min(1.0, color[2] * factor)),
        color[3],
    )


def create_building_materials(
    roof_color: Tuple[float, float, float, float] = DEFAULT_ROOF,
    wall_color: Tuple[float, float, float, float] = DEFAULT_WALL,
) -> dict:
    """Create three face materials for the isometric building.

    Returns:
        {'roof': mat, 'south': mat, 'west': mat}
    """
    _ensure_bpy()

    # Remove any pre-existing materials with these names
    for name in ('M_Roof', 'M_SouthWall', 'M_WestWall'):
        if name in bpy.data.materials:
            bpy.data.materials.remove(bpy.data.materials[name])

    mat_roof = _create_toon_material('M_Roof', roof_color, shadow_levels=2)
    mat_south = _create_toon_material('M_SouthWall',
                                       _scale_color(wall_color, SOUTH_BRIGHTNESS),
                                       shadow_levels=2)
    mat_west = _create_toon_material('M_WestWall',
                                      _scale_color(wall_color, WEST_BRIGHTNESS),
                                      shadow_levels=2)

    return {'roof': mat_roof, 'south': mat_south, 'west': mat_west}


def apply_materials_to_object(obj, materials: dict):
    """Assign roof/south/west materials to an object's material slots.

    The object should have three material slots in order:
      [0] = roof,  [1] = south wall,  [2] = west wall.
    If the object has no slots, they are created.
    If it has fewer than 3, the existing ones are filled in order.
    """
    _ensure_bpy()

    if obj.type != 'MESH':
        print(f"  [npr]  Skipping {obj.name}: not a mesh")
        return

    # Clear existing slots
    obj.data.materials.clear()

    for key in ('roof', 'south', 'west'):
        obj.data.materials.append(materials[key])

    print(f"  [npr]  Applied M_Roof / M_SouthWall / M_WestWall to '{obj.name}'")


# ── Lighting ────────────────────────────────────────────────────────────

def setup_npr_lighting(scene=None):
    """Set up single hard sun light for NPR look. No environment."""
    _ensure_bpy()

    if scene is None:
        scene = bpy.context.scene

    # Remove existing lights
    for obj in list(scene.objects):
        if obj.type == 'LIGHT':
            bpy.data.objects.remove(obj, do_unlink=True)

    # Create sun light
    light_data = bpy.data.lights.new('NPR_Sun', 'SUN')
    light_data.energy = 3.0
    light_data.angle = 0.0           # Hard shadows (crisp NPR)
    light_data.use_shadow = True

    light_obj = bpy.data.objects.new('NPR_Sun', light_data)
    scene.collection.objects.link(light_obj)

    # Position: upper-left front for isometric lighting.
    # Sun direction in world space points FROM light location TO origin.
    # We want light rays coming from upper-left (in screen terms, which
    # is roughly the +X +Y +Z quadrant after the camera rotation).
    light_obj.location = (10.0, 10.0, 15.0)
    light_obj.rotation_euler = (math.radians(30), 0, math.radians(-45))

    print(f"  [npr]  Sun light '{light_obj.name}' created")

    return light_obj


# Need math for the sun rotation above; import at module level
import math


# ── EEVEE NPR render settings ───────────────────────────────────────────

def configure_eevee_npr(scene=None):
    """Apply EEVEE settings optimized for crisp NPR output."""
    _ensure_bpy()

    if scene is None:
        scene = bpy.context.scene

    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.film_transparent = True

    # No anti-aliasing — we want crisp 1-pixel edges
    scene.eevee.taa_render_samples = 1
    scene.render.filter_size = 0.01

    # Disable effects that soften the image
    scene.eevee.use_soft_shadows = False
    scene.eevee.use_gtao = False
    scene.eevee.use_bloom = False
    scene.eevee.use_ssr = False
    scene.eevee.use_motion_blur = False

    # Standard colour transform (no filmic) for flat NPR colours
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'

    # Pure black world background (no environment lighting)
    world = bpy.data.worlds.new("NPR_World") if "NPR_World" not in bpy.data.worlds \
            else bpy.data.worlds["NPR_World"]
    world.use_nodes = True
    bg = world.node_tree.nodes.get('Background')
    if bg:
        bg.inputs['Color'].default_value = (0, 0, 0, 1)
        bg.inputs['Strength'].default_value = 0.0
    scene.world = world

    print(f"  [npr]  EEVEE NPR settings applied")
