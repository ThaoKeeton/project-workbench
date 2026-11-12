using System.Text.Json;
using System.Text.RegularExpressions;

namespace Workbench.TestExecutor;

/// <summary>
/// Builds one function call for <c>Runtime.evaluate</c>.
/// A waiting selector must not be composed as <c>(x = ...;, ...)</c>: the semicolon makes that a syntax error.
/// </summary>
internal static class CdpExpression
{
    private static readonly Regex AssignmentComma = new(@"\([A-Za-z_$][\w$]*\s*=[^;]*;,", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    public static string Build(object request)
        => "(() => {\nconst request = " + JsonSerializer.Serialize(request) + ";\n" + ScenePage.Source + "\n" + Script + "\n})()";

    public static bool UsesAssignmentComma(string expression) => AssignmentComma.IsMatch(expression);

    // Each call is a complete function. Wait loops live in C#, so this script never assigns and then commas.
    // Clicks use element.click(), and the center strategy also reports the box center for a real mouse click.
    private const string Script = """
        const visible = (el) => {
          if (!el || el.hidden || el.getAttribute('aria-hidden') === 'true') return false;
          const style = getComputedStyle(el);
          if (style.display === 'none' || style.visibility === 'hidden') return false;
          const rect = el.getBoundingClientRect();
          return rect.width > 0 && rect.height > 0;
        };
        const roleOf = (el) => el.getAttribute('role') || ({BUTTON:'button',A:'link',INPUT:'textbox',TEXTAREA:'textbox',SELECT:'combobox'}[el.tagName] || '');
        const nameOf = (el) => el.getAttribute('aria-label') || el.innerText || '';
        const nodes = () => Array.from(document.querySelectorAll('body *')).filter(visible);
        const popupText = () => {
          const modal = document.querySelector(request.popup || '.ant-modal');
          if (!visible(modal)) return '';
          return (modal.innerText || '').trim();
        };
        if (request.op === 'ping') return {found:true, ok:true};
        if (request.op === 'screenshot') {
          const shot = document.createElement('canvas');
          shot.width = 320; shot.height = 180;
          const ctx = shot.getContext('2d');
          ctx.fillStyle = '#f4f1ea';
          ctx.fillRect(0, 0, shot.width, shot.height);
          ctx.fillStyle = '#1c1915';
          ctx.font = '14px sans-serif';
          ctx.fillText((document.body.innerText || '').replace(/\s+/g, ' ').slice(0, 60), 8, 20);
          const view = document.querySelector('canvas');
          if (view && view !== shot) ctx.drawImage(view, 0, 28, 320, 140);
          const pixels = new Uint8Array(ctx.getImageData(0, 0, shot.width, shot.height).data);
          const chunks = [];
          for (let index = 0; index < pixels.length; index += 8192)
            chunks.push(String.fromCharCode.apply(null, pixels.subarray(index, Math.min(index + 8192, pixels.length))));
          return {found:true, ok:true, width: shot.width, height: shot.height, rgba: btoa(chunks.join(''))};
        }
        if (request.op === 'popup') {
          const text = popupText();
          return text ? {found:true, ok:false, popup:true, actual:text} : {found:false};
        }
        if (request.op === 'read-body') return {found:true, ok:true, actual: document.body.innerText || ''};
        if (request.op === 'snapshot') {
          const listed = nodes().filter(el => roleOf(el) || nameOf(el)).slice(0, 40).map(el => ({
            role: roleOf(el), name: nameOf(el).slice(0, 80), text: (el.innerText || '').slice(0, 80), selector: el.id ? '#' + el.id : ''
          }));
          return {found:true, nodes:listed};
        }
        if (request.op === 'record-start') {
          window.__workbenchRecording = [];
          return {found:true, ok:true};
        }
        if (request.op === 'record-export') return {found:true, events: window.__workbenchRecording || []};
        const find = () => {
          if (request.selector) return document.querySelector(request.selector);
          if (request.role) return nodes().find(el => roleOf(el) === request.role && (!request.name || nameOf(el).includes(request.name))) || null;
          if (request.text) {
            const matches = nodes().filter(el => (el.innerText || '').includes(request.text));
            matches.sort((a, b) => (a.innerText || '').length - (b.innerText || '').length);
            return matches[0] || null;
          }
          if (request.objectName) return document.querySelector('canvas');
          return null;
        };
        if (request.op === 'wait-for') {
          const waiting = find();
          if (!visible(waiting)) return {found:false};
          return {found:true, ok:true, actual: (waiting.value || waiting.innerText || '').trim()};
        }
        if (request.op === 'assert-network') {
          const urls = performance.getEntriesByType('resource').map(entry => entry.name);
          const ok = !request.expected || urls.some(url => url.includes(request.expected));
          return {found:true, ok, actual: urls.join(',')};
        }
        const el = find();
        if (!el) return {found:false};
        if (request.op === 'click' || request.op === 'invoke' || request.op === 'expand') {
          const rect = el.getBoundingClientRect();
          const x = rect.left + rect.width / 2;
          const y = rect.top + rect.height / 2;
          if (request.strategy === 'center') {
            el.dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true, clientX:x, clientY:y, view:window}));
          }
          if (typeof el.click === 'function') el.click();
          if (window.__workbenchRecording) window.__workbenchRecording.push({op:'click', selector: request.selector || '', text: request.text || '', value:''});
          return {found:true, ok:true, x, y, actual: document.body.innerText || ''};
        }
        if (request.op === 'type' || request.op === 'set-value' || request.op === 'open-file') {
          el.focus();
          el.value = request.value || '';
          el.dispatchEvent(new Event('input', {bubbles:true}));
          if (window.__workbenchRecording) window.__workbenchRecording.push({op:'type', selector: request.selector || '', text:'', value: el.value || ''});
          return {found:true, ok:true, value: el.value || ''};
        }
        const actual = (((el.tagName === 'INPUT' || el.tagName === 'TEXTAREA') ? el.value : el.textContent) || '').trim();
        const ok = !request.expected || actual === request.expected || actual.includes(request.expected);
        return {found:true, ok, actual, visible: visible(el)};
        """;
}
