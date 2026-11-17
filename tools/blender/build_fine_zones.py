"""Generates the Hard and Hardcore zone levels (spec 4.2, decision D8: automatic proposal, the owner corrects).

Run on the model built by build_model.py (Easy / Medium are not changed):
    blender -b tools/blender/body.blend --python tools/blender/build_fine_zones.py

Every Medium zone is split into Hard zones and every Hard zone into Hardcore zones by rules over each triangle:
the dominant MakeHuman bone (fingers, phalanges, toes, wrist), a local frame of the limb segment (front / back /
outer / inner side, position along the segment), heights of landmarks (eyes, chin, clavicle) and the normal.
Borders are smoothed inside the parent zone, so the hierarchy always holds.

Writes the face attributes zone_hard / zone_hardcore into body.blend and the zone list (Easy and Medium kept,
Hard and Hardcore regenerated) into zones.json. After manual edits in Blender run build_all.sh --export-only.
"""
import json
import math
import os
import sys
from collections import Counter, defaultdict

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import build_model  # noqa: E402  (load_makehuman: bone positions in the model's coordinates)

ZONES_FILE = os.path.join(HERE, "zones.json")
FORWARD = Vector((0.0, -1.0, 0.0))     # Blender: -Y is the front of the body
MIN_ISLAND = {"Hard": 12, "Hardcore": 8}
SMOOTHING_PASSES = 4

# ----------------------------------------------------------------------------- names

LEFT_RIGHT = [("Lewy", "Prawy"), ("Lewa", "Prawa"), ("Lewe", "Prawe"), ("lewy", "prawy"), ("lewa", "prawa"),
              ("lewe", "prawe"), ("lewego", "prawego"), ("lewej", "prawej"), ("Lewej", "Prawej"), ("lewym", "prawym")]


def sided(template, side):
    """'{lewego} uda' -> 'lewego uda' / 'prawego uda'."""
    text = template
    for left, right in LEFT_RIGHT:
        text = text.replace("{" + left + "}", left if side == "l" else right)
    return text


# ----------------------------------------------------------------------------- geometry helpers

class Face:
    __slots__ = ("index", "c", "n", "bone", "medium", "side")


def limb_frame(start, end, side):
    """Axis along the segment, forward and outward directions perpendicular to it."""
    axis = (end - start).normalized()
    f = (FORWARD - axis * FORWARD.dot(axis)).normalized()
    out_world = Vector((1.0 if side == "l" else -1.0, 0.0, 0.0))
    o = out_world - axis * out_world.dot(axis) - f * out_world.dot(f)
    if o.length < 1e-3:
        o = axis.cross(f)
    o.normalize()
    return axis, f, o, (end - start).length


def quadrant(face, frame):
    """front / back / outer / inner side of a limb segment from the face normal."""
    _, f, o, _ = frame
    nf, no = face.n.dot(f), face.n.dot(o)
    if abs(nf) >= abs(no):
        return "front" if nf >= 0 else "back"
    return "outer" if no >= 0 else "inner"


def along(face, start, frame):
    axis, _, _, length = frame
    return max(0.0, min(1.0, (face.c - start).dot(axis) / max(length, 1e-6)))


def thirds(t):
    return "upper" if t < 1 / 3 else "middle" if t < 2 / 3 else "lower"


def halves(t):
    return "upper" if t < 0.5 else "lower"


# ----------------------------------------------------------------------------- rules

class Rules:
    """Hard and Hardcore labels per face. Returns (hard_key, hard_name, hardcore_key, hardcore_name)."""

    def __init__(self, bones, faces, hand_dorsal):
        self.b = {k: (Vector(v[0]), Vector(v[1])) for k, v in bones.items()}
        self.hand_dorsal = hand_dorsal
        by_medium = defaultdict(list)
        for face in faces:
            by_medium[face.medium].append(face)
        face_z = [f.c.z for f in by_medium["head.face"]]
        self.eye_z = self.b["eye.L"][0].z
        self.chin_z = min(face_z)
        self.clavicle_z = {s: (self.b[f"clavicle.{s.upper()}"][0].z + self.b[f"clavicle.{s.upper()}"][1].z) / 2 for s in "lr"}
        self.ear_low = {}
        for s in "lr":
            zs = [f.c.z for f in by_medium[f"head.ear-{s}"]]
            self.ear_low[s] = (min(zs), max(zs)) if zs else (0, 1)
        top = max(f.c.z for f in faces)
        self.scalp_top = top

    def joint(self, bone, side=None, end=0):
        name = f"{bone}.{side.upper()}" if side else bone
        return self.b[name][end]

    # Each method returns (hard_key, hard_name, hardcore_key, hardcore_name); names use {lewy} placeholders.
    def label(self, face):
        m = face.medium
        root, _, part = m.partition(".")
        s = face.side
        if root == "pelvis":
            return "all", "Miednica", "all", "Miednica"
        if root == "head":
            return self.head(face, part, s)
        if root == "neck":
            return self.neck(face, part)
        if root == "chest":
            return self.chest(face, s)
        if root == "upper-back":
            return self.upper_back(face, s)
        if root == "lower-back":
            return self.lower_back(face, s)
        if root == "belly":
            return self.belly(face, part)
        if root.startswith("arm-"):
            return getattr(self, "arm_" + part.replace("-", "_"))(face, s)
        if root.startswith("leg-"):
            return getattr(self, "leg_" + part)(face, s)
        raise ValueError(f"No rule for zone {m}")

    # ---- head
    def head(self, face, part, s):
        c, n = face.c, face.n
        if part == "face":
            d = self.eye_z - self.chin_z
            ax = abs(c.x)
            fs = "l" if c.x >= 0 else "r"
            if c.z > self.eye_z + 1.8:
                key, name = "forehead", "Czoło"
                hc = "left" if c.x > 1.5 else "right" if c.x < -1.5 else "center"
                return key, name, hc, {"left": "Czoło – lewa strona", "right": "Czoło – prawa strona", "center": "Środek czoła"}[hc]
            if c.z < self.chin_z + 0.2 * d:
                key, name = "chin", "Broda"
                return (key, name, "under", "Pod brodą") if n.z < -0.45 else (key, name, "front", "Czubek brody")
            if ax < 1.9 and c.z > self.chin_z + 0.47 * d:
                key, name = "nose", "Nos"
                return (key, name, "bridge", "Grzbiet nosa") if c.z > self.eye_z - 0.3 * d else (key, name, "tip", "Czubek nosa")
            if ax < 2.9 and c.z <= self.chin_z + 0.47 * d:
                key, name = "mouth", "Usta"
                return (key, name, "upper", "Górna warga") if c.z > self.chin_z + 0.33 * d else (key, name, "lower", "Dolna warga")
            if 1.5 < ax < 6.5 and c.z > self.eye_z - 1.6:
                key, name = f"eye-{fs}", sided("Okolica {lewego} oka", fs)
                return (key, name, "lid", sided("Powieka {lewego} oka", fs)) if c.z > self.eye_z - 0.2 \
                    else (key, name, "under", sided("Pod {lewym} okiem", fs))
            key, name = f"cheek-{fs}", sided("{Lewy} policzek", fs)
            return (key, name, "upper", sided("{Lewa} kość policzkowa", fs)) if c.z > self.chin_z + 0.5 * d \
                else (key, name, "lower", sided("Dolna część {lewego} policzka", fs))
        if part == "scalp":
            fs = "l" if c.x >= 0 else "r"
            if n.z > 0.55:
                hc = ("front", "Czubek głowy – przód") if c.y < self.joint("head").y else ("back", "Czubek głowy – tył")
                return "top", "Czubek głowy", *hc
            if -n.y < -0.35:
                upper = c.z > self.eye_z
                return "back", "Tył głowy", *(("upper", "Potylica") if upper else ("lower", "Tył głowy nad karkiem"))
            key, name = f"side-{fs}", sided("{Lewa} strona głowy", fs)
            return (key, name, "temple", sided("{Lewa} skroń", fs)) if -n.y > 0.1 and c.z < self.eye_z + 6 \
                else (key, name, "above-ear", sided("Nad {lewym} uchem", fs))
        # ears
        lo, hi = self.ear_low[s]
        if c.z < lo + 0.3 * (hi - lo):
            return "lobe", sided("Płatek {lewego} ucha", s), "all", sided("Płatek {lewego} ucha", s)
        return "auricle", sided("Małżowina {lewego} ucha", s), *(("upper", sided("Górna część {lewego} ucha", s)) if c.z > lo + 0.65 * (hi - lo)
                                                                else ("middle", sided("Środek {lewego} ucha", s)))

    # ---- neck
    def neck(self, face, part):
        c = face.c
        fs = "l" if c.x >= 0 else "r"
        lo = self.joint("neck01").z
        hi = self.joint("neck03", end=1).z
        upper = c.z > (lo + hi) / 2
        hc = ("upper", "góra") if upper else ("lower", "dół")
        if part == "front":
            if abs(c.x) < 2.5:
                return "throat", "Krtań", hc[0], f"Krtań – {hc[1]}"
            name = sided("{Lewa} strona szyi", fs)
            return f"side-{fs}", name, hc[0], f"{name} – {hc[1]}"
        if abs(c.x) < 3.0:
            return "nape", "Środek karku", hc[0], f"Środek karku – {hc[1]}"
        name = sided("{Lewa} strona karku", fs)
        return f"side-{fs}", name, hc[0], f"{name} – {hc[1]}"

    # ---- torso
    def chest(self, face, s):
        c = face.c
        top = self.clavicle_z[s]
        mid = 113.0 + (top - 113.0) * 0.45
        inner = abs(c.x) < (abs(self.joint("clavicle", s).x) + abs(self.joint("clavicle", s, 1).x)) / 2
        if c.z > top - 3.5:
            name = sided("Okolica {lewego} obojczyka", s)
            return "clavicle", name, *(("inner", sided("{Lewy} obojczyk – przy szyi", s)) if inner else ("outer", sided("{Lewy} obojczyk – przy barku", s)))
        if abs(c.x) < 3.5:
            name = sided("{Lewa} strona mostka", s)
            return "sternum", name, *(("upper", f"{name} – góra") if c.z > mid else ("lower", f"{name} – dół"))
        if c.z > mid:
            name = sided("{Lewy} mięsień piersiowy", s)
            return "pectoral", name, *(("inner", f"{name} – bliżej mostka") if abs(c.x) < 10 else ("outer", f"{name} – bliżej pachy"))
        name = sided("{Lewe} żebra z przodu", s)
        return "ribs", name, *(("inner", f"{name} – bliżej mostka") if abs(c.x) < 9 else ("outer", f"{name} – z boku"))

    def upper_back(self, face, s):
        c, n = face.c, face.n
        top = self.clavicle_z[s]
        outward = n.x if s == "l" else -n.x
        mid = 113.0 + (top - 113.0) * 0.5
        hc = ("upper", "góra") if c.z > mid else ("lower", "dół")
        if c.z > top - 4:
            name = sided("{Lewa} strona pleców pod karkiem", s)
            return "top", name, *(("inner", f"{name} – przy kręgosłupie") if abs(c.x) < 7 else ("outer", f"{name} – przy barku"))
        if abs(c.x) < 3.5:
            name = sided("{Lewa} strona kręgosłupa (łopatki)", s)
            return "spine", name, hc[0], f"{name} – {hc[1]}"
        if outward > 0.55 or abs(c.x) > 15.5:
            name = sided("{Lewy} bok pleców pod pachą", s)
            return "side", name, hc[0], f"{name} – {hc[1]}"
        name = sided("{Lewa} łopatka", s)
        return "scapula", name, hc[0], f"{name} – {hc[1]}"

    def lower_back(self, face, s):
        c, n = face.c, face.n
        outward = n.x if s == "l" else -n.x
        hc = ("upper", "góra") if c.z > 102 else ("lower", "dół")
        if abs(c.x) < 3.5:
            name = sided("{Lewa} strona kręgosłupa (lędźwie)", s)
        elif outward > 0.45:
            name = sided("{Lewa} talia od tyłu", s)
            return "waist", name, hc[0], f"{name} – {hc[1]}"
        else:
            name = sided("{Lewa} część lędźwi", s)
            return "lumbar", name, hc[0], f"{name} – {hc[1]}"
        return "spine", name, hc[0], f"{name} – {hc[1]}"

    def belly(self, face, part):
        c, n = face.c, face.n
        if part == "center":
            fs = "l" if c.x >= 0 else "r"
            if abs(c.x) < 3.0 and 99.5 < c.z < 104.5:
                return "navel", "Pępek", "all", "Pępek"
            if c.z >= 104.5 and abs(c.x) < 6:
                hc = ("left", "Górna część brzucha – lewa strona") if c.x >= 0 else ("right", "Górna część brzucha – prawa strona")
                return "upper", "Górna część brzucha", *hc
            if c.z <= 99.5 and abs(c.x) < 6:
                return "lower", "Podbrzusze", *(("left", "Podbrzusze – lewa strona") if c.x >= 0 else ("right", "Podbrzusze – prawa strona"))
            name = sided("{Lewa} strona brzucha", fs)
            return f"side-{fs}", name, *(("upper", f"{name} – góra") if c.z > 102 else ("lower", f"{name} – dół"))
        s = part[-1]
        hc_front = -n.y >= 0
        hc = ("front", "z przodu") if hc_front else ("back", "z tyłu")
        if c.z > 107:
            name = sided("{Lewy} bok – żebra", s)
            key = "ribs"
        elif c.z < 97:
            name = sided("{Lewy} bok nad biodrem", s)
            key = "above-hip"
        else:
            name = sided("{Lewa} talia", s)
            key = "waist"
        return key, name, hc[0], f"{name} – {hc[1]}"

    # ---- arms
    def arm_shoulder(self, face, s):
        c, n = face.c, face.n
        joint = self.joint("upperarm01", s)
        hc = ("inner", "bliżej szyi") if abs(c.x) < abs(joint.x) else ("outer", "bliżej ramienia")
        if n.z > 0.5:
            name = sided("Szczyt {lewego} barku", s)
            return "top", name, hc[0], f"{name} – {hc[1]}"
        if -n.y >= 0:
            name = sided("Przód {lewego} barku", s)
            return "front", name, hc[0], f"{name} – {hc[1]}"
        name = sided("Tył {lewego} barku", s)
        return "back", name, hc[0], f"{name} – {hc[1]}"

    def arm_upper_arm(self, face, s):
        start, end = self.joint("upperarm01", s), self.joint("lowerarm01", s)
        frame = limb_frame(start, end, s)
        q = quadrant(face, frame)
        names = {"front": "{Lewy} biceps", "back": "{Lewy} triceps", "outer": "Zewnętrzna strona {lewego} ramienia",
                 "inner": "Wewnętrzna strona {lewego} ramienia"}
        name = sided(names[q], s)
        h = halves(along(face, start, frame))
        return q, name, h, f"{name} – {'bliżej barku' if h == 'upper' else 'bliżej łokcia'}"

    def arm_elbow(self, face, s):
        start, end = self.joint("upperarm01", s), self.joint("lowerarm01", s, 1)
        frame = limb_frame(start, end, s)
        q = quadrant(face, frame)
        names = {"front": "Zgięcie {lewego} łokcia", "back": "Czubek {lewego} łokcia",
                 "outer": "Zewnętrzna strona {lewego} łokcia", "inner": "Wewnętrzna strona {lewego} łokcia"}
        name = sided(names[q], s)
        above = (face.c - self.joint("lowerarm01", s)).dot(frame[0]) < 0
        return q, name, ("upper" if above else "lower"), f"{name} – {'od strony ramienia' if above else 'od strony przedramienia'}"

    def arm_forearm(self, face, s):
        start, end = self.joint("lowerarm01", s), self.joint("wrist", s)
        frame = limb_frame(start, end, s)
        q = quadrant(face, frame)
        names = {"front": "Przód {lewego} przedramienia", "back": "Tył {lewego} przedramienia",
                 "outer": "Zewnętrzna strona {lewego} przedramienia", "inner": "Wewnętrzna strona {lewego} przedramienia"}
        name = sided(names[q], s)
        h = halves(along(face, start, frame))
        return q, name, h, f"{name} – {'bliżej łokcia' if h == 'upper' else 'bliżej nadgarstka'}"

    def arm_hand(self, face, s):
        bone = face.bone or ""
        dorsal = face.n.dot(self.hand_dorsal[s]) >= 0
        fingers = {"1": "{Lewy} kciuk", "2": "{Lewy} palec wskazujący", "3": "{Lewy} palec środkowy",
                   "4": "{Lewy} palec serdeczny", "5": "{Lewy} mały palec"}
        if bone.startswith("finger"):
            finger, phalanx = bone[6], bone[8]
            name = sided(fingers[finger], s)
            if finger == "1":
                parts = {"1": ("base", "nasada"), "2": ("middle", "środek"), "3": ("tip", "opuszek")}
            else:
                parts = {"1": ("proximal", "paliczek bliższy"), "2": ("middle", "paliczek środkowy"), "3": ("distal", "paliczek dalszy")}
            key, text = parts[phalanx]
            return f"finger{finger}", name, key, f"{name} – {text}"
        if bone.startswith("wrist") or not bone.startswith("metacarpal"):
            name = sided("{Lewy} nadgarstek", s)
            return "wrist", name, *(("back", f"{name} – od zewnątrz") if dorsal else ("front", f"{name} – od wewnątrz"))
        wrist = self.joint("wrist", s)
        knuckles = sum((self.joint(f"finger{i}-1", s) for i in range(2, 6)), Vector()) / 4
        near_fingers = (face.c - wrist).length > (knuckles - wrist).length * 0.55
        if dorsal:
            name = sided("Grzbiet {lewej} dłoni", s)
            return "back", name, *(("knuckles", sided("Knykcie {lewej} dłoni", s)) if near_fingers else ("middle", f"{name} – środek"))
        name = sided("Wnętrze {lewej} dłoni", s)
        return "palm", name, *(("fingers", f"{name} – przy palcach") if near_fingers else ("heel", f"{name} – przy nadgarstku"))

    # ---- legs
    def leg_hip(self, face, s):
        c, n = face.c, face.n
        hc = ("upper", "góra") if c.z > 86 else ("lower", "dół")
        if -n.y > 0.3:
            name = sided("Przód {lewego} biodra", s)
            return "front", name, hc[0], f"{name} – {hc[1]}"
        name = sided("Bok {lewego} biodra", s)
        return "side", name, hc[0], f"{name} – {hc[1]}"

    def leg_thigh(self, face, s):
        start, end = self.joint("upperleg01", s), self.joint("lowerleg01", s)
        frame = limb_frame(start, end, s)
        q = quadrant(face, frame)
        names = {"front": "Przód {lewego} uda", "back": "Tył {lewego} uda", "outer": "Zewnętrzna strona {lewego} uda",
                 "inner": "Wewnętrzna strona {lewego} uda"}
        name = sided(names[q], s)
        t = thirds(along(face, start, frame))
        return q, name, t, f"{name} – {dict(upper='góra', middle='środek', lower='dół')[t]}"

    def leg_knee(self, face, s):
        start, end = self.joint("upperleg01", s), self.joint("lowerleg01", s, 1)
        frame = limb_frame(start, end, s)
        q = quadrant(face, frame)
        names = {"front": "{Lewa} rzepka", "back": "{Lewy} dół podkolanowy", "outer": "Zewnętrzna strona {lewego} kolana",
                 "inner": "Wewnętrzna strona {lewego} kolana"}
        name = sided(names[q], s)
        above = face.c.z > self.joint("lowerleg01", s).z
        return q, name, ("upper" if above else "lower"), f"{name} – {'góra' if above else 'dół'}"

    def leg_shin(self, face, s):
        start, end = self.joint("lowerleg01", s), self.joint("foot", s)
        frame = limb_frame(start, end, s)
        _, f, o, _ = frame
        no = face.n.dot(o)
        if abs(no) < 0.45:
            q, template = "front", "Przednia krawędź {lewej} goleni"
        elif no > 0:
            q, template = "outer", "Zewnętrzna strona {lewej} goleni"
        else:
            q, template = "inner", "Wewnętrzna strona {lewej} goleni"
        name = sided(template, s)
        h = halves(along(face, start, frame))
        return q, name, h, f"{name} – {'góra' if h == 'upper' else 'dół'}"

    def leg_calf(self, face, s):
        start, end = self.joint("lowerleg01", s), self.joint("foot", s)
        frame = limb_frame(start, end, s)
        t = along(face, start, frame)
        if t > 0.78:
            name = sided("{Lewe} ścięgno Achillesa", s)
            return "achilles", name, "all", name
        no = face.n.dot(frame[2])
        q, template = ("outer", "Zewnętrzna strona {lewej} łydki") if no >= 0 else ("inner", "Wewnętrzna strona {lewej} łydki")
        name = sided(template, s)
        h = "upper" if t < 0.4 else "lower"
        return q, name, h, f"{name} – {'góra' if h == 'upper' else 'dół'}"

    def leg_foot(self, face, s):
        c, n = face.c, face.n
        bone = face.bone or ""
        ankle = self.joint("foot", s)
        if bone.startswith("toe"):
            toe = bone[3]
            names = {"1": "Duży palec {lewej} stopy", "2": "Drugi palec {lewej} stopy", "3": "Trzeci palec {lewej} stopy",
                     "4": "Czwarty palec {lewej} stopy", "5": "Mały palec {lewej} stopy"}
            return "toes", sided("Palce {lewej} stopy", s), f"toe{toe}", sided(names[toe], s)
        if (c - ankle).length < 5.0 and c.z > ankle.z - 3.0:
            outer = abs(c.x) > abs(ankle.x)
            name = sided("{Lewa} kostka", s)
            return "ankle", name, *(("outer", sided("{Lewa} kostka zewnętrzna", s)) if outer else ("inner", sided("{Lewa} kostka wewnętrzna", s)))
        if c.y > ankle.y + 0.5 and c.z < ankle.z:
            name = sided("{Lewa} pięta", s)
            return "heel", name, "all", name
        toe_base = self.joint("toe3-1", s)
        front = (c - ankle).dot(toe_base - ankle) > (toe_base - ankle).length_squared * 0.5
        if n.z < -0.45:
            name = sided("Podeszwa {lewej} stopy", s)
            return "sole", name, *(("front", f"{name} – przód") if front else ("arch", f"{name} – łuk"))
        name = sided("Grzbiet {lewej} stopy", s)
        return "top", name, *(("front", f"{name} – przy palcach") if front else ("back", f"{name} – przy kostce"))


# ----------------------------------------------------------------------------- smoothing inside the parent zone

def neighbours_of(mesh):
    edge_faces = defaultdict(list)
    for poly in mesh.polygons:
        for key in poly.edge_keys:
            edge_faces[key].append(poly.index)
    neighbours = [set() for _ in mesh.polygons]
    for faces in edge_faces.values():
        for a in faces:
            for b in faces:
                if a != b:
                    neighbours[a].add(b)
    return neighbours


def clean(labels, parents, neighbours, min_island):
    """Majority smoothing and small-island merging, never across the parent zone."""
    labels = list(labels)
    ring2 = [set(g for f in nb for g in neighbours[f]) | nb for nb in neighbours]
    for _ in range(SMOOTHING_PASSES):
        new = list(labels)
        for f, around in enumerate(ring2):
            votes = Counter(labels[n] for n in around if parents[n] == parents[f])
            if not votes:
                continue
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
                around = Counter(labels[n] for f in component for n in neighbours[f]
                                 if n not in members and parents[n] == parents[start])
                if around:
                    target = around.most_common(1)[0][0]
                    for f in component:
                        labels[f] = target
                    changed += 1
        if not changed:
            break
    return labels


# ----------------------------------------------------------------------------- main

def hand_dorsal_directions(mesh, obj, bones):
    """Normal of the back of each hand: the smallest-variance axis of the metacarpal vertices, oriented to the
    side the knuckles bulge towards (away from the palm's centre of mass)."""
    groups = {g.index: g.name for g in obj.vertex_groups}
    result = {}
    for s in "lr":
        S = s.upper()
        points = []
        for v in mesh.vertices:
            for g in v.groups:
                name = groups[g.group]
                if name.startswith("metacarpal") and name.endswith("." + S) and g.weight > 0.6:
                    points.append(v.co.copy())
                    break
        p = np.array([[q.x, q.y, q.z] for q in points])
        centre = p.mean(axis=0)
        _, _, vt = np.linalg.svd(p - centre)
        normal = Vector(vt[2])
        # The metacarpal bones lie closer to the back of the hand than to the palm (the palm is padded).
        bone_mid = sum((Vector(bones[f"metacarpal{i}.{S}"][0]) for i in range(1, 5)), Vector()) / 4
        if (bone_mid - Vector(centre)).dot(normal) < 0:
            normal = -normal
        result[s] = normal.normalized()
    return result


def main():
    with open(ZONES_FILE, encoding="utf-8") as f:
        data = json.load(f)
    base = [z for z in data["zones"] if z["level"] in ("Easy", "Medium")]
    medium_ids = [z["id"] for z in base]

    obj = bpy.data.objects["Body"]
    mesh = obj.data
    _, _, bones, _, _ = build_model.load_makehuman(os.path.join(HERE, "source", "makehuman"))

    count = len(mesh.polygons)
    medium = [0] * count
    mesh.attributes["zone_medium"].data.foreach_get("value", medium)

    # Dominant bone per face (sum of the weights of its corners).
    names = {g.index: g.name for g in obj.vertex_groups}
    vertex_bone = []
    for v in mesh.vertices:
        vertex_bone.append([(names[g.group], g.weight) for g in v.groups])
    faces = []
    for poly in mesh.polygons:
        total = defaultdict(float)
        for i in poly.vertices:
            for name, w in vertex_bone[i]:
                total[name] += w
        face = Face()
        face.index = poly.index
        face.c = poly.center.copy()
        face.n = poly.normal.copy()
        face.bone = max(total.items(), key=lambda kv: kv[1])[0] if total else None
        face.medium = medium_ids[medium[poly.index]]
        root = face.medium.split(".")[0]
        face.side = root[-1] if root.endswith(("-l", "-r")) else ("l" if face.c.x >= 0 else "r")
        if face.medium.startswith("head.ear-"):
            face.side = face.medium[-1]
        faces.append(face)

    rules = Rules(bones, faces, hand_dorsal_directions(mesh, obj, bones))
    hard_of, hardcore_of = [], []
    hard_names, hardcore_names, order = {}, {}, []
    for face in faces:
        hk, hn, ck, cn = rules.label(face)
        hard_id = f"{face.medium}.{hk}"
        core_id = f"{hard_id}.{ck}"
        hard_names.setdefault(hard_id, hn)
        hardcore_names.setdefault(core_id, cn)
        hard_of.append(hard_id)
        hardcore_of.append(core_id)

    neighbours = neighbours_of(mesh)
    hard_of = clean(hard_of, [f.medium for f in faces], neighbours, MIN_ISLAND["Hard"])
    # Hardcore labels whose Hard parent changed during smoothing are re-derived inside the new parent.
    hardcore_of = [c if c.rsplit(".", 1)[0] == h else None for c, h in zip(hardcore_of, hard_of)]
    for i, c in enumerate(hardcore_of):
        if c is None:
            around = Counter(hardcore_of[n] for n in neighbours[i] if hardcore_of[n] and hard_of[n] == hard_of[i])
            hardcore_of[i] = around.most_common(1)[0][0] if around else f"{hard_of[i]}.all"
            hardcore_names.setdefault(hardcore_of[i], hard_names[hard_of[i]])
    hardcore_of = clean(hardcore_of, hard_of, neighbours, MIN_ISLAND["Hardcore"])

    # Zone list: Easy and Medium unchanged (attribute indices stay valid), then Hard and Hardcore in mesh order
    # of their parents.
    used_hard = sorted(set(hard_of), key=lambda z: (medium_ids.index(z.rsplit(".", 1)[0]), z))
    used_core = sorted(set(hardcore_of), key=lambda z: (used_hard.index(z.rsplit(".", 1)[0]), z))
    by_id = {z["id"]: z for z in base}
    zones = list(base)
    for zid in used_hard:
        parent = by_id[zid.rsplit(".", 1)[0]]
        zones.append({"id": zid, "level": "Hard", "parentId": parent["id"], "namePl": hard_names[zid],
                      "side": side_of(zid, hard_names[zid], parent["side"]), **drawable(parent)})
    by_id.update({z["id"]: z for z in zones})
    for zid in used_core:
        parent = by_id[zid.rsplit(".", 1)[0]]
        zones.append({"id": zid, "level": "Hardcore", "parentId": parent["id"], "namePl": hardcore_names[zid],
                      "side": side_of(zid, hardcore_names[zid], parent["side"]), **drawable(parent)})
    index = {z["id"]: i for i, z in enumerate(zones)}

    for attribute, values in (("zone_hard", hard_of), ("zone_hardcore", hardcore_of)):
        if attribute in mesh.attributes:
            mesh.attributes.remove(mesh.attributes[attribute])
        attr = mesh.attributes.new(attribute, "INT", "FACE")
        attr.data.foreach_set("value", [index[v] for v in values])

    data["zones"] = zones
    data["comment"] = ("Built-in zone list (spec 4.2). Ids: dot-separated hierarchy, side encoded in the root segment "
                       "(-l / -r) or, for central parents, in a '-l' / '-r' segment. 'drawable': false = never drawn as a "
                       "target. Hard and Hardcore are generated by build_fine_zones.py (automatic proposal, D8).")
    with open(ZONES_FILE, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")
    bpy.ops.wm.save_mainfile()

    hard_counts = Counter(hard_of)
    core_counts = Counter(hardcore_of)
    print(json.dumps({
        "hard_zones": len(used_hard), "hardcore_zones": len(used_core),
        "smallest_hard": sorted(((v, k) for k, v in hard_counts.items()))[:5],
        "smallest_hardcore": sorted(((v, k) for k, v in core_counts.items()))[:8],
        "hand_dorsal": {s: [round(x, 2) for x in v] for s, v in rules.hand_dorsal.items()},
    }, ensure_ascii=False, indent=1))


def drawable(parent):
    return {} if parent.get("drawable", True) else {"drawable": False}


def side_of(zid, name, parent_side):
    if parent_side != "Center":
        return parent_side
    segments = zid.split(".")
    if any(seg.endswith("-l") or seg == "left" for seg in segments[1:]):
        return "Left"
    if any(seg.endswith("-r") or seg == "right" for seg in segments[1:]):
        return "Right"
    return "Center"


if __name__ == "__main__":
    main()
