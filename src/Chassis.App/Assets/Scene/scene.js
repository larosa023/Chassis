// Chassis 3D bench. Receives the build from the app (WebView2 messages) and draws it.
// Units: centimetres. Case interior: x = depth (front is +x), y = height (floor is 0), z = width (glass side is +z).
(() => {
  'use strict';
  const host = window.chrome && window.chrome.webview ? window.chrome.webview : null;
  const send = (m) => { try { if (host) host.postMessage(m); } catch (e) { /* app closing */ } };

  const canvas = document.getElementById('c');
  const tip = document.getElementById('tip');
  const empty = document.getElementById('empty');
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, preserveDrawingBuffer: true });
  renderer.setPixelRatio(Math.min(2, window.devicePixelRatio || 1));
  renderer.outputEncoding = THREE.sRGBEncoding;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 0.88;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  renderer.setClearColor(0x000000, 0);

  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(32, 1, 1, 2000);

  const hemi = new THREE.HemisphereLight(0xffffff, 0x8890a0, 0.5);
  scene.add(hemi);
  const key = new THREE.DirectionalLight(0xffffff, 1.3);
  key.position.set(60, 110, 90);
  key.castShadow = true;
  key.shadow.mapSize.set(2048, 2048);
  Object.assign(key.shadow.camera, { left: -60, right: 60, top: 80, bottom: -20, near: 10, far: 400 });
  key.shadow.bias = -0.0005;
  scene.add(key);
  const rim = new THREE.DirectionalLight(0xbfd4ff, 0.55);
  rim.position.set(-80, 50, -60);
  scene.add(rim);
  const glow = new THREE.PointLight(0xff5a1f, 0.0, 70, 2);
  scene.add(glow);

  const floor = new THREE.Mesh(new THREE.PlaneGeometry(600, 600), new THREE.ShadowMaterial({ opacity: 0.16 }));
  floor.rotation.x = -Math.PI / 2;
  floor.receiveShadow = true;
  scene.add(floor);
  // Showroom turntable under the case.
  const plinthM = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.85, metalness: 0.0 });
  const plinth = new THREE.Mesh(new THREE.CylinderGeometry(1, 1.03, 1, 96), plinthM);
  plinth.receiveShadow = true;
  scene.add(plinth);
  const plinthRing = new THREE.Mesh(new THREE.TorusGeometry(1, 0.006, 6, 128), new THREE.MeshBasicMaterial({ color: 0xff5a1f }));
  plinthRing.rotation.x = Math.PI / 2;
  scene.add(plinthRing);

  // ---------- state ----------
  let build = null;          // last build message
  let prevIds = {};          // category -> part id, to animate only what changed
  let theme = { accent: '#FF5A1F', isDark: false, bg: '#EEF0F3', surface: '#FFFFFF', text: '#14161A', muted: '#5A606B', line: '#DCDFE5' };
  const view = { exploded: false, rgb: true, autoRotate: false };
  let explode = 0;           // 0..1, eased toward view.exploded
  const root = new THREE.Group();
  scene.add(root);
  let parts = {};            // category -> { group, home: Vector3, out: Vector3, t, spin: [] }
  let rgbMats = [];          // emissive materials that cycle colour when RGB is on
  let fans = [];             // rotors to spin
  let problemBoxes = [];     // red outlines for parts with problems
  let glassMats = [];
  let caseDims = { D: 45, H: 47, W: 22 };

  // ---------- helpers ----------
  const col = (hex, fallback) => new THREE.Color(hex || fallback || '#333333');
  const mat = (hex, o = {}) => new THREE.MeshStandardMaterial({ color: col(hex), metalness: o.metal ?? 0.25, roughness: o.rough ?? 0.55, ...(o.extra || {}) });
  const isLight = (hex) => col(hex).getHSL({ h: 0, s: 0, l: 0 }).l > 0.6;

  function box(w, h, d, m, x = 0, y = 0, z = 0, parent) {
    const mesh = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), m);
    mesh.position.set(x, y, z);
    mesh.castShadow = true; mesh.receiveShadow = true;
    if (parent) parent.add(mesh);
    return mesh;
  }

  function rgbMat(seed) {
    const m = new THREE.MeshStandardMaterial({ color: 0x111111, emissive: col(theme.accent), emissiveIntensity: 1.4, roughness: 0.4 });
    m.userData.seed = seed;
    rgbMats.push(m);
    return m;
  }

  function label(text, opts = {}) {
    const c = document.createElement('canvas');
    const w = opts.w || 512, h = opts.h || 96;
    c.width = w; c.height = h;
    const g = c.getContext('2d');
    g.fillStyle = opts.bg || 'rgba(0,0,0,0)';
    g.fillRect(0, 0, w, h);
    g.fillStyle = opts.color || '#ffffff';
    g.font = `${opts.weight || 700} ${opts.size || 54}px "Segoe UI Variable Display","Segoe UI",system-ui,sans-serif`;
    g.textAlign = opts.align || 'left';
    g.textBaseline = 'middle';
    g.fillText(text, opts.align === 'center' ? w / 2 : 12, h / 2);
    const t = new THREE.CanvasTexture(c);
    t.encoding = THREE.sRGBEncoding;
    t.anisotropy = 4;
    return new THREE.MeshBasicMaterial({ map: t, transparent: true, depthWrite: false });
  }

  /** A case/GPU fan facing +z. size = diameter in cm. */
  function fan(size, frameHex, ring, square = true) {
    const g = new THREE.Group();
    const frameM = mat(frameHex || '#1b1c1f', { rough: 0.6, metal: 0.1 });
    const r = size / 2;
    const frame = new THREE.Mesh(new THREE.TorusGeometry(r - 0.3, 0.3, 8, 40), frameM);
    frame.castShadow = true;
    g.add(frame);
    if (square) {
      const bw = 0.7;
      for (const [w, h, x, y] of [[size, bw, 0, r - bw / 2], [size, bw, 0, -r + bw / 2], [bw, size, r - bw / 2, 0], [bw, size, -r + bw / 2, 0]]) box(w, h, 2.4, frameM, x, y, 0, g);
      for (const cx of [-1, 1]) for (const cy of [-1, 1]) {
        const corner = new THREE.Mesh(new THREE.BoxGeometry(r * 0.42, r * 0.42, 2.4), frameM);
        corner.position.set(cx * r * 0.79, cy * r * 0.79, 0); corner.castShadow = true; g.add(corner);
      }
    }
    if (ring) {
      const halo = new THREE.Mesh(new THREE.TorusGeometry(r - 0.45, 0.16, 8, 48), rgbMat(Math.random()));
      halo.position.z = 0.42;
      g.add(halo);
    }
    const rotor = new THREE.Group();
    const bladeM = mat(frameHex && isLight(frameHex) ? '#d9dade' : '#1e1f23', { rough: 0.5, metal: 0.05, extra: { side: THREE.DoubleSide, transparent: true, opacity: 0.92 } });
    const hub = new THREE.Mesh(new THREE.CylinderGeometry(r * 0.28, r * 0.28, 0.9, 24), mat('#202124', { rough: 0.45 }));
    hub.rotation.x = Math.PI / 2;
    rotor.add(hub);
    for (let i = 0; i < 9; i++) {
      const a = i / 9 * Math.PI * 2;
      const blade = new THREE.Mesh(new THREE.BoxGeometry(r * 0.66, r * 0.42, 0.08), bladeM);
      blade.position.set(Math.cos(a) * r * 0.58, Math.sin(a) * r * 0.58, 0);
      blade.rotation.z = a + 0.5;
      blade.rotation.x = 0.55;
      blade.castShadow = true;
      rotor.add(blade);
    }
    g.add(rotor);
    fans.push(rotor);
    return g;
  }

  // ---------- layout ----------
  function boardSize(form) {
    if (form === 'ITX') return { w: 17, h: 17 };
    if (form === 'mATX') return { w: 24.4, h: 24.4 };
    return { w: 24.4, h: 30.5 };
  }

  function layout(b) {
    const c = b.case || { depthMm: 450, heightMm: 470, widthMm: 220 };
    const D = c.depthMm / 10, H = c.heightMm / 10, W = c.widthMm / 10;
    const form = (b.board && b.board.form) || (c.boards && c.boards.length === 1 ? c.boards[0] : 'ATX');
    const bs = boardSize(form);
    const x0 = -D / 2 + 1.6;                    // rear edge of the board
    const y1 = H - 2.6;                         // top edge of the board
    const bz = -W / 2 + 2.2;                    // board surface (standoffs)
    const itx = form === 'ITX';
    return {
      D, H, W, form, bs, x0, y1, bz,
      socket: new THREE.Vector3(x0 + (itx ? 8.2 : 11.2), y1 - (itx ? 5.6 : 7.4), bz),
      ramX: x0 + bs.w - (itx ? 2.2 : 3.0), ramY: y1 - (itx ? 8.6 : 8.4),
      slotY: y1 - (itx ? 12.8 : 14.2),
      m2: new THREE.Vector3(x0 + (itx ? 9 : 11.5), y1 - (itx ? 15.6 : 18.6), bz),
    };
  }

  // ---------- part builders ----------
  function buildCase(b, L, ghost) {
    const g = new THREE.Group();
    const c = b.case;
    const { D, H, W } = L;
    const hex = ghost ? (theme.isDark ? '#3a3f4a' : '#c8ccd4') : c.color;
    const light = isLight(hex);
    const shell = mat(hex, { rough: 0.62, metal: light ? 0.05 : 0.35 });
    const t = 0.5;
    if (ghost) {
      const edges = new THREE.LineSegments(new THREE.EdgesGeometry(new THREE.BoxGeometry(D, H, W)),
        new THREE.LineDashedMaterial({ color: col(theme.muted), dashSize: 1.6, gapSize: 1.2, transparent: true, opacity: 0.7 }));
      edges.computeLineDistances();
      edges.position.y = H / 2;
      g.add(edges);
      return g;
    }
    box(D, t, W, shell, 0, t / 2, 0, g);                         // floor
    box(t, H, W, shell, -D / 2 + t / 2, H / 2, 0, g);            // rear (I/O side)
    box(D, H, t, shell, 0, H / 2, -W / 2 + t / 2, g);            // back panel (behind board)
    // Top and front: mesh panels you can see through a little.
    const meshM = mat(hex, { rough: 0.75, metal: 0.2, extra: { transparent: true, opacity: 0.38, depthWrite: false } });
    const top = box(D, t, W, meshM, 0, H - t / 2, 0, g); top.castShadow = false;
    const front = box(t, H, W, meshM, D / 2 - t / 2, H / 2, 0, g); front.castShadow = false;
    // Frame along the glass edges.
    const frameM = mat(hex, { rough: 0.5, metal: 0.4 });
    box(D, 1.2, 0.8, frameM, 0, H - 0.6, W / 2 - 0.4, g);
    box(D, 1.2, 0.8, frameM, 0, 0.6, W / 2 - 0.4, g);
    box(1.2, H, 0.8, frameM, D / 2 - 0.6, H / 2, W / 2 - 0.4, g);
    box(1.2, H, 0.8, frameM, -D / 2 + 0.6, H / 2, W / 2 - 0.4, g);
    // Rear I/O cut-out and expansion slot covers.
    const darkM = mat('#0c0d10', { rough: 0.7 });
    box(0.1, 4.6, 16.5, darkM, -D / 2 + t + 0.06, L.y1 - 3, L.bz + 8.5, g);
    for (let i = 0; i < (L.form === 'ITX' ? 2 : 7); i++) box(0.1, 1.2, 12, darkM, -D / 2 + t + 0.06, L.slotY - 1 - i * 2.03, L.bz + 7, g);
    // Glass side panel.
    const glassM = new THREE.MeshPhysicalMaterial({ color: 0xffffff, metalness: 0, roughness: 0.05, transparent: true, opacity: 0.12, depthWrite: false, clearcoat: 1 });
    glassMats.push(glassM);
    const glass = new THREE.Mesh(new THREE.BoxGeometry(D - 1.4, H - 1.4, 0.3), glassM);
    glass.position.set(0, H / 2, W / 2 - 0.15);
    glass.renderOrder = 10;
    g.add(glass);
    // Feet.
    for (const fx of [-D / 2 + 4, D / 2 - 4]) for (const fz of [-W / 2 + 3, W / 2 - 3]) box(4, 0.8, 2.5, darkM, fx, -0.4, fz, g);
    // Fans: front intakes, rear exhaust.
    const fanHex = light ? '#f1f1f3' : '#1b1c1f';
    const n = Math.min(c.fans || 0, Math.floor((H - 8) / 13));
    for (let i = 0; i < n; i++) {
      const f = fan(12, fanHex, view.rgb);
      f.rotation.y = -Math.PI / 2;
      f.position.set(D / 2 - 2, 7 + i * 13.2, 0);
      g.add(f);
    }
    const rear = fan(12, fanHex, view.rgb);
    rear.rotation.y = Math.PI / 2;
    rear.position.set(-D / 2 + 2, L.y1 - 6.5, L.bz + 8.5);
    if (L.y1 - 6.5 > 8) g.add(rear);
    g.userData.cat = 'Case';
    return g;
  }

  function buildBoard(b, L) {
    const g = new THREE.Group();
    const pcb = mat(b.board.color, { rough: 0.8, metal: 0.1 });
    pcb.color.multiplyScalar(0.55);
    const { w, h } = L.bs;
    const cx = L.x0 + w / 2, cy = L.y1 - h / 2;
    box(w, h, 0.18, pcb, cx, cy, L.bz + 0.09, g);
    const heat = mat(isLight(b.board.color) ? '#bfc3ca' : '#3a3d42', { metal: 0.7, rough: 0.35 });
    const dark = mat('#141518', { rough: 0.6 });
    const silver = mat('#c9cdd3', { metal: 0.85, rough: 0.3 });
    // VRM heatsinks around the socket, I/O shroud.
    box(2.2, 8.5, 2.2, heat, L.socket.x - 5.2, L.socket.y + 0.5, L.bz + 1.2, g);
    box(9.5, 2.0, 2.0, heat, L.socket.x + 0.2, L.socket.y + 4.8, L.bz + 1.1, g);
    box(3.2, 7.5, 3.0, heat, L.x0 + 1.7, L.y1 - 4.2, L.bz + 1.6, g);
    // Socket.
    box(5.4, 5.4, 0.35, silver, L.socket.x, L.socket.y, L.bz + 0.35, g);
    // RAM slots.
    const slots = b.board.memorySlots || 4;
    for (let i = 0; i < slots; i++) box(0.75, 13.6, 0.7, dark, L.ramX - i * 0.95, L.ramY, L.bz + 0.5, g);
    // PCIe slots.
    box(9, 0.75, 0.9, silver, L.x0 + 9, L.slotY, L.bz + 0.55, g);
    if (L.form !== 'ITX') { box(9, 0.6, 0.7, dark, L.x0 + 9, L.slotY - 8, L.bz + 0.45, g); }
    // M.2 heatsink and chipset.
    box(8, 2.4, 0.5, heat, L.m2.x, L.m2.y, L.bz + 0.4, g);
    if (L.form !== 'ITX') box(4.5, 4.5, 0.8, heat, L.x0 + w - 6, L.y1 - h + 6, L.bz + 0.5, g);
    // Accent stripe (RGB-ish) on the I/O shroud.
    const strip = box(0.25, 6.6, 0.1, rgbMat(0.1), L.x0 + 3.35, L.y1 - 4.2, L.bz + 3.1, g);
    strip.castShadow = false;
    const name = new THREE.Mesh(new THREE.PlaneGeometry(7.5, 1.4), label(b.board.chipset || '', { color: isLight(b.board.color) ? '#222' : '#e8e8ea', size: 60 }));
    name.position.set(L.x0 + w - 7, L.y1 - h + 2.0, L.bz + 0.2);
    g.add(name);
    g.userData.cat = 'Motherboard';
    return g;
  }

  function buildCpu(b, L) {
    const g = new THREE.Group();
    box(4.5, 4.5, 0.12, mat('#2f6b3a', { rough: 0.7 }), L.socket.x, L.socket.y, L.bz + 0.58, g);
    box(3.9, 3.9, 0.35, mat('#d5d8dd', { metal: 0.9, rough: 0.25 }), L.socket.x, L.socket.y, L.bz + 0.82, g);
    const name = new THREE.Mesh(new THREE.PlaneGeometry(3.4, 0.8), label(b.cpu.brand === 'Intel' ? 'intel' : 'RYZEN', { color: '#3a3d42', size: 66, align: 'center' }));
    name.position.set(L.socket.x, L.socket.y, L.bz + 1.0);
    g.add(name);
    g.userData.cat = 'Cpu';
    return g;
  }

  function buildCooler(b, L) {
    const g = new THREE.Group();
    const c = b.cooler;
    const s = L.socket;
    const base = L.bz + 1.0;
    const capM = mat(c.color, { rough: 0.45, metal: 0.5 });
    if (c.kind === 'Liquid') {
      const pump = new THREE.Mesh(new THREE.CylinderGeometry(3.3, 3.3, 2.4, 40), capM);
      pump.rotation.x = Math.PI / 2;
      pump.position.set(s.x, s.y, base + 1.2);
      pump.castShadow = true;
      g.add(pump);
      const top = new THREE.Mesh(new THREE.CylinderGeometry(2.9, 2.9, 0.1, 40), rgbMat(0.3));
      top.rotation.x = Math.PI / 2;
      top.position.set(s.x, s.y, base + 2.45);
      g.add(top);
      const len = c.radMm / 10 + 1.5;
      const ry = L.H - 2.2;
      const rx = Math.min(L.D / 2 - len / 2 - 1.5, s.x + 6);
      const rz = Math.max(L.bz + 7.5, -L.W / 2 + 7);
      box(len, 2.7, 12.4, mat('#17181b', { rough: 0.8, metal: 0.3 }), rx, ry, rz, g);
      const nf = Math.max(2, Math.round(c.radMm / 120));
      for (let i = 0; i < nf; i++) {
        const f = fan(12, isLight(c.color) ? '#f1f1f3' : '#1b1c1f', view.rgb);
        f.rotation.x = Math.PI / 2;
        f.position.set(rx - len / 2 + 0.75 + 6 + i * (c.radMm / 10 / nf), ry - 2.0, rz);
        g.add(f);
      }
      const tubeM = mat('#101114', { rough: 0.55 });
      for (const off of [-0.9, 0.9]) {
        const curve = new THREE.CatmullRomCurve3([
          new THREE.Vector3(s.x + off, s.y + 3.0, base + 1.2),
          new THREE.Vector3(s.x + off + 2, s.y + 8, base + 4),
          new THREE.Vector3(rx - len / 2 + 2 + off, ry - 1.5, rz - 3),
        ]);
        const tube = new THREE.Mesh(new THREE.TubeGeometry(curve, 24, 0.55, 10, false), tubeM);
        tube.castShadow = true;
        g.add(tube);
      }
    } else if (c.heightMm <= 90) {
      // Low profile: horizontal fins with a fan on top.
      const h = c.heightMm / 10;
      box(11.5, 11.5, h - 1.6, mat('#c9ccd1', { metal: 0.8, rough: 0.35 }), s.x + 1, s.y, base + (h - 1.6) / 2, g);
      const f = fan(12, c.color, false);
      f.position.set(s.x + 1, s.y, base + h - 0.7);
      g.add(f);
    } else {
      // Tower: fin stack (instanced) with a fan on the front.
      const h = c.heightMm / 10;
      const twin = c.capacityW >= 250;
      const finM = mat('#cfd2d7', { metal: 0.85, rough: 0.32 });
      const fins = 38;
      const stack = twin ? [s.x - 3.6, s.x + 3.6] : [s.x];
      const finGeo = new THREE.BoxGeometry(0.07, 12.5, h - 3.2);
      for (const sx of stack) {
        const inst = new THREE.InstancedMesh(finGeo, finM, fins);
        const m4 = new THREE.Matrix4();
        for (let i = 0; i < fins; i++) { m4.makeTranslation(sx - 2.6 + i * (5.2 / fins), s.y, base + 2.4 + (h - 3.2) / 2); inst.setMatrixAt(i, m4); }
        inst.castShadow = true;
        g.add(inst);
        box(5.4, 12.6, 0.6, capM, sx, s.y, base + h - 0.5, g);
      }
      const copper = mat('#b87333', { metal: 0.9, rough: 0.3 });
      for (let i = 0; i < 4; i++) {
        const p = new THREE.Mesh(new THREE.CylinderGeometry(0.3, 0.3, h - 1.2, 10), copper);
        p.rotation.x = Math.PI / 2;
        p.position.set(s.x - 1.8 + i * 1.2, s.y, base + (h - 1.2) / 2 + 0.4);
        g.add(p);
      }
      box(4.5, 4.5, 1.4, copper, s.x, s.y, base + 0.7, g);
      const f = fan(12, c.color, view.rgb && !isLight(c.color) ? false : false);
      f.rotation.y = Math.PI / 2;
      f.position.set((twin ? s.x + 7.2 : s.x + 3.6), s.y, base + 2.2 + 6);
      g.add(f);
    }
    g.userData.cat = 'Cooler';
    return g;
  }

  function buildMemory(b, L) {
    const g = new THREE.Group();
    const m = b.memory;
    const spreader = mat(m.color, { metal: 0.55, rough: 0.35 });
    const slots = (b.board && b.board.memorySlots) || 4;
    // Fill the slots the manual recommends first (A2 and B2).
    const order = slots >= 4 ? [1, 3, 0, 2] : [0, 1];
    for (let k = 0; k < Math.min(m.modules, slots); k++) {
      const i = order[k];
      const x = L.ramX - i * 0.95;
      box(0.62, 13.3, 3.4, spreader, x, L.ramY, L.bz + 0.85 + 1.7, g);
      if (m.rgb) { const bar = box(0.5, 13.0, 0.6, rgbMat(0.2 + k * 0.08), x, L.ramY, L.bz + 0.85 + 3.7, g); bar.castShadow = false; }
    }
    g.userData.cat = 'Memory';
    return g;
  }

  function buildGpu(b, L) {
    const g = new THREE.Group();
    const c = b.gpu;
    const len = c.lengthMm / 10;
    const thick = Math.max(3.6, c.slots * 2.03);
    const depth = 13.2;
    const x = L.x0 + len / 2 - 0.3;
    const y = L.slotY - 0.5 - thick / 2;
    const z = L.bz + 0.8 + depth / 2;
    const shroud = mat(c.color, { metal: 0.6, rough: 0.38 });
    box(len, thick, depth, shroud, x, y, z, g);
    box(len - 0.4, 0.25, depth - 0.4, mat('#1d1e21', { metal: 0.7, rough: 0.4 }), x, y + thick / 2 + 0.12, z, g);  // backplate
    box(0.25, thick + 1.5, depth + 0.6, mat('#a9adb4', { metal: 0.9, rough: 0.3 }), L.x0 - 0.2, y + 0.6, z, g);   // bracket
    const nf = len < 24 ? 2 : 3;
    const size = Math.min(9.6, (len - 2) / nf - 0.4);
    for (let i = 0; i < nf; i++) {
      const f = fan(size, '#151618', false, false);
      f.rotation.x = Math.PI / 2;
      f.position.set(x - len / 2 + 1 + size / 2 + i * ((len - 2 - size) / Math.max(1, nf - 1)), y - thick / 2 - 0.25, z);
      g.add(f);
    }
    const strip = box(len * 0.55, 0.35, 0.12, rgbMat(0.5), x + len * 0.12, y - thick / 2 + 0.9, z + depth / 2 + 0.06, g);
    strip.castShadow = false;
    const brand = new THREE.Mesh(new THREE.PlaneGeometry(Math.min(14, len * 0.42), Math.min(2.2, thick * 0.55)),
      label((c.brand || '').toUpperCase(), { color: isLight(c.color) ? '#222' : '#f1f1f3', size: 64 }));
    brand.position.set(x - len * 0.18, y + 0.3, z + depth / 2 + 0.07);
    g.add(brand);
    g.userData.cat = 'Gpu';
    return g;
  }

  function buildStorage(b, L) {
    const g = new THREE.Group();
    const s = b.storage;
    if (s.iface === 'SATA') {
      box(10, 0.7, 7, mat('#2b2b2b', { metal: 0.5, rough: 0.4 }), L.D / 2 - 9, 1.0, L.W / 2 - 6, g);
    } else {
      box(8, 2.2, 0.15, mat('#1f5132', { rough: 0.6 }), L.m2.x, L.m2.y, L.bz + 0.7, g);
      box(7, 2.0, 0.25, mat(s.color, { metal: 0.6, rough: 0.35 }), L.m2.x, L.m2.y, L.bz + 0.9, g);
    }
    g.userData.cat = 'Storage';
    return g;
  }

  function buildPsu(b, L) {
    const g = new THREE.Group();
    const p = b.psu;
    const sfx = p.form === 'SFX';
    const w = sfx ? 12.5 : 15, h = sfx ? 6.35 : 8.6, d = sfx ? 10 : 14;
    const x = -L.D / 2 + 0.6 + w / 2, y = 0.5 + h / 2, z = -L.W / 2 + 0.6 + d / 2;
    box(w, h, d, mat(p.color, { metal: 0.45, rough: 0.5 }), x, y, z, g);
    const f = fan(Math.min(12, d - 1.5), '#111214', false, false);
    f.rotation.x = Math.PI / 2;
    f.position.set(x, y - h / 2 - 0.2, z);
    g.add(f);
    const tag = new THREE.Mesh(new THREE.PlaneGeometry(Math.min(9, w * 0.6), 2.0), label(`${p.watts} W`, { color: '#e8e8ea', size: 62 }));
    tag.position.set(x - 1, y, z + d / 2 + 0.03);
    g.add(tag);
    g.userData.cat = 'Psu';
    return g;
  }

  // Exploded view: where each part slides to (cm).
  const OUT = {
    Motherboard: [0, 0, 4], Cpu: [0, 2, 14], Cooler: [0, 10, 30], Memory: [6, 4, 20], Gpu: [8, -8, 40], Storage: [0, -6, 12], Psu: [0, -4, 34], Case: [0, 0, 0],
  };
  const BUILDERS = { Case: buildCase, Motherboard: buildBoard, Cpu: buildCpu, Cooler: buildCooler, Memory: buildMemory, Gpu: buildGpu, Storage: buildStorage, Psu: buildPsu };
  const SPEC = { Case: 'case', Motherboard: 'board', Cpu: 'cpu', Cooler: 'cooler', Memory: 'memory', Gpu: 'gpu', Storage: 'storage', Psu: 'psu' };

  function disposeTree(o) {
    o.traverse((n) => {
      if (n.geometry) n.geometry.dispose();
      if (n.material) (Array.isArray(n.material) ? n.material : [n.material]).forEach((m) => { if (m.map) m.map.dispose(); m.dispose(); });
    });
  }

  function rebuild() {
    for (const k of Object.keys(parts)) { root.remove(parts[k].group); disposeTree(parts[k].group); }
    for (const p of problemBoxes) { root.remove(p); disposeTree(p); }
    parts = {}; rgbMats = []; fans = []; problemBoxes = []; glassMats = [];
    if (!build) return;
    const L = layout(build);
    caseDims = { D: L.D, H: L.H, W: L.W };
    empty.style.display = build.case ? 'none' : 'flex';
    const ids = {};
    for (const cat of Object.keys(BUILDERS)) {
      const spec = build[SPEC[cat]];
      ids[cat] = spec ? spec.id : null;
      if (!spec && cat !== 'Case') continue;
      const g = cat === 'Case' && !spec ? buildCase(build, L, true) : BUILDERS[cat](build, L);
      g.userData.cat = cat;
      g.userData.name = spec ? spec.name : '';
      g.userData.brand = spec ? spec.brand : '';
      g.userData.facts = spec ? spec.facts || '' : '';
      const changed = prevIds[cat] !== ids[cat] && !!spec;
      const o = OUT[cat];
      parts[cat] = { group: g, out: new THREE.Vector3(o[0], o[1], o[2]), t: changed && Object.keys(prevIds).length ? 0 : 1 };
      root.add(g);
    }
    prevIds = ids;
    // Problem outlines.
    for (const cat of build.problems || []) {
      const p = parts[cat];
      if (!p || cat === 'Case') continue;
      const bb = new THREE.Box3().setFromObject(p.group);
      if (bb.isEmpty()) continue;
      const size = bb.getSize(new THREE.Vector3()).addScalar(1.2), center = bb.getCenter(new THREE.Vector3());
      const edges = new THREE.LineSegments(new THREE.EdgesGeometry(new THREE.BoxGeometry(size.x, size.y, size.z)),
        new THREE.LineBasicMaterial({ color: 0xff3b30, transparent: true, opacity: 0.9, depthTest: false }));
      const fill = new THREE.Mesh(new THREE.BoxGeometry(size.x, size.y, size.z),
        new THREE.MeshBasicMaterial({ color: 0xff3b30, transparent: true, opacity: 0.14, depthWrite: false }));
      edges.add(fill);
      edges.position.copy(center);
      edges.renderOrder = 20;
      edges.userData.cat = cat;
      problemBoxes.push(edges);
      root.add(edges);
    }
    glow.position.set(0, L.H * 0.55, 0);
    const pr = Math.hypot(L.D, L.W) / 2 + 6;
    plinth.scale.set(pr, 1.6, pr); plinth.position.y = -1.6 / 2 - 0.8;
    plinthRing.scale.set(pr * 1.03, pr * 1.03, 1); plinthRing.position.y = -0.8;
    floor.position.y = -2.4;
    if (!orbit.userMoved) frameCase(false);
  }

  // ---------- camera ----------
  const orbit = { az: 0.62, el: 0.26, r: 160, target: new THREE.Vector3(0, 23, 0), goal: null, userMoved: false };

  function frameCase(animate) {
    const { D, H, W } = caseDims;
    const r = Math.max(D, H, W * 1.4) * 2.75;
    const g = { az: 0.62, el: 0.26, r, target: new THREE.Vector3(0, H / 2, 0) };
    if (animate) orbit.goal = g; else Object.assign(orbit, g, { goal: null });
  }

  function focus(cat) {
    const p = parts[cat];
    if (!p) return;
    const bb = new THREE.Box3().setFromObject(p.group);
    const c = bb.getCenter(new THREE.Vector3());
    const s = bb.getSize(new THREE.Vector3()).length();
    orbit.goal = { az: 0.35, el: 0.2, r: Math.max(55, s * 3.2), target: c };
    orbit.userMoved = true;
  }

  function placeCamera() {
    if (orbit.goal) {
      const g = orbit.goal, k = 0.12;
      orbit.az += (g.az - orbit.az) * k; orbit.el += (g.el - orbit.el) * k; orbit.r += (g.r - orbit.r) * k;
      orbit.target.lerp(g.target, k);
      if (Math.abs(g.r - orbit.r) < 0.2 && orbit.target.distanceTo(g.target) < 0.05) orbit.goal = null;
    }
    const { az, el } = orbit;
    const r = orbit.r * (1 + 0.3 * explode);
    const target = orbit.target.clone().add(new THREE.Vector3(0, -2 * explode, 12 * explode));
    camera.position.set(target.x + r * Math.sin(az) * Math.cos(el), target.y + r * Math.sin(el), target.z + r * Math.cos(az) * Math.cos(el));
    camera.lookAt(target);
  }

  let drag = null;
  canvas.addEventListener('pointerdown', (e) => {
    drag = { x: e.clientX, y: e.clientY, moved: 0, button: e.button };
    canvas.setPointerCapture(e.pointerId);
    canvas.classList.add('dragging');
  });
  canvas.addEventListener('pointermove', (e) => {
    if (!drag) { hover(e); return; }
    const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
    drag.x = e.clientX; drag.y = e.clientY; drag.moved += Math.abs(dx) + Math.abs(dy);
    orbit.goal = null; orbit.userMoved = true;
    if (drag.button === 2 || e.shiftKey) {
      const right = new THREE.Vector3().subVectors(camera.position, orbit.target).cross(camera.up).normalize();
      orbit.target.addScaledVector(right, dx * orbit.r * 0.0012);
      orbit.target.y += dy * orbit.r * 0.0012;
    } else {
      orbit.az -= dx * 0.008;
      orbit.el = Math.max(-0.15, Math.min(1.35, orbit.el + dy * 0.006));
    }
  });
  canvas.addEventListener('pointerup', (e) => {
    canvas.classList.remove('dragging');
    if (drag && drag.moved < 5 && drag.button === 0) {
      const hit = pick(e);
      if (hit) send({ type: 'pick', category: hit });
    }
    drag = null;
  });
  canvas.addEventListener('contextmenu', (e) => e.preventDefault());
  canvas.addEventListener('wheel', (e) => {
    e.preventDefault();
    orbit.goal = null; orbit.userMoved = true;
    orbit.r = Math.max(30, Math.min(420, orbit.r * Math.pow(1.0015, e.deltaY)));
  }, { passive: false });
  canvas.addEventListener('dblclick', () => { orbit.userMoved = false; frameCase(true); });

  const ray = new THREE.Raycaster();
  function pick(e) {
    const r = canvas.getBoundingClientRect();
    ray.setFromCamera(new THREE.Vector2(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1), camera);
    const groups = Object.keys(parts).filter((k) => k !== 'Case').map((k) => parts[k].group);
    let hits = ray.intersectObjects(groups, true);
    if (!hits.length && parts.Case) hits = ray.intersectObject(parts.Case.group, true).filter((h) => h.object.material && !h.object.material.transparent);
    for (const h of hits) {
      let o = h.object;
      while (o && !o.userData.cat) o = o.parent;
      if (o) return o.userData.cat;
    }
    return null;
  }

  const NAMES = { Case: 'Case', Motherboard: 'Motherboard', Cpu: 'Processor', Cooler: 'CPU cooler', Memory: 'Memory', Gpu: 'Graphics card', Storage: 'Storage', Psu: 'Power supply' };
  let hovered = null;
  function hover(e) {
    const cat = pick(e);
    hovered = cat;
    canvas.classList.toggle('hovering', !!cat);
    if (!cat || !parts[cat]) { tip.classList.remove('on'); return; }
    const u = parts[cat].group.userData;
    tip.querySelector('b').textContent = NAMES[cat] + ((build.problems || []).includes(cat) ? ' · needs attention' : '');
    tip.querySelector('span').textContent = u.name ? `${u.brand} ${u.name}` : 'Empty: click to choose one';
    tip.querySelector('i').textContent = u.facts || 'Click to swap';
    const x = Math.min(window.innerWidth - 300, e.clientX + 16), y = Math.min(window.innerHeight - 90, e.clientY + 16);
    tip.style.left = x + 'px'; tip.style.top = y + 'px';
    tip.classList.add('on');
  }
  canvas.addEventListener('pointerleave', () => { tip.classList.remove('on'); hovered = null; });

  // ---------- messages ----------
  function applyTheme(t) {
    theme = Object.assign(theme, t);
    const s = document.documentElement.style;
    s.setProperty('--bg', theme.bg); s.setProperty('--surface', theme.surface); s.setProperty('--text', theme.text);
    s.setProperty('--muted', theme.muted); s.setProperty('--accent', theme.accent); s.setProperty('--line', theme.line);
    floor.material.opacity = theme.isDark ? 0.42 : 0.16;
    hemi.intensity = theme.isDark ? 0.45 : 0.5;
    hemi.groundColor = col(theme.isDark ? '#2a2d36' : '#8890a0');
    glow.color = col(theme.accent);
    plinthM.color = theme.isDark ? col(theme.bg).multiplyScalar(0.5) : col(theme.surface).offsetHSL(0, 0, -0.03);
    plinthRing.material.color = col(theme.accent);
  }

  function receive(m) {
    if (!m || typeof m !== 'object') return;
    if (m.type === 'theme') { applyTheme(m); rebuild(); }
    else if (m.type === 'build') { build = m; rebuild(); }
    else if (m.type === 'view') {
      if ('exploded' in m) view.exploded = !!m.exploded;
      if ('autoRotate' in m) view.autoRotate = !!m.autoRotate;
      if ('rgb' in m && view.rgb !== !!m.rgb) { view.rgb = !!m.rgb; rebuild(); }
      if (m.reset) { orbit.userMoved = false; frameCase(true); }
      if (m.focus) focus(m.focus);
    }
  }
  window.chassis = { receive, snapshot: () => canvas.toDataURL('image/png') };
  if (host) host.addEventListener('message', (e) => receive(e.data));

  // ---------- loop ----------
  function resize() {
    const w = window.innerWidth, h = window.innerHeight;
    renderer.setSize(w, h, false);
    camera.aspect = w / Math.max(1, h);
    camera.updateProjectionMatrix();
  }
  window.addEventListener('resize', resize);
  resize();

  const ease = (t) => 1 - Math.pow(1 - t, 3);
  const clock = new THREE.Clock();
  function frame() {
    const dt = Math.min(0.05, clock.getDelta());
    const time = clock.elapsedTime;
    explode += ((view.exploded ? 1 : 0) - explode) * Math.min(1, dt * 6);
    for (const cat of Object.keys(parts)) {
      const p = parts[cat];
      if (p.t < 1) p.t = Math.min(1, p.t + dt * 1.8);
      const drop = 1 - ease(p.t);
      p.group.position.set(p.out.x * explode, p.out.y * explode + drop * 26, p.out.z * explode + drop * 18);
    }
    for (const pb of problemBoxes) {
      const p = parts[pb.userData.cat];
      if (p) new THREE.Box3().setFromObject(p.group).getCenter(pb.position);
      pb.material.opacity = 0.55 + 0.45 * Math.sin(time * 5);
      if (pb.children[0]) pb.children[0].material.opacity = 0.1 + 0.08 * Math.sin(time * 5);
    }
    for (const m of glassMats) m.opacity = 0.12 * (1 - explode) + 0.02;
    const accent = col(theme.accent);
    const hsl = accent.getHSL({ h: 0, s: 0, l: 0 });
    for (const m of rgbMats) {
      if (view.rgb) m.emissive.setHSL((hsl.h + time * 0.08 + m.userData.seed) % 1, 0.9, 0.55);
      else m.emissive.copy(accent).multiplyScalar(0.35);
    }
    glow.intensity = view.rgb && build && build.case ? 0.45 : 0;
    for (const f of fans) f.rotation.z += dt * 9;
    if (view.autoRotate && !drag) { orbit.az += dt * 0.25; }
    placeCamera();
    renderer.render(scene, camera);
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
  send({ type: 'ready' });
})();
