namespace Workbench.TestExecutor;

/// <summary>
/// Read-only page script. It walks scenes captured by the devtools hook, then window globals and <c>canvas.__r3f</c>.
/// Screen positions are returned as NDC plus the canvas box so C# can apply devicePixelRatio and window zoom.
/// </summary>
internal static class ScenePage
{
    public const string Source = """
        function devtoolsBag() {
          const target = window.__THREE_DEVTOOLS__;
          return target && target.wb ? target.wb : null;
        }
        function devtoolsScenes() {
          const bag = devtoolsBag();
          if (!bag || !bag.scenes) return [];
          return bag.scenes.filter((scene) => scene && scene.isScene === true);
        }
        function devtoolsRenderers() {
          const bag = devtoolsBag();
          if (!bag || !bag.renderers) return [];
          return bag.renderers.filter((renderer) => renderer && renderer.isWebGLRenderer === true);
        }
        function readR3f(canvas) {
          const root = canvas && canvas.__r3f;
          if (!root) return null;
          if (typeof root.getState === 'function') return root.getState();
          if (root.store && typeof root.store.getState === 'function') return root.store.getState();
          if (root.root && typeof root.root.getState === 'function') return root.root.getState();
          if (root.root && root.root.store && typeof root.root.store.getState === 'function') return root.root.store.getState();
          if (root.scene && root.scene.isScene) return root;
          return null;
        }
        function r3fScenes() {
          const scenes = [];
          const canvases = document.querySelectorAll('canvas');
          for (let index = 0; index < canvases.length; index += 1) {
            const state = readR3f(canvases[index]);
            if (state && state.scene && state.scene.isScene) scenes.push(state.scene);
          }
          return scenes;
        }
        function windowScenes() {
          const found = [];
          try {
            if (window.scene && window.scene.isScene) found.push(window.scene);
            const keys = Object.getOwnPropertyNames(window);
            const limit = Math.min(keys.length, 250);
            for (let index = 0; index < limit && found.length < 4; index += 1) {
              const key = keys[index];
              if (key === 'window' || key === 'document' || key === 'self' || key === 'top') continue;
              let value = null;
              try { value = window[key]; } catch (error) { value = null; }
              if (value && value.isScene === true && found.indexOf(value) < 0) found.push(value);
            }
          } catch (error) { return found; }
          return found;
        }
        function pickMount() {
          const devtools = devtoolsScenes();
          if (devtools.length) return { mount: 'devtools', scenes: devtools };
          const fiber = r3fScenes();
          if (fiber.length) return { mount: 'r3f', scenes: fiber };
          const globals = windowScenes();
          if (globals.length) return { mount: 'window', scenes: globals };
          return { mount: 'none', scenes: [] };
        }
        function signalReport() {
          const bag = devtoolsBag();
          const webgl = bag && bag.webgl ? bag.webgl : null;
          const current = webgl && webgl.current ? webgl.current : { drawCalls: 0, vertices: 0, bufferBytes: 0 };
          let windowScene = false;
          let windowCamera = false;
          let windowRenderer = false;
          try {
            windowScene = !!(window.scene && window.scene.isScene);
            windowCamera = !!(window.camera && window.camera.isCamera);
            windowRenderer = !!(window.renderer && window.renderer.isWebGLRenderer);
          } catch (error) { windowScene = false; }
          const diagnostics = [];
          if (!window.__THREE_DEVTOOLS__) diagnostics.push('devtools hook is missing; inject window.__THREE_DEVTOOLS__ before the app scripts');
          else if (!devtoolsScenes().length) diagnostics.push('devtools hook is installed but no scene was observed; reload so three.js constructors run after the hook');
          if (!windowScene && !windowCamera && !windowRenderer) diagnostics.push('no scene, camera, or renderer on window');
          if (!r3fScenes().length) diagnostics.push('no canvas.__r3f root');
          if (!webgl || !webgl.contexts) diagnostics.push('no wrapped WebGL context');
          return {
            signals: {
              devtoolsScenes: devtoolsScenes().length,
              devtoolsRenderers: devtoolsRenderers().length,
              windowScene, windowCamera, windowRenderer,
              r3fCanvases: r3fScenes().length,
              webglContexts: webgl ? webgl.contexts || 0 : 0,
              drawCalls: current.drawCalls || 0,
              vertices: current.vertices || 0,
              bufferBytes: current.bufferBytes || 0,
              frames: webgl ? webgl.frames || 0 : 0,
              globals: {
                THREE: typeof window.THREE !== 'undefined',
                scene: windowScene,
                workbenchHook: typeof window.__workbenchScene !== 'undefined'
              }
            },
            diagnostics
          };
        }
        function identity() { return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]; }
        function localMatrix(object, out) {
          const x = object.quaternion.x, y = object.quaternion.y, z = object.quaternion.z, w = object.quaternion.w;
          const x2 = x + x, y2 = y + y, z2 = z + z;
          const xx = x * x2, xy = x * y2, xz = x * z2, yy = y * y2, yz = y * z2, zz = z * z2;
          const wx = w * x2, wy = w * y2, wz = w * z2;
          const sx = object.scale.x, sy = object.scale.y, sz = object.scale.z;
          out[0] = (1 - (yy + zz)) * sx; out[1] = (xy + wz) * sx; out[2] = (xz - wy) * sx; out[3] = 0;
          out[4] = (xy - wz) * sy; out[5] = (1 - (xx + zz)) * sy; out[6] = (yz + wx) * sy; out[7] = 0;
          out[8] = (xz + wy) * sz; out[9] = (yz - wx) * sz; out[10] = (1 - (xx + yy)) * sz; out[11] = 0;
          out[12] = object.position.x; out[13] = object.position.y; out[14] = object.position.z; out[15] = 1;
        }
        function multiply(a, b) {
          const out = new Array(16);
          for (let column = 0; column < 4; column += 1) {
            for (let row = 0; row < 4; row += 1) {
              out[column * 4 + row] = a[row] * b[column * 4] + a[4 + row] * b[column * 4 + 1] + a[8 + row] * b[column * 4 + 2] + a[12 + row] * b[column * 4 + 3];
            }
          }
          return out;
        }
        function worldMatrix(object) {
          const chain = [];
          let current = object;
          while (current) { chain.push(current); current = current.parent; }
          let world = identity();
          for (let index = chain.length - 1; index >= 0; index -= 1) {
            const local = new Array(16);
            localMatrix(chain[index], local);
            world = multiply(world, local);
          }
          return world;
        }
        function transformPoint(matrix, x, y, z) {
          return {
            x: matrix[0] * x + matrix[4] * y + matrix[8] * z + matrix[12],
            y: matrix[1] * x + matrix[5] * y + matrix[9] * z + matrix[13],
            z: matrix[2] * x + matrix[6] * y + matrix[10] * z + matrix[14]
          };
        }
        function localBox(object) {
          const geometry = object.geometry;
          const position = geometry && geometry.attributes && geometry.attributes.position;
          if (!position || !position.count) return null;
          let minX = Infinity, minY = Infinity, minZ = Infinity, maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;
          const count = Math.min(position.count, 200000);
          for (let index = 0; index < count; index += 1) {
            const x = position.getX(index), y = position.getY(index), z = position.getZ(index);
            if (x < minX) minX = x; if (y < minY) minY = y; if (z < minZ) minZ = z;
            if (x > maxX) maxX = x; if (y > maxY) maxY = y; if (z > maxZ) maxZ = z;
          }
          return { min: { x: minX, y: minY, z: minZ }, max: { x: maxX, y: maxY, z: maxZ } };
        }
        function worldBox(object, local) {
          if (!local) {
            const point = { x: object.position.x, y: object.position.y, z: object.position.z };
            return { min: point, max: point };
          }
          const matrix = worldMatrix(object);
          const xs = [local.min.x, local.max.x], ys = [local.min.y, local.max.y], zs = [local.min.z, local.max.z];
          let minX = Infinity, minY = Infinity, minZ = Infinity, maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;
          for (let ix = 0; ix < 2; ix += 1) {
            for (let iy = 0; iy < 2; iy += 1) {
              for (let iz = 0; iz < 2; iz += 1) {
                const point = transformPoint(matrix, xs[ix], ys[iy], zs[iz]);
                if (point.x < minX) minX = point.x; if (point.y < minY) minY = point.y; if (point.z < minZ) minZ = point.z;
                if (point.x > maxX) maxX = point.x; if (point.y > maxY) maxY = point.y; if (point.z > maxZ) maxZ = point.z;
              }
            }
          }
          return { min: { x: minX, y: minY, z: minZ }, max: { x: maxX, y: maxY, z: maxZ } };
        }
        function hashPositions(position) {
          let hash = 2166136261;
          const count = Math.min(position.count, 4096);
          for (let index = 0; index < count; index += 1) {
            const values = [position.getX(index), position.getY(index), position.getZ(index)];
            for (let axis = 0; axis < 3; axis += 1) {
              hash ^= Math.round(values[axis] * 10000) | 0;
              hash = Math.imul(hash, 16777619);
            }
          }
          return (hash >>> 0).toString(16);
        }
        function volumeOf(geometry) {
          const position = geometry.attributes && geometry.attributes.position;
          if (!position) return 0;
          const index = geometry.index;
          let sum = 0;
          const triangle = (ia, ib, ic) => {
            const ax = position.getX(ia), ay = position.getY(ia), az = position.getZ(ia);
            const bx = position.getX(ib), by = position.getY(ib), bz = position.getZ(ib);
            const cx = position.getX(ic), cy = position.getY(ic), cz = position.getZ(ic);
            sum += ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx);
          };
          const limit = index ? Math.min(index.count, 60000) : Math.min(position.count, 60000);
          if (index) {
            for (let cursor = 0; cursor + 2 < limit; cursor += 3) triangle(index.getX(cursor), index.getX(cursor + 1), index.getX(cursor + 2));
          } else {
            for (let cursor = 0; cursor + 2 < limit; cursor += 3) triangle(cursor, cursor + 1, cursor + 2);
          }
          return Math.abs(sum) / 6;
        }
        function firstVertices(position) {
          const vertices = [];
          if (!position) return vertices;
          const count = Math.min(position.count, 8);
          for (let index = 0; index < count; index += 1) vertices.push({ x: position.getX(index), y: position.getY(index), z: position.getZ(index) });
          return vertices;
        }
        function describe(object, path) {
          const local = object.isMesh ? localBox(object) : null;
          const world = worldBox(object, local);
          const geometry = object.geometry;
          const position = geometry && geometry.attributes && geometry.attributes.position;
          const index = geometry && geometry.index;
          const node = {
            name: object.name || '',
            uuid: object.uuid || '',
            type: object.type || '',
            path,
            visible: object.visible ? 1 : 0,
            position: { x: object.position.x, y: object.position.y, z: object.position.z },
            rotation: { x: object.rotation.x, y: object.rotation.y, z: object.rotation.z },
            min: world.min,
            max: world.max
          };
          if (object.isMesh) {
            node.vertexCount = position ? position.count : 0;
            node.faceCount = index ? Math.floor(index.count / 3) : (position ? Math.floor(position.count / 3) : 0);
            node.volume = geometry ? volumeOf(geometry) : 0;
            node.hash = position ? hashPositions(position) : '';
            node.vertices = firstVertices(position);
          }
          return node;
        }
        function walk(scene) {
          const nodes = [];
          const visit = (object, path) => {
            if (nodes.length >= 80) return;
            const label = object.name || object.type || object.uuid || 'object';
            const here = path ? path + '/' + label : label;
            nodes.push(describe(object, here));
            const children = object.children || [];
            for (let index = 0; index < children.length; index += 1) visit(children[index], here);
          };
          visit(scene, '');
          return nodes;
        }
        function findCamera(scene) {
          let camera = null;
          scene.traverse((object) => { if (!camera && object.isCamera) camera = object; });
          if (!camera && window.camera && window.camera.isCamera) camera = window.camera;
          const canvases = document.querySelectorAll('canvas');
          for (let index = 0; !camera && index < canvases.length; index += 1) {
            const state = readR3f(canvases[index]);
            if (state && state.camera && state.camera.isCamera) camera = state.camera;
          }
          return camera;
        }
        function cameraView(scene) {
          const camera = findCamera(scene);
          if (!camera) return null;
          return {
            x: camera.position.x, y: camera.position.y, z: camera.position.z,
            fov: camera.fov || 0,
            azimuth: Math.atan2(camera.position.x, camera.position.z)
          };
        }
        function findObject(scene, spec) {
          const uuid = spec.uuid || '';
          const name = spec.objectName || '';
          const path = spec.path || '';
          if (path) {
            const parts = path.split('/').filter((part) => part.length > 0);
            let node = scene;
            let start = 0;
            if (parts.length && (parts[0] === scene.name || parts[0] === scene.type || parts[0] === scene.uuid)) start = 1;
            for (let index = start; index < parts.length; index += 1) {
              const part = parts[index];
              const children = node.children || [];
              let next = null;
              for (let child = 0; child < children.length; child += 1) {
                if (children[child].name === part || children[child].uuid === part) { next = children[child]; break; }
              }
              if (!next) return null;
              node = next;
            }
            return node;
          }
          let found = null;
          scene.traverse((object) => {
            if (found) return;
            if (uuid && object.uuid === uuid) found = object;
            else if (!uuid && name && object.name === name) found = object;
          });
          return found;
        }
        function numbers3(text) {
          const parts = text.split(',');
          if (parts.length !== 3) return null;
          const x = Number(parts[0]), y = Number(parts[1]), z = Number(parts[2]);
          if (!Number.isFinite(x) || !Number.isFinite(y) || !Number.isFinite(z)) return null;
          return { x, y, z };
        }
        function explicitWorld(input) {
          if (typeof input !== 'string' || input.indexOf('world:') !== 0) return null;
          return numbers3(input.slice(6));
        }
        function worldPath(input) {
          if (typeof input !== 'string' || input.indexOf('world-path:') !== 0) return null;
          const points = input.slice(11).split(';').map((part) => numbers3(part.trim())).filter((point) => point);
          return points.length ? points : null;
        }
        function centerOf(object) {
          const local = localBox(object);
          if (!local) return { x: object.position.x, y: object.position.y, z: object.position.z };
          return transformPoint(worldMatrix(object), (local.min.x + local.max.x) / 2, (local.min.y + local.max.y) / 2, (local.min.z + local.max.z) / 2);
        }
        function localPoint(object, input) {
          if (!object || typeof input !== 'string' || input.indexOf('local:') !== 0) return null;
          const local = numbers3(input.slice(6));
          if (!local) return null;
          return transformPoint(worldMatrix(object), local.x, local.y, local.z);
        }
        function rayThrough(camera, ndcX, ndcY) {
          const Vector3 = camera.position.constructor;
          const near = new Vector3(ndcX, ndcY, -1).unproject(camera);
          const far = new Vector3(ndcX, ndcY, 1).unproject(camera);
          let dx = far.x - near.x, dy = far.y - near.y, dz = far.z - near.z;
          const length = Math.hypot(dx, dy, dz) || 1;
          return { x: near.x, y: near.y, z: near.z, dx: dx / length, dy: dy / length, dz: dz / length, Vector3 };
        }
        function hitTriangle(ox, oy, oz, dx, dy, dz, ax, ay, az, bx, by, bz, cx, cy, cz) {
          const e1x = bx - ax, e1y = by - ay, e1z = bz - az;
          const e2x = cx - ax, e2y = cy - ay, e2z = cz - az;
          const px = dy * e2z - dz * e2y, py = dz * e2x - dx * e2z, pz = dx * e2y - dy * e2x;
          const det = e1x * px + e1y * py + e1z * pz;
          if (Math.abs(det) < 1e-8) return null;
          const inv = 1 / det;
          const tx = ox - ax, ty = oy - ay, tz = oz - az;
          const u = (tx * px + ty * py + tz * pz) * inv;
          if (u < 0 || u > 1) return null;
          const qx = ty * e1z - tz * e1y, qy = tz * e1x - tx * e1z, qz = tx * e1y - ty * e1x;
          const v = (dx * qx + dy * qy + dz * qz) * inv;
          if (v < 0 || u + v > 1) return null;
          const t = (e2x * qx + e2y * qy + e2z * qz) * inv;
          return t > 1e-4 ? t : null;
        }
        function meshDistance(object, ray) {
          const geometry = object.geometry;
          const position = geometry && geometry.attributes && geometry.attributes.position;
          if (!position) return null;
          const Matrix4 = object.matrixWorld && object.matrixWorld.constructor;
          if (!Matrix4) return null;
          const world = new Matrix4().fromArray(worldMatrix(object));
          const inverse = world.clone().invert();
          const origin = new ray.Vector3(ray.x, ray.y, ray.z).applyMatrix4(inverse);
          const direction = new ray.Vector3(ray.dx, ray.dy, ray.dz).transformDirection(inverse);
          const index = geometry.index;
          const limit = index ? Math.min(index.count, 6000) : Math.min(position.count, 6000);
          let best = null;
          const tri = (ia, ib, ic) => {
            const t = hitTriangle(origin.x, origin.y, origin.z, direction.x, direction.y, direction.z,
              position.getX(ia), position.getY(ia), position.getZ(ia),
              position.getX(ib), position.getY(ib), position.getZ(ib),
              position.getX(ic), position.getY(ic), position.getZ(ic));
            if (t !== null && (best === null || t < best)) best = t;
          };
          if (index) {
            for (let cursor = 0; cursor + 2 < limit; cursor += 3) tri(index.getX(cursor), index.getX(cursor + 1), index.getX(cursor + 2));
          } else {
            for (let cursor = 0; cursor + 2 < limit; cursor += 3) tri(cursor, cursor + 1, cursor + 2);
          }
          return best;
        }
        function occlusion(scene, camera, ndcX, ndcY, target) {
          if (!target) return { hit: true };
          const ray = rayThrough(camera, ndcX, ndcY);
          let best = null;
          let bestT = Infinity;
          scene.traverse((object) => {
            if (!object.isMesh || object.visible === false) return;
            const distance = meshDistance(object, ray);
            if (distance !== null && distance < bestT) { bestT = distance; best = object; }
          });
          if (!best) return { hit: false, actual: 'miss' };
          const same = (target.uuid && best.uuid === target.uuid) || (!target.uuid && target.name && best.name === target.name);
          if (!same) return { hit: false, actual: 'occluded-by:' + (best.name || best.uuid) };
          return { hit: true };
        }
        function projectPoint(camera, x, y, z) {
          const Vector3 = camera.position.constructor;
          const point = new Vector3(x, y, z);
          point.project(camera);
          if (!Number.isFinite(point.x) || !Number.isFinite(point.y)) return null;
          return { x: point.x, y: point.y };
        }
        function canvasFor(selector) {
          if (selector) {
            const found = document.querySelector(selector);
            if (found && found.tagName === 'CANVAS') return found;
          }
          const renderers = devtoolsRenderers();
          if (renderers.length && renderers[0].domElement && renderers[0].domElement.tagName === 'CANVAS') return renderers[0].domElement;
          return document.querySelector('canvas');
        }
        function frameOf(canvas) {
          const rect = canvas.getBoundingClientRect();
          const dpr = window.devicePixelRatio || 1;
          const viewport = window.visualViewport;
          const zoom = viewport && viewport.scale ? viewport.scale : 1;
          const cssWidth = canvas.clientWidth || rect.width;
          const deviceBox = cssWidth > 0 && Math.abs(rect.width - cssWidth) > 1 && Math.abs(rect.width - cssWidth * dpr) < 2;
          return {
            left: rect.left, top: rect.top, width: rect.width, height: rect.height,
            dpr, zoom,
            offsetLeft: viewport ? viewport.offsetLeft : 0,
            offsetTop: viewport ? viewport.offsetTop : 0,
            deviceBox
          };
        }
        function readScene() {
          const picked = pickMount();
          const report = signalReport();
          if (!picked.scenes.length) return { found: false, ok: false, actual: report.diagnostics.join('; ') || 'scene-not-found' };
          const scene = picked.scenes[0];
          const bag = devtoolsBag();
          return {
            found: true, ok: true,
            actual: JSON.stringify({
              mount: picked.mount,
              camera: cameraView(scene),
              nodes: walk(scene),
              webgl: bag && bag.webgl ? bag.webgl.current : null,
              diagnostics: report.diagnostics
            })
          };
        }
        function projectScene() {
          const picked = pickMount();
          if (!picked.scenes.length) return { found: false, ok: false, actual: 'scene-not-found' };
          const scene = picked.scenes[0];
          const camera = findCamera(scene);
          if (!camera) return { found: false, ok: false, actual: 'camera-not-found' };
          const canvas = canvasFor(request.selector || '');
          if (!canvas) return { found: false, ok: false, actual: 'canvas-not-found' };
          const object = findObject(scene, request);
          const path = worldPath(request.value || '');
          const points = path || [localPoint(object, request.value || '') || explicitWorld(request.value || '') || (object ? centerOf(object) : null)];
          if (!points[0]) return { found: false, ok: false, actual: object ? 'project-failed' : 'object-not-found' };
          const projected = [];
          for (let index = 0; index < points.length; index += 1) {
            const ndc = projectPoint(camera, points[index].x, points[index].y, points[index].z);
            if (!ndc) return { found: false, ok: false, actual: 'project-failed' };
            const cover = occlusion(scene, camera, ndc.x, ndc.y, object);
            if (!cover.hit) return { found: false, ok: false, hit: false, actual: cover.actual || 'occluded' };
            projected.push({ ndcX: ndc.x, ndcY: ndc.y });
          }
          const frame = frameOf(canvas);
          return {
            found: true, ok: true, hit: true, actual: 'projected',
            ndcX: projected[0].ndcX, ndcY: projected[0].ndcY,
            points: projected,
            left: frame.left, top: frame.top, width: frame.width, height: frame.height,
            dpr: frame.dpr, zoom: frame.zoom, offsetLeft: frame.offsetLeft, offsetTop: frame.offsetTop,
            deviceBox: frame.deviceBox
          };
        }
        if (request.op === 'ready') return { found: document.readyState === 'complete', ok: true };
        if (request.op === 'probe') return { found: true, ok: true, actual: JSON.stringify(signalReport()) };
        if (request.op === 'canvas-rect') {
          const canvas = canvasFor(request.selector || '');
          if (!canvas) return { found: false, ok: false, actual: 'canvas-not-found' };
          const frame = frameOf(canvas);
          const scale = frame.deviceBox ? (frame.dpr || 1) : 1;
          return { found: true, ok: true, left: frame.left / scale, top: frame.top / scale, width: frame.width / scale, height: frame.height / scale };
        }
        if (request.op === 'project') return projectScene();
        if (request.op === 'read-scene' || request.op === 'assert-scene' || request.op === 'diff') return readScene();
        """;
}
