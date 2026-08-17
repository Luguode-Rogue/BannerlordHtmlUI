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
        public static object GetContextSnapshot()
        {
            var context = FindActiveUiContext();
            if (context == null)
            {
                return new
                {
                    available = false,
                    reason = "No active Gauntlet UIContext was found."
                };
            }

            var brushes = context.Brushes?.ToList() ?? new List<Brush>();
            return new
            {
                available = true,
                contextName = context.Name,
                brushCount = brushes.Count
            };
        }

        public static object ListBrushes(JToken payload)
        {
            var context = FindActiveUiContext();
            if (context == null)
            {
                return new
                {
                    available = false,
                    contextName = (string)null,
                    brushes = Array.Empty<object>()
                };
            }

            var filter = payload?["filter"]?.Value<string>();
            var limit = payload?["limit"]?.Value<int>() ?? 200;
            if (limit < 1) limit = 1;
            if (limit > 500) limit = 500;

            IEnumerable<Brush> query = context.Brushes ?? Enumerable.Empty<Brush>();
            if (!string.IsNullOrWhiteSpace(filter))
            {
                query = query.Where(brush =>
                    brush != null &&
                    !string.IsNullOrWhiteSpace(brush.Name) &&
                    brush.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var result = query
                .Where(brush => brush != null)
                .OrderBy(brush => brush.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(brush => new
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
                    sprite = SpriteSnapshot(brush.Sprite),
                    layers = brush.Layers == null
                        ? Array.Empty<object>()
                        : brush.Layers
                            .Where(layer => layer != null)
                            .Select(layer => new
                            {
                                name = layer.Name,
                                hidden = layer.IsHidden,
                                color = ColorToHex(layer.Color),
                                colorFactor = layer.ColorFactor,
                                alpha = layer.AlphaFactor,
                                hue = layer.HueFactor,
                                saturation = layer.SaturationFactor,
                                value = layer.ValueFactor,
                                sprite = SpriteSnapshot(layer.Sprite)
                            })
                            .Cast<object>()
                            .ToArray()
                })
                .Cast<object>()
                .ToArray();

            return new
            {
                available = true,
                contextName = context.Name,
                total = context.Brushes?.Count() ?? 0,
                returned = result.Length,
                brushes = result
            };
        }

        public static object GetBrush(JToken payload)
        {
            var name = payload?["name"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Brush name is required.");

            var context = FindActiveUiContext();
            if (context == null)
                throw new InvalidOperationException("No active Gauntlet UIContext was found.");

            var brush = context.GetBrush(name);
            if (brush == null)
                throw new KeyNotFoundException("Brush not found: " + name);

            return new
            {
                available = true,
                contextName = context.Name,
                brush = new
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
                    fontColor = ColorToHex(brush.FontColor),
                    textColorFactor = brush.TextColorFactor,
                    textAlpha = brush.TextAlphaFactor,
                    textHue = brush.TextHueFactor,
                    textSaturation = brush.TextSaturationFactor,
                    textValue = brush.TextValueFactor,
                    sprite = SpriteSnapshot(brush.Sprite),
                    layers = brush.Layers == null
                        ? Array.Empty<object>()
                        : brush.Layers
                            .Where(layer => layer != null)
                            .Select(layer => new
                            {
                                name = layer.Name,
                                hidden = layer.IsHidden,
                                color = ColorToHex(layer.Color),
                                colorFactor = layer.ColorFactor,
                                alpha = layer.AlphaFactor,
                                hue = layer.HueFactor,
                                saturation = layer.SaturationFactor,
                                value = layer.ValueFactor,
                                overlayMask = layer.UseOverlayAlphaAsMask,
                                sprite = SpriteSnapshot(layer.Sprite)
                            })
                            .Cast<object>()
                            .ToArray()
                }
            };
        }

        private static UIContext FindActiveUiContext()
        {
            try
            {
                var topScreen = ScreenManager.TopScreen;
                if (topScreen == null)
                    return null;

                foreach (var layer in topScreen.Layers)
                {
                    if (layer is GauntletLayer gauntletLayer && gauntletLayer.IsActive)
                        return gauntletLayer.UIContext;
                }

                foreach (var layer in topScreen.Layers)
                {
                    if (layer is GauntletLayer gauntletLayer)
                        return gauntletLayer.UIContext;
                }
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Warn("Brush service failed to locate Gauntlet UIContext: " + ex.Message);
            }

            return null;
        }

        private static object SpriteSnapshot(object sprite)
        {
            if (sprite == null) return null;

            var type = sprite.GetType();
            return new
            {
                type = type.FullName,
                name = GetProperty<string>(sprite, "Name"),
                width = GetProperty<int?>(sprite, "Width"),
                height = GetProperty<int?>(sprite, "Height")
            };
        }

        private static string ColorToHex(object color)
        {
            if (color == null) return null;

            var r = GetComponent(color, "R");
            var g = GetComponent(color, "G");
            var b = GetComponent(color, "B");
            var a = GetComponent(color, "A");
            if (!r.HasValue || !g.HasValue || !b.HasValue)
                return color.ToString();

            var rr = ToByte(r.Value);
            var gg = ToByte(g.Value);
            var bb = ToByte(b.Value);
            var aa = a.HasValue ? ToByte(a.Value) : (byte)255;
            return "#" + rr.ToString("X2") + gg.ToString("X2") + bb.ToString("X2") + aa.ToString("X2");
        }

        private static double? GetComponent(object value, string name)
        {
            var property = value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property == null) return null;

            try
            {
                var raw = property.GetValue(value, null);
                if (raw == null) return null;
                return Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static byte ToByte(double value)
        {
            if (value <= 1.0)
                value *= 255.0;
            return (byte)Math.Max(0, Math.Min(255, Math.Round(value)));
        }

        private static T GetProperty<T>(object instance, string propertyName)
        {
            var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null) return default(T);

            try
            {
                var value = property.GetValue(instance, null);
                if (value == null) return default(T);
                return (T)value;
            }
            catch
            {
                return default(T);
            }
        }
    }
}
