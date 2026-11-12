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
        function explicitWorld(input) {
          if (typeof input !== 'string' || input.indexOf('world:') !== 0) return null;
          const parts = input.slice(6).split(',');
          if (parts.length !== 3) return null;
          const x = Number(parts[0]), y = Number(parts[1]), z = Number(parts[2]);
          if (!Number.isFinite(x) || !Number.isFinite(y) || !Number.isFinite(z)) return null;
          return { x, y, z };
        }
        function centerOf(object) {
          const local = localBox(object);
          if (!local) return { x: object.position.x, y: object.position.y, z: object.position.z };
          return transformPoint(worldMatrix(object), (local.min.x + local.max.x) / 2, (local.min.y + local.max.y) / 2, (local.min.z + local.max.z) / 2);
        }
        function projectPoint(camera, x, y, z) {
          const view = camera.matrixWorldInverse.elements;
          const proj = camera.projectionMatrix.elements;
          const vx = view[0] * x + view[4] * y + view[8] * z + view[12];
          const vy = view[1] * x + view[5] * y + view[9] * z + view[13];
          const vz = view[2] * x + view[6] * y + view[10] * z + view[14];
          const vw = view[3] * x + view[7] * y + view[11] * z + view[15];
          const cx = proj[0] * vx + proj[4] * vy + proj[8] * vz + proj[12] * vw;
          const cy = proj[1] * vx + proj[5] * vy + proj[9] * vz + proj[13] * vw;
          const cw = proj[3] * vx + proj[7] * vy + proj[11] * vz + proj[15] * vw;
          if (!cw) return null;
          return { x: cx / cw, y: cy / cw };
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
          let point = explicitWorld(request.value || '');
          if (!point) {
            const object = findObject(scene, request);
            if (!object) return { found: false, ok: false, actual: 'object-not-found' };
            point = centerOf(object);
          }
          const ndc = projectPoint(camera, point.x, point.y, point.z);
          if (!ndc) return { found: false, ok: false, actual: 'project-failed' };
          const frame = frameOf(canvas);
          return {
            found: true, ok: true, actual: 'projected',
            ndcX: ndc.x, ndcY: ndc.y,
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
