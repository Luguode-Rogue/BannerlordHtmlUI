# v0.29

- Added the unified `game.app` frontend application API.
- `game.app` groups call/request, state, events, page context, lifecycle, errors, input and page navigation.
- Consumer pages automatically receive a scoped app based on their owner id.
- Framework pages receive an unscoped app for framework operations.
- Kept legacy `game.*` and `game.scope()` APIs for compatibility.
- Added TypeScript declarations and frontend documentation.
