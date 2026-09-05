(() => {
  const ROOT_ID = 'ui-root';
  const SURFACES_KEY = 'framework.surfaces';
  const AGGREGATE_KEY = 'framework.surfaces.aggregate';

  const root = document.getElementById(ROOT_ID);
  if (!root) {
    console.error('[BannerlordHtmlUI] shell root element missing.');
    return;
  }
  if (!window.game || typeof window.game.on !== 'function') {
    console.error('[BannerlordHtmlUI] runtime unavailable in shell.');
    return;
  }

  // id -> { element, signature, uri }
  const mounted = new Map();
  let inputOwnerId = '';

  const report = (kind, message) => {
    try {
      window.game.call('runtime.error', { kind, message: String(message) });
    } catch (_) { /* never let diagnostics break rendering */ }
  };

  const signatureOf = surface => JSON.stringify([surface.uri || '', surface.zIndex || 0]);

  const applyInputOwner = () => {
    for (const [id, entry] of mounted) {
      entry.element.dataset.inputOwner = id === inputOwnerId ? 'true' : 'false';
    }
  };

  const unmount = (id, entry) => {
    if (!entry) return;
    try { entry.element.remove(); } catch (_) {}
    mounted.delete(id);
  };

  const render = surfaces => {
    const list = Array.isArray(surfaces) ? surfaces : [];
    const seen = new Set();

    for (const surface of list) {
      if (!surface || !surface.id) continue;
      seen.add(surface.id);

      if (!surface.visible) {
        // Hide instead of unmounting: the surface document keeps its JS/DOM state and
        // remounting on the next Show is instant. Only uri changes rebuild the frame.
        const hidden = mounted.get(surface.id);
        if (hidden) {
          hidden.element.style.display = 'none';
          hidden.element.dataset.inputOwner = 'false';
        }
        continue;
      }

      const uri = surface.uri || '';
      const signature = signatureOf(surface);
      const existing = mounted.get(surface.id);

      if (existing) {
        if (existing.signature === signature) {
          existing.element.style.display = '';
          continue;
        }
        if (existing.uri === uri) {
          // Only the stacking order changed.
          existing.element.style.zIndex = String(surface.zIndex || 0);
          existing.signature = signature;
          existing.element.style.display = '';
          continue;
        }
        unmount(surface.id, existing);
      }

      if (!uri) {
        report('surface-mount', 'Surface has no uri and was skipped: ' + surface.id);
        continue;
      }

      const element = document.createElement('iframe');
      element.className = 'bh-surface';
      element.dataset.surface = surface.id;
      element.dataset.owner = surface.ownerId || '';
      element.style.zIndex = String(surface.zIndex || 0);
      element.setAttribute('scrolling', 'no');
      element.src = uri;
      root.appendChild(element);
      mounted.set(surface.id, { element, signature, uri });
    }

    for (const id of [...mounted.keys()]) {
      if (!seen.has(id)) unmount(id, mounted.get(id));
    }

    applyInputOwner();
  };

  const renderAggregate = aggregate => {
    inputOwnerId = (aggregate && aggregate.inputOwnerId) || '';
    applyInputOwner();
  };

  const hydrate = () => {
    try {
      render(window.game.state.get(SURFACES_KEY) || []);
      renderAggregate(window.game.state.get(AGGREGATE_KEY) || {});
    } catch (error) {
      report('shell-hydrate', error && error.message ? error.message : error);
    }
  };

  // The state snapshot arrives asynchronously, so the authoritative initial render
  // happens on 'ready'. The synchronous call below covers hot reloads where the
  // snapshot is already present.
  hydrate();
  window.game.on('ready', hydrate);
  window.game.on('state:' + SURFACES_KEY, render);
  window.game.on('state:' + AGGREGATE_KEY, renderAggregate);
})();
