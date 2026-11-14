"""Builds the Body Touch Game mannequin from the MakeHuman CC0 base mesh.

Run headless:
  blender -b --python tools/blender/build_model.py -- --out tools/blender/body.blend

Steps:
  1. Load the 'body' group of MakeHuman base.obj (13 380 vertices, closed manifold), apply the macro
     targets (young adult, gender blend GENDER), load skeleton joints and skinning weights. Convert to centimetres, Z up, front = -Y, feet at z = 0, height 170 cm.
  2. Pose and stylise into a neutral mannequin: spread the fingers slightly, flatten the chest (no sexual
     features), close and smooth the eyes, soften the lips (Laplacian smoothing weighted by MakeHuman weights).
  3. Check the pose: hands/arms must not touch the torso or thighs; fingers slightly apart.
  4. Assign every face a Medium zone from tools/blender/zones.json (dominant bone + geometric rules),
     clean up speckles and small islands, derive the Easy zone from the parent.
  5. Add smooth eyeballs (separate object 'Eyes', not part of zones or the distance graph).
  6. Triangulate and save the .blend (source of truth for manual zone painting, see README).

Zones are stored as integer face attributes 'zone_easy' and 'zone_medium' holding the index into the
zones.json list. A JSON report with measurements is written next to the .blend.
"""
import argparse
import json
import math
import os
import sys
from collections import Counter, defaultdict

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree

HERE = os.path.dirname(os.path.abspath(__file__))
TARGET_HEIGHT_CM = 170.0
BODY_VERTEX_COUNT = 13380          # the 'body' group occupies the first vertices of base.obj
SKIN_COLOR_SRGB = (0xC8, 0xCC, 0xD6)

# ---- zone rule constants (centimetres, for a 170 cm figure) ----
Z_CHEST_BOTTOM = 113.0     # front: chest above, belly below; back: upper back above, lower back below
Z_PUBIC = 91.0             # below: pelvis / hip / thigh rules
Z_GLUTEAL_FOLD = 74.0      # lower edge of the buttocks
X_GROIN = 7.5              # |x| below this between the legs = pelvis
FLANK_NORMAL_X = 0.55      # belly faces turned sideways more than this = flank
ELBOW_RADIUS = 6.5
KNEE_RADIUS = 7.5
SHOULDER_RADIUS = 9.0
ARMPIT_EXCLUSION = 14.0    # pose check ignores the arm this close to the shoulder joint
MIN_ARM_GAP = 3.0          # required gap between the arm (beyond the armpit) and torso / thighs

BONE_GROUPS = [
    ("hand", ("wrist", "metacarpal", "finger")),
    ("forearm", ("lowerarm",)),
    ("upper-arm", ("upperarm",)),
    ("shoulder", ("clavicle", "shoulder")),
    ("foot", ("foot", "toe")),
    ("lowerleg", ("lowerleg",)),
    ("thigh", ("upperleg",)),
    ("pelvis", ("pelvis",)),
    ("neck", ("neck",)),
    ("torso", ("spine", "breast", "root")),
    ("head", ("head", "jaw", "eye", "oculi", "orbicularis", "oris", "risorius", "levator",
              "temporalis", "tongue", "special")),
]
# ---- stylising / pose ----
FINGER_SPREAD_DEGREES = {2: 6.0, 4: 6.0, 5: 12.0}   # index, ring, little finger (thumb and middle unchanged)
GENDER = 0.65             # MakeHuman gender blend: androgynous, flat chest
NIPPLE_RADIUS = 3.6
NIPPLE_ITERATIONS = 300
CHEST_FLATTEN_STRENGTH = 1.0
CHEST_FLATTEN_ITERATIONS = 150
LIP_ITERATIONS = 12
EYEBALL_RADIUS_FACTOR = 1.03   # eyeball radius = distance from eye centre to the socket * factor
LABEL_SMOOTHING_PASSES = 6


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--source", default=os.path.join(HERE, "source", "makehuman"))
    p.add_argument("--zones", default=os.path.join(HERE, "zones.json"))
    p.add_argument("--out", default=os.path.join(HERE, "body.blend"))
    return p.parse_args(argv)


def srgb_to_linear(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


# --------------------------------------------------------------------------- loading

def load_makehuman(source):
    vertices, faces, group = [], [], None
    with open(os.path.join(source, "base.obj"), encoding="utf-8") as f:
        for line in f:
            if line.startswith("v "):
                vertices.append([float(x) for x in line.split()[1:4]])
            elif line.startswith("g "):
                group = line.split()[1]
            elif line.startswith("f ") and group == "body":
                faces.append([int(t.split("/")[0]) - 1 for t in line.split()[1:]])
    with open(os.path.join(source, "default.mhskel"), encoding="utf-8") as f:
        skeleton = json.load(f)
    with open(os.path.join(source, "default_weights.mhw"), encoding="utf-8") as f:
        weights = json.load(f)["weights"]

    all_v = np.array(vertices, dtype=np.float64)
    # Macro shape as in MakeHuman: young adult, average muscle and weight, equal mix of the three
    # ethnic targets, gender GENDER (0 = female, 1 = male). The 'universal-*-average*' targets are empty.
    for race in ("african", "asian", "caucasian"):
        for sex, amount in (("female", 1.0 - GENDER), ("male", GENDER)):
            apply_target(all_v, os.path.join(source, "targets", f"{race}-{sex}-young.target"), amount / 3.0)
    body_y = all_v[:BODY_VERTEX_COUNT, 1]
    scale = TARGET_HEIGHT_CM / (body_y.max() - body_y.min())
    # MakeHuman: Y up, +Z front, decimetres -> Blender: Z up, -Y front, centimetres, feet on z = 0.
    converted = np.stack([all_v[:, 0], -all_v[:, 2], all_v[:, 1] - body_y.min()], axis=1) * scale
    joints = {name: converted[idx].mean(axis=0) for name, idx in skeleton["joints"].items()}
    bones = {name: (joints[b["head"]], joints[b["tail"]]) for name, b in skeleton["bones"].items()}
    body_weights = {bone: [(i, w) for i, w in pairs if i < BODY_VERTEX_COUNT] for bone, pairs in weights.items()}
    return converted[:BODY_VERTEX_COUNT], faces, bones, body_weights, scale


def apply_target(vertices, path, amount):
    with open(path, encoding="utf-8") as f:
        for line in f:
            if line.startswith("#") or not line.strip():
                continue
            parts = line.split()
            vertices[int(parts[0])] += amount * np.array([float(x) for x in parts[1:4]])


def bone_group(bone):
    for group, prefixes in BONE_GROUPS:
        if bone.startswith(prefixes):
            return group
    return "torso"


def create_object(vertices, faces, weights):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mesh = bpy.data.meshes.new("Body")
    mesh.from_pydata([tuple(v) for v in vertices], [], faces)
    mesh.update()
    obj = bpy.data.objects.new("Body", mesh)
    bpy.context.scene.collection.objects.link(obj)
    for bone, pairs in sorted(weights.items()):
        vg = obj.vertex_groups.new(name=bone)
        for i, w in pairs:
            vg.add([i], w, "REPLACE")
    return obj


# --------------------------------------------------------------------------- stylising

def edge_arrays(count, faces):
    """Directed edge list (src, dst) and vertex degrees for vectorised smoothing."""
    pairs = set()
    for face in faces:
        for a, b in zip(face, face[1:] + face[:1]):
            pairs.add((a, b))
            pairs.add((b, a))
    edges = np.array(sorted(pairs), dtype=np.int64)
    degree = np.bincount(edges[:, 0], minlength=count).astype(np.float64)
    return edges[:, 0], edges[:, 1], degree


def smooth_region(co, edges, strength, iterations, step=0.5):
    """Weighted umbrella (Laplacian) smoothing: v += step * strength * (mean(neighbours) - v).
    With enough iterations a region of strength 1 relaxes into a membrane spanned by its border."""
    src, dst, degree = edges
    co = co.copy()
    s = (np.clip(strength, 0, 1) * step)[:, None]
    for _ in range(iterations):
        means = np.zeros_like(co)
        np.add.at(means, src, co[dst])
        means /= np.maximum(degree, 1)[:, None]
        co += s * (means - co)
    return co


def ball(co, centre, inner, outer):
    """1 inside radius `inner`, smooth falloff to 0 at radius `outer`."""
    d = np.linalg.norm(co - centre, axis=1)
    t = np.clip((outer - d) / (outer - inner), 0, 1)
    return t * t * (3 - 2 * t)


def weight_sum(count, weights, prefixes):
    total = np.zeros(count)
    for bone, pairs in weights.items():
        if bone.startswith(prefixes):
            for i, w in pairs:
                total[i] += w
    return total


def spread_fingers(co, weights, bones):
    """Rotates index, ring and little fingers away from the middle finger around their knuckles,
    in the plane of the palm, so the fingers are slightly apart (spec 4.1)."""
    co = co.copy()
    for side in ("L", "R"):
        def head(b): return np.asarray(bones[f"{b}.{side}"][0])
        def tail(b): return np.asarray(bones[f"{b}.{side}"][1])
        wrist = head("wrist")
        palm_normal = np.cross(head("finger5-1") - head("finger2-1"), head("finger3-1") - wrist)
        palm_normal /= np.linalg.norm(palm_normal)
        middle_tip = tail("finger3-3")
        for finger, degrees in FINGER_SPREAD_DEGREES.items():
            w = weight_sum(len(co), weights, tuple(f"finger{finger}-{k}.{side}" for k in (1, 2, 3)))
            if not w.any():
                continue
            pivot = head(f"finger{finger}-1")
            tip = tail(f"finger{finger}-3")
            # Pick the rotation direction that moves the fingertip away from the middle fingertip.
            sign = max((1, -1), key=lambda s: np.linalg.norm(rotate(tip[None], pivot, palm_normal, s * degrees)[0] - middle_tip))
            idx = np.nonzero(w > 0)[0]
            angles = sign * degrees * np.clip(w[idx], 0, 1)
            co[idx] = rotate(co[idx], pivot, palm_normal, angles)
    return co


def rotate(points, pivot, axis, degrees):
    """Rodrigues rotation of points about an axis through pivot; degrees may be per point."""
    theta = np.radians(np.broadcast_to(degrees, (len(points),)))[:, None]
    p = points - pivot
    k = axis[None, :]
    rotated = p * np.cos(theta) + np.cross(k, p) * np.sin(theta) + k * (p @ axis)[:, None] * (1 - np.cos(theta))
    return rotated + pivot


def stylise(co, faces, weights, bones):
    """Neutral mannequin finish: remove the nipples (local smoothing of a small, shallow area) and
    soften the lips slightly. Eyes are covered by separate smooth eyeballs (see create_eyes)."""
    edges = edge_arrays(len(co), faces)

    # Gentle overall flattening of the pectorals; the mask is the (smooth) MakeHuman breast weight field.
    pectorals = weight_sum(len(co), weights, ("breast",))
    pectorals = np.clip(pectorals / pectorals.max() * 1.6, 0, 1) * CHEST_FLATTEN_STRENGTH
    co = smooth_region(co, edges, pectorals, iterations=CHEST_FLATTEN_ITERATIONS)


    nipples = np.zeros(len(co))
    for side in ("L", "R"):
        _, tail = bones[f"breast.{side}"]                           # the breast bone ends at the nipple
        tip = co[np.argmin(np.linalg.norm(co - tail, axis=1))]
        nipples = np.maximum(nipples, ball(co, tip, NIPPLE_RADIUS * 0.4, NIPPLE_RADIUS))
    co = smooth_region(co, edges, nipples, iterations=NIPPLE_ITERATIONS)

    lips = np.clip(weight_sum(len(co), weights, ("oris",)) * 0.6, 0, 1)
    return smooth_region(co, edges, lips, iterations=LIP_ITERATIONS)


def create_eyes(body_co, bones, material):
    """Smooth eyeballs in the mannequin material, as a separate object (not part of zones or the graph)."""
    mesh = bpy.data.meshes.new("Eyes")
    bm = bmesh.new()
    radii = {}
    for side in ("L", "R"):
        centre = np.asarray(bones[f"eye.{side}"][0])
        radius = float(np.min(np.linalg.norm(body_co - centre, axis=1))) * EYEBALL_RADIUS_FACTOR
        radii[side] = round(radius, 2)
        geom = bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=12, radius=radius)
        bmesh.ops.translate(bm, verts=geom["verts"], vec=Vector(centre))
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    for poly in mesh.polygons:
        poly.use_smooth = True
    mesh.materials.append(material)
    obj = bpy.data.objects.new("Eyes", mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj, radii


# --------------------------------------------------------------------------- pose checks

def pose_report(obj, vertex_group_of, dominant, bones):
    co = [v.co.copy() for v in obj.data.vertices]
    # The armpit legitimately joins arm and torso; check only the arm beyond ARMPIT_EXCLUSION from the shoulder joint.
    shoulder = {s: Vector(bones[f"upperarm01.{s}"][0]) for s in "LR"}
    limb = [i for i, g in enumerate(vertex_group_of)
            if g in ("hand", "forearm", "upper-arm")
            and (co[i] - shoulder["L" if co[i].x >= 0 else "R"]).length > ARMPIT_EXCLUSION]
    body = [i for i, g in enumerate(vertex_group_of) if g in ("torso", "pelvis", "thigh")]
    tree = KDTree(len(body))
    for k, i in enumerate(body):
        tree.insert(co[i], k)
    tree.balance()
    gap, at = min((tree.find(co[i])[2], i) for i in limb)

    # Fingers: closest pair of vertices on different fingers, ignoring the webbing near the knuckles.
    finger_gaps = {}
    for side in ("L", "R"):
        per_finger = defaultdict(list)
        for i, name in enumerate(dominant):
            if name.startswith("finger") and name.endswith(side) and not name.startswith(("finger1-1", "finger2-1", "finger3-1", "finger4-1", "finger5-1")):
                per_finger[name.split("-")[0]].append(i)
        best = math.inf
        names = sorted(per_finger)
        for a in range(len(names)):
            for b in range(a + 1, len(names)):
                tb = KDTree(len(per_finger[names[b]]))
                for k, i in enumerate(per_finger[names[b]]):
                    tb.insert(co[i], k)
                tb.balance()
                best = min(best, min(tb.find(co[i])[2] for i in per_finger[names[a]]))
        finger_gaps[side] = round(best, 2)

    zs = [c.z for c in co]
    return {
        "height_cm": round(max(zs) - min(zs), 2),
        "width_cm": round(max(c.x for c in co) - min(c.x for c in co), 2),
        "depth_cm": round(max(c.y for c in co) - min(c.y for c in co), 2),
        "min_gap_arm_to_torso_or_thigh_cm": round(gap, 2),
        "min_gap_location": [round(x, 1) for x in co[at]],
        "min_gap_between_fingers_cm": finger_gaps,
    }


# --------------------------------------------------------------------------- zones

def dominant_bones(obj, weights):
    best = [("root", 0.0)] * len(obj.data.vertices)
    for bone, pairs in weights.items():
        for i, w in pairs:
            if w > best[i][1]:
                best[i] = (bone, w)
    return [b for b, _ in best]


def face_groups(mesh, weights):
    """Dominant bone group per face from summed vertex weights of its corners."""
    per_vertex = defaultdict(lambda: defaultdict(float))
    for bone, pairs in weights.items():
        group = bone_group(bone)
        for i, w in pairs:
            per_vertex[i][group] += w
    result = []
    for poly in mesh.polygons:
        total = defaultdict(float)
        for i in poly.vertices:
            for g, w in per_vertex[i].items():
                total[g] += w
        result.append(max(total.items(), key=lambda kv: kv[1])[0] if total else "torso")
    return result


def classify(mesh, groups, bones, zone_index):
    elbow = {s: Vector(bones[f"lowerarm01.{s}"][0]) for s in "LR"}
    shoulder = {s: Vector(bones[f"upperarm01.{s}"][0]) for s in "LR"}
    knee = {s: Vector(bones[f"lowerleg01.{s}"][0]) for s in "LR"}
    head_centre = (Vector(bones["head"][0]) + Vector(bones["head"][1])) / 2

    head_faces = [p for p, g in zip(mesh.polygons, groups) if g == "head"]
    ear_band = [p for p in head_faces if head_centre.z - 7 < p.center.z < head_centre.z + 3]
    skull_half_width = sorted(abs(p.center.x) for p in ear_band)[int(len(ear_band) * 0.80)] if ear_band else 7.0

    labels = []
    for poly, group in zip(mesh.polygons, groups):
        c, n = poly.center, poly.normal
        side = "l" if c.x >= 0 else "r"
        S = side.upper()
        forward = -n.y                                  # > 0: faces the front
        outward = n.x if c.x >= 0 else -n.x             # > 0: faces away from the body midline

        if group == "head":
            ear = abs(c.x) > skull_half_width and head_centre.z - 9 < c.z < head_centre.z + 4 and c.y > head_centre.y - 4
            if ear:
                z = f"head.ear-{side}"
            elif c.z < head_centre.z + 4 and (forward > 0.25 or (n.z < -0.4 and c.y < head_centre.y)):
                z = "head.face"
            else:
                z = "head.scalp"
        elif group == "neck":
            z = "neck.front" if forward >= 0 else "neck.back"
        elif (group in ("shoulder", "upper-arm", "torso") and (c - shoulder[S]).length < SHOULDER_RADIUS
              and c.z > shoulder[S].z - 6 and abs(c.x) > abs(shoulder[S].x) - 5):
            z = f"arm-{side}.shoulder"                       # deltoid cap around the shoulder joint
        elif group in ("hand", "forearm", "upper-arm", "shoulder"):
            if group in ("forearm", "upper-arm") and (c - elbow[S]).length < ELBOW_RADIUS:
                z = f"arm-{side}.elbow"
            else:
                z = f"arm-{side}.{'upper-arm' if group == 'shoulder' else group}"
        elif group in ("thigh", "lowerleg") and (c - knee[S]).length < KNEE_RADIUS:
            z = f"leg-{side}.knee"
        elif group == "lowerleg":
            z = f"leg-{side}.shin" if forward > 0 else f"leg-{side}.calf"
        elif group == "foot":
            z = f"leg-{side}.foot"
        elif c.z < Z_PUBIC:                              # torso / pelvis / thigh below the pubic line
            if forward < -0.3 and c.z > Z_GLUTEAL_FOLD and abs(c.x) < 17:
                z = "pelvis.all"                             # buttocks
            elif n.z < -0.4 and abs(c.x) < X_GROIN and c.z > 66:
                z = "pelvis.all"                             # crotch, seen from below
            elif forward > 0.2 and abs(c.x) < X_GROIN and c.z > 79 and outward > -0.5:
                z = "pelvis.all"                             # pubic area (not the inner thighs)
            elif outward > 0.45 and c.z > 76:
                z = f"leg-{side}.hip"
            elif group in ("torso", "pelvis") and c.z > 85:
                z = f"leg-{side}.hip"
            else:
                z = f"leg-{side}.thigh"
        elif c.z >= Z_CHEST_BOTTOM:
            z = f"chest.{side}" if forward >= 0 else f"upper-back.{side}"
        elif abs(n.x) > FLANK_NORMAL_X:
            z = f"belly.flank-{side}"
        elif forward >= 0:
            z = "belly.center"
        else:
            z = f"lower-back.{side}"
        labels.append(zone_index[z])
    return labels


def clean_labels(mesh, labels, min_island=15, passes=LABEL_SMOOTHING_PASSES):
    """Smooths zone borders (majority vote over the faces sharing a vertex, two rings wide) and
    merges small islands into the surrounding zone."""
    edge_faces = defaultdict(list)
    vertex_faces = defaultdict(list)
    for poly in mesh.polygons:
        for key in poly.edge_keys:
            edge_faces[key].append(poly.index)
        for v in poly.vertices:
            vertex_faces[v].append(poly.index)
    neighbours = [set() for _ in mesh.polygons]
    for faces in edge_faces.values():
        for a in faces:
            for b in faces:
                if a != b:
                    neighbours[a].add(b)
    ring1 = [set(f for v in poly.vertices for f in vertex_faces[v]) for poly in mesh.polygons]
    ring2 = [set(g for f in r for g in ring1[f]) for r in ring1]

    labels = list(labels)
    for _ in range(passes):
        new = list(labels)
        for f, around in enumerate(ring2):
            votes = Counter(labels[n] for n in around)
            label, count = votes.most_common(1)[0]
            if label != labels[f] and count > votes[labels[f]]:
                new[f] = label
        if new == labels:
            break
        labels = new

    for _ in range(10):
        seen = [False] * len(labels)
        changed = 0
        for start in range(len(labels)):
            if seen[start]:
                continue
            component, stack = [], [start]
            seen[start] = True
            while stack:
                f = stack.pop()
                component.append(f)
                for n in neighbours[f]:
                    if not seen[n] and labels[n] == labels[start]:
                        seen[n] = True
                        stack.append(n)
            if len(component) < min_island:
                members = set(component)
                around = Counter(labels[n] for f in component for n in neighbours[f] if n not in members)
                if around:
                    target = around.most_common(1)[0][0]
                    for f in component:
                        labels[f] = target
                    changed += 1
        if not changed:
            break

    # Remove single-face notches: a quad with 3 of 4 edge neighbours in another zone joins it.
    for _ in range(20):
        new = list(labels)
        for f, nb in enumerate(neighbours):
            votes = Counter(labels[n] for n in nb)
            label, count = votes.most_common(1)[0]
            if label != labels[f] and count >= 3:
                new[f] = label
        if new == labels:
            break
        labels = new
    return labels


# --------------------------------------------------------------------------- main

def main():
    args = parse_args()
    with open(args.zones, encoding="utf-8") as f:
        zones = json.load(f)["zones"]
    zone_index = {z["id"]: i for i, z in enumerate(zones)}

    vertices, faces, bones, weights, scale = load_makehuman(args.source)
    vertices = spread_fingers(vertices, weights, bones)
    vertices = stylise(vertices, faces, weights, bones)
    obj = create_object(vertices, faces, weights)
    dominant = dominant_bones(obj, weights)

    vertex_group_of = [bone_group(b) for b in dominant]
    report = pose_report(obj, vertex_group_of, dominant, bones)

    if report["min_gap_arm_to_torso_or_thigh_cm"] < MIN_ARM_GAP:
        raise SystemExit(f"Arm touches the body: gap {report['min_gap_arm_to_torso_or_thigh_cm']} cm < {MIN_ARM_GAP} cm")

    # Zones are assigned per quad (clean borders along the anatomical edge flow of the MakeHuman mesh;
    # per-triangle labels give saw-tooth borders), then both triangles of a quad inherit the label.
    mesh = obj.data
    groups = face_groups(mesh, weights)
    medium = clean_labels(mesh, classify(mesh, groups, bones, zone_index))
    easy = [zone_index[zones[m]["parentId"]] for m in medium]
    for name, values in (("zone_medium", medium), ("zone_easy", easy)):
        attr = mesh.attributes.new(name, "INT", "FACE")
        attr.data.foreach_set("value", values)

    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.triangulate(bm, faces=bm.faces, quad_method="FIXED", ngon_method="BEAUTY")
    bm.to_mesh(mesh)
    bm.free()
    for poly in mesh.polygons:
        poly.use_smooth = True

    material = bpy.data.materials.new("Mannequin")
    material.use_nodes = True
    bsdf = material.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*[srgb_to_linear(c) for c in SKIN_COLOR_SRGB], 1.0)
    bsdf.inputs["Roughness"].default_value = 0.85
    mesh.materials.append(material)
    eyes, eyeball_radii = create_eyes(np.array([v.co for v in mesh.vertices]), bones, material)

    tri_medium = [0] * len(mesh.polygons)
    mesh.attributes["zone_medium"].data.foreach_get("value", tri_medium)
    counts = Counter(tri_medium)
    report.update({
        "source": "MakeHuman base.obj (CC0), group 'body'",
        "scale_from_makehuman_units": round(scale, 4),
        "gender_blend": GENDER,
        "eyeball_radius_cm": eyeball_radii,
        "eyes_triangles": len(eyes.data.polygons),
        "vertices": len(mesh.vertices),
        "triangles": len(mesh.polygons),
        "zones_used_medium": len(counts),
        "triangles_per_zone": {zones[k]["id"]: v for k, v in sorted(counts.items())},
    })

    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(args.out))
    with open(os.path.splitext(args.out)[0] + "_report.json", "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    print(json.dumps({k: v for k, v in report.items() if k != "triangles_per_zone"}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
