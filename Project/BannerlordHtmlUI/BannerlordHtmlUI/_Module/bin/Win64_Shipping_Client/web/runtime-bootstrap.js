(() => {
  const key = '__bannerlordHtmlUiRuntime';
  const runtime = window[key] = window[key] || {};
  if (runtime.gameAssignmentBridgeInstalled) return;

  const existingGame = window.game;
  let currentGame = existingGame;
  const queuedSubscriptions = new Set();

  const attachQueuedSubscriptions = game => {
    if (!game || typeof game.on !== 'function') return;
    for (const subscription of queuedSubscriptions) {
      if (!subscription.active || subscription.detach) continue;
      try {
        subscription.detach = game.on(subscription.name, subscription.handler);
      } catch (error) {
        console.error('BannerlordHtmlUI runtime subscription bridge failed:', error);
      }
    }
  };

  const proxyGame = {
    on(name, handler) {
      if (typeof handler !== 'function') throw new Error('A listener handler is required.');
      const subscription = {
        name,
        handler,
        active: true,
        detach: null
      };
      queuedSubscriptions.add(subscription);
      if (currentGame && currentGame !== proxyGame && typeof currentGame.on === 'function') {
        try { subscription.detach = currentGame.on(name, handler); } catch (error) {
          console.error('BannerlordHtmlUI runtime subscription bridge failed:', error);
        }
      }
      return () => {
        if (!subscription.active) return;
        subscription.active = false;
        queuedSubscriptions.delete(subscription);
        try { subscription.detach?.(); } catch (_) {}
        subscription.detach = null;
      };
    }
  };

  try {
    Object.defineProperty(window, 'game', {
      configurable: true,
      enumerable: true,
      get() {
        return currentGame || proxyGame;
      },
      set(value) {
        currentGame = value;
        attachQueuedSubscriptions(value);
      }
    });
    currentGame = existingGame || proxyGame;
  } catch (error) {
    runtime.gameAssignmentBridgeInstalled = false;
    throw new Error('BannerlordHtmlUI could not install the runtime game-assignment bridge: ' + error.message);
  }

  runtime.gameAssignmentBridgeInstalled = true;
  runtime.runtimeCoreLoaded = false;
})();