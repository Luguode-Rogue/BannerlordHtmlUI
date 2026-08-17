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

namespace BannerlordHtmlUI
{
    internal static class HtmlUiBrushResourceServiceCore
    {
        private const string ContentRootId = "framework-brush-cache";
        private static readonly ConcurrentDictionary<string, TextureExportResult> Cache = new ConcurrentDictionary<string, TextureExportResult>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> Failures = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            Cache.Clear();
            Failures.Clear();
        }

        public static object CreateSpriteSnapshot(object sprite, bool includeResource)
        {
            if (sprite == null) return null;
            var part = GetPropertyValue(sprite, "SpritePart") ?? GetPropertyValue(sprite, "BaseSprite");
            var texture2D = GetPropertyValue(part ?? sprite, "Texture");
            var spriteName = GetString(sprite, "Name");
            var textureName = GetString(texture2D, "Name");
            var resourceName = !string.IsNullOrWhiteSpace(spriteName) ? spriteName : textureName;
            var width = GetInt(sprite, "Width") ?? GetInt(part, "Width") ?? 0;
            var height = GetInt(sprite, "Height") ?? GetInt(part, "Height") ?? 0;
            var sheetId = GetInt(part, "SheetID") ?? -1;
            var sheetX = GetInt(part, "SheetX") ?? 0;
            var sheetY = GetInt(part, "SheetY") ?? 0;
            var sheetWidth = GetInt(part, "SheetWidth") ?? width;
            var sheetHeight = GetInt(part, "SheetHeight") ?? height;

            string url = null;
            string error = null;
            string source = null;
            string runtimePath = null;
            if (includeResource)
            {
                try
                {
                    var result = EnsureTextureCached(part, resourceName, sheetWidth, sheetHeight, sheetX, sheetY, width, height);
                    url = result.Url;
                    source = result.Source;
                    runtimePath = result.RuntimePath;
                }
                catch (Exception ex)
                {
                    error = ex.GetType().Name + ": " + ex.Message;
                    Failures[BuildIdentity(resourceName, sheetId, sheetWidth, sheetHeight)] = error;
                    HtmlUiLogger.Warn("Brush sprite cache failed: " + error);
                }
            }

            return new
            {
                type = sprite.GetType().FullName,
                name = spriteName,
                width,
                height,
                resourceUrl = url,
                resourceError = error,
                resourceSource = source,
                runtimeTexturePath = runtimePath,
                textureName,
                resourceName,
                sheetId,
                sheetX,
                sheetY,
                sheetWidth,
                sheetHeight,
                minU = GetFloat(part, "MinU"),
                minV = GetFloat(part, "MinV"),
                maxU = GetFloat(part, "MaxU"),
                maxV = GetFloat(part, "MaxV")
            };
        }

        private sealed class TextureExportResult
        {
            public string Url;
            public string Source;
            public string RuntimePath;
        }

        private static TextureExportResult EnsureTextureCached(object spritePart, string resourceName, int sheetWidth, int sheetHeight, int sheetX, int sheetY, int spriteWidth, int spriteHeight)
        {
            if (!_initialized) throw new InvalidOperationException("Native Brush resource cache is not initialized.");
            if (spritePart == null) throw new InvalidOperationException("SpritePart is unavailable.");

            var sheetId = GetInt(spritePart, "SheetID") ?? -1;
            var identity = BuildIdentity(resourceName, sheetId, sheetWidth, sheetHeight);
            TextureExportResult cached;
            if (Cache.TryGetValue(identity, out cached))
            {
                var cachedPath = System.IO.Path.Combine(_cacheDirectory, System.IO.Path.GetFileName(new Uri(cached.Url).AbsolutePath));
                if (File.Exists(cachedPath) && new FileInfo(cachedPath).Length > 8) return cached;
            }

            string runtimePath;
            var texture = ResolveEngineTexture(spritePart, out runtimePath);
            if (texture == null) throw new InvalidOperationException("Unable to resolve the native texture used by SpritePart. " + runtimePath);

            try { texture.PreloadTexture(true); } catch { }
            try { texture.SetTextureAsAlwaysValid(); } catch { }
            if (!texture.IsValid) throw new InvalidOperationException("Resolved native engine texture is invalid. " + runtimePath);

            var width = texture.Width > 0 ? texture.Width : sheetWidth;
            var height = texture.Height > 0 ? texture.Height : sheetHeight;
            if (width <= 0 || height <= 0) throw new InvalidOperationException("Resolved native engine texture dimensions are unavailable. " + runtimePath);

            var hash = Sha256(identity + "|" + width + "x" + height).Substring(0, 24);
            var filename = "sprite-" + hash + ".png";
            var path = System.IO.Path.Combine(_cacheDirectory, filename);

            // Preferred path: ask Bannerlord's own texture exporter for the actual GPU texture.
            Exception nativeExportError = null;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                texture.SaveToFile(path, false);
                if (IsValidPng(path))
                {
                    var native = new TextureExportResult
                    {
                        Url = _publicHost + "/" + filename,
                        Source = "" + runtimePath + " [Engine.Texture.SaveToFile]",
                        RuntimePath = runtimePath
                    };
                    Cache[identity] = native;
                    HtmlUiLogger.Info("Native Brush sprite exported by Engine.Texture.SaveToFile: " + resourceName);
                    return native;
                }
                nativeExportError = new IOException("Engine Texture.SaveToFile produced no valid PNG file.");
            }
            catch (Exception ex)
            {
                nativeExportError = ex;
            }

            // Fallback only when the native exporter is unavailable. Keep the fallback diagnostic-only.
            var bytes = checked(width * height * 4);
            var raw = new byte[bytes];
            texture.GetPixelData(raw);
            HtmlUiLogger.Info("Brush pixel fallback diagnostics: " + DiagnosePixels(raw, width, height, runtimePath));

            if (AllZero(raw))
            {
                throw new IOException("Native texture export failed and Texture.GetPixelData returned all-zero pixel data. " +
                    "SaveToFileError=" + (nativeExportError == null ? "unknown" : nativeExportError.Message));
            }

            WritePng(path, raw, width, height, PixelLayout.Bgra);
            if (!IsValidPng(path))
                throw new IOException("Pixel fallback did not produce a valid PNG.");

            var fallback = new TextureExportResult
            {
                Url = _publicHost + "/" + filename,
                Source = runtimePath + " [Texture.GetPixelData fallback]",
                RuntimePath = runtimePath
            };
            Cache[identity] = fallback;
            return fallback;
        }

        private static TaleWorlds.Engine.Texture ResolveEngineTexture(object spritePart, out string runtimePath)
        {
            var texture2D = GetPropertyValue(spritePart, "Texture");
            runtimePath = "SpritePart.Texture=" + (texture2D == null ? "<null>" : texture2D.GetType().FullName);
            if (texture2D == null) return null;

            var platform = GetPropertyValue(texture2D, "PlatformTexture");
            runtimePath += "; PlatformTexture=" + (platform == null ? "<null>" : platform.GetType().FullName);
            if (platform == null) return null;

            if (platform is TaleWorlds.Engine.Texture direct)
            {
                runtimePath += " [Engine.Texture]";
                return direct;
            }

            var engine = GetPropertyValue(platform, "Texture");
            runtimePath += "; PlatformTexture.Texture=" + (engine == null ? "<null>" : engine.GetType().FullName);
            if (engine is TaleWorlds.Engine.Texture wrapped)
            {
                runtimePath += " [EngineTexture]";
                return wrapped;
            }

            return null;
        }

        private static bool IsValidPng(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length < 8) return false;
            using (var stream = File.OpenRead(path))
            {
                var signature = new byte[8];
                if (stream.Read(signature, 0, 8) != 8) return false;
                return signature[0] == 0x89 && signature[1] == 0x50 && signature[2] == 0x4E && signature[3] == 0x47
                    && signature[4] == 0x0D && signature[5] == 0x0A && signature[6] == 0x1A && signature[7] == 0x0A;
            }
        }

        private static bool AllZero(byte[] raw)
        {
            if (raw == null || raw.Length == 0) return true;
            for (var i = 0; i < raw.Length; i++) if (raw[i] != 0) return false;
            return true;
        }

        private enum PixelLayout { Rgba, Bgra, Argb }

        private static string DiagnosePixels(byte[] raw, int width, int height, string source)
        {
            if (raw == null || raw.Length == 0) return "empty, source=" + source;
            var sampleBytes = Math.Min(raw.Length, 4 * 4096);
            long[] min = { 255, 255, 255, 255 };
            long[] max = { 0, 0, 0, 0 };
            long[] sum = { 0, 0, 0, 0 };
            long alphaNonZero = 0;
            for (var i = 0; i + 3 < sampleBytes; i += 4)
            {
                for (var c = 0; c < 4; c++)
                {
                    var value = raw[i + c];
                    if (value < min[c]) min[c] = value;
                    if (value > max[c]) max[c] = value;
                    sum[c] += value;
                }
                if (raw[i + 3] != 0) alphaNonZero++;
            }
            var count = Math.Max(1, sampleBytes / 4);
            return "source=" + source + ", dimensions=" + width + "x" + height + ", bytes=" + raw.Length
                + ", samplePixels=" + count + ", min=[" + string.Join(",", min) + "]"
                + ", max=[" + string.Join(",", max) + "]"
                + ", avg=[" + string.Join(",", sum.Select(v => (v / (double)count).ToString("F1", System.Globalization.CultureInfo.InvariantCulture))) + "]"
                + ", alphaNonZero=" + alphaNonZero;
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
                    var rowBytes = width * 4;
                    var converted = new byte[rowBytes * height];
                    for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var source = (y * width + x) * 4;
                        var target = y * rowBytes + x * 4;
                        byte r, g, b, a;
                        if (layout == PixelLayout.Rgba)
                        { r = raw[source]; g = raw[source + 1]; b = raw[source + 2]; a = raw[source + 3]; }
                        else if (layout == PixelLayout.Argb)
                        { a = raw[source]; r = raw[source + 1]; g = raw[source + 2]; b = raw[source + 3]; }
                        else
                        { b = raw[source]; g = raw[source + 1]; r = raw[source + 2]; a = raw[source + 3]; }
                        converted[target] = b; converted[target + 1] = g; converted[target + 2] = r; converted[target + 3] = a;
                    }
                    Marshal.Copy(converted, 0, data.Scan0, converted.Length);
                }
                finally { bitmap.UnlockBits(data); }
                bitmap.Save(path, ImageFormat.Png);
            }
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
            try { return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture); } catch { return null; }
        }

        private static float? GetFloat(object instance, string name)
        {
            var value = GetPropertyValue(instance, name);
            if (value == null) return null;
            try { return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture); } catch { return null; }
        }

        private static string BuildIdentity(string resourceName, int sheetId, int sheetWidth, int sheetHeight)
            => (resourceName ?? string.Empty) + "|" + sheetId + "|" + sheetWidth + "x" + sheetHeight;

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
