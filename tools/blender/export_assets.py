"""Exports the mannequin from body.blend for the app and for the C# data tool.

  blender -b tools/blender/body.blend --python tools/blender/export_assets.py

Outputs (coordinates in the glTF frame: Y up, front = +Z, centimetres):
  <app>/Resources/Raw/wwwroot/models/body.glb   render model: mesh 'Body' (zones, raycast) + mesh 'Eyes'
  tools/blender/build/body_faces.json           zone list + per Blender triangle: centroid and zone per level
  tools/blender/build/body_helper.obj           simplified helper mesh (~5k vertices) for the distance graph

The C# tool (tools/BodyDataTool) turns these into body_zones.json and body_graph.bin, matching triangles
to the .glb by centroid, so the triangle order of the exporter does not matter.
"""
import argparse
import json
import os
import sys

import bmesh
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
LEVELS = (("Easy", "zone_easy"), ("Medium", "zone_medium"))
HELPER_DECIMATE_VERTICES = 3300   # after splitting long edges the helper ends up at ~5k vertices
HELPER_MAX_EDGE_CM = 4.0


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--zones", default=os.path.join(HERE, "zones.json"))
    p.add_argument("--glb", default=os.path.join(REPO, "src", "BodyTouchGame.App", "Resources", "Raw", "wwwroot", "models", "body.glb"))
    p.add_argument("--build", default=os.path.join(HERE, "build"))
    return p.parse_args(argv)


def to_gltf(v):
    """Blender (Z up, front -Y) -> glTF (Y up, front +Z), as done by the glTF exporter."""
    return (v.x, v.z, -v.y)


def export_glb(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for name in ("Body", "Eyes"):
        bpy.data.objects[name].select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_yup=True,
        export_apply=True,
        export_normals=True,
        export_texcoords=False,
        export_attributes=False,
        export_materials="EXPORT",
        export_animations=False,
        export_skins=False,
        export_morph=False,
    )


def export_faces(path, zones):
    mesh = bpy.data.objects["Body"].data
    count = len(mesh.polygons)
    labels = {}
    for level, attribute in LEVELS:
        values = [0] * count
        mesh.attributes[attribute].data.foreach_get("value", values)
        labels[level] = values
    centroids = []
    for poly in mesh.polygons:
        centroids.extend(round(c, 4) for c in to_gltf(poly.center))
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"version": 1, "zones": zones, "triangleCount": count, "centroids": centroids, "labels": labels}, f,
                  ensure_ascii=False, separators=(",", ":"))


def export_helper(path):
    source = bpy.data.objects["Body"]
    mesh = source.data.copy()
    helper = bpy.data.objects.new("Helper", mesh)
    bpy.context.scene.collection.objects.link(helper)
    modifier = helper.modifiers.new("Decimate", "DECIMATE")
    modifier.decimate_type = "COLLAPSE"
    modifier.use_collapse_triangulate = True
    modifier.ratio = HELPER_DECIMATE_VERTICES / len(mesh.vertices)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = helper.evaluated_get(depsgraph).to_mesh()
    evaluated.calc_loop_triangles()
    positions = [tuple(v.co) for v in evaluated.vertices]
    triangles = [tuple(t.vertices) for t in evaluated.loop_triangles]
    check_manifold(positions, triangles, "decimated helper")

    positions, triangles = split_long_edges(positions, triangles, HELPER_MAX_EDGE_CM)
    check_manifold(positions, triangles, "helper after edge splitting")
    max_edge = max(length(positions, a, b) for tri in triangles for a, b in edges_of(tri))

    with open(path, "w", encoding="utf-8") as f:
        f.write("# Body Touch Game helper mesh (glTF frame, cm)\n")
        for x, y, z in positions:
            f.write("v %.5f %.5f %.5f\n" % (x, z, -y))
        for tri in triangles:
            f.write("f %d %d %d\n" % tuple(i + 1 for i in tri))
    return len(positions), len(triangles), round(max_edge, 2)


def edges_of(tri):
    a, b, c = tri
    return ((a, b), (b, c), (c, a))


def length(positions, a, b):
    pa, pb = positions[a], positions[b]
    return ((pa[0] - pb[0]) ** 2 + (pa[1] - pb[1]) ** 2 + (pa[2] - pb[2]) ** 2) ** 0.5


def split_long_edges(positions, triangles, max_length):
    """Longest-edge bisection: split an edge at its midpoint and each of its two triangles in two.
    Collapse decimation keeps detail where the surface bends (fingers, face) but leaves long slivers on
    straight parts (shins); splitting gives every render triangle nearby anchor vertices."""
    positions = list(positions)
    triangles = [list(t) for t in triangles]
    edge_faces = {}
    for f, tri in enumerate(triangles):
        for a, b in edges_of(tri):
            edge_faces.setdefault(frozenset((a, b)), set()).add(f)
    while True:
        edge, longest = None, max_length
        for key in edge_faces:
            a, b = tuple(key)
            l = length(positions, a, b)
            if l > longest:
                edge, longest = key, l
        if edge is None:
            return positions, [tuple(t) for t in triangles]
        a, b = tuple(edge)
        m = len(positions)
        positions.append(tuple((positions[a][k] + positions[b][k]) / 2 for k in range(3)))
        for f in list(edge_faces.pop(edge)):
            tri = triangles[f]
            i = tri.index(a)
            if tri[(i + 1) % 3] != b:                      # keep the winding: rotate so the edge is a -> b
                a2, b2 = b, a
                i = tri.index(a2)
            else:
                a2, b2 = a, b
            c = tri[(i + 2) % 3]
            for e in (frozenset((b2, c)),):
                edge_faces[e].discard(f)
            new = len(triangles)
            triangles[f] = [a2, m, c]
            triangles.append([m, b2, c])
            edge_faces.setdefault(frozenset((a2, m)), set()).add(f)
            edge_faces.setdefault(frozenset((m, c)), set()).update((f, new))
            edge_faces.setdefault(frozenset((m, b2)), set()).add(new)
            edge_faces[frozenset((b2, c))].add(new)


def check_manifold(positions, triangles, what):
    counts = {}
    for tri in triangles:
        for a, b in edges_of(tri):
            key = frozenset((a, b))
            counts[key] = counts.get(key, 0) + 1
    bad = sum(1 for c in counts.values() if c != 2)
    if bad or len(triangles) != 2 * len(positions) - 4:
        raise SystemExit(f"The {what} is not a closed manifold: {bad} bad edges, "
                         f"{len(positions)} vertices, {len(triangles)} triangles.")


def main():
    args = parse_args()
    with open(args.zones, encoding="utf-8") as f:
        zones = json.load(f)["zones"]
    os.makedirs(args.build, exist_ok=True)

    export_glb(args.glb)
    export_faces(os.path.join(args.build, "body_faces.json"), zones)
    vertices, triangles, max_edge = export_helper(os.path.join(args.build, "body_helper.obj"))
    print(json.dumps({
        "glb": os.path.relpath(args.glb, REPO),
        "glb_bytes": os.path.getsize(args.glb),
        "helper_vertices": vertices,
        "helper_triangles": triangles,
        "helper_max_edge_cm": max_edge,
    }, indent=2))


if __name__ == "__main__":
    main()
