# NavigationCompleted Race

Date: 2026-08-15
Branch: `dev`

## Confirmed behavior

`HtmlUiHost.OnNavigationCompleted()` previously used `Pages.Current` when publishing `framework.page.lifecycle = ready`, but did not associate the callback with the `NavigationId` that started the navigation.

This created a race during rapid page navigation:

```text
Open(A)
  -> NavigationStarting A

Open(B)
  -> NavigationStarting B
  -> Pages.Current = B

NavigationCompleted A arrives late
  -> Host reads Pages.Current
  -> publishes ready for B
```

The issue is limited to rapid overlapping navigation / reload lifecycle reporting. It is independent of the previously fixed Overlay/WebView2 rendering visibility bug.

## Regression harness

The Consumer Test page contains a `Rapid Open/Reload Race` control. It starts two consecutive opens of the same registered page without waiting between them, allowing NavigationCompleted ordering to be exercised.

The harness is intentionally non-invasive: normal F11/F9 behavior is unchanged.

## Production fix

`HtmlUiNavigationRacePatch` now installs a narrow Harmony guard around the existing private host navigation callbacks:

```text
NavigationStarting
  -> record current NavigationId

NavigationCompleted
  -> compare NavigationId
  -> matching current navigation: allow original handler
  -> stale navigation: suppress original handler
```

The original `HtmlUiHost` navigation implementation remains unchanged. This prevents an older completion callback from publishing the newer `Pages.Current` page as `ready` early.

## Current status

- Regression harness: implemented.
- Root cause: confirmed by source audit.
- Production guard: implemented and installed from `SubModule.RegisterFrameworkPages()`.
- CI/status checks: no repository status entries reported for commit `a31a091a9749234b1f07825c23bd6113615bfd68`.
- Real-device regression: still pending; run the `Rapid Open/Reload Race` control before declaring this fixed.
