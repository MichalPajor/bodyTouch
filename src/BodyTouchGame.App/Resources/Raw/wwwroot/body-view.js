// Body Touch Game – 3D body view (Three.js), driven from C# through the HybridWebView bridge (spec section 12).
// C# -> JS: functions on window.bodyView (called with HybridWebView.InvokeJavaScriptAsync("bodyView.<name>")).
// JS -> C#: window.HybridWebView.InvokeDotNet("Receive", [eventName, jsonPayload]).
//
// Target secrecy (spec 5): the target exists here only after focusTarget (Draw) or showReveal (Reveal), or as the
// model's target pin in Draw with allowTargetPin. clearTarget removes all of it; C# calls it on every other state.
import * as THREE from 'three';
import { GLTFLoader } from './lib/three/addons/loaders/GLTFLoader.js';
import { OrbitControls } from './lib/three/addons/controls/OrbitControls.js';

const COLORS = {
  mannequin: '#C8CCD6',
  surface: '#171329',
  pin: '#5B8CFF',
  target: '#FF9A3C',
  path: '#FFFFFF',
};
const MAX_ZOOM = 8;            // spec 12: zoom up to about 8x
const PIN_RADIUS_CM = 1.1;
const PATH_RADIUS_CM = 0.3;
const PATH_LIFT_CM = 0.45;     // the path is drawn slightly above the skin
const DASH_CM = 1.8;
const GAP_CM = 1.1;
const TAP_MAX_MS = 350;
const TAP_MAX_MOVE_PX = 10;
const DOUBLE_TAP_MS = 320;
const PIN_GRAB_PX = 44;        // touching the pin closer than this drags it
const CAMERA_TWEEN_MS = 700;
const REVEAL_PATH_MS = 1300;

const state = {
  mode: 'View',
  allowTargetPin: false,
  editorLevel: 'Medium',
  root: null,                  // loaded glTF scene
  body: null,                  // THREE.Mesh 'Body'
  zones: null,                 // body_zones.json
  zoneById: new Map(),
  vertexTriangleCount: null,   // Uint16Array: triangles around each vertex
  baseColors: null,            // Float32Array: colours from setZoneColors (or the mannequin colour)
  highlight: null,             // { weights: Map<vertex, weight>, pulse: bool }
  target: null,                // what the view knows about the target (see secrecy note above)
  targetMarker: null,          // marker of a point target / model's target pin
  pin: null,                   // guesser's pin { mesh, point }
  revealGroup: null,
  revealAnim: null,
  cameraTween: null,
  vertexGrid: null,
  home: { target: new THREE.Vector3(), distance: 300 },
  pageStart: performance.now(),
  frames: [],
  insets: { top: 0, bottom: 0 },
  bench: null,
  userMoved: false,
};

// ------------------------------------------------------------------ bridge

function send(eventName, payload = {}) {
  const json = JSON.stringify(payload);
  const bridge = window.HybridWebView;
  if (bridge && typeof bridge.InvokeDotNet === 'function') {
    bridge.InvokeDotNet('Receive', [eventName, json]).catch((e) => console.error('[bridge]', e));
  } else {
    console.log('[bridge]', eventName, json);   // desktop browser: no host
  }
}

window.addEventListener('error', (e) => send('onError', { message: String(e.message || e) }));
window.addEventListener('unhandledrejection', (e) => send('onError', { message: String(e.reason?.message || e.reason) }));

// ------------------------------------------------------------------ scene

const container = document.getElementById('view');
const statsEl = document.getElementById('stats');
const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
renderer.setClearColor(COLORS.surface);
container.appendChild(renderer.domElement);

const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(30, 1, 1, 5000);
scene.add(new THREE.HemisphereLight(0xffffff, 0x3a3456, 1.4));
const key = new THREE.DirectionalLight(0xffffff, 1.7);
key.position.set(120, 220, 260);
scene.add(key);
const rim = new THREE.DirectionalLight(0xc9c0ff, 1.3);
rim.position.set(-160, 180, -260);
scene.add(rim);

const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.dampingFactor = 0.12;
controls.rotateSpeed = 0.9;
controls.enablePan = true;
controls.screenSpacePanning = true;
controls.touches = { ONE: THREE.TOUCH.ROTATE, TWO: THREE.TOUCH.DOLLY_PAN };

const material = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.85, metalness: 0, vertexColors: true });
// Soft rim light along the silhouette ("delikatna poświata krawędzi", spec 13): fresnel term added to the shading.
const RIM = { color: new THREE.Color('#BFB4FF'), strength: 0.32, power: 3.0 };
material.onBeforeCompile = (shader) => {
  shader.uniforms.uRimColor = { value: RIM.color };
  shader.uniforms.uRimStrength = { value: RIM.strength };
  shader.uniforms.uRimPower = { value: RIM.power };
  shader.fragmentShader = 'uniform vec3 uRimColor;\nuniform float uRimStrength;\nuniform float uRimPower;\n' +
    shader.fragmentShader.replace('#include <opaque_fragment>', `#include <opaque_fragment>
      float rimTerm = pow(1.0 - clamp(dot(normal, normalize(vViewPosition)), 0.0, 1.0), uRimPower);
      gl_FragColor.rgb += uRimColor * rimTerm * uRimStrength;`);
};
const eyeMaterial = new THREE.MeshStandardMaterial({ color: COLORS.mannequin, roughness: 0.6, metalness: 0 });
const pinMaterial = new THREE.MeshStandardMaterial({ color: COLORS.pin, roughness: 0.35 });
const targetMaterial = new THREE.MeshStandardMaterial({ color: COLORS.target, roughness: 0.35, emissive: COLORS.target, emissiveIntensity: 0.25 });

function resize() {
  const w = container.clientWidth || 1;
  const h = container.clientHeight || 1;
  renderer.setSize(w, h, false);
  camera.aspect = w / h;
  // Shift the picture so its centre is the centre of the area not covered by overlays.
  const shift = (state.insets.bottom - state.insets.top) / 2;
  if (shift !== 0) camera.setViewOffset(w, h, 0, shift, w, h); else camera.clearViewOffset();
  camera.updateProjectionMatrix();
  if (state.body) {
    updateHome(state.body);
    if (!state.userMoved && !state.cameraTween) resetCamera('Front');
  }
}
new ResizeObserver(resize).observe(container);
resize();

// ------------------------------------------------------------------ public API (C# -> JS)

async function loadModel(url) {
  const started = performance.now();
  const gltf = await new GLTFLoader().loadAsync(url);
  const body = gltf.scene.getObjectByName('Body');
  if (!body || !body.isMesh) throw new Error(`Mesh 'Body' not found in ${url}`);
  const eyes = gltf.scene.getObjectByName('Eyes');
  if (eyes && eyes.isMesh) eyes.material = eyeMaterial;

  const zonesUrl = url.replace(/\.glb$/i, '_zones.json');
  const zones = await (await fetch(zonesUrl)).json();
  const triangles = body.geometry.index.count / 3;
  if (zones.triangleCount !== triangles) {
    throw new Error(`body_zones.json describes ${zones.triangleCount} triangles, the model has ${triangles}`);
  }

  if (state.root) scene.remove(state.root);
  body.material = material;
  scene.add(gltf.scene);
  state.root = gltf.scene;
  state.body = body;
  state.zones = zones;
  state.zoneById = new Map(zones.zones.map((z, i) => [z.id, { ...z, index: i }]));
  prepareColors(body.geometry);
  state.vertexGrid = buildVertexGrid(body.geometry);
  frameModel(body);

  return { triangles, loadMs: performance.now() - started, pageMs: performance.now() - state.pageStart };
}

/** mode: View | Draw | Guess | Reveal | Editor. options: { allowTargetPin, level } (level = Editor zone level). */
function setMode(mode, options = {}) {
  state.mode = mode;
  state.allowTargetPin = mode === 'Draw' && !!options.allowTargetPin;
  if (options.level) state.editorLevel = options.level;
  return true;
}

function resetCamera(view = 'Front', animate = false) {
  const angles = { Front: 0, Right: Math.PI / 2, Back: Math.PI, Left: -Math.PI / 2 };
  const a = angles[view] ?? 0;
  const { target, distance } = state.home;
  const position = new THREE.Vector3(target.x + distance * Math.sin(a), target.y, target.z + distance * Math.cos(a));
  moveCamera(position, target, animate);
  return true;
}

/** zoneId -> '#RRGGBB' base colours. Colours blend softly across zone borders (per-vertex averaging, spec 13). */
function setZoneColors(colors = {}) {
  const geometry = state.body.geometry;
  const index = geometry.index.array;
  const triangleCount = index.length / 3;
  const triangleColor = new Array(triangleCount).fill(null);

  const entries = Object.entries(colors)
    .map(([id, hex]) => ({ zone: state.zoneById.get(id), color: new THREE.Color(hex) }))
    .filter((e) => e.zone)
    .sort((a, b) => levelRank(a.zone.level) - levelRank(b.zone.level));   // finer levels override coarser ones
  for (const { zone, color } of entries) {
    const labels = state.zones.triangleZones[zone.level];
    for (let t = 0; t < triangleCount; t++) if (labels[t] === zone.index) triangleColor[t] = color;
  }

  const base = new THREE.Color(COLORS.mannequin);
  const count = geometry.attributes.position.count;
  const sum = new Float32Array(count * 3);
  for (let t = 0; t < triangleCount; t++) {
    const c = triangleColor[t] || base;
    for (let k = 0; k < 3; k++) {
      const v = index[t * 3 + k];
      sum[v * 3] += c.r; sum[v * 3 + 1] += c.g; sum[v * 3 + 2] += c.b;
    }
  }
  const n = state.vertexTriangleCount;
  for (let v = 0; v < count; v++) {
    const d = n[v] || 1;
    sum[v * 3] /= d; sum[v * 3 + 1] /= d; sum[v * 3 + 2] /= d;
  }
  state.baseColors = sum;
  applyColors(1);
  return true;
}

/**
 * Rotates to the target and highlights it (pulsing).
 * target: { kind: 'Zone', zoneId } | { kind: 'Point', triangleIndex, baryU, baryV, radiusCm }.
 */
function focusTarget(target, animate = true) {
  clearTarget();
  state.target = target;
  const info = describeTarget(target, true);
  applyColors(1);
  if (info.direction) rotateTowards(info.direction, animate);
  return true;
}

/** Removes the target from the view and from memory (highlight, markers, model's target pin). */
function clearTarget() {
  state.target = null;
  state.highlight = null;
  removeObject(state.targetMarker);
  state.targetMarker = null;
  applyColors(1);
  return true;
}

/** Removes the guesser's pin and the result path. */
function clearPin() {
  removeObject(state.pin?.mesh);
  state.pin = null;
  clearReveal();
  return true;
}

/**
 * Result: { pin: {triangleIndex, baryU, baryV} | null, target, path: [x, y, z, ...], distanceCm }.
 * Pin -> path drawn along the body -> target (spec 5, Reveal).
 */
function showReveal(payload) {
  clearReveal();
  clearTarget();
  state.target = payload.target;
  const info = describeTarget(payload.target, false);
  applyColors(1);
  if (payload.pin) setGuessPin(pointFromSurface(payload.pin), payload.pin, false);

  const group = new THREE.Group();
  const points = [];
  for (let i = 0; i + 2 < (payload.path || []).length; i += 3) {
    points.push(liftAboveSkin(new THREE.Vector3(payload.path[i], payload.path[i + 1], payload.path[i + 2])));
  }
  const dashes = [];
  if (points.length >= 2) {
    const curve = new THREE.CatmullRomCurve3(points, false, 'centripetal');
    const length = curve.getLength();
    const pathMaterial = new THREE.MeshBasicMaterial({ color: COLORS.path });
    for (let s = 0; s < length; s += DASH_CM + GAP_CM) {
      const e = Math.min(length, s + DASH_CM);
      const sub = [];
      const steps = 4;
      for (let k = 0; k <= steps; k++) sub.push(curve.getPointAt((s + (e - s) * k / steps) / length));
      const mesh = new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(sub), 4, PATH_RADIUS_CM, 6, false), pathMaterial);
      mesh.visible = false;
      group.add(mesh);
      dashes.push({ mesh, start: s / length });
    }
  }
  scene.add(group);
  state.revealGroup = group;
  state.revealAnim = { start: performance.now(), dashes };

  // Turn to a side from which both the target and the pin can be seen: the mean of their horizontal directions
  // (when they are on opposite sides, the target's side wins).
  const flat = (v) => (v ? new THREE.Vector3(v.x, 0, v.z) : null);
  const targetDir = flat(info.direction);
  const pinDir = state.pin ? flat(state.pin.mesh.position.clone().sub(state.home.target)) : null;
  let direction = targetDir ?? pinDir;
  if (targetDir && pinDir && targetDir.lengthSq() > 1e-6 && pinDir.lengthSq() > 1e-6) {
    const mean = targetDir.clone().normalize().add(pinDir.clone().normalize());
    if (mean.lengthSq() > 0.25) direction = mean;
  }
  if (direction) rotateTowards(direction, true);
  return true;
}

function clearReveal() {
  if (state.revealGroup) {
    scene.remove(state.revealGroup);
    state.revealGroup.traverse((o) => { o.geometry?.dispose(); });
    state.revealGroup = null;
  }
  state.revealAnim = null;
  return true;
}

/** Diagnostics for the target-secrecy check (spec 5): what the view currently holds. */
function getDebugState() {
  return {
    mode: state.mode,
    hasTarget: state.target !== null || state.highlight !== null || state.targetMarker !== null,
    hasPin: state.pin !== null,
    hasReveal: state.revealGroup !== null,
  };
}

/** CSS pixels at the top and bottom covered by native overlays; the model is framed in the remaining area. */
function setViewInsets(top, bottom) {
  state.insets = { top: Math.max(0, top || 0), bottom: Math.max(0, bottom || 0) };
  resize();
  return true;
}

/** Shows or hides the diagnostic FPS counter (hidden by default). */
function setStatsVisible(visible) {
  statsEl.style.display = visible ? 'block' : 'none';
  return true;
}

/** Render resolution relative to CSS pixels (quality / performance knob; default min(devicePixelRatio, 2)). */
function setPixelRatio(ratio) {
  renderer.setPixelRatio(Math.min(ratio, window.devicePixelRatio || 1));
  resize();
  return renderer.getPixelRatio();
}

function zoomBy(factor) {
  const offset = camera.position.clone().sub(controls.target);
  const distance = THREE.MathUtils.clamp(offset.length() / factor, controls.minDistance, controls.maxDistance);
  moveCamera(controls.target.clone().add(offset.setLength(distance)), controls.target.clone(), true);
  return true;
}

window.bodyView = {
  loadModel, setMode, resetCamera, setZoneColors, focusTarget, clearTarget, clearPin, showReveal, clearReveal,
  getDebugState, zoomBy, setPixelRatio, setViewInsets, setStatsVisible,
  startBenchmark: () => { startBenchmark(); return true; },
};

// ------------------------------------------------------------------ targets, pins, colours

/** Highlights the target and returns { centre, direction } for the camera. */
function describeTarget(target, pulse) {
  const geometry = state.body.geometry;
  const index = geometry.index.array;
  const pos = geometry.attributes.position;
  if (target.kind === 'Zone') {
    const zone = state.zoneById.get(target.zoneId);
    if (!zone) return {};
    const labels = state.zones.triangleZones[zone.level];
    const counts = new Map();
    const centre = new THREE.Vector3();
    const normal = new THREE.Vector3();
    const tri = new THREE.Triangle();
    const tmp = new THREE.Vector3();
    let n = 0;
    for (let t = 0; t < labels.length; t++) {
      if (labels[t] !== zone.index) continue;
      for (let k = 0; k < 3; k++) counts.set(index[t * 3 + k], (counts.get(index[t * 3 + k]) || 0) + 1);
      tri.setFromAttributeAndIndices(pos, index[t * 3], index[t * 3 + 1], index[t * 3 + 2]);
      centre.add(tri.getMidpoint(tmp));
      normal.add(tri.getNormal(tmp).multiplyScalar(tri.getArea()));
      n++;
    }
    const weights = new Map();
    for (const [v, c] of counts) weights.set(v, c / state.vertexTriangleCount[v]);
    state.highlight = { weights, pulse };
    centre.divideScalar(Math.max(1, n));
    state.body.localToWorld(centre);
    return { centre, direction: normal.lengthSq() > 0 ? normal.normalize() : centre.clone().sub(state.home.target) };
  }

  // Point target: marker on the surface, highlight within the radius.
  const point = pointFromSurface(target);
  const normal = surfaceNormal(target);
  state.targetMarker = makeMarker(targetMaterial, point, normal, true);
  scene.add(state.targetMarker);
  const radius = target.radiusCm || 0;
  if (radius > 0) {
    const weights = new Map();
    const local = state.body.worldToLocal(point.clone());
    for (let v = 0; v < pos.count; v++) {
      const d = Math.hypot(pos.getX(v) - local.x, pos.getY(v) - local.y, pos.getZ(v) - local.z);
      if (d < radius) weights.set(v, 1 - (d / radius) * 0.3);
    }
    state.highlight = { weights, pulse };
  }
  return { centre: point, direction: normal };
}

function setGuessPin(point, surface, notify) {
  if (!state.pin) {
    state.pin = { mesh: new THREE.Mesh(new THREE.SphereGeometry(PIN_RADIUS_CM, 20, 14), pinMaterial), point: surface };
    scene.add(state.pin.mesh);
  }
  state.pin.mesh.position.copy(point);
  state.pin.point = surface;
  if (notify) send('onPinPlaced', { ...surface, zoneIds: zoneIdsAt(surface.triangleIndex) });
}

function setTargetPin(point, surface) {
  removeObject(state.targetMarker);
  state.target = { kind: 'Point', ...surface, radiusCm: 0 };
  state.targetMarker = makeMarker(targetMaterial, point, surfaceNormal(surface), true);
  scene.add(state.targetMarker);
  send('onPinPlaced', { ...surface, zoneIds: zoneIdsAt(surface.triangleIndex) });
}

function makeMarker(markerMaterial, point, normal, withRing) {
  const group = new THREE.Group();
  const ball = new THREE.Mesh(new THREE.SphereGeometry(PIN_RADIUS_CM, 20, 14), markerMaterial);
  group.add(ball);
  if (withRing) {
    const ring = new THREE.Mesh(new THREE.TorusGeometry(PIN_RADIUS_CM * 2.2, 0.25, 8, 32), markerMaterial);
    ring.lookAt(normal);
    group.add(ring);
  }
  group.position.copy(point);
  return group;
}

function removeObject(object) {
  if (!object) return;
  scene.remove(object);
  object.traverse?.((o) => o.geometry?.dispose());
}

function pointFromSurface(p) {
  const geometry = state.body.geometry;
  const index = geometry.index.array;
  const pos = geometry.attributes.position;
  const t = p.triangleIndex;
  const a = new THREE.Vector3().fromBufferAttribute(pos, index[t * 3]);
  const b = new THREE.Vector3().fromBufferAttribute(pos, index[t * 3 + 1]);
  const c = new THREE.Vector3().fromBufferAttribute(pos, index[t * 3 + 2]);
  const w = 1 - p.baryU - p.baryV;
  const local = a.multiplyScalar(w).add(b.multiplyScalar(p.baryU)).add(c.multiplyScalar(p.baryV));
  return state.body.localToWorld(local);
}

function surfaceNormal(p) {
  const geometry = state.body.geometry;
  const index = geometry.index.array;
  const tri = new THREE.Triangle().setFromAttributeAndIndices(geometry.attributes.position,
    index[p.triangleIndex * 3], index[p.triangleIndex * 3 + 1], index[p.triangleIndex * 3 + 2]);
  return tri.getNormal(new THREE.Vector3());
}

function zoneIdsAt(triangle) {
  const zoneIds = {};
  for (const [level, labels] of Object.entries(state.zones.triangleZones)) zoneIds[level] = state.zones.zones[labels[triangle]].id;
  return zoneIds;
}

/** Writes base colours mixed with the (pulsing) target highlight into the vertex colour attribute. */
const highlightColor = new THREE.Color(COLORS.target);
function applyColors(amount) {
  if (!state.body) return;
  const attr = state.body.geometry.attributes.color;
  const base = state.baseColors;
  attr.array.set(base);
  if (state.highlight) {
    for (const [v, w] of state.highlight.weights) {
      const k = w * amount;
      attr.array[v * 3] = base[v * 3] * (1 - k) + highlightColor.r * k;
      attr.array[v * 3 + 1] = base[v * 3 + 1] * (1 - k) + highlightColor.g * k;
      attr.array[v * 3 + 2] = base[v * 3 + 2] * (1 - k) + highlightColor.b * k;
    }
  }
  attr.needsUpdate = true;
}

// ------------------------------------------------------------------ camera

function moveCamera(position, target, animate) {
  if (!animate) {
    state.cameraTween = null;
    controls.target.copy(target);
    camera.position.copy(position);
    camera.lookAt(target);
    controls.update();
    return;
  }
  state.cameraTween = {
    start: performance.now(),
    fromPos: camera.position.clone(), toPos: position.clone(),
    fromTarget: controls.target.clone(), toTarget: target.clone(),
  };
}

/** Turns the camera around the body so that the given direction faces the viewer (full body stays visible). */
function rotateTowards(direction, animate) {
  const horizontal = new THREE.Vector3(direction.x, 0, direction.z);
  if (horizontal.lengthSq() < 1e-4) horizontal.set(0, 0, 1);
  horizontal.normalize();
  const { target, distance } = state.home;
  moveCamera(new THREE.Vector3(target.x + horizontal.x * distance, target.y, target.z + horizontal.z * distance), target, animate);
}

function stepCamera(now) {
  const t = state.cameraTween;
  if (!t) return;
  const k = Math.min(1, (now - t.start) / CAMERA_TWEEN_MS);
  const e = k < 0.5 ? 2 * k * k : 1 - Math.pow(-2 * k + 2, 2) / 2;   // ease in-out
  controls.target.lerpVectors(t.fromTarget, t.toTarget, e);
  // Orbit around the target instead of cutting through the body.
  const from = t.fromPos.clone().sub(t.fromTarget);
  const to = t.toPos.clone().sub(t.toTarget);
  const fromSph = new THREE.Spherical().setFromVector3(from);
  const toSph = new THREE.Spherical().setFromVector3(to);
  let dTheta = toSph.theta - fromSph.theta;
  if (dTheta > Math.PI) dTheta -= 2 * Math.PI;
  if (dTheta < -Math.PI) dTheta += 2 * Math.PI;
  const sph = new THREE.Spherical(
    fromSph.radius + (toSph.radius - fromSph.radius) * e,
    fromSph.phi + (toSph.phi - fromSph.phi) * e,
    fromSph.theta + dTheta * e);
  camera.position.copy(controls.target).add(new THREE.Vector3().setFromSpherical(sph));
  camera.lookAt(controls.target);
  if (k >= 1) state.cameraTween = null;
}

// ------------------------------------------------------------------ helpers

function levelRank(level) {
  return ['Easy', 'Medium', 'Hard', 'Hardcore'].indexOf(level);
}

function prepareColors(geometry) {
  const count = geometry.attributes.position.count;
  const index = geometry.index.array;
  const n = new Uint16Array(count);
  for (let i = 0; i < index.length; i++) n[index[i]]++;
  state.vertexTriangleCount = n;
  const base = new THREE.Color(COLORS.mannequin);
  const colors = new Float32Array(count * 3);
  for (let v = 0; v < count; v++) { colors[v * 3] = base.r; colors[v * 3 + 1] = base.g; colors[v * 3 + 2] = base.b; }
  state.baseColors = colors;
  geometry.setAttribute('color', new THREE.BufferAttribute(colors.slice(), 3));
}

function frameModel(body) {
  updateHome(body);
  state.userMoved = false;
  resetCamera('Front');
}

/** Camera distance that fits the whole figure (height and arm span) in the current viewport. */
function updateHome(body) {
  body.geometry.computeBoundingBox();
  const box = body.geometry.boundingBox;
  const size = box.getSize(new THREE.Vector3());
  const centre = box.getCenter(new THREE.Vector3());
  const halfFov = THREE.MathUtils.degToRad(camera.fov / 2);
  const h = container.clientHeight || 1;
  const free = Math.max(0.3, (h - state.insets.top - state.insets.bottom) / h);   // share of the height not covered
  const fitHeight = (size.y / 2) / (Math.tan(halfFov) * free);
  const fitWidth = (size.x / 2) / (Math.tan(halfFov) * camera.aspect);
  const distance = Math.max(fitHeight, fitWidth) * 1.12 + size.z / 2;
  state.home = { target: centre, distance };
  controls.minDistance = distance / MAX_ZOOM;
  controls.maxDistance = distance * 1.6;
}

/** Uniform grid over vertices: nearest vertex normal for points of the result path. */
function buildVertexGrid(geometry) {
  if (!geometry.attributes.normal) geometry.computeVertexNormals();
  const pos = geometry.attributes.position;
  const cell = 2.0;
  const map = new Map();
  for (let v = 0; v < pos.count; v++) {
    const k = `${Math.floor(pos.getX(v) / cell)},${Math.floor(pos.getY(v) / cell)},${Math.floor(pos.getZ(v) / cell)}`;
    let list = map.get(k);
    if (!list) map.set(k, list = []);
    list.push(v);
  }
  return { cell, map, pos, normal: geometry.attributes.normal };
}

function liftAboveSkin(p) {
  const { cell, map, pos, normal } = state.vertexGrid;
  const cx = Math.floor(p.x / cell), cy = Math.floor(p.y / cell), cz = Math.floor(p.z / cell);
  let best = -1, bestD = Infinity;
  for (let dx = -1; dx <= 1; dx++) for (let dy = -1; dy <= 1; dy++) for (let dz = -1; dz <= 1; dz++) {
    for (const v of map.get(`${cx + dx},${cy + dy},${cz + dz}`) || []) {
      const d = (pos.getX(v) - p.x) ** 2 + (pos.getY(v) - p.y) ** 2 + (pos.getZ(v) - p.z) ** 2;
      if (d < bestD) { bestD = d; best = v; }
    }
  }
  if (best < 0) return p;
  return p.add(new THREE.Vector3(normal.getX(best), normal.getY(best), normal.getZ(best)).multiplyScalar(PATH_LIFT_CM));
}

// ------------------------------------------------------------------ input: taps, double tap, pin dragging

const raycaster = new THREE.Raycaster();
const pointers = new Map();
let tapCandidate = null;
let lastTap = null;
let dragging = null;     // { kind: 'pin' | 'target', hit } while the user drags a pin

function hitAt(clientX, clientY) {
  if (!state.body) return null;
  const rect = renderer.domElement.getBoundingClientRect();
  const ndc = new THREE.Vector2(((clientX - rect.left) / rect.width) * 2 - 1, -((clientY - rect.top) / rect.height) * 2 + 1);
  raycaster.setFromCamera(ndc, camera);
  const hit = raycaster.intersectObject(state.body, false)[0];   // raycast on 'Body' only (spec 4.1)
  if (!hit) return null;
  const geometry = state.body.geometry;
  const index = geometry.index.array;
  const pos = geometry.attributes.position;
  const t = hit.faceIndex;
  const corner = (k) => new THREE.Vector3().fromBufferAttribute(pos, index[t * 3 + k]);
  const local = state.body.worldToLocal(hit.point.clone());
  const bary = THREE.Triangle.getBarycoord(local, corner(0), corner(1), corner(2), new THREE.Vector3());
  return { point: hit.point, surface: { triangleIndex: t, baryU: bary.y, baryV: bary.z } };
}

function screenDistanceTo(object, clientX, clientY) {
  if (!object) return Infinity;
  const rect = renderer.domElement.getBoundingClientRect();
  const p = object.position.clone().project(camera);
  const x = rect.left + (p.x + 1) / 2 * rect.width;
  const y = rect.top + (1 - p.y) / 2 * rect.height;
  return Math.hypot(x - clientX, y - clientY);
}

renderer.domElement.addEventListener('pointerdown', (e) => {
  pointers.set(e.pointerId, e);
  state.userMoved = true;
  state.cameraTween = null;
  tapCandidate = pointers.size === 1 ? { id: e.pointerId, x: e.clientX, y: e.clientY, t: performance.now() } : null;
  // Touching an existing pin drags it ("Przytrzymaj, by przesunąć").
  if (pointers.size === 1) {
    if (state.mode === 'Guess' && screenDistanceTo(state.pin?.mesh, e.clientX, e.clientY) < PIN_GRAB_PX) dragging = { kind: 'pin', hit: null };
    else if (state.allowTargetPin && screenDistanceTo(state.targetMarker, e.clientX, e.clientY) < PIN_GRAB_PX) dragging = { kind: 'target', hit: null };
    if (dragging) controls.enabled = false;
  } else if (dragging) {
    dragging = null;
    controls.enabled = true;
  }
});
renderer.domElement.addEventListener('pointermove', (e) => {
  if (dragging) {
    const hit = hitAt(e.clientX, e.clientY);
    if (!hit) return;
    dragging.hit = hit;
    if (dragging.kind === 'pin') state.pin.mesh.position.copy(hit.point);
    else state.targetMarker.position.copy(hit.point);
    return;
  }
  if (tapCandidate && e.pointerId === tapCandidate.id &&
      Math.hypot(e.clientX - tapCandidate.x, e.clientY - tapCandidate.y) > TAP_MAX_MOVE_PX) tapCandidate = null;
});
const pointerEnd = (e) => {
  pointers.delete(e.pointerId);
  if (dragging) {
    const last = dragging;
    if (last.hit) {
      if (last.kind === 'pin') setGuessPin(last.hit.point, last.hit.surface, true);
      else setTargetPin(last.hit.point, last.hit.surface);
    } else if (e.type === 'pointerup' && tapCandidate) {
      handleTap(e.clientX, e.clientY);
    }
    dragging = null;
    controls.enabled = true;
    tapCandidate = null;
    return;
  }
  if (e.type === 'pointerup' && tapCandidate && e.pointerId === tapCandidate.id &&
      performance.now() - tapCandidate.t < TAP_MAX_MS) handleTap(e.clientX, e.clientY);
  tapCandidate = null;
};
renderer.domElement.addEventListener('pointerup', pointerEnd);
renderer.domElement.addEventListener('pointercancel', pointerEnd);

function handleTap(clientX, clientY) {
  const now = performance.now();
  const isDouble = lastTap && now - lastTap.t < DOUBLE_TAP_MS && Math.hypot(clientX - lastTap.x, clientY - lastTap.y) < 30;
  lastTap = isDouble ? null : { t: now, x: clientX, y: clientY };
  const hit = hitAt(clientX, clientY);
  if (!hit) return;

  if (isDouble) {
    // Double tap zooms in on the tapped place (spec 12).
    const offset = camera.position.clone().sub(controls.target);
    const distance = THREE.MathUtils.clamp(offset.length() / 2, controls.minDistance, controls.maxDistance);
    moveCamera(hit.point.clone().add(offset.setLength(distance)), hit.point.clone(), true);
    return;
  }

  if (state.mode === 'Guess') setGuessPin(hit.point, hit.surface, true);
  else if (state.mode === 'Draw' && state.allowTargetPin) setTargetPin(hit.point, hit.surface);
  else if (state.mode === 'Editor') {
    const labels = state.zones.triangleZones[state.editorLevel];
    if (labels) send('onZoneTapped', { zoneId: state.zones.zones[labels[hit.surface.triangleIndex]].id, level: state.editorLevel });
  }
}

// ------------------------------------------------------------------ render loop, FPS meter, benchmark

let lastStats = 0;

function frame(now) {
  requestAnimationFrame(frame);
  if (state.bench) stepBenchmark(now);
  stepCamera(now);
  if (state.highlight?.pulse) applyColors(0.55 + 0.45 * (0.5 + 0.5 * Math.sin(now / 1000 * Math.PI * 1.6)));
  if (state.revealAnim) {
    const progress = Math.min(1, (now - state.revealAnim.start) / REVEAL_PATH_MS);
    for (const d of state.revealAnim.dashes) d.mesh.visible = d.start <= progress;
    if (progress >= 1) state.revealAnim = null;
  }
  if (!state.cameraTween) controls.update();
  renderer.render(scene, camera);

  state.frames.push(now);
  while (state.frames.length && now - state.frames[0] > 1000) state.frames.shift();
  if (now - lastStats > 500 && statsEl.style.display === 'block') {
    lastStats = now;
    const size = renderer.getDrawingBufferSize(new THREE.Vector2());
    const tri = renderer.info.render.triangles;
    statsEl.textContent = `FPS ${state.frames.length}\n${size.x}×${size.y} px\n${tri.toLocaleString('pl-PL')} tr.` +
      (state.benchResult ? `\ntest: śr. ${state.benchResult.fpsAvg.toFixed(0)} / min ${state.benchResult.fpsMin.toFixed(0)}` : '');
  }
}
requestAnimationFrame(frame);

/** Rotates the model 360° in BENCH_SECONDS and measures frame times (repeatable FPS reading). */
const BENCH_SECONDS = 6;
function startBenchmark() {
  if (!state.body || state.bench) return;
  resetCamera('Front');
  state.bench = { start: performance.now(), last: performance.now(), times: [] };
}
function stepBenchmark(now) {
  const b = state.bench;
  const elapsed = (now - b.start) / 1000;
  b.times.push(now - b.last);
  b.last = now;
  const angle = (elapsed / BENCH_SECONDS) * Math.PI * 2;
  const { target, distance } = state.home;
  camera.position.set(target.x + distance * Math.sin(angle), target.y, target.z + distance * Math.cos(angle));
  camera.lookAt(target);
  if (elapsed >= BENCH_SECONDS) {
    const times = b.times.slice(1).sort((x, y) => x - y);
    const avgMs = times.reduce((s, x) => s + x, 0) / times.length;
    const p95 = times[Math.floor(times.length * 0.95)];
    const result = { fpsAvg: 1000 / avgMs, fpsMin: 1000 / times[times.length - 1], frameMsP95: p95, frames: times.length,
                     seconds: BENCH_SECONDS, width: renderer.domElement.width, height: renderer.domElement.height };
    state.bench = null;
    state.benchResult = result;
    resetCamera('Front');
    send('onStats', result);
  }
}

send('onReady', {});

// Desktop development: open index.html?dev in a browser (no MAUI host) to load the model directly.
if (new URLSearchParams(location.search).has('dev')) {
  loadModel('models/body.glb').then((r) => console.log('[dev] loaded', JSON.stringify(r)));
}
