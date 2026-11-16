"""Renders the mannequin for the Start screen: front view, transparent background, tightly cropped.

  blender -b tools/blender/body.blend --python tools/blender/render_start_image.py

Output: src/BodyTouchGame.App/Resources/Images/start_mannequin.png (630 x 1020 px = 210 x 340 at 3x density;
the csproj sets BaseSize 210,340). The A-pose arm span does not fit the 1:2 box of the mockup.
"""
import math
import os

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "src", "BodyTouchGame.App", "Resources", "Images", "start_mannequin.png")


def main():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 64
    scene.cycles.use_denoising = True
    scene.render.film_transparent = True
    scene.view_settings.view_transform = "Standard"
    scene.render.resolution_x = 630
    scene.render.resolution_y = 1020
    scene.render.image_settings.color_mode = "RGBA"

    world = bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.02, 0.016, 0.04, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.8

    for name, energy, rotation, color in (
        ("Key", 1.9, (55, 0, -35), (1, 1, 1)),
        ("Fill", 0.6, (70, 0, 40), (1, 1, 1)),
        ("Rim", 2.2, (-60, 0, 170), (0.8, 0.75, 1.0)),
    ):
        light = bpy.data.lights.new(name, "SUN")
        light.energy = energy
        light.color = color
        light.angle = math.radians(25)
        obj = bpy.data.objects.new(name, light)
        obj.rotation_euler = [math.radians(a) for a in rotation]
        scene.collection.objects.link(obj)

    camera = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 176          # height 170 cm + margin; at 630:1020 the width (109 cm) fits the arm span
    scene.collection.objects.link(camera)
    scene.camera = camera
    target = Vector((0, 0, 86))
    location = target + Vector((0, -400, 0))
    forward = (target - location).normalized()
    right = forward.cross(Vector((0, 0, 1))).normalized()
    up = right.cross(forward)
    camera.matrix_world = Matrix.Translation(location) @ Matrix((right, up, -forward)).transposed().to_4x4()

    scene.render.filepath = os.path.abspath(OUT)
    bpy.ops.render.render(write_still=True)


main()
