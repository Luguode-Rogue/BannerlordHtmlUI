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
        private static readonly ConcurrentDictionary<string, string> FailedIdentities = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string _cacheDirectory;
        private static string _publicHost;
        private static bool _initialized;
        private static int _typeDiagnosticsLogged;

        public static void Initialize(HtmlUiHost host)
        {
            if (_initialized || host == null) return;

            _cacheDirectory = Path.Combine(Path.GetTempPath(), "BannerlordHtmlUI", "BrushCache");
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
            _typeDiagnosticsLogged = 0;
        }

        public static object CreateSpriteSnapshot(object sprite, bool includeResource)
        {
            if (sprite == null) return null;

            var spritePart = GetPropertyValue(sprite, "SpritePart")
                             ?? GetPropertyValue(sprite, "BaseSprite");
            var textureWrapper = GetPropertyValue(spritePart ?? sprite, "Texture");
            var platformTexture = GetPropertyValue(textureWrapper, "PlatformTexture");
            var engineTexture = GetPropertyValue(platformTexture, "Texture") ?? platformTexture;

            LogTextureTypesOnce(textureWrapper, platformTexture, engineTexture);

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
                    var identity = BuildIdentity(textureWrapper, platformTexture, sheetWidth, sheetHeight);
                    if (!FailedIdentities.ContainsKey(identity))
                    {
                        url = EnsureTextureCached(engineTexture, platformTexture, width, height, sheetWidth, sheetHeight,
                            GetPropertyValue<string>(textureWrapper, "Name"),
                            GetPropertyValue<string>(platformTexture, "Name"));
                    }
                    else
                    {
                        cacheError = FailedIdentities[identity];
                    }
                }
                catch (Exception ex)
                {
                    cacheError = ex.GetType().Name + ": " + ex.Message;
                    var identity = BuildIdentity(textureWrapper, platformTexture, sheetWidth, sheetHeight);
                    FailedIdentities[identity] = cacheError;
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

        private static string EnsureTextureCached(object engineTexture, object platformTexture, int width, int height, int sheetWidth, int sheetHeight, string textureName, string platformName)
        {
            if (!_initialized)
                throw new InvalidOperationException("Native Brush resource cache is not initialized.");
            if (engineTexture == null && platformTexture == null)
                throw new InvalidOperationException("Sprite texture object is unavailable.");

            var identity = (textureName ?? string.Empty) + "|" + (platformName ?? string.Empty) + "|" + sheetWidth + "x" + sheetHeight;
            var hash = Sha256(identity).Substring(0, 24);
            var filename = "sprite-" + hash + ".png";
            var path = Path.Combine(_cacheDirectory, filename);
            var relative = filename.Replace(Path.DirectorySeparatorChar, '/');

            if (!File.Exists(path))
            {
                if (!TrySaveTexture(engineTexture, platformTexture, path, out var error))
                    throw new InvalidOperationException(error);

                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    throw new IOException("Texture export did not produce a valid file: " + path);
            }

            var url = _publicHost + "/" + relative;
            CachedUrls[identity] = url;
            return url;
        }

        private static bool TrySaveTexture(object engineTexture, object platformTexture, string path, out string error)
        {
            error = null;

            // Older/alternate Bannerlord versions expose Texture.SaveToFile(string) directly.
            foreach (var candidate in new[] { engineTexture, platformTexture })
            {
                if (candidate == null) continue;
                var method = candidate.GetType().GetMethod("SaveToFile", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
                if (method == null) continue;

                try
                {
                    method.Invoke(candidate, new object[] { path });
                    return true;
                }
                catch (TargetInvocationException ex)
                {
                    error = ex.InnerException == null ? ex.Message : ex.InnerException.Message;
                    return false;
                }
            }

            // Current generated engine bindings expose ITexture.SaveToFile(UIntPtr,string),
            // with the native pointer supplied explicitly. Resolve the provider dynamically
            // so this bridge remains tolerant of binding layout differences.
            var pointer = GetUIntPtr(engineTexture) ?? GetUIntPtr(platformTexture);
            if (!pointer.HasValue || pointer.Value == UIntPtr.Zero)
            {
                error = "No native texture pointer was exposed by the runtime object.";
                return false;
            }

            try
            {
                var appInterfaceType = ResolveType("TaleWorlds.Engine.EngineApplicationInterface");
                if (appInterfaceType == null)
                {
                    error = "TaleWorlds.Engine.EngineApplicationInterface type was not found.";
                    return false;
                }

                object textureInterface = GetStaticMember(appInterfaceType, "ITexture");
                if (textureInterface == null)
                {
                    error = "EngineApplicationInterface.ITexture provider was not found.";
                    return false;
                }

                var save = textureInterface.GetType().GetMethod("SaveToFile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(UIntPtr), typeof(string) }, null);
                if (save == null)
                {
                    error = "ITexture.SaveToFile(UIntPtr,string) was not found on the runtime provider.";
                    return false;
                }

                save.Invoke(textureInterface, new object[] { pointer.Value, path });
                return true;
            }
            catch (TargetInvocationException ex)
            {
                error = ex.InnerException == null ? ex.Message : ex.InnerException.GetType().Name + ": " + ex.InnerException.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static UIntPtr? GetUIntPtr(object instance)
        {
            if (instance == null) return null;
            var pointerProperty = instance.GetType().GetProperty("Pointer", BindingFlags.Instance | BindingFlags.Public);
            if (pointerProperty == null) return null;

            try
            {
                var value = pointerProperty.GetValue(instance, null);
                if (value is UIntPtr ptr) return ptr;
                if (value is IntPtr iptr) return new UIntPtr(iptr.ToPointer());
            }
            catch { }

            return null;
        }

        private static void LogTextureTypesOnce(object textureWrapper, object platformTexture, object engineTexture)
        {
            if (System.Threading.Interlocked.Exchange(ref _typeDiagnosticsLogged, 1) != 0) return;

            HtmlUiLogger.Info("Brush texture runtime types: wrapper=" + TypeName(textureWrapper)
                + ", platform=" + TypeName(platformTexture)
                + ", engine=" + TypeName(engineTexture));

            DumpTypeMembers("platform", platformTexture);
            DumpTypeMembers("engine", engineTexture);
        }

        private static void DumpTypeMembers(string label, object instance)
        {
            if (instance == null) return;
            try
            {
                var methods = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Select(m => m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")")
                    .Distinct()
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .Take(80);
                HtmlUiLogger.Info("Brush texture " + label + " methods: " + string.Join("; ", methods));
            }
            catch { }
        }

        private static string TypeName(object instance) => instance == null ? "<null>" : instance.GetType().FullName;

        private static string BuildIdentity(object textureWrapper, object platformTexture, int sheetWidth, int sheetHeight)
        {
            var textureName = GetPropertyValue<string>(textureWrapper, "Name") ?? string.Empty;
            var platformName = GetPropertyValue<string>(platformTexture, "Name") ?? string.Empty;
            return textureName + "|" + platformName + "|" + sheetWidth + "x" + sheetHeight;
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName, false);
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }

        private static object GetStaticMember(Type type, string name)
        {
            var property = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                try { return property.GetValue(null, null); } catch { }
            }

            var field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try { return field.GetValue(null); } catch { }
            }

            return null;
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