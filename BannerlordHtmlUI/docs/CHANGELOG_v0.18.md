# v0.18

## Consumer UI resource architecture

- Added multiple HTML/CSS/JS content roots.
- Added `HtmlUiService.RegisterContentRoot(id, directory)`.
- Added `HtmlUiPage.ContentRootId`.
- Framework pages continue to use the `framework` root.
- Consumer Mods can keep their UI files inside their own Module directory.
- `runtime.js` is automatically injected into every document; consumer pages no longer need to copy it.
- Top-level navigation is restricted to registered local content-root hosts.
- Page path traversal is rejected and page files are validated before open.
- Fixed consumer integration architecture: one framework Host, multiple consumer content roots.
