namespace Workbench.TestExecutor;

/// <summary>
/// Installed with <c>Page.addScriptToEvaluateOnNewDocument</c> before the app runs.
/// three.js notifies <c>window.__THREE_DEVTOOLS__</c> when it constructs a scene or renderer.
/// The hook only listens. It does not change scene objects or add a test API the app must call.
/// </summary>
internal static class SceneHook
{
    public const string Source = """
        (function () {
          if (window.__THREE_DEVTOOLS__ && window.__THREE_DEVTOOLS__.wb) return;
          var target = new EventTarget();
          var scenes = [];
          var renderers = [];
          var frameDraws = 0;
          var frameVerts = 0;
          var frameBytes = 0;
          var webgl = {
            drawCalls: 0,
            vertices: 0,
            bufferBytes: 0,
            frames: 0,
            contexts: 0,
            last: { drawCalls: 0, vertices: 0, bufferBytes: 0 },
            current: { drawCalls: 0, vertices: 0, bufferBytes: 0 }
          };
          function touch() {
            webgl.current = { drawCalls: frameDraws, vertices: frameVerts, bufferBytes: frameBytes };
          }
          target.addEventListener('observe', function (event) {
            var detail = event && event.detail;
            if (!detail || typeof detail !== 'object') return;
            if (detail.isScene === true) scenes.push(detail);
            else if (detail.isWebGLRenderer === true) renderers.push(detail);
          });
          Object.defineProperty(target, 'wb', {
            value: { scenes: scenes, renderers: renderers, webgl: webgl },
            enumerable: false
          });
          window.__THREE_DEVTOOLS__ = target;
          if (typeof HTMLCanvasElement === 'function' && HTMLCanvasElement.prototype) {
            var original = HTMLCanvasElement.prototype.getContext;
            HTMLCanvasElement.prototype.getContext = function (kind, attrs) {
              var gl = original.call(this, kind, attrs);
              if (!gl || (kind !== 'webgl' && kind !== 'webgl2' && kind !== 'experimental-webgl') || gl.__wbObserved) return gl;
              try { Object.defineProperty(gl, '__wbObserved', { value: true }); } catch (error) { gl.__wbObserved = true; }
              webgl.contexts += 1;
              var drawArrays = gl.drawArrays;
              var drawElements = gl.drawElements;
              var bufferData = gl.bufferData;
              gl.drawArrays = function (mode, first, count) {
                frameDraws += 1;
                frameVerts += count || 0;
                touch();
                return drawArrays.apply(this, arguments);
              };
              gl.drawElements = function (mode, count) {
                frameDraws += 1;
                frameVerts += count || 0;
                touch();
                return drawElements.apply(this, arguments);
              };
              gl.bufferData = function (bufferKind, data) {
                if (data && data.byteLength) frameBytes += data.byteLength;
                else if (typeof data === 'number') frameBytes += data;
                touch();
                return bufferData.apply(this, arguments);
              };
              return gl;
            };
          }
          if (typeof window.requestAnimationFrame === 'function') {
            var raf = window.requestAnimationFrame.bind(window);
            window.requestAnimationFrame = function (callback) {
              return raf(function (time) {
                webgl.last = { drawCalls: frameDraws, vertices: frameVerts, bufferBytes: frameBytes };
                webgl.drawCalls += frameDraws;
                webgl.vertices += frameVerts;
                webgl.bufferBytes += frameBytes;
                if (frameDraws || frameVerts || frameBytes) webgl.frames += 1;
                frameDraws = 0;
                frameVerts = 0;
                frameBytes = 0;
                touch();
                return callback(time);
              });
            };
          }
        })();
        """;
}
