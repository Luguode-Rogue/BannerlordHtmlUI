using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using TaleWorlds.Engine;
using TaleWorlds.TwoDimension;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiBrushResourceService
    {
        private const string ContentRootId = "framework-brush-cache";
        private static readonly ConcurrentDictionary<string, string> CachedUrls = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> FailedIdentities = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string _cacheDirectory;
        private static string _publicHost;
        private static bool _initialized;

        public static void Initialize(HtmlUiHost host)
        {
            if (_initialized || host == null) return;
            _cacheDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BannerlordHtmlUI", "BrushCache");
            Directory.CreateDirectory(_cacheDirectory);
            host.RegisterContentRoot(ContentRootId, _cacheDirectory);
            _publicHost = "https://bannerlord-htmlui-" + SanitizeHostPart(ContentRootId) + ".local";
            _initialized = true;
            HtmlUiLogger.Info("Native Brush resource cache initialized: " + _cacheDirectory);
        }

        public static void Dispose()
        {
            _initialized = false;
            _cacheDirectory = null;
            _publicHost = null;
            CachedUrls.Clear();
            FailedIdentities.Clear();
        }

        public static object CreateSpriteSnapshot(object sprite, bool includeResource)
        {
            if (sprite == null) return null;

            var spritePart = GetPropertyValue(sprite, "SpritePart") ?? GetPropertyValue(sprite, "BaseSprite");
            var textureWrapper = GetPropertyValue(spritePart ?? sprite, "Texture");
            var spriteName = GetPropertyValue<string>(sprite, "Name");
            var textureName = GetPropertyValue<string>(textureWrapper, "Name");
            var resourceName = !string.IsNullOrWhiteSpace(spriteName) ? spriteName : textureName;

            var width = GetInt(sprite, "Width") ?? GetInt(spritePart, "Width") ?? GetInt(textureWrapper, "Width") ?? 0;
            var height = GetInt(sprite, "Height") ?? GetInt(spritePart, "Height") ?? GetInt(textureWrapper, "Height") ?? 0;
            var sheetX = GetInt(spritePart, "SheetX") ?? 0;
            var sheetY = GetInt(spritePart, "SheetY") ?? 0;
            var sheetWidth = GetInt(spritePart, "SheetWidth") ?? GetInt(textureWrapper, "Width") ?? width;
            var sheetHeight = GetInt(spritePart, "SheetHeight") ?? GetInt(textureWrapper, "Height") ?? height;
            var sheetId = GetInt(spritePart, "SheetID") ?? -1;

            string url = null;
            string cacheError = null;
            object pixelDiagnostics = null;
            object variantUrls = null;
            string exportSource = null;
            string runtimeTexturePath = null;
            if (includeResource)
            {
                var identity = (resourceName ?? string.Empty) + "|" + sheetId + "|" + sheetWidth + "x" + sheetHeight;
                if (FailedIdentities.TryGetValue(identity, out var previousError))
                {
                    cacheError = previousError;
                }
                else
                {
                    try
                    {
                        var export = EnsureTextureCached(spritePart, resourceName, sheetWidth, sheetHeight, sheetX, sheetY, width, height);
                        url = export.FullTextureUrl;
                        pixelDiagnostics = export.Diagnostics;
                        variantUrls = export.Variants;
                        exportSource = export.Source;
                        runtimeTexturePath = export.RuntimeTexturePath;
                    }
                    catch (Exception ex)
                    {
                        cacheError = ex.GetType().Name + ": " + ex.Message;
                        FailedIdentities[identity] = cacheError;
                        HtmlUiLogger.Warn("Brush sprite cache failed: " + cacheError);
                    }
                }
            }

            return new
            {
                type = sprite.GetType().FullName,
                name = spriteName,
                width,
                height,
                resourceUrl = url,
                resourceError = cacheError,
                resourceSource = exportSource,
                runtimeTexturePath,
                textureName,
                resourceName,
                sheetId,
                sheetX,
                sheetY,
                sheetWidth,
                sheetHeight,
                minU = GetFloat(spritePart, "MinU"),
                minV = GetFloat(spritePart, "MinV"),
                maxU = GetFloat(spritePart, "MaxU"),
                maxV = GetFloat(spritePart, "MaxV"),
                pixelDiagnostics,
                variantUrls
            };
        }

        private sealed class TextureExportResult
        {
            public string FullTextureUrl;
            public object Diagnostics;
            public object Variants;
            public string Source;
            public string RuntimeTexturePath;
        }

        private static TextureExportResult EnsureTextureCached(object spritePart, string resourceName, int sheetWidth, int sheetHeight, int sheetX, int sheetY, int spriteWidth, int spriteHeight)
        {
            if (!_initialized)
                throw new InvalidOperationException("Native Brush resource cache is not initialized.");
            if (spritePart == null)
                throw new InvalidOperationException("SpritePart is unavailable.");

            var textureWrapper = GetPropertyValue(spritePart, "Texture");
            if (textureWrapper == null)
                throw new InvalidOperationException("SpritePart.Texture is null.");

            var platformTexture = GetPropertyValue(textureWrapper, "PlatformTexture");
            var runtimePath = "SpritePart.Texture=" + textureWrapper.GetType().FullName
                + "; PlatformTexture=" + (platformTexture == null ? "<null>" : platformTexture.GetType().FullName);

            if (platformTexture == null)
                throw new InvalidOperationException("SpritePart.Texture.PlatformTexture is null. " + runtimePath);

            TaleWorlds.Engine.Texture texture = null;
            string source = "SpritePart.Texture.PlatformTexture";

            if (platformTexture is TaleWorlds.Engine.Texture direct)
            {
                texture = direct;
                source = "SpritePart.Texture.PlatformTexture [Engine.Texture]";
            }
            else
            {
                var engineTexture = GetPropertyValue(platformTexture, "Texture");
                if (engineTexture is TaleWorlds.Engine.Texture wrapped)
                {
                    texture = wrapped;
                    source = "SpritePart.Texture.PlatformTexture.Texture [EngineTexture]";
                }
                else
                {
                    runtimePath += "; PlatformTexture.Texture=" + (engineTexture == null ? "<null>" : engineTexture.GetType().FullName);
                }
            }

            if (texture == null)
            {
                var category = GetPropertyValue(spritePart, "Category");
                var sheets = category == null ? null : GetPropertyValue(category, "SpriteSheets") as IEnumerable;
                var sheetId = GetInt(spritePart, "SheetID") ?? -1;
                runtimePath += "; Category=" + (category == null ? "<null>" : category.GetType().FullName)
                    + "; SpriteSheets=" + (sheets == null ? "<null>" : sheets.GetType().FullName)
                    + "; SheetID=" + sheetId;

                if (sheets != null && sheetId >= 0)
                {
                    var index = 0;
                    foreach (var sheet in sheets)
                    {
                        if (index == sheetId)
                        {
                            runtimePath += "; SheetObject=" + (sheet == null ? "<null>" : sheet.GetType().FullName);
                            var sheetPlatform = GetPropertyValue(sheet, "PlatformTexture");
                            runtimePath += "; Sheet.PlatformTexture=" + (sheetPlatform == null ? "<null>" : sheetPlatform.GetType().FullName);
                            if (sheetPlatform is TaleWorlds.Engine.Texture sheetEngine)
                            {
                                texture = sheetEngine;
                                source = "SpriteCategory.SpriteSheets[SheetID].PlatformTexture [Engine.Texture]";
                            }
                            else
                            {
                                var sheetEngineTexture = GetPropertyValue(sheetPlatform, "Texture");
                                if (sheetEngineTexture is TaleWorlds.Engine.Texture sheetWrapped)
                                {
                                    texture = sheetWrapped;
                                    source = "SpriteCategory.SpriteSheets[SheetID].PlatformTexture.Texture [EngineTexture]";
                                }
                            }
                            break;
                        }
                        index++;
                    }
                }
            }

            if (texture == null)
                throw new InvalidOperationException("Unable to unwrap the SpritePart's loaded PlatformTexture. " + runtimePath);

            try { texture.PreloadTexture(true); } catch (Exception ex) { HtmlUiLogger.Debug("Native Brush texture preload failed: " + ex.Message); }
            try { texture.SetTextureAsAlwaysValid(); } catch { }

            if (!texture.IsValid)
                throw new InvalidOperationException("Resolved native engine texture is invalid. source='" + source + "'. " + runtimePath);

            var width = texture.Width > 0 ? texture.Width : sheetWidth;
            var height = texture.Height > 0 ? texture.Height : sheetHeight;
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("Resolved native engine texture dimensions are unavailable. source='" + source + "'. " + runtimePath);

            var pixelCount = checked(width * height);
            var raw = new byte[checked(pixelCount * 4)];
            texture.GetPixelData(raw);

            var diagnostics = DiagnosePixels(raw, width, height, source);
            HtmlUiLogger.Info("Brush pixel diagnostics: " + diagnostics);

            var identity = resourceName + "|" + source + "|" + width + "x" + height;
            var hash = Sha256(identity).Substring(0, 24);
            var filename = "sprite-" + hash + ".png";
            var path = System.IO.Path.Combine(_cacheDirectory, filename);

            WritePng(path, raw, width, height, PixelLayout.Bgra);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new IOException("Texture pixel readback did not produce a valid PNG: " + path);

            var variantPayload = new JObjectLike();
            if (spriteWidth > 0 && spriteHeight > 0 && sheetX >= 0 && sheetY >= 0 && sheetX + spriteWidth <= width && sheetY + spriteHeight <= height)
            {
                var crop = Crop(raw, width, height, sheetX, sheetY, spriteWidth, spriteHeight);
                var basePrefix = "sprite-crop-" + hash;
                var rgbaPath = System.IO.Path.Combine(_cacheDirectory, basePrefix + "-rgba.png");
                var bgraPath = System.IO.Path.Combine(_cacheDirectory, basePrefix + "-bgra.png");
                var argbPath = System.IO.Path.Combine(_cacheDirectory, basePrefix + "-argb.png");
                WritePng(rgbaPath, crop, spriteWidth, spriteHeight, PixelLayout.Rgba);
                WritePng(bgraPath, crop, spriteWidth, spriteHeight, PixelLayout.Bgra);
                WritePng(argbPath, crop, spriteWidth, spriteHeight, PixelLayout.Argb);
                variantPayload.Rgba = _publicHost + "/" + System.IO.Path.GetFileName(rgbaPath);
                variantPayload.Bgra = _publicHost + "/" + System.IO.Path.GetFileName(bgraPath);
                variantPayload.Argb = _publicHost + "/" + System.IO.Path.GetFileName(argbPath);
            }

            return new TextureExportResult
            {
                FullTextureUrl = _publicHost + "/" + filename,
                Diagnostics = diagnostics,
                Variants = variantPayload,
                Source = source,
                RuntimeTexturePath = runtimePath
            };
        }

        private enum PixelLayout
        {
            Rgba,
            Bgra,
            Argb
        }

        private static string DiagnosePixels(byte[] raw, int width, int height, string source)
        {
            if (raw == null || raw.Length == 0) return "empty, source=" + source;
            var expected = checked(width * height * 4);
            var samples = Math.Min(raw.Length, 4 * 4096);
            long[] min = { 255, 255, 255, 255 };
            long[] max = { 0, 0, 0, 0 };
            long[] sum = { 0, 0, 0, 0 };
            long nonZeroAlpha = 0;
            for (var i = 0; i + 3 < samples; i += 4)
            {
                for (var c = 0; c < 4; c++)
                {
                    var v = raw[i + c];
                    if (v < min[c]) min[c] = v;
                    if (v > max[c]) max[c] = v;
                    sum[c] += v;
                }
                if (raw[i + 3] != 0) nonZeroAlpha++;
            }
            var count = Math.Max(1, samples / 4);
            var first = string.Join(",", raw.Take(Math.Min(32, raw.Length)).Select(b => b.ToString("X2")));
            return "source=" + source
                + ", dimensions=" + width + "x" + height
                + ", bytes=" + raw.Length
                + ", expectedBytes=" + expected
                + ", samplePixels=" + count
                + ", min=[" + string.Join(",", min) + "]"
                + ", max=[" + string.Join(",", max) + "]"
                + ", avg=[" + string.Join(",", sum.Select(v => (v / (double)count).ToString("F1", System.Globalization.CultureInfo.InvariantCulture))) + "]"
                + ", alphaNonZero=" + nonZeroAlpha
                + ", first=" + first;
        }

        private static byte[] Crop(byte[] raw, int width, int height, int x, int y, int cropWidth, int cropHeight)
        {
            var output = new byte[checked(cropWidth * cropHeight * 4)];
            var srcRow = width * 4;
            var dstRow = cropWidth * 4;
            for (var row = 0; row < cropHeight; row++)
                Buffer.BlockCopy(raw, (y + row) * srcRow + x * 4, output, row * dstRow, dstRow);
            return output;
        }

        private static void WritePng(string path, byte[] raw, int width, int height, PixelLayout layout)
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                var rectangle = new Rectangle(0, 0, width, height);
                var data = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var stride = Math.Abs(data.Stride);
                    var rowBytes = width * 4;
                    var converted = new byte[rowBytes * height];
                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var source = (y * width + x) * 4;
                            var target = y * rowBytes + x * 4;
                            byte r, g, b, a;
                            switch (layout)
                            {
                                case PixelLayout.Rgba:
                                    r = raw[source]; g = raw[source + 1]; b = raw[source + 2]; a = raw[source + 3];
                                    break;
                                case PixelLayout.Argb:
                                    a = raw[source]; r = raw[source + 1]; g = raw[source + 2]; b = raw[source + 3];
                                    break;
                                default:
                                    b = raw[source]; g = raw[source + 1]; r = raw[source + 2]; a = raw[source + 3];
                                    break;
                            }
                            converted[target] = b;
                            converted[target + 1] = g;
                            converted[target + 2] = r;
                            converted[target + 3] = a;
                        }
                    }

                    if (stride == rowBytes)
                        Marshal.Copy(converted, 0, data.Scan0, converted.Length);
                    else
                        for (var y = 0; y < height; y++)
                            Marshal.Copy(converted, y * rowBytes, IntPtr.Add(data.Scan0, y * data.Stride), rowBytes);
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private sealed class JObjectLike
        {
            public string Rgba;
            public string Bgra;
            public string Argb;
        }

        private static object GetPropertyValue(object instance, string name)
        {
            if (instance == null) return null;
            var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property == null) return null;
            try { return property.GetValue(instance, null); } catch { return null; }
        }

        private static T GetPropertyValue<T>(object instance, string name)
        {
            var value = GetPropertyValue(instance, name);
            if (value == null) return default(T);
            try { return (T)value; } catch { return default(T); }
        }

        private static int? GetInt(object instance, string name)
        {
            var value = GetPropertyValue(instance, name);
            if (value == null) return null;
            try { return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture); } catch { return null; }
        }

        private static float? GetFloat(object instance, string name)
        {
            var value = GetPropertyValue(instance, name);
            if (value == null) return null;
            try { return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture); } catch { return null; }
        }

        private static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }

        private static string SanitizeHostPart(string value)
        {
            var chars = (value ?? string.Empty).ToLowerInvariant().ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (!((chars[i] >= 'a' && chars[i] <= 'z') || (chars[i] >= '0' && chars[i] <= '9') || chars[i] == '-')) chars[i] = '-';
            var result = new string(chars).Trim('-');
            return result.Length == 0 ? "mod" : result;
        }
    }
}
