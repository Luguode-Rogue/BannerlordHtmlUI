using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.ScreenSystem;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Native runtime texture probe. It runs before the TPAC service and uses the actual
    /// SpritePart Engine.Texture on the current UI/game thread, then crops the SpriteData region.
    /// </summary>
    [HarmonyPatch(typeof(HtmlUiNativeAtlasAssetService), nameof(HtmlUiNativeAtlasAssetService.ProbeAsync))]
    internal static class HtmlUiNativeRuntimeTexturePatch
    {
        public static bool Prefix(JToken payload, CancellationToken cancellationToken, ref Task<object> __result)
        {
            try
            {
                var request = CaptureRequest(payload);
                if (request == null || request.Texture == null)
                    return true;

                var result = TryExportRuntimeTexture(request, cancellationToken);
                if (result != null)
                {
                    __result = Task.FromResult<object>(result);
                    return false;
                }
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Warn("Native runtime texture probe failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            return true;
        }

        private sealed class Request
        {
            public string BrushName;
            public string SpriteName;
            public string CategoryName;
            public int SheetId;
            public int SheetX;
            public int SheetY;
            public int Width;
            public int Height;
            public Texture Texture;
        }

        private static Request CaptureRequest(JToken payload)
        {
            var brushName = payload?["name"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(brushName)) return null;

            var context = FindActiveUiContext();
            if (context == null) return null;

            var brush = context.GetBrush(brushName);
            if (brush == null || brush.Sprite == null) return null;

            var sprite = brush.Sprite;
            var part = GetPropertyValue(sprite, "SpritePart") ?? GetPropertyValue(sprite, "BaseSprite");
            if (part == null) return null;

            var category = GetPropertyValue(part, "Category");
            var twoDTexture = GetPropertyValue(part, "Texture");
            var platformTexture = GetPropertyValue(twoDTexture, "PlatformTexture");
            var engineTexture = GetPropertyValue(platformTexture, "Texture") as Texture;

            return new Request
            {
                BrushName = brushName,
                SpriteName = GetString(sprite, "Name") ?? string.Empty,
                CategoryName = GetString(category, "Name") ?? string.Empty,
                SheetId = GetInt(part, "SheetID") ?? -1,
                SheetX = GetInt(part, "SheetX") ?? 0,
                SheetY = GetInt(part, "SheetY") ?? 0,
                Width = GetInt(sprite, "Width") ?? GetInt(part, "Width") ?? 0,
                Height = GetInt(sprite, "Height") ?? GetInt(part, "Height") ?? 0,
                Texture = engineTexture
            };
        }

        private static object TryExportRuntimeTexture(Request request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cacheRoot = Path.Combine(Path.GetTempPath(), "BannerlordHtmlUI", "NativeAtlasCache");
            Directory.CreateDirectory(cacheRoot);

            var key = SafeHash(request.BrushName + "|" + request.SpriteName + "|" + request.SheetX + ":" + request.SheetY + ":" + request.Width + ":" + request.Height + "|runtime-save");
            var atlasPath = Path.Combine(cacheRoot, "runtime-atlas-" + key + ".png");
            var spritePath = Path.Combine(cacheRoot, "runtime-sprite-" + key + ".png");

            if (!IsValidPng(atlasPath))
            {
                try { request.Texture.PreloadTexture(true); } catch (Exception ex) { HtmlUiLogger.Warn("Native texture preload failed: " + ex.GetType().Name + ": " + ex.Message); }
                try { request.Texture.SetTextureAsAlwaysValid(); } catch { }

                HtmlUiLogger.Info("Native runtime texture export: name=" + (request.Texture.Name ?? "<unnamed>") + " size=" + request.Texture.Width + "x" + request.Texture.Height);
                request.Texture.SaveToFile(atlasPath);
            }

            if (!IsValidPng(atlasPath))
            {
                HtmlUiLogger.Warn("Native runtime texture SaveToFile produced no valid PNG: " + atlasPath);
                return null;
            }

            if (!File.Exists(spritePath))
            {
                using (var bitmap = new Bitmap(atlasPath))
                {
                    if (request.SheetX < 0 || request.SheetY < 0 || request.SheetX >= bitmap.Width || request.SheetY >= bitmap.Height)
                        return null;

                    var width = Math.Min(Math.Max(request.Width, 1), bitmap.Width - request.SheetX);
                    var height = Math.Min(Math.Max(request.Height, 1), bitmap.Height - request.SheetY);
                    using (var crop = bitmap.Clone(new Rectangle(request.SheetX, request.SheetY, width, height), PixelFormat.Format32bppArgb))
                        crop.Save(spritePath, ImageFormat.Png);
                }
            }

            if (!IsValidPng(spritePath)) return null;

            const string publicHost = "https://bannerlord-htmlui-framework-native-atlas-cache.local";
            return new
            {
                available = true,
                brushName = request.BrushName,
                spriteName = request.SpriteName,
                categoryName = request.CategoryName,
                sheetId = request.SheetId,
                sheetX = request.SheetX,
                sheetY = request.SheetY,
                width = request.Width,
                height = request.Height,
                provider = "runtime-engine-texture-save-game-thread",
                atlasName = request.Texture.Name,
                atlasPath = atlasPath,
                atlasUrl = publicHost + "/" + Path.GetFileName(atlasPath),
                spriteUrl = publicHost + "/" + Path.GetFileName(spritePath),
                status = "ready",
                error = (string)null,
                atlasWidth = request.Texture.Width,
                atlasHeight = request.Texture.Height
            };
        }

        private static UIContext FindActiveUiContext()
        {
            var top = ScreenManager.TopScreen;
            if (top == null) return null;

            foreach (var layer in top.Layers)
                if (layer is GauntletLayer gauntlet && gauntlet.IsActive)
                    return gauntlet.UIContext;

            foreach (var layer in top.Layers)
                if (layer is GauntletLayer fallback)
                    return fallback.UIContext;

            return null;
        }

        private static object GetPropertyValue(object instance, string name)
        {
            if (instance == null) return null;
            var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property == null) return null;
            try { return property.GetValue(instance, null); } catch { return null; }
        }

        private static string GetString(object instance, string name) => GetPropertyValue(instance, name) as string;

        private static int? GetInt(object instance, string name)
        {
            var value = GetPropertyValue(instance, name);
            if (value == null) return null;
            try { return Convert.ToInt32(value); } catch { return null; }
        }

        private static bool IsValidPng(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length < 8) return false;
            using (var stream = File.OpenRead(path))
            {
                var sig = new byte[8];
                if (stream.Read(sig, 0, 8) != 8) return false;
                return sig[0] == 0x89 && sig[1] == 0x50 && sig[2] == 0x4E && sig[3] == 0x47 && sig[4] == 0x0D && sig[5] == 0x0A && sig[6] == 0x1A && sig[7] == 0x0A;
            }
        }

        private static string SafeHash(string value)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var ch in value ?? string.Empty)
                    hash = (hash ^ ch) * 16777619u;
                return hash.ToString("x8");
            }
        }
    }
}
