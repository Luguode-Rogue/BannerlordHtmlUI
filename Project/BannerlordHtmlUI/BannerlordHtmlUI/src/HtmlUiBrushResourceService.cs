using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiBrushResourceService
    {
        private const string ContentRootId = "framework-brush-cache";
        private static readonly ConcurrentDictionary<string, string> CachedUrls = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string _cacheDirectory;
        private static string _publicHost;
        private static bool _initialized;

        public static void Initialize(HtmlUiHost host)
        {
            if (_initialized || host == null) return;

            _cacheDirectory = Path.Combine(Path.GetTempPath(), "BannerlordHtmlUI", "BrushCache");
            Directory.CreateDirectory(_cacheDirectory);
            host.RegisterContentRoot(ContentRootId, _cacheDirectory);
            _publicHost = "bannerlord-htmlui-" + SanitizeHostPart(ContentRootId) + ".local";
            _initialized = true;
            HtmlUiLogger.Info("Native Brush resource cache initialized: " + _cacheDirectory);
        }

        public static object CreateSpriteSnapshot(object sprite, bool includeResource)
        {
            if (sprite == null) return null;

            var spritePart = GetPropertyValue(sprite, "SpritePart")
                             ?? GetPropertyValue(sprite, "BaseSprite");
            var textureWrapper = GetPropertyValue(spritePart ?? sprite, "Texture");
            var platformTexture = GetPropertyValue(textureWrapper, "PlatformTexture");
            var engineTexture = GetPropertyValue(platformTexture, "Texture") ?? platformTexture;

            var width = GetInt(sprite, "Width") ?? GetInt(spritePart, "Width") ?? GetInt(textureWrapper, "Width") ?? 0;
            var height = GetInt(sprite, "Height") ?? GetInt(spritePart, "Height") ?? GetInt(textureWrapper, "Height") ?? 0;
            var sheetX = GetInt(spritePart, "SheetX") ?? 0;
            var sheetY = GetInt(spritePart, "SheetY") ?? 0;
            var sheetWidth = GetInt(spritePart, "SheetWidth") ?? GetInt(textureWrapper, "Width") ?? width;
            var sheetHeight = GetInt(spritePart, "SheetHeight") ?? GetInt(textureWrapper, "Height") ?? height;

            string url = null;
            string cacheError = null;
            if (includeResource)
            {
                try
                {
                    url = EnsureTextureCached(engineTexture, width, height, sheetWidth, sheetHeight,
                        GetPropertyValue<string>(textureWrapper, "Name"),
                        GetPropertyValue<string>(platformTexture, "Name"));
                }
                catch (Exception ex)
                {
                    cacheError = ex.GetType().Name + ": " + ex.Message;
                    HtmlUiLogger.Warn("Brush sprite cache failed: " + cacheError);
                }
            }

            return new
            {
                type = sprite.GetType().FullName,
                name = GetPropertyValue<string>(sprite, "Name"),
                width,
                height,
                resourceUrl = url,
                resourceError = cacheError,
                sheetX,
                sheetY,
                sheetWidth,
                sheetHeight,
                minU = GetFloat(spritePart, "MinU"),
                minV = GetFloat(spritePart, "MinV"),
                maxU = GetFloat(spritePart, "MaxU"),
                maxV = GetFloat(spritePart, "MaxV")
            };
        }

        private static string EnsureTextureCached(object engineTexture, int width, int height, int sheetWidth, int sheetHeight, string textureName, string platformName)
        {
            if (!_initialized)
                throw new InvalidOperationException("Native Brush resource cache is not initialized.");
            if (engineTexture == null)
                throw new InvalidOperationException("Sprite texture platform object is unavailable.");

            var saveMethod = engineTexture.GetType().GetMethod("SaveToFile", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
            if (saveMethod == null)
                throw new MissingMethodException(engineTexture.GetType().FullName, "SaveToFile(string)");

            var identity = (textureName ?? string.Empty) + "|" + (platformName ?? string.Empty) + "|" + sheetWidth + "x" + sheetHeight;
            var hash = Sha256(identity).Substring(0, 24);
            var filename = "sprite-" + hash + ".png";
            var path = Path.Combine(_cacheDirectory, filename);
            var relative = filename.Replace(Path.DirectorySeparatorChar, '/');

            if (!File.Exists(path))
            {
                saveMethod.Invoke(engineTexture, new object[] { path });
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    throw new IOException("Engine Texture.SaveToFile did not produce a valid file: " + path);
            }

            var url = "https://" + _publicHost + "/" + relative;
            CachedUrls[identity] = url;
            return url;
        }

        private static object GetPropertyValue(object instance, string name)
        {
            if (instance == null) return null;
            var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property == null) return null;
            try { return property.GetValue(instance, null); }
            catch { return null; }
        }

        private static T GetPropertyValue<T>(object instance, string name)
        {
            var value = GetPropertyValue(instance, name);
            if (value == null) return default(T);
            try { return (T)value; }
            catch { return default(T); }
        }

        private static int? GetInt(object instance, string name)
        {
            var value = GetPropertyValue(instance, name);
            if (value == null) return null;
            try { return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        private static float? GetFloat(object instance, string name)
        {
            var value = GetPropertyValue(instance, name);
            if (value == null) return null;
            try { return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return null; }
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
            {
                if (!((chars[i] >= 'a' && chars[i] <= 'z') || (chars[i] >= '0' && chars[i] <= '9') || chars[i] == '-'))
                    chars[i] = '-';
            }
            var result = new string(chars).Trim('-');
            return result.Length == 0 ? "mod" : result;
        }
    }
}
