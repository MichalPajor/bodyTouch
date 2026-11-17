"""Renders review images of the mannequin and its zones (Cycles, CPU, headless).

  blender -b tools/blender/body.blend --python tools/blender/render_previews.py -- --out screens/etap-02

Writes model_*.png (plain mannequin), zones_<level>_*.png for every level in the model (close-ups for Hard and
Hardcore) and zone_colors.json
(zone id -> preview colour, used for the legend).
"""
import argparse
import colorsys
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
BACKGROUND_SRGB = (0x17, 0x13, 0x29)   # 'Surface' token: the 3D panel background in the app


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True)
    p.add_argument("--zones", default=os.path.join(HERE, "zones.json"))
    p.add_argument("--samples", type=int, default=48)
    return p.parse_args(argv)


def lin(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def setup_scene(samples):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"

    world = bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (*[lin(c) for c in BACKGROUND_SRGB], 1)
    bg.inputs[1].default_value = 0.8

    def light(name, kind, energy, rotation, size=None, color=(1, 1, 1)):
        data = bpy.data.lights.new(name, kind)
        data.energy = energy
        data.color = color
        if size is not None:
            data.angle = size
        obj = bpy.data.objects.new(name, data)
        obj.rotation_euler = [math.radians(a) for a in rotation]
        scene.collection.objects.link(obj)

    light("Key", "SUN", 1.9, (55, 0, -35), size=math.radians(25))
    light("Fill", "SUN", 0.6, (70, 0, 40), size=math.radians(40))
    light("Back", "SUN", 1.4, (60, 0, 160), size=math.radians(35))
    light("Rim", "SUN", 1.6, (-60, 0, 170), size=math.radians(15), color=(0.85, 0.8, 1.0))

    camera = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    camera.data.type = "ORTHO"
    scene.collection.objects.link(camera)
    scene.camera = camera
    return scene, camera


def aim(camera, target, direction, scale, distance=400):
    target = Vector(target)
    direction = Vector(direction).normalized()
    location = target + direction * distance
    forward = -direction
    right = forward.cross(Vector((0, 0, 1)))
    if right.length < 1e-6:
        right = Vector((1, 0, 0))
    right.normalize()
    up = right.cross(forward)
    rotation = Matrix((right, up, -forward)).transposed()      # camera looks along its -Z axis
    camera.matrix_world = Matrix.Translation(location) @ rotation.to_4x4()
    camera.data.ortho_scale = scale
    camera.data.clip_end = distance * 3


def zone_colors(zones):
    colors = {}
    golden = 0.61803398875
    hue = 0.05
    for zone in zones:
        if zone.get("drawable", True) is False:
            colors[zone["id"]] = (0.32, 0.32, 0.36)
            continue
        hue = (hue + golden) % 1.0
        sat = 0.55 if zone["level"] == "Easy" else 0.65
        # Many small zones on the fine levels: vary the brightness as well, so neighbours differ.
        value = 0.95 if zone["level"] in ("Easy", "Medium") or len(colors) % 2 == 0 else 0.72
        colors[zone["id"]] = colorsys.hsv_to_rgb(hue, sat, value)
    return colors


def paint(mesh, attribute, zones, colors):
    values = [0] * len(mesh.polygons)
    mesh.attributes[attribute].data.foreach_get("value", values)
    name = f"preview_{attribute}"
    layer = mesh.color_attributes.get(name) or mesh.color_attributes.new(name, "BYTE_COLOR", "CORNER")
    for poly in mesh.polygons:
        r, g, b = colors[zones[values[poly.index]]["id"]]
        for loop in poly.loop_indices:
            layer.data[loop].color = (lin(r * 255), lin(g * 255), lin(b * 255), 1)
    return name


def zone_material(layer_name):
    mat = bpy.data.materials.new(f"M_{layer_name}")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    attr = nodes.new("ShaderNodeVertexColor")
    attr.layer_name = layer_name
    bsdf = nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.8
    mat.node_tree.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def render(scene, path, width, height):
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    args = parse_args()
    os.makedirs(args.out, exist_ok=True)
    with open(args.zones, encoding="utf-8") as f:
        zones = json.load(f)["zones"]

    body = bpy.data.objects["Body"]
    mesh = body.data
    scene, camera = setup_scene(args.samples)
    base_material = mesh.materials[0]

    full = (0, 0, 86)
    views = {
        "front": ((0, -1, 0), full, 182, (700, 1000)),
        "side": ((1, 0, 0), full, 182, (700, 1000)),
        "back": ((0, 1, 0), full, 182, (700, 1000)),
    }
    for name, (direction, target, scale, (w, h)) in views.items():
        aim(camera, target, direction, scale)
        render(scene, os.path.join(args.out, f"model_{name}.png"), w, h)

    closeups = {
        "face": ((0.25, -1, 0.05), (0, -2, 158), 30),
        "hand": ((0.3, -1, 0.35), (45, -24, 103), 30),
        "hand_back": ((0.6, 0.2, 0.5), (45, -24, 103), 30),
        "foot": ((0.7, -1, 0.6), (19, -7, 4), 34),
    }
    for name, (direction, target, scale) in closeups.items():
        aim(camera, target, direction, scale)
        render(scene, os.path.join(args.out, f"model_{name}.png"), 800, 800)

    colors = zone_colors(zones)
    levels = [(level, f"zone_{level}") for level in ("easy", "medium", "hard", "hardcore") if f"zone_{level}" in mesh.attributes]
    for level, attribute in levels:
        mesh.materials[0] = zone_material(paint(mesh, attribute, zones, colors))
        for name, (direction, target, scale, (w, h)) in views.items():
            aim(camera, target, direction, scale)
            render(scene, os.path.join(args.out, f"zones_{level}_{name}.png"), w, h)
        if level in ("hard", "hardcore"):
            # Fine zones are small: close-ups of the face, hands and feet.
            for name, (direction, target, scale) in closeups.items():
                aim(camera, target, direction, scale)
                render(scene, os.path.join(args.out, f"zones_{level}_{name}.png"), 800, 800)
    mesh.materials[0] = base_material

    with open(os.path.join(args.out, "zone_colors.json"), "w", encoding="utf-8") as f:
        json.dump({k: "#%02X%02X%02X" % tuple(round(c * 255) for c in v) for k, v in colors.items()}, f, indent=2)


if __name__ == "__main__":
    main()
