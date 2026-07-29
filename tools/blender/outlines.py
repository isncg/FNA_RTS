"""
Edge outlines for NPR 三渲二 building rendering.

Two approaches are provided:

  1. Freestyle — Blender's built-in NPR line engine.
     Selects silhouette + crease + border edges and renders them as
     constant-thickness strokes in the edge colour (#404050).
     Best visual quality; preferred default.

  2. Compositor — post-process edge detection on Normal + Depth passes.
     Faster for batch rendering (no Freestyle overhead).
     Good fallback when exporting many buildings.

Edge colour matches ProceduralAssetProvider edgeColor: #404050.
"""

from __future__ import annotations

try:
    import bpy
    HAS_BPY = True
except ImportError:
    HAS_BPY = False


def _ensure_bpy():
    if not HAS_BPY:
        raise RuntimeError("outlines requires Blender's bpy module")


# ── Freestyle ───────────────────────────────────────────────────────────

def setup_freestyle_outlines(
    edge_color: tuple = (0.251, 0.251, 0.314, 1.0),  # #404050
    thickness: float = 1.5,
    scene=None,
):
    """Enable Freestyle and add a line set for building edges.

    Args:
        edge_color: (r, g, b, a) stroke colour (0…1).
        thickness: Line width in pixels.
        scene: Blender scene (default: bpy.context.scene).
    """
    _ensure_bpy()

    if scene is None:
        scene = bpy.context.scene

    scene.render.use_freestyle = True
    scene.render.line_thickness_mode = 'ABSOLUTE'
    scene.render.line_thickness = thickness

    # Get or create the Freestyle settings on the active view layer
    vl = scene.view_layers[0]
    fs = vl.freestyle_settings

    # Remove any existing linesets with our name
    for ls in list(fs.linesets):
        if ls.name == 'BuildingEdges':
            fs.linesets.remove(ls)

    lineset = fs.linesets.new('BuildingEdges')
    lineset.select_by_visibility = True
    lineset.select_by_edge_types = True
    lineset.edge_type_combination = 'OR'
    lineset.select_silhouette = True
    lineset.select_crease = True
    lineset.select_border = True
    lineset.select_contour = False
    lineset.select_suggestive_contour = False
    lineset.select_ridge_valley = False
    lineset.select_material_boundary = False

    # Line style
    linestyle = lineset.linestyle
    linestyle.color = edge_color
    linestyle.thickness = thickness
    linestyle.thickness_position = 'ABSOLUTE'
    linestyle.thickness_ratio = 1.0
    linestyle.use_chaining = True
    linestyle.chaining = 'PLAIN'

    print(f"  [outline] Freestyle enabled ({thickness}px, "
          f"#{int(edge_color[0]*255):02X}{int(edge_color[1]*255):02X}{int(edge_color[2]*255):02X})")


def disable_freestyle(scene=None):
    """Turn off Freestyle for faster rendering."""
    _ensure_bpy()
    if scene is None:
        scene = bpy.context.scene
    scene.render.use_freestyle = False
    print("  [outline] Freestyle disabled")


# ── Compositor edge detection (fallback) ────────────────────────────────

def setup_compositor_outlines(
    edge_color: tuple = (0.251, 0.251, 0.314, 1.0),
    thickness: int = 1,
    scene=None,
):
    """Build a compositor node graph that overlays edge-detected outlines.

    Uses Normal and Depth passes for edge detection, then alpha-overs
    the detected edges onto the beauty (combined) pass.

    This is faster than Freestyle for batch rendering but produces
    slightly less precise edges.

    Requires view layer passes: Combined, Z, Normal.
    """
    _ensure_bpy()

    if scene is None:
        scene = bpy.context.scene

    # Enable required passes
    vl = scene.view_layers[0]
    vl.use_pass_combined = True
    vl.use_pass_z = True
    vl.use_pass_normal = True

    # Compositor nodes
    scene.use_nodes = True
    tree = scene.node_tree
    nodes = tree.nodes
    links = tree.links
    nodes.clear()

    # Render layers
    rl = nodes.new('CompositorNodeRLayers')
    rl.location = (0, 0)

    # ── Normal edge detection ─────────────────────────────────────
    dilate_n = nodes.new('CompositorNodeDilateErode')
    dilate_n.location = (200, 100)
    dilate_n.mode = 'STEP'
    dilate_n.distance = thickness

    diff_n = nodes.new('CompositorNodeMixRGB')
    diff_n.blend_type = 'DIFFERENCE'
    diff_n.location = (400, 100)
    diff_n.inputs[0].default_value = 1.0  # Fac

    links.new(rl.outputs['Normal'], dilate_n.inputs[0])
    links.new(rl.outputs['Normal'], diff_n.inputs[1])
    links.new(dilate_n.outputs[0], diff_n.inputs[2])

    # ── Depth edge detection ─────────────────────────────────────
    dilate_z = nodes.new('CompositorNodeDilateErode')
    dilate_z.location = (200, -150)
    dilate_z.mode = 'STEP'
    dilate_z.distance = thickness

    diff_z = nodes.new('CompositorNodeMixRGB')
    diff_z.blend_type = 'DIFFERENCE'
    diff_z.location = (400, -150)
    diff_z.inputs[0].default_value = 1.0

    links.new(rl.outputs['Depth'], dilate_z.inputs[0])
    links.new(rl.outputs['Depth'], diff_z.inputs[1])
    links.new(dilate_z.outputs[0], diff_z.inputs[2])

    # ── Threshold and combine ────────────────────────────────────
    # Normal edges
    thresh_n = nodes.new('CompositorNodeValToRGB')
    thresh_n.location = (600, 100)
    thresh_n.color_ramp.interpolation = 'CONSTANT'
    thresh_n.color_ramp.elements[0].position = 0.05
    thresh_n.color_ramp.elements[0].color = (0, 0, 0, 0)
    thresh_n.color_ramp.elements[1].color = edge_color
    links.new(diff_n.outputs[0], thresh_n.inputs[0])

    # Depth edges
    thresh_z = nodes.new('CompositorNodeValToRGB')
    thresh_z.location = (600, -150)
    thresh_z.color_ramp.interpolation = 'CONSTANT'
    thresh_z.color_ramp.elements[0].position = 0.005
    thresh_z.color_ramp.elements[0].color = (0, 0, 0, 0)
    thresh_z.color_ramp.elements[1].color = edge_color
    links.new(diff_z.outputs[0], thresh_z.inputs[0])

    # Add edges together
    add_edges = nodes.new('CompositorNodeMixRGB')
    add_edges.blend_type = 'ADD'
    add_edges.location = (800, -25)
    add_edges.inputs[0].default_value = 1.0
    links.new(thresh_n.outputs[0], add_edges.inputs[1])
    links.new(thresh_z.outputs[0], add_edges.inputs[2])

    # ── Composite: beauty + edges ─────────────────────────────────
    alpha_over = nodes.new('CompositorNodeAlphaOver')
    alpha_over.location = (1000, 0)
    links.new(rl.outputs['Image'], alpha_over.inputs[1])
    links.new(add_edges.outputs[0], alpha_over.inputs[2])

    comp_out = nodes.new('CompositorNodeComposite')
    comp_out.location = (1200, 0)
    links.new(alpha_over.outputs[0], comp_out.inputs['Image'])

    print(f"  [outline] Compositor edge detection enabled "
          f"(#{int(edge_color[0]*255):02X}{int(edge_color[1]*255):02X}{int(edge_color[2]*255):02X})")


def clear_compositor_nodes(scene=None):
    """Remove all compositor nodes."""
    _ensure_bpy()
    if scene is None:
        scene = bpy.context.scene
    scene.use_nodes = False
    print("  [outline] Compositor nodes cleared")
