# NavigationCompleted Race

Date: 2026-08-15
Branch: `dev`

## Confirmed behavior

`HtmlUiHost.OnNavigationCompleted()` currently uses `Pages.Current` when publishing `framework.page.lifecycle = ready`, but does not associate the callback with the `NavigationId` that started the navigation.

This creates a race during rapid page navigation:

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

The Consumer Test page now contains a `Rapid Open/Reload Race` control. It starts two consecutive opens of the same registered page without waiting between them, allowing the NavigationCompleted ordering to be observed.

The harness is intentionally non-invasive: normal F11/F9 behavior is unchanged.

## Planned fix

The host should associate each navigation with its `NavigationId` and target page. `NavigationCompleted` must publish `ready` only when its `NavigationId` matches the currently tracked navigation for the active page.

Do not solve this by hiding the race in Diagnostics or by changing Overlay/WebView2 window styles.

## Current status

- Regression harness: implemented.
- Root cause: confirmed by source audit.
- Production fix: pending a minimal `HtmlUiHost.cs` change so the navigation ID is recorded at `NavigationStarting` and validated at `NavigationCompleted`.
- No real-device test is required yet; existing non-navigation test runs should continue uninterrupted.
