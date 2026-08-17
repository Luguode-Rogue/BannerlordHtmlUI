(() => {
  const pending = new Map();
  const listeners = new Map();
  const state = new Map();
  const lifecycleListeners = new Set();
  const errorListeners = new Set();
  let nextId = 1;
  let lastError = null;
  let runtimeDisposed = false;

  const emit = (name, payload) => {
    const set = listeners.get(name);
    if (!set) return;
    for (const fn of [...set]) {
      try { fn(payload); } catch (e) { console.error(e); }
    }
  };

  const send = (type, name, payload, id = null) => {
    if (runtimeDisposed) throw new Error('BannerlordHtmlUI runtime is disposed.');
    const message = { version: 1, type, id, name, payload };
    try {
      console.debug('[BannerlordHtmlUI] postMessage', type, name, id);
      chrome.webview.postMessage(message);
      console.debug('[BannerlordHtmlUI] postMessage dispatched', type, name, id);
    } catch (e) {
      console.error('[BannerlordHtmlUI] postMessage failed', type, name, id, e);
      throw e;
    }
  };

  const requestInternal = (type, name, payload, timeoutMs) => new Promise((resolve, reject) => {
    if (runtimeDisposed) {
      reject(new Error('BannerlordHtmlUI runtime is disposed.'));
      return;
    }

    const id = `${type[0]}${Date.now()}_${nextId++}`;
    const item = { resolve, reject, timer: null };
    pending.set(id, item);

    try {
      send(type, name, payload, id);
    } catch (e) {
      pending.delete(id);
      reject(e);
      return;
    }

    item.timer = setTimeout(() => {
      const current = pending.get(id);
      if (!current) return;
      pending.delete(id);
      console.error('[BannerlordHtmlUI] request timeout', type, name, id);
      reject(new Error(`${type} timeout: ${name}`));
    }, Math.max(1, Number(timeoutMs) || 10000));
  });

  const disposeRuntime = (reason = 'Page unloaded') => {
    if (runtimeDisposed) return;
    runtimeDisposed = true;
    const error = new Error(reason);
    for (const [id, item] of pending.entries()) {
      pending.delete(id);
      if (item.timer) clearTimeout(item.timer);
      try { item.reject(error); } catch (_) {}
    }
    listeners.clear();
    lifecycleListeners.clear();
    errorListeners.clear();
  };

  const emitPageLifecycle = (payload) => {
    try { state.set('framework.page.lifecycle', payload); } catch (_) {}
    for (const fn of [...lifecycleListeners]) {
      try { fn(payload); } catch (e) { console.error(e); }
    }
    emit('framework.page.lifecycle', payload);
  };

  const emitRuntimeError = (payload) => {
    lastError = payload || null;
    try { state.set('framework.runtimeError', lastError); } catch (_) {}
    for (const fn of [...errorListeners]) {
      try { fn(lastError); } catch (e) { console.error(e); }
    }
    emit('framework.runtimeError', lastError);
  };

  const scopedName = (ownerId, name) => {
    if (!ownerId) return name;
    if (!name) throw new Error('A command/event/state name is required.');
    return `${ownerId}.${String(name).replace(/^\.+/, '')}`;
  };

  const createI18n = () => {
    const localeListeners = new Set();
    let locale = null;
    const cache = new Map();
    const emitLocale = (payload) => {
      locale = payload && payload.language ? payload.language : locale;
      cache.clear();
      for (const fn of [...localeListeners]) { try { fn(locale); } catch (e) { console.error(e); } }
    };
    const t = async (key, variables = null, options = {}) => {
      if (!key) return '';
      const cacheKey = `${key}|${JSON.stringify(variables || {})}|${options.fallbackLanguage || ''}`;
      if (cache.has(cacheKey)) return cache.get(cacheKey);
      const result = await window.game.request('framework.i18n.translate', { key, variables, fallbackLanguage: options.fallbackLanguage || null });
      const value = result && result.text != null ? String(result.text) : String(key);
      cache.set(cacheKey, value);
      return value;
    };
    const getLocale = async () => {
      if (locale) return locale;
      const result = await window.game.request('framework.i18n.getLocale', {});
      locale = result && result.language ? result.language : null;
      return locale;
    };
    const getLanguages = () => window.game.request('framework.i18n.getLanguages', {});
    const formatDate = value => window.game.request('framework.i18n.formatDate', { value: new Date(value).toISOString() }).then(r => r.text);
    const formatTime = value => window.game.request('framework.i18n.formatTime', { value: new Date(value).toISOString() }).then(r => r.text);
    const bind = async (root = document) => {
      const elements = root.querySelectorAll ? root.querySelectorAll('[data-bhui-i18n],[data-bhui-i18n-placeholder],[data-bhui-i18n-title],[data-bhui-i18n-alt]') : [];
      const jobs = [];
      for (const el of elements) {
        const mappings = [
          ['data-bhui-i18n', 'textContent'],
          ['data-bhui-i18n-placeholder', 'placeholder'],
          ['data-bhui-i18n-title', 'title'],
          ['data-bhui-i18n-alt', 'alt']
        ];
        for (const [attribute, property] of mappings) {
          if (!el.hasAttribute(attribute)) continue;
          const key = el.getAttribute(attribute);
          jobs.push(t(key).then(value => { el[property] = value; }));
        }
      }
      await Promise.all(jobs);
      return () => {};
    };
    const localeOff = window.game.on('framework.i18n.localeChanged', emitLocale);
    return {
      get locale() { return locale; },
      getLocale,
      getLanguages,
      t,
      bind,
      formatDate,
      formatTime,
      onLocaleChanged(handler) { localeListeners.add(handler); return () => localeListeners.delete(handler); },
      dispose() {
        try { localeOff(); } catch (_) {}
        localeListeners.clear();
        cache.clear();
      }
    };
  };
  const i18n = createI18n();

  const createScope = (ownerId) => {
    if (!ownerId) throw new Error('Owner id is required.');
    const prefix = `${ownerId}.`;
    const scope = {
      ownerId,
      call(name, payload = {}, timeoutMs = 10000) {
        return window.game.call(scopedName(ownerId, name), payload, timeoutMs);
      },
      request(name, payload = {}, timeoutMs = 10000) {
        return window.game.request(scopedName(ownerId, name), payload, timeoutMs);
      },
      on(name, handler) {
        return window.game.on(scopedName(ownerId, name), handler);
      },
      state: {
        get(key) { return state.get(scopedName(ownerId, key)); },
        has(key) { return state.has(scopedName(ownerId, key)); },
        subscribe(key, handler) {
          return window.game.on(`state:${scopedName(ownerId, key)}`, handler);
        },
        snapshot() {
          const result = {};
          for (const [key, value] of state.entries()) {
            if (key.startsWith(prefix)) result[key.substring(prefix.length)] = value;
          }
          return result;
        }
      },
      events: {
        on(name, handler) { return window.game.on(scopedName(ownerId, name), handler); }
      },
      page: window.game && window.game.page ? window.game.page : null,
      lifecycle: {
        get state() { return window.game.page.lifecycle; },
        on(handler) { return window.game.page.onLifecycle(handler); }
      },
      pageLifecycle: {
        on(handler) { return window.game.page.onLifecycle(handler); }
      },
      errors: {
        on(handler) { return window.game.errors.on(handler); },
        get last() { return window.game.errors.last; }
      },
      i18n,
      bind: createBinder(ownerId),
      input: window.game ? window.game.input : null,
      pages: {
        open(id) {
          return window.game.call('framework.openPage', { ownerId, pageId: id });
        },
        close() {
          return window.game.call('framework.closePage', { ownerId });
        }
      }
    };
    scope.app = scope;
    return scope;
  };

  const createBinder = (ownerId = null) => {
    const resolveKey = (key) => ownerId ? scopedName(ownerId, key) : key;
    const subscriptions = [];
    const getElement = (elementOrSelector) => typeof elementOrSelector === 'string' ? document.querySelector(elementOrSelector) : elementOrSelector;
    const subscribeElement = (element, key, apply) => {
      if (!element) return () => {};
      const fullKey = resolveKey(key);
      const update = (value) => { try { apply(element, value); } catch (e) { emitRuntimeError({ kind: 'binding', message: String(e), key: fullKey }); } };
      update(state.get(fullKey));
      const off = window.game.on(`state:${fullKey}`, update);
      subscriptions.push(off);
      return off;
    };
    const bindText = (elementOrSelector, key) => subscribeElement(getElement(elementOrSelector), key, (el, value) => { el.textContent = value == null ? '' : String(value); });
    const bindValue = (elementOrSelector, key) => subscribeElement(getElement(elementOrSelector), key, (el, value) => { if ('value' in el) el.value = value == null ? '' : String(value); });
    const bindChecked = (elementOrSelector, key) => subscribeElement(getElement(elementOrSelector), key, (el, value) => { el.checked = !!value; });
    const bindDisabled = (elementOrSelector, key) => subscribeElement(getElement(elementOrSelector), key, (el, value) => { el.disabled = !!value; });
    const bindHidden = (elementOrSelector, key) => subscribeElement(getElement(elementOrSelector), key, (el, value) => { el.hidden = !!value; });
    const bindVisible = (elementOrSelector, key) => subscribeElement(getElement(elementOrSelector), key, (el, value) => { el.hidden = !value; });
    const bindClass = (elementOrSelector, className, key, truthy = true) => subscribeElement(getElement(elementOrSelector), key, (el, value) => { el.classList.toggle(className, value === truthy || (truthy === true && !!value)); });
    const bindAttribute = (elementOrSelector, attribute, key) => subscribeElement(getElement(elementOrSelector), key, (el, value) => { if (value == null || value === false) el.removeAttribute(attribute); else el.setAttribute(attribute, String(value)); });

    const bindTemplate = (containerOrSelector, key, template, options = {}) => {
      const container = getElement(containerOrSelector);
      if (!container || typeof template !== 'function') return () => {};
      const fullKey = resolveKey(key); let children = [];
      const getKey = typeof options.key === 'function' ? options.key : (_item, index) => index;
      const disposeChild = (child) => { try { child?.dispose?.(); } catch (_) {} };
      const clear = () => { for (const child of children) disposeChild(child); children = []; while (container.firstChild) container.removeChild(container.firstChild); };
      const render = (value) => {
        try {
          const items = Array.isArray(value) ? value : []; const next = []; const old = new Map(children.map(child => [String(child.key), child]));
          const fragment = document.createDocumentFragment ? document.createDocumentFragment() : null; const target = fragment || container;
          items.forEach((item, index) => {
            const keyValue = getKey(item, index); const keyString = String(keyValue); const existing = old.get(keyString);
            if (existing) { old.delete(keyString); try { if (typeof existing.update === 'function') existing.update(item, index); } catch (e) { emitRuntimeError({ kind: 'template-update', message: String(e), key: fullKey }); } next.push(existing); target.appendChild(existing.element); return; }
            const result = template(item, index); const element = result && result.element ? result.element : result; if (!element) return;
            const child = { key: keyValue, element, update: result && typeof result.update === 'function' ? result.update : null, dispose: result && typeof result.dispose === 'function' ? result.dispose : null }; next.push(child); target.appendChild(element);
          });
          for (const stale of old.values()) { disposeChild(stale); if (stale.element && stale.element.parentNode === container) container.removeChild(stale.element); }
          children = next; if (fragment) { while (container.firstChild) container.removeChild(container.firstChild); container.appendChild(fragment); }
        } catch (e) { emitRuntimeError({ kind: 'template-binding', message: String(e), key: fullKey }); }
      };
      render(state.get(fullKey)); const off = window.game.on(`state:${fullKey}`, render); subscriptions.push(off); return () => { try { off(); } catch (_) {} clear(); };
    };

    const delegate = (rootOrSelector, eventName, selector, handler, options = {}) => {
      const root = getElement(rootOrSelector) || document; if (typeof handler !== 'function') return () => {};
      const listener = (event) => { try { const target = event.target && event.target.closest ? event.target.closest(selector) : null; if (!target || !root.contains(target)) return; handler(event, target); } catch (e) { emitRuntimeError({ kind: 'event-delegate', message: String(e), event: eventName }); } };
      root.addEventListener(eventName, listener, options); const off = () => root.removeEventListener(eventName, listener, options); subscriptions.push(off); return off;
    };
    const bindEvents = (rootOrSelector, events = {}) => { const offs = []; for (const [eventName, definitions] of Object.entries(events)) { const entries = Array.isArray(definitions) ? definitions : [definitions]; for (const definition of entries) { if (!definition || typeof definition.selector !== 'string' || typeof definition.handler !== 'function') continue; offs.push(delegate(rootOrSelector, eventName, definition.selector, definition.handler, definition.options)); } } return () => offs.forEach(off => { try { off(); } catch (_) {} }); };
    const scheduleWriter = (writer, value, event, element, options = {}) => {
      if (typeof writer !== 'function') return; const debounceMs = Math.max(0, Number(options.debounce || 0)); const throttleMs = Math.max(0, Number(options.throttle || 0));
      if (debounceMs > 0) { if (!scheduleWriter._debounceTimers) scheduleWriter._debounceTimers = new WeakMap(); const timers = scheduleWriter._debounceTimers; const prior = timers.get(element); if (prior) clearTimeout(prior); const timer = setTimeout(() => { timers.delete(element); try { writer(value, event, element); } catch (e) { emitRuntimeError({ kind: 'binding-writer', message: String(e) }); } }, debounceMs); timers.set(element, timer); return; }
      if (throttleMs > 0) { if (!scheduleWriter._throttleState) scheduleWriter._throttleState = new WeakMap(); const states = scheduleWriter._throttleState; let state = states.get(element); const now = Date.now(); if (!state) { state = { last: 0, timer: null, queued: null }; states.set(element, state); } const run = (v, ev) => { state.last = Date.now(); state.queued = null; try { writer(v, ev, element); } catch (e) { emitRuntimeError({ kind: 'binding-writer', message: String(e) }); } }; if (now - state.last >= throttleMs) { if (state.timer) { clearTimeout(state.timer); state.timer = null; } run(value, event); } else { state.queued = { value, event }; if (!state.timer) state.timer = setTimeout(() => { state.timer = null; if (state.queued) run(state.queued.value, state.queued.event); }, throttleMs - (now - state.last)); } return; }
      try { writer(value, event, element); } catch (e) { emitRuntimeError({ kind: 'binding-writer', message: String(e) }); }
    };
    const bindTwoWay = (elementOrSelector, key, writer, options = {}) => {
      const element = getElement(elementOrSelector); if (!element) return () => {}; const fullKey = resolveKey(key); const eventName = options.event || (element.type === 'checkbox' || element.type === 'radio' ? 'change' : 'input'); const read = typeof options.read === 'function' ? options.read : (el => 'value' in el ? el.value : el.checked); const apply = value => { if (element.type === 'checkbox' || element.type === 'radio') element.checked = !!value; else if ('value' in element) element.value = value == null ? '' : String(value); }; apply(state.get(fullKey)); const onState = value => apply(value); const onInput = event => scheduleWriter(writer, read(element), event, element, options); const offState = window.game.on(`state:${fullKey}`, onState); element.addEventListener(eventName, onInput, options.listenerOptions || false); const off = () => { try { offState(); } catch (_) {} try { element.removeEventListener(eventName, onInput, options.listenerOptions || false); } catch (_) {} if (scheduleWriter._debounceTimers) { const timer = scheduleWriter._debounceTimers.get(element); if (timer) clearTimeout(timer); scheduleWriter._debounceTimers.delete(element); } if (scheduleWriter._throttleState) { const throttleState = scheduleWriter._throttleState.get(element); if (throttleState?.timer) clearTimeout(throttleState.timer); scheduleWriter._throttleState.delete(element); } }; subscriptions.push(off); return off;
    };
    const bindGroup = (...disposers) => { const list = disposers.flat(Infinity).filter(d => typeof d === 'function'); const dispose = () => { while (list.length) { const off = list.pop(); try { off(); } catch (_) {} } }; subscriptions.push(dispose); return dispose; };
    const bindForm = (formOrSelector, map = {}) => { const form = getElement(formOrSelector); if (!form) return () => {}; const offs = []; for (const [fieldName, key] of Object.entries(map)) { const field = form.elements && form.elements.namedItem ? form.elements.namedItem(fieldName) : form.querySelector(`[name="${fieldName}"]`); if (!field) continue; offs.push(bindValue(field, key)); } return () => { for (const off of offs) { try { off(); } catch (_) {} } }; };
    const bindList = (containerOrSelector, key, render, options = {}) => {
      const container = getElement(containerOrSelector); if (!container || typeof render !== 'function') return () => {}; const fullKey = resolveKey(key); let children = []; let generation = 0; const getKey = typeof options.key === 'function' ? options.key : (_item, index) => index;
      const disposeChild = (child) => { try { child?.dispose?.(); } catch (_) {} }; const removeChildNode = (child) => { const element = child?.element; if (element && element.parentNode === container) container.removeChild(element); }; const clear = () => { for (const child of children) disposeChild(child); children = []; while (container.firstChild) container.removeChild(container.firstChild); }; const createChild = (item, index, currentGeneration) => { const result = render(item, index, currentGeneration); const element = result && result.element ? result.element : result; if (!element) return null; return { key: getKey(item, index), element, update: result && typeof result.update === 'function' ? result.update : null, dispose: result && result.dispose ? result.dispose : null }; };
      const update = (value) => { try { generation += 1; const currentGeneration = generation; const items = Array.isArray(value) ? value : []; if (options.diff === false) { clear(); const fragment = document.createDocumentFragment ? document.createDocumentFragment() : null; const target = fragment || container; items.forEach((item, index) => { const child = createChild(item, index, currentGeneration); if (child) { target.appendChild(child.element); children.push(child); } }); if (fragment) container.appendChild(fragment); return; } const oldByKey = new Map(); for (const child of children) oldByKey.set(String(child.key), child); const nextChildren = []; const fragment = document.createDocumentFragment ? document.createDocumentFragment() : null; const target = fragment || container; items.forEach((item, index) => { const keyValue = getKey(item, index); const keyString = String(keyValue); const existing = oldByKey.get(keyString); if (existing) { oldByKey.delete(keyString); try { if (typeof existing.update === 'function') existing.update(item, index, currentGeneration); } catch (e) { emitRuntimeError({ kind: 'list-update', message: String(e), key: fullKey }); } nextChildren.push(existing); target.appendChild(existing.element); return; } const child = createChild(item, index, currentGeneration); if (child) { nextChildren.push(child); target.appendChild(child.element); } }); for (const stale of oldByKey.values()) { disposeChild(stale); removeChildNode(stale); } children = nextChildren; if (fragment) { while (container.firstChild) container.removeChild(container.firstChild); container.appendChild(fragment); } } catch (e) { emitRuntimeError({ kind: 'list-binding', message: String(e), key: fullKey }); } };
      update(state.get(fullKey)); const off = window.game.on(`state:${fullKey}`, update); subscriptions.push(off); return () => { try { off(); } catch (_) {} clear(); if (options.clearOnDispose !== false) generation += 1; };
    };
    const component = (rootOrSelector, factory, props = {}) => { const root = getElement(rootOrSelector); if (!root || typeof factory !== 'function') return { element: root || null, update() {}, dispose() {} }; let mounted = null; let currentProps = props || {}; const mount = () => { try { mounted?.dispose?.(); while (root.firstChild) root.removeChild(root.firstChild); const result = factory(currentProps, root); mounted = result && result.element ? result : { element: result, dispose: null, update: null }; if (mounted.element && mounted.element !== root && mounted.element.parentNode !== root) root.appendChild(mounted.element); } catch (e) { emitRuntimeError({ kind: 'component', message: String(e) }); } }; const update = (nextProps = {}) => { currentProps = nextProps || {}; if (typeof mounted?.update === 'function') { try { mounted.update(currentProps); return; } catch (e) { emitRuntimeError({ kind: 'component-update', message: String(e) }); } } mount(); }; mount(); return { element: root, update, dispose() { try { mounted?.dispose?.(); } catch (_) {} while (root.firstChild) root.removeChild(root.firstChild); } }; };
    const apply = (root = document) => { const elements = root.querySelectorAll ? root.querySelectorAll('[data-bhui-text],[data-bhui-value],[data-bhui-checked],[data-bhui-disabled],[data-bhui-hidden],[data-bhui-visible]') : []; for (const el of elements) { if (el.hasAttribute('data-bhui-text')) bindText(el, el.getAttribute('data-bhui-text')); if (el.hasAttribute('data-bhui-value')) bindValue(el, el.getAttribute('data-bhui-value')); if (el.hasAttribute('data-bhui-checked')) bindChecked(el, el.getAttribute('data-bhui-checked')); if (el.hasAttribute('data-bhui-disabled')) bindDisabled(el, el.getAttribute('data-bhui-disabled')); if (el.hasAttribute('data-bhui-hidden')) bindHidden(el, el.getAttribute('data-bhui-hidden')); if (el.hasAttribute('data-bhui-visible')) bindVisible(el, el.getAttribute('data-bhui-visible')); } return () => dispose(); };
    const dispose = () => { while (subscriptions.length) { const off = subscriptions.pop(); try { off(); } catch (_) {} } };
    return { text: bindText, value: bindValue, checked: bindChecked, disabled: bindDisabled, hidden: bindHidden, visible: bindVisible, class: bindClass, attr: bindAttribute, form: bindForm, twoWayValue(elementOrSelector, key, writer, options = {}) { return bindTwoWay(elementOrSelector, key, writer, options); }, twoWayChecked(elementOrSelector, key, writer, options = {}) { return bindTwoWay(elementOrSelector, key, writer, { ...options, event: options.event || 'change', read: el => !!el.checked }); }, group: bindGroup, debounce(writer, ms) { return (value, event, element) => scheduleWriter(writer, value, event, element, { debounce: ms }); }, throttle(writer, ms) { return (value, event, element) => scheduleWriter(writer, value, event, element, { throttle: ms }); }, list: bindList, template: bindTemplate, delegate, events: bindEvents, component, apply, dispose };
  };

  const getQueryParam = (name) => {
    const search = (window.location && window.location.search) || '';
    const query = search.charAt(0) === '?' ? search.substring(1) : search;
    for (const part of query.split('&')) {
      if (!part) continue;
      const pieces = part.split('=');
      const key = decodeURIComponent(pieces.shift() || '');
      if (key !== name) continue;
      return decodeURIComponent(pieces.join('=').replace(/\+/g, ' '));
    }
    return null;
  };
  const queryOwner = getQueryParam('__bannerlord_htmlui_owner');
  const queryPage = getQueryParam('__bannerlord_htmlui_page');
  const currentOwnerId = queryOwner && queryOwner !== 'framework' ? queryOwner : null;

  window.game = {
    ownerId: queryOwner || null,
    page: { id: queryPage || null, ownerId: queryOwner || null, isConsumer() { return !!queryOwner && queryOwner !== 'framework'; }, lifecycle: 'loading', onLifecycle(handler) { if (typeof handler !== 'function') throw new Error('A lifecycle handler is required.'); lifecycleListeners.add(handler); return () => lifecycleListeners.delete(handler); } },
    scope(ownerId = queryOwner) { return createScope(ownerId); },
    call(name, payload = {}, timeoutMs = 10000) { return requestInternal('command', name, payload, timeoutMs); },
    request(name, payload = {}, timeoutMs = 10000) { return requestInternal('request', name, payload, timeoutMs); },
    on(name, handler) { if (!listeners.has(name)) listeners.set(name, new Set()); listeners.get(name).add(handler); return () => listeners.get(name)?.delete(handler); },
    state: { get(key) { return state.get(key); }, has(key) { return state.has(key); }, subscribe(key, handler) { return window.game.on(`state:${key}`, handler); }, snapshot() { return Object.fromEntries(state.entries()); } },
    lifecycle: { get state() { return state.get('framework.lifecycle'); }, isReady() { return state.get('framework.lifecycle') === 'Ready'; }, window() { return { foreground: state.get('window.foreground'), visible: state.get('window.visible'), minimized: state.get('window.minimized'), bounds: state.get('window.bounds') }; } },
    bind: createBinder(null),
    input: { capture() { return window.game.call('framework.captureInput'); }, release() { return window.game.call('framework.releaseInput'); }, passive() { return window.game.call('framework.passiveInput'); }, setMode(mode) { return window.game.call('framework.setInputMode', { mode }); } },
    errors: { get last() { return lastError; }, on(handler) { if (typeof handler !== 'function') throw new Error('An error handler is required.'); errorListeners.add(handler); return () => errorListeners.delete(handler); } },
    i18n,
    app: null,
    __receive(messageJson) {
      if (runtimeDisposed) return;
      const msg = typeof messageJson === 'string' ? JSON.parse(messageJson) : messageJson;
      if (msg.type === 'response') { const item = pending.get(msg.id); if (!item) return; pending.delete(msg.id); if (item.timer) clearTimeout(item.timer); msg.ok ? item.resolve(msg.payload) : item.reject(new Error(msg.error || 'Request failed')); return; }
      if (msg.type === 'event') { if (msg.name.startsWith('state:')) state.set(msg.name.substring(6), msg.payload); if (msg.name === 'framework.page.lifecycle') { if (msg.payload && msg.payload.state) window.game.page.lifecycle = msg.payload.state; emitPageLifecycle(msg.payload); return; } if (msg.name === 'framework.runtimeError') { emitRuntimeError(msg.payload); return; } emit(msg.name, msg.payload); }
    }
  };
  window.game.app = currentOwnerId ? createScope(currentOwnerId) : { ownerId: null, call: window.game.call, request: window.game.request, on: window.game.on, state: window.game.state, events: { on(name, handler) { return window.game.on(name, handler); } }, page: window.game.page, lifecycle: { get state() { return window.game.page.lifecycle; }, on(handler) { return window.game.page.onLifecycle(handler); } }, errors: window.game.errors, i18n: window.game.i18n, bind: window.game.bind, input: window.game.input, pages: { open(id) { return window.game.call('framework.openPage', { ownerId: 'framework', pageId: id }); }, close() { return window.game.call('framework.closePage', { ownerId: 'framework' }); } }, app: null };
  window.game.app.app = window.game.app;
  window.game.ready = async () => {
    try {
      const snapshot = await window.game.request('framework.getStateSnapshot');
      if (runtimeDisposed) throw new Error('BannerlordHtmlUI runtime was disposed during initialization.');
      for (const [key, value] of Object.entries(snapshot || {})) state.set(key, value);
      if (snapshot && snapshot['framework.page.lifecycle']) { const lifecycle = snapshot['framework.page.lifecycle']; window.game.page.lifecycle = lifecycle.state || lifecycle; emitPageLifecycle(lifecycle); } else window.game.page.lifecycle = 'ready';
      emit('ready', snapshot || {}); return snapshot || {};
    } catch (e) { if (!runtimeDisposed) console.error('BannerlordHtmlUI runtime initialization failed:', e); throw e; }
  };
  window.addEventListener('pagehide', () => { try { i18n.dispose?.(); } catch (_) {} disposeRuntime('BannerlordHtmlUI page unloaded'); }, { once: true });
  window.addEventListener('error', e => { const payload = { kind: 'error', message: String(e.error || e.message || 'Unknown error'), source: e.filename || null, line: e.lineno || 0, column: e.colno || 0 }; emitRuntimeError(payload); console.error('HTML UI error:', payload); });
  window.addEventListener('unhandledrejection', e => { const payload = { kind: 'unhandledrejection', message: String(e.reason || 'Unhandled rejection') }; emitRuntimeError(payload); console.error('HTML UI rejection:', payload); });
  queueMicrotask(() => window.game.ready().catch(() => {}));
})();
