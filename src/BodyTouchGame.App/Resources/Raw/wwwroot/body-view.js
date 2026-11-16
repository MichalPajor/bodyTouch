// Body Touch Game – 3D body view (Three.js), driven from C# through the HybridWebView bridge (spec section 12).
// C# -> JS: functions on window.bodyView (called with HybridWebView.InvokeJavaScriptAsync("bodyView.<name>")).
// JS -> C#: window.HybridWebView.InvokeDotNet("Receive", [eventName, jsonPayload]).
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
const TAP_MAX_MS = 350;
const TAP_MAX_MOVE_PX = 10;

const state = {
  mode: 'Guess',
  root: null,                  // loaded glTF scene
  body: null,                  // THREE.Mesh 'Body'
  zones: null,                 // body_zones.json
  zoneById: new Map(),
  baseColors: null,            // Float32Array, mannequin colour per vertex
  vertexGrid: null,            // spatial hash for normals near path points
  home: { target: new THREE.Vector3(), distance: 300 },
  pin: null,
  revealGroup: null,
  pageStart: performance.now(),
  frames: [],                  // timestamps of recent frames
  bench: null,
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
const eyeMaterial = new THREE.MeshStandardMaterial({ color: COLORS.mannequin, roughness: 0.6, metalness: 0 });

function resize() {
  const w = container.clientWidth || 1;
  const h = container.clientHeight || 1;
  renderer.setSize(w, h, false);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
  if (state.body) {
    updateHome(state.body);
    if (!state.userMoved) resetCamera('Front');
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

  const loadMs = performance.now() - started;
  return { triangles, loadMs, pageMs: performance.now() - state.pageStart };
}

function setMode(mode) {
  state.mode = mode;
  return true;
}

function resetCamera(view = 'Front') {
  const angles = { Front: 0, Right: Math.PI / 2, Back: Math.PI, Left: -Math.PI / 2 };
  const a = angles[view] ?? 0;
  const { target, distance } = state.home;
  controls.target.copy(target);
  camera.position.set(target.x + distance * Math.sin(a), target.y, target.z + distance * Math.cos(a));
  camera.lookAt(target);
  controls.update();
  return true;
}

/** zoneId -> '#RRGGBB'. Colours blend softly across zone borders (per-vertex averaging, spec 13). */
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
  const n = new Uint16Array(count);
  for (let t = 0; t < triangleCount; t++) {
    const c = triangleColor[t] || base;
    for (let k = 0; k < 3; k++) {
      const v = index[t * 3 + k];
      sum[v * 3] += c.r; sum[v * 3 + 1] += c.g; sum[v * 3 + 2] += c.b;
      n[v]++;
    }
  }
  const attr = geometry.attributes.color;
  for (let v = 0; v < count; v++) {
    const d = n[v] || 1;
    attr.setXYZ(v, sum[v * 3] / d, sum[v * 3 + 1] / d, sum[v * 3 + 2] / d);
  }
  attr.needsUpdate = true;
  return true;
}

/** Result animation (prototype: static). path = flat [x, y, z, ...] in model coordinates (cm). */
function showReveal(payload) {
  clearReveal();
  const group = new THREE.Group();
  const points = [];
  for (let i = 0; i + 2 < payload.path.length; i += 3) {
    points.push(liftAboveSkin(new THREE.Vector3(payload.path[i], payload.path[i + 1], payload.path[i + 2])));
  }
  if (points.length >= 2) {
    const curve = new THREE.CatmullRomCurve3(points, false, 'centripetal');
    const tube = new THREE.TubeGeometry(curve, Math.max(16, points.length * 3), PATH_RADIUS_CM, 6, false);
    group.add(new THREE.Mesh(tube, new THREE.MeshBasicMaterial({ color: COLORS.path })));
    const end = new THREE.Mesh(new THREE.SphereGeometry(PIN_RADIUS_CM * 0.8, 16, 12),
      new THREE.MeshBasicMaterial({ color: COLORS.target }));
    end.position.copy(points[points.length - 1]);
    group.add(end);
  }
  scene.add(group);
  state.revealGroup = group;
  return true;
}

function clearReveal() {
  if (state.revealGroup) {
    scene.remove(state.revealGroup);
    state.revealGroup.traverse((o) => { o.geometry?.dispose(); o.material?.dispose(); });
    state.revealGroup = null;
  }
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
  camera.position.copy(controls.target).add(offset.setLength(distance));
  controls.update();
  return true;
}

window.bodyView = { loadModel, setMode, resetCamera, setZoneColors, showReveal, clearReveal, zoomBy, setPixelRatio, startBenchmark: () => { startBenchmark(); return true; } };

// ------------------------------------------------------------------ helpers

function levelRank(level) {
  return ['Easy', 'Medium', 'Hard', 'Hardcore'].indexOf(level);
}

function prepareColors(geometry) {
  const count = geometry.attributes.position.count;
  const base = new THREE.Color(COLORS.mannequin);
  const colors = new Float32Array(count * 3);
  for (let v = 0; v < count; v++) { colors[v * 3] = base.r; colors[v * 3 + 1] = base.g; colors[v * 3 + 2] = base.b; }
  geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3));
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
  const fitHeight = (size.y / 2) / Math.tan(halfFov);
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

// ------------------------------------------------------------------ tap -> pin

const raycaster = new THREE.Raycaster();
const pointers = new Map();
let tapCandidate = null;

renderer.domElement.addEventListener('pointerdown', (e) => {
  pointers.set(e.pointerId, e);
  state.userMoved = true;
  tapCandidate = pointers.size === 1 ? { id: e.pointerId, x: e.clientX, y: e.clientY, t: performance.now() } : null;
});
renderer.domElement.addEventListener('pointermove', (e) => {
  if (tapCandidate && e.pointerId === tapCandidate.id &&
      Math.hypot(e.clientX - tapCandidate.x, e.clientY - tapCandidate.y) > TAP_MAX_MOVE_PX) tapCandidate = null;
});
const pointerEnd = (e) => {
  pointers.delete(e.pointerId);
  if (e.type === 'pointerup' && tapCandidate && e.pointerId === tapCandidate.id &&
      performance.now() - tapCandidate.t < TAP_MAX_MS) handleTap(e.clientX, e.clientY);
  tapCandidate = null;
};
renderer.domElement.addEventListener('pointerup', pointerEnd);
renderer.domElement.addEventListener('pointercancel', pointerEnd);

function handleTap(clientX, clientY) {
  if (!state.body || state.mode === 'View') return;
  const rect = renderer.domElement.getBoundingClientRect();
  const ndc = new THREE.Vector2(((clientX - rect.left) / rect.width) * 2 - 1, -((clientY - rect.top) / rect.height) * 2 + 1);
  raycaster.setFromCamera(ndc, camera);
  const hit = raycaster.intersectObject(state.body, false)[0];   // raycast on 'Body' only (spec 4.1)
  if (!hit) return;

  const geometry = state.body.geometry;
  const index = geometry.index.array;
  const pos = geometry.attributes.position;
  const t = hit.faceIndex;
  const corner = (k) => new THREE.Vector3().fromBufferAttribute(pos, index[t * 3 + k]);
  const local = state.body.worldToLocal(hit.point.clone());
  const bary = THREE.Triangle.getBarycoord(local, corner(0), corner(1), corner(2), new THREE.Vector3());

  const zoneIds = {};
  for (const [level, labels] of Object.entries(state.zones.triangleZones)) zoneIds[level] = state.zones.zones[labels[t]].id;

  placePin(hit.point);
  send('onPinPlaced', { triangleIndex: t, baryU: bary.y, baryV: bary.z, zoneIds });
}

function placePin(point) {
  if (!state.pin) {
    state.pin = new THREE.Mesh(new THREE.SphereGeometry(PIN_RADIUS_CM, 20, 14),
      new THREE.MeshStandardMaterial({ color: COLORS.pin, roughness: 0.4 }));
    scene.add(state.pin);
  }
  state.pin.position.copy(point);
}

// ------------------------------------------------------------------ render loop, FPS meter, benchmark

const statsEl = document.getElementById('stats');
let lastStats = 0;

function frame(now) {
  requestAnimationFrame(frame);
  if (state.bench) stepBenchmark(now);
  controls.update();
  renderer.render(scene, camera);

  state.frames.push(now);
  while (state.frames.length && now - state.frames[0] > 1000) state.frames.shift();
  if (now - lastStats > 500) {
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
  state.bench = { start: performance.now(), last: performance.now(), times: [], startAngle: controls.getAzimuthalAngle() };
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
