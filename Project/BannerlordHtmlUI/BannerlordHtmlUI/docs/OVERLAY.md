# Overlay / HUD (Non-Fullscreen Transparent UI)

Since v0.44 the Framework supports **non-fullscreen, transparent overlay pages** — the building
block for battlefield minimaps, resource HUDs, and skill bars drawn over the live game.

## Why this matters

A "window is always full-screen" overlay cannot show the game around it. To place a small HUD
anywhere on screen while the rest of the game stays visible and interactive, three things are
needed together:

```text
game window (always visible)
   │
   └─ overlay window sized & positioned (e.g. 360x260, bottom-right)
        │
        └─ WebView2 with fully transparent background (alpha = 0)
             │
             └─ HTML/CSS draws only its own translucent panels
```

## Key WebView2 constraint

`CoreWebView2Controller.DefaultBackgroundColor` supports only two meaningful alpha values:

```text
alpha = 0    → fully transparent   (host content shows through)
alpha = 255  → fully opaque
```

Intermediate alpha (1..254) is **not supported**. So you must NOT try to set a 50% translucent
WebView background. Instead:

- WebView background = fully transparent (`alpha=0`),
- each HTML panel itself uses a translucent color, e.g. `background: rgba(0,0,0,.6)`.

## How to configure an overlay page

In the consumer scope:

```csharp
_scope.RegisterPage(new HtmlUiPage("tactical-map", "Minimap/index.html")
{
    ContentRootId = rootId,
    OverlayWidth = 320,        // non-null → non-fullscreen
    OverlayHeight = 320,
    Transparent = true,        // sets WebView2 DefaultBackgroundColor = transparent
    DefaultInputMode = HtmlUiInputMode.Passive, // mouse passes through
    HotReload = true
});
```

- `OverlayWidth` / `OverlayHeight` are `int?`. Leaving them `null` keeps the legacy full-screen
  behavior (window tracks the whole game window).
- `Transparent` sets the WebView2 controller background to `Color.FromArgb(0,0,0,0)`.
  It is harmless for opaque pages (their own body background still renders), so it can be on globally.

## Window placement

`HtmlUiHost.ComputeOverlayBounds()` places overlay pages at a fixed size anchored to the
**bottom-right** of the game window with a 16px margin:

```csharp
x = gameLeft + max(0, gameWidth  - OverlayWidth  - 16)
y = gameTop  + max(0, gameHeight - OverlayHeight - 16)
```

Full-screen pages still use the entire game window rect.

## HTML contract

For transparency to work the page itself must not force an opaque background:

```html
<style>
  html, body { margin:0; padding:0; width:100%; height:100%;
               background: transparent !important; }
  .panel     { background: rgba(0,0,0,.6); }   /* own translucent surface */
</style>
```

The WebView shows through everywhere the HTML background is transparent.

## Input modes for overlays

| Mode      | Behavior                                                         |
|-----------|------------------------------------------------------------------|
| Passive   | whole overlay window passes mouse through to the game (HUD view) |
| Captured  | whole overlay window captures input (full interactive surface)   |
| Hidden    | overlay not shown                                                |

> Note: current v0.44 overlay hit-testing is **window-wide**, not per-element. A Passive overlay
> is fully mouse-transparent (display-only HUD). Per-element hit-testing (click the HUD panel,
> pass through elsewhere) is a planned follow-up.

## Verification checklist

A minimal 400x300 overlay PoC passes when all of these hold:

1. WebView is not full-screen (only 400x300).
2. Window is positioned at the desired corner.
3. HTML background is transparent.
4. The UI card itself is translucent (`rgba`).
5. The Bannerlord game is visible through transparent areas.
6. Mouse clicks outside the overlay keep working on the game.

## Files touched (v0.44)

- `HtmlUiPage` — added `OverlayWidth`, `OverlayHeight`, `Transparent`, `IsOverlay`.
- `HtmlUiHost` — transparent `DefaultBackgroundColor`, `ComputeOverlayBounds()`.
- `HtmlUiConsumerTestMod` — second page is a 360x260 bottom-right translucent HUD demo.
