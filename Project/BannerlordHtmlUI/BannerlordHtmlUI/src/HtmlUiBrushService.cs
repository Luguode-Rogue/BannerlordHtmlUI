using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.ScreenSystem;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiBrushService
    {
        private static readonly string[] ProbeStates = { "Default", "Hovered", "Pressed", "Disabled" };

        public static object GetContextSnapshot()
        {
            var context = FindActiveUiContext();
            if (context == null) return new { available = false, reason = "No active Gauntlet UIContext was found." };
            var brushes = context.Brushes?.ToList() ?? new List<Brush>();
            return new { available = true, contextName = context.Name, brushCount = brushes.Count };
        }

        public static object ListBrushes(JToken payload)
        {
            var context = FindActiveUiContext();
            if (context == null) return new { available = false, contextName = (string)null, total = 0, returned = 0, offset = 0, hasMore = false, brushes = Array.Empty<object>() };

            var filter = payload?["filter"]?.Value<string>();
            var limit = payload?["limit"]?.Value<int>() ?? 50;
            var offset = payload?["offset"]?.Value<int>() ?? 0;
            if (limit < 1) limit = 1;
            if (limit > 100) limit = 100;
            if (offset < 0) offset = 0;

            IEnumerable<Brush> query = context.Brushes ?? Enumerable.Empty<Brush>();
            if (!string.IsNullOrWhiteSpace(filter))
                query = query.Where(brush => brush != null && !string.IsNullOrWhiteSpace(brush.Name) && brush.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);

            // Keep the existing rich list payload, but fetch it in deterministic pages.
            var matching = query.Where(brush => brush != null).OrderBy(brush => brush.Name, StringComparer.OrdinalIgnoreCase);
            var total = matching.Count();
            var result = matching.Skip(offset).Take(limit).Select(brush => new
            {
                name = brush.Name,
                fontSize = brush.FontSize,
                fontStyle = brush.FontStyle.ToString(),
                textHorizontalAlignment = brush.TextHorizontalAlignment.ToString(),
                textVerticalAlignment = brush.TextVerticalAlignment.ToString(),
                color = ColorToHex(brush.Color),
                alpha = brush.AlphaFactor,
                fontColor = ColorToHex(brush.FontColor),
                textAlpha = brush.TextAlphaFactor,
                sprite = SpriteSnapshot(brush.Sprite, false),
                styleNames = brush.Styles == null ? Array.Empty<string>() : brush.Styles.Select(style => style?.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray()
            }).Cast<object>().ToArray();

            return new
            {
                available = true,
                contextName = context.Name,
                total,
                returned = result.Length,
                offset,
                hasMore = offset + result.Length < total,
                brushes = result
            };
        }

        public static object GetBrush(JToken payload)
        {
            var brush = ResolveBrush(payload);
            return new { available = true, contextName = FindActiveUiContext().Name, brush = BrushSnapshot(brush) };
        }

        public static object GetBrushResource(JToken payload)
        {
            var brush = ResolveBrush(payload);
            var state = payload?["state"]?.Value<string>();
            var resolved = string.IsNullOrWhiteSpace(state) || string.Equals(state, "Default", StringComparison.OrdinalIgnoreCase)
                ? brush.GetStyleOrDefault("Default")
                : brush.GetStyleOrDefault(state);
            return new
            {
                available = true,
                brushName = brush.Name,
                state = string.IsNullOrWhiteSpace(state) ? "Default" : state,
                sprite = SpriteSnapshot(brush.Sprite, true),
                style = resolved == null ? null : StyleSnapshot(resolved, true)
            };
        }

        public static object GetBrushState(JToken payload)
        {
            var brush = ResolveBrush(payload);
            var state = payload?["state"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(state)) throw new ArgumentException("Brush state is required.");
            var exact = brush.GetStyle(state);
            var resolved = brush.GetStyleOrDefault(state);
            if (resolved == null) throw new InvalidOperationException("Brush has no resolvable style for state '" + state + "'.");
            return new { available = true, brushName = brush.Name, state, exactStyle = exact != null, resolvedStyleName = resolved.Name, style = StyleSnapshot(resolved, false) };
        }

        public static object GetBrushStateProbe(JToken payload)
        {
            var brush = ResolveBrush(payload);
            return new
            {
                brushName = brush.Name,
                states = ProbeStates.Select(state =>
                {
                    var exact = brush.GetStyle(state);
                    var resolved = brush.GetStyleOrDefault(state);
                    return new { state, exactStyle = exact != null, resolvedStyleName = resolved?.Name, style = resolved == null ? null : StyleSnapshot(resolved, false) };
                }).Cast<object>().ToArray()
            };
        }

        private static Brush ResolveBrush(JToken payload)
        {
            var name = payload?["name"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Brush name is required.");
            var context = FindActiveUiContext();
            if (context == null) throw new InvalidOperationException("No active Gauntlet UIContext was found.");
            var brush = context.GetBrush(name);
            if (brush == null) throw new KeyNotFoundException("Brush not found: " + name);
            return brush;
        }

        private static object BrushSnapshot(Brush brush)
        {
            return new
            {
                name = brush.Name,
                fontSize = brush.FontSize,
                fontStyle = brush.FontStyle.ToString(),
                textHorizontalAlignment = brush.TextHorizontalAlignment.ToString(),
                textVerticalAlignment = brush.TextVerticalAlignment.ToString(),
                transitionDuration = brush.TransitionDuration,
                color = ColorToHex(brush.Color),
                colorFactor = brush.ColorFactor,
                alpha = brush.AlphaFactor,
                hue = brush.HueFactor,
                saturation = brush.SaturationFactor,
                value = brush.ValueFactor,
                horizontalFlip = brush.HorizontalFlip,
                verticalFlip = brush.VerticalFlip,
                fontColor = ColorToHex(brush.FontColor),
                textColorFactor = brush.TextColorFactor,
                textAlpha = brush.TextAlphaFactor,
                textHue = brush.TextHueFactor,
                textSaturation = brush.TextSaturationFactor,
                textValue = brush.TextValueFactor,
                sprite = SpriteSnapshot(brush.Sprite, false),
                layers = brush.Layers == null ? Array.Empty<object>() : brush.Layers.Where(layer => layer != null).Select(layer => LayerSnapshot(layer, false)).Cast<object>().ToArray(),
                styles = brush.Styles == null ? Array.Empty<object>() : brush.Styles.Where(style => style != null).Select(style => StyleSnapshot(style, false)).Cast<object>().ToArray()
            };
        }

        private static object LayerSnapshot(IBrushLayerData layer, bool includeResource)
        {
            return new
            {
                name = layer.Name,
                hidden = layer.IsHidden,
                color = ColorToHex(layer.Color),
                colorFactor = layer.ColorFactor,
                alpha = layer.AlphaFactor,
                hue = layer.HueFactor,
                saturation = layer.SaturationFactor,
                value = layer.ValueFactor,
                sprite = SpriteSnapshot(layer.Sprite, includeResource)
            };
        }

        private static object StyleSnapshot(Style style, bool includeResource)
        {
            var layers = style.GetLayers() ?? Array.Empty<StyleLayer>();
            return new
            {
                name = style.Name,
                fontSize = style.FontSize,
                fontStyle = style.FontStyle.ToString(),
                fontColor = ColorToHex(style.FontColor),
                textGlowColor = ColorToHex(style.TextGlowColor),
                textOutlineColor = ColorToHex(style.TextOutlineColor),
                textOutlineAmount = style.TextOutlineAmount,
                textGlowRadius = style.TextGlowRadius,
                textBlur = style.TextBlur,
                textShadowOffset = style.TextShadowOffset,
                textShadowAngle = style.TextShadowAngle,
                textColorFactor = style.TextColorFactor,
                textAlphaFactor = style.TextAlphaFactor,
                textHueFactor = style.TextHueFactor,
                textSaturationFactor = style.TextSaturationFactor,
                textValueFactor = style.TextValueFactor,
                animationMode = style.AnimationMode.ToString(),
                animationToPlayOnBegin = style.AnimationToPlayOnBegin,
                layers = layers.Where(layer => layer != null).Select(layer => LayerSnapshot(layer, includeResource)).Cast<object>().ToArray()
            };
        }

        private static UIContext FindActiveUiContext()
        {
            try
            {
                var topScreen = ScreenManager.TopScreen;
                if (topScreen == null) return null;
                foreach (var layer in topScreen.Layers) if (layer is GauntletLayer gauntletLayer && gauntletLayer.IsActive) return gauntletLayer.UIContext;
                foreach (var layer in topScreen.Layers) if (layer is GauntletLayer gauntletLayer) return gauntletLayer.UIContext;
            }
            catch (Exception ex) { HtmlUiLogger.Warn("Brush service failed to locate Gauntlet UIContext: " + ex.Message); }
            return null;
        }

        private static object SpriteSnapshot(object sprite, bool includeResource) => sprite == null ? null : HtmlUiBrushResourceService.CreateSpriteSnapshot(sprite, includeResource);

        private static string ColorToHex(object color)
        {
            if (color == null) return null;
            var r = GetComponent(color, "R"); var g = GetComponent(color, "G"); var b = GetComponent(color, "B"); var a = GetComponent(color, "A");
            if (!r.HasValue || !g.HasValue || !b.HasValue) return color.ToString();
            return "#" + ToByte(r.Value).ToString("X2") + ToByte(g.Value).ToString("X2") + ToByte(b.Value).ToString("X2") + (a.HasValue ? ToByte(a.Value) : (byte)255).ToString("X2");
        }

        private static double? GetComponent(object value, string name)
        {
            var property = value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property == null) return null;
            try { var raw = property.GetValue(value, null); return raw == null ? (double?)null : Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture); } catch { return null; }
        }

        private static byte ToByte(double value)
        {
            if (value <= 1.0) value *= 255.0;
            return (byte)Math.Max(0, Math.Min(255, Math.Round(value)));
        }
    }
}
