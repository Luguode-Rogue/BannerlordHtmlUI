using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private static readonly ConcurrentDictionary<string, object> Cache = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
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
            HtmlUiLogger.Info("Native Brush resource strategy matrix initialized: " + _cacheDirectory);
        }

        public static void Dispose()
        {
            _initialized = false;
            _cacheDirectory = null;
            _publicHost = null;
            Cache.Clear();
        }

        public static object CreateSpriteSnapshot(object sprite, bool includeResource)
        {
            if (sprite == null) return null;

            var part = GetPropertyValue(sprite, "SpritePart") ?? GetPropertyValue(sprite, "BaseSprite");
            var texture2D = GetPropertyValue(part ?? sprite, "Texture");
            var platformTexture = GetPropertyValue(texture2D, "PlatformTexture");
            var category = GetPropertyValue(part, "Category");
            var spriteName = GetString(sprite, "Name");
            var textureName = GetString(texture2D, "Name");
            var platformTextureName = GetString(platformTexture, "Name");
            var categoryName = GetString(category, "Name");
            var sheetId = GetInt(part, "SheetID") ?? -1;
            var width = GetInt(sprite, "Width") ?? GetInt(part, "Width") ?? 0;
            var height = GetInt(sprite, "Height") ?? GetInt(part, "Height") ?? 0;
            var sheetX = GetInt(part, "SheetX") ?? 0;
            var sheetY = GetInt(part, "SheetY") ?? 0;
            var sheetWidth = GetInt(part, "SheetWidth") ?? width;
            var sheetHeight = GetInt(part, "SheetHeight") ?? height;
            var spriteSheet = ResolveSpriteSheet(category, sheetId);

            var matrix = includeResource
                ? BuildStrategyMatrix(sprite, part, category, spriteSheet, categoryName, spriteName, textureName, platformTextureName,
                    sheetId, sheetX, sheetY, sheetWidth, sheetHeight, width, height)
                : null;

            return new
            {
                type = sprite.GetType().FullName,
                name = spriteName,
                width,
                height,
                resourceUrl = matrix?.PrimaryUrl,
                resourceError = matrix?.PrimaryError,
                resourceSource = matrix?.PrimarySource,
                runtimeTexturePath = matrix?.RuntimePath,
                textureName,
                platformTextureName,
                sheetPlatformName = GetString(GetPropertyValue(spriteSheet, "PlatformTexture"), "Name"),
                resourceName = matrix?.ResourceName ?? spriteName,
                categoryName,
                categoryRuntimeType = category?.GetType().FullName,
                categoryLoaded = GetBool(category, "IsLoaded"),
                categoryPartiallyLoaded = GetBool(category, "IsPartiallyLoaded"),
                categorySpriteSheetCount = GetInt(category, "SpriteSheetCount"),
                categorySpriteSheetsCount = GetCollectionCount(GetPropertyValue(category, "SpriteSheets")),
                spriteSheetRuntimeType = spriteSheet?.GetType().FullName,
                spriteSheetName = GetString(spriteSheet, "Name"),
                sheetId,
                sheetX,
                sheetY,
                sheetWidth,
                sheetHeight,
                minU = GetFloat(part, "MinU"),
                minV = GetFloat(part, "MinV"),
                maxU = GetFloat(part, "MaxU"),
                maxV = GetFloat(part, "MaxV"),
                variantUrls = matrix?.VariantUrls,
                strategyUrls = matrix?.Strategies,
                pixelDiagnostics = matrix?.PixelDiagnostics
            };
        }

        private sealed class StrategyMatrix
        {
            public string PrimaryUrl;
            public string PrimarySource;
            public string PrimaryError;
            public string RuntimePath;
            public string ResourceName;
            public string PixelDiagnostics;
            public object VariantUrls;
            public object[] Strategies;
        }

        private sealed class StrategyResult
        {
            public string Name;
            public string Status;
            public string Url;
            public string Source;
            public string Error;
            public string RuntimeType;
            public int Width;
            public int Height;
        }

        private static StrategyMatrix BuildStrategyMatrix(object sprite, object spritePart, object category, object spriteSheet,
            string categoryName, string spriteName, string textureName, string platformTextureName,
            int sheetId, int sheetX, int sheetY, int sheetWidth, int sheetHeight, int spriteWidth, int spriteHeight)
        {
            var matrix = new StrategyMatrix { ResourceName = spriteName };
            var results = new List<StrategyResult>();
            var identity = BuildIdentity(categoryName, spriteName, sheetId, sheetWidth, sheetHeight);

            // A: exact runtime texture currently attached to SpritePart -> native engine export.
            var runtimePath = string.Empty;
            var runtimeTexture = ResolveEngineTexture(spritePart, out runtimePath);
            matrix.RuntimePath = runtimePath;
            if (runtimeTexture != null)
            {
                AddNativeTextureStrategy(results, "A Runtime Texture · SaveToFile", runtimeTexture, identity, sheetX, sheetY, spriteWidth, spriteHeight);
                AddRawTextureStrategies(results, "B Runtime Texture · GetPixelData", runtimeTexture, identity, sheetX, sheetY, spriteWidth, spriteHeight, out matrix.PixelDiagnostics);
            }
            else
            {
                results.Add(new StrategyResult { Name = "A/B Runtime Texture", Status = "failed", Error = "SpritePart runtime Engine.Texture unavailable.", RuntimeType = runtimePath });
            }

            // C: loaded SpriteCategory -> locate same SpritePart by sprite name, then use its own runtime texture.
            try
            {
                var loadedCategory = LoadSpriteCategory(categoryName);
                var loadedPart = FindSpritePart(loadedCategory, spriteName);
                var loadedTexture = ResolveEngineTexture(loadedPart, out var loadedPath);
                if (loadedTexture != null)
                {
                    AddNativeTextureStrategy(results, "C UIResourceManager · LoadSpriteCategory", loadedTexture, identity + "|loaded", sheetX, sheetY, spriteWidth, spriteHeight);
                    results[results.Count - 1].RuntimeType = loadedPath;
                }
                else
                {
                    results.Add(new StrategyResult { Name = "C UIResourceManager · LoadSpriteCategory", Status = "failed", Error = "Loaded category did not expose a usable SpritePart texture.", RuntimeType = loadedPath });
                }
            }
            catch (Exception ex)
            {
                results.Add(new StrategyResult { Name = "C UIResourceManager · LoadSpriteCategory", Status = "failed", Error = ex.GetType().Name + ": " + ex.Message });
            }

            // D: enumerate every SpriteSheet object that the Category exposes. This handles non-IList collections and 0/1-based quirks.
            try
            {
                var sheets = GetPropertyValue(category, "SpriteSheets");
                var index = 0;
                foreach (var candidate in EnumerateCollection(sheets).Take(8))
                {
                    var candidateTexture = ResolveEngineTextureFromWrapper(candidate, out var path);
                    if (candidateTexture != null)
                    {
                        AddNativeTextureStrategy(results, "D Category SpriteSheet[" + index + "]", candidateTexture,
                            identity + "|sheet" + index, sheetX, sheetY, spriteWidth, spriteHeight);
                        results[results.Count - 1].RuntimeType = path;
                    }
                    else
                    {
                        results.Add(new StrategyResult { Name = "D Category SpriteSheet[" + index + "]", Status = "failed", Error = "No Engine.Texture.", RuntimeType = path });
                    }
                    index++;
                }
                if (index == 0)
                    results.Add(new StrategyResult { Name = "D Category SpriteSheets enumeration", Status = "failed", Error = "SpriteSheets collection returned no enumerable values." });
            }
            catch (Exception ex)
            {
                results.Add(new StrategyResult { Name = "D Category SpriteSheets enumeration", Status = "failed", Error = ex.GetType().Name + ": " + ex.Message });
            }

            // E: explicit resource-name candidates. Try both Sprite and common ui atlas names.
            var names = new List<string>();
            AddCandidate(names, platformTextureName);
            AddCandidate(names, textureName);
            AddCandidate(names, spriteName);
            if (!string.IsNullOrWhiteSpace(categoryName) && sheetId >= 0)
            {
                AddCandidate(names, categoryName + "_" + sheetId);
                AddCandidate(names, categoryName + "_" + (sheetId + 1));
                AddCandidate(names, categoryName + "_" + Math.Max(0, sheetId - 1));
            }
            foreach (var candidateName in names.Distinct(StringComparer.OrdinalIgnoreCase).Take(10))
            {
                try
                {
                    var tex = TaleWorlds.Engine.Texture.GetFromResource(candidateName);
                    if (tex == null)
                    {
                        results.Add(new StrategyResult { Name = "E Resource " + candidateName, Status = "failed", Error = "GetFromResource returned null." });
                        continue;
                    }
                    AddNativeTextureStrategy(results, "E Resource " + candidateName, tex, identity + "|resource|" + candidateName,
                        sheetX, sheetY, spriteWidth, spriteHeight);
                }
                catch (Exception ex)
                {
                    results.Add(new StrategyResult { Name = "E Resource " + candidateName, Status = "failed", Error = ex.GetType().Name + ": " + ex.Message });
                }
            }

            var successful = results.Where(r => r.Status == "ok" && !string.IsNullOrWhiteSpace(r.Url)).ToList();
            var primary = successful.FirstOrDefault(r => r.Name.StartsWith("C ", StringComparison.Ordinal))
                ?? successful.FirstOrDefault(r => r.Name.StartsWith("A ", StringComparison.Ordinal))
                ?? successful.FirstOrDefault(r => r.Name.StartsWith("E ", StringComparison.Ordinal))
                ?? successful.FirstOrDefault();

            matrix.PrimaryUrl = primary?.Url;
            matrix.PrimarySource = primary?.Source;
            matrix.PrimaryError = primary == null ? "No strategy produced a usable PNG." : null;
            matrix.VariantUrls = new
            {
                Rgba = FindStrategyUrl(successful, "B Runtime Texture · GetPixelData · RGBA"),
                Bgra = FindStrategyUrl(successful, "B Runtime Texture · GetPixelData · BGRA"),
                Argb = FindStrategyUrl(successful, "B Runtime Texture · GetPixelData · ARGB"),
                RgbaFlipY = FindStrategyUrl(successful, "B Runtime Texture · GetPixelData · RGBA FlipY"),
                Native = FindStrategyUrl(successful, "A Runtime Texture · SaveToFile")
            };
            matrix.Strategies = results.Cast<object>().ToArray();
            return matrix;
        }

        private static void AddNativeTextureStrategy(List<StrategyResult> results, string name, TaleWorlds.Engine.Texture texture,
            string identity, int sheetX, int sheetY, int spriteWidth, int spriteHeight)
        {
            try
            {
                texture.PreloadTexture(true);
                try { texture.SetTextureAsAlwaysValid(); } catch { }
                var width = texture.Width;
                var height = texture.Height;
                if (width <= 0 || height <= 0) throw new InvalidOperationException("Invalid texture dimensions.");
                var hash = Sha256(identity + "|native").Substring(0, 24);
                var fullPath = System.IO.Path.Combine(_cacheDirectory, "strategy-" + hash + "-native.png");
                if (TryNativeSave(texture, fullPath))
                {
                    var cropPath = System.IO.Path.Combine(_cacheDirectory, "strategy-" + hash + "-native-crop.png");
                    if (CropPng(fullPath, cropPath, sheetX, sheetY, spriteWidth, spriteHeight, false))
                    {
                        results.Add(new StrategyResult { Name = name, Status = "ok", Url = PublicUrl(cropPath), Source = "Engine.Texture.SaveToFile + native crop", RuntimeType = texture.GetType().FullName, Width = spriteWidth, Height = spriteHeight });
                        return;
                    }
                    results.Add(new StrategyResult { Name = name, Status = "ok", Url = PublicUrl(fullPath), Source = "Engine.Texture.SaveToFile", RuntimeType = texture.GetType().FullName, Width = width, Height = height });
                    return;
                }
                results.Add(new StrategyResult { Name = name, Status = "failed", Error = "SaveToFile did not create a valid PNG.", RuntimeType = texture.GetType().FullName, Width = width, Height = height });
            }
            catch (Exception ex)
            {
                results.Add(new StrategyResult { Name = name, Status = "failed", Error = ex.GetType().Name + ": " + ex.Message, RuntimeType = texture?.GetType().FullName });
            }
        }

        private static void AddRawTextureStrategies(List<StrategyResult> results, string prefix, TaleWorlds.Engine.Texture texture,
            string identity, int sheetX, int sheetY, int spriteWidth, int spriteHeight, out string diagnostics)
        {
            diagnostics = null;
            try
            {
                var width = texture.Width;
                var height = texture.Height;
                if (width <= 0 || height <= 0) throw new InvalidOperationException("Invalid texture dimensions.");
                var raw = new byte[checked(width * height * 4)];
                texture.GetPixelData(raw);
                diagnostics = DiagnosePixels(raw, width, height);
                if (AllZero(raw))
                {
                    results.Add(new StrategyResult { Name = prefix, Status = "failed", Error = "GetPixelData returned all-zero data.", RuntimeType = texture.GetType().FullName, Width = width, Height = height });
                    return;
                }

                AddRawCrop(results, prefix + " · RGBA", raw, width, height, identity + "|rgba", sheetX, sheetY, spriteWidth, spriteHeight, PixelLayout.Rgba, false);
                AddRawCrop(results, prefix + " · BGRA", raw, width, height, identity + "|bgra", sheetX, sheetY, spriteWidth, spriteHeight, PixelLayout.Bgra, false);
                AddRawCrop(results, prefix + " · ARGB", raw, width, height, identity + "|argb", sheetX, sheetY, spriteWidth, spriteHeight, PixelLayout.Argb, false);
                AddRawCrop(results, prefix + " · RGBA FlipY", raw, width, height, identity + "|rgbaflip", sheetX, sheetY, spriteWidth, spriteHeight, PixelLayout.Rgba, true);
            }
            catch (Exception ex)
            {
                diagnostics = ex.GetType().Name + ": " + ex.Message;
                results.Add(new StrategyResult { Name = prefix, Status = "failed", Error = diagnostics });
            }
        }

        private static void AddRawCrop(List<StrategyResult> results, string name, byte[] raw, int width, int height, string identity,
            int sheetX, int sheetY, int spriteWidth, int spriteHeight, PixelLayout layout, bool flipY)
        {
            var hash = Sha256(identity).Substring(0, 24);
            var path = System.IO.Path.Combine(_cacheDirectory, "strategy-" + hash + ".png");
            try
            {
                WritePngCrop(path, raw, width, height, sheetX, sheetY, spriteWidth, spriteHeight, layout, flipY);
                if (!IsValidPng(path)) throw new IOException("Generated PNG is invalid.");
                results.Add(new StrategyResult { Name = name, Status = "ok", Url = PublicUrl(path), Source = "Texture.GetPixelData crop " + layout + (flipY ? " FlipY" : string.Empty), RuntimeType = "raw-pixel", Width = spriteWidth, Height = spriteHeight });
            }
            catch (Exception ex)
            {
                results.Add(new StrategyResult { Name = name, Status = "failed", Error = ex.GetType().Name + ": " + ex.Message });
            }
        }

        private static string FindStrategyUrl(IEnumerable<StrategyResult> results, string exactName)
            => results.FirstOrDefault(x => string.Equals(x.Name, exactName, StringComparison.OrdinalIgnoreCase) && x.Status == "ok")?.Url;

        private static string PublicUrl(string path) => _publicHost + "/" + System.IO.Path.GetFileName(path);

        private static object LoadSpriteCategory(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return null;
            var manager = typeof(TaleWorlds.Engine.GauntletUI.UIResourceManager);
            var method = manager.GetMethod("LoadSpriteCategory", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null);
            if (method == null) return null;
            return method.Invoke(null, new object[] { categoryName });
        }

        private static object FindSpritePart(object loadedCategory, string spriteName)
        {
            if (loadedCategory == null || string.IsNullOrWhiteSpace(spriteName)) return null;
            foreach (var propertyName in new[] { "SpriteParts", "Parts" })
            {
                var collection = GetPropertyValue(loadedCategory, propertyName);
                foreach (var part in EnumerateCollection(collection))
                {
                    if (string.Equals(GetString(part, "Name"), spriteName, StringComparison.OrdinalIgnoreCase)) return part;
                }
            }
            foreach (var methodName in new[] { "GetSpritePart", "FindSpritePart" })
            {
                var method = loadedCategory.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
                if (method == null) continue;
                try { var value = method.Invoke(loadedCategory, new object[] { spriteName }); if (value != null) return value; } catch { }
            }
            return null;
        }

        private static TaleWorlds.Engine.Texture ResolveEngineTexture(object spritePart, out string runtimePath)
        {
            runtimePath = "SpritePart=<null>";
            if (spritePart == null) return null;
            runtimePath = "SpritePart=" + spritePart.GetType().FullName;
            var texture2D = GetPropertyValue(spritePart, "Texture");
            runtimePath += "; Texture=" + (texture2D == null ? "<null>" : texture2D.GetType().FullName);
            var platform = GetPropertyValue(texture2D, "PlatformTexture");
            runtimePath += "; PlatformTexture=" + (platform == null ? "<null>" : platform.GetType().FullName) + "; Name=" + (GetString(platform, "Name") ?? "<null>");
            var engine = GetPropertyValue(platform, "Texture");
            if (engine is TaleWorlds.Engine.Texture direct) { runtimePath += "; TextureObject=Engine.Texture"; return direct; }
            if (platform is TaleWorlds.Engine.Texture directPlatform) { runtimePath += "; PlatformIsEngine.Texture"; return directPlatform; }
            runtimePath += "; PlatformTexture.Texture=" + (engine == null ? "<null>" : engine.GetType().FullName);
            return engine as TaleWorlds.Engine.Texture;
        }

        private static TaleWorlds.Engine.Texture ResolveEngineTextureFromWrapper(object wrapper, out string runtimePath)
        {
            runtimePath = wrapper == null ? "wrapper=<null>" : "wrapper=" + wrapper.GetType().FullName;
            var platform = GetPropertyValue(wrapper, "PlatformTexture");
            if (platform is TaleWorlds.Engine.Texture direct) return direct;
            var engine = GetPropertyValue(platform, "Texture");
            return engine as TaleWorlds.Engine.Texture;
        }

        private static object ResolveSpriteSheet(object category, int sheetId)
        {
            if (category == null || sheetId < 0) return null;
            var sheets = GetPropertyValue(category, "SpriteSheets");
            var direct = GetIndexedValue(sheets, sheetId);
            if (direct != null) return direct;
            return null;
        }

        private static IEnumerable<object> EnumerateCollection(object collection)
        {
            if (collection == null) yield break;
            if (collection is IEnumerable enumerable)
            {
                foreach (var item in enumerable) yield return item;
            }
        }

        private static object GetIndexedValue(object collection, int index)
        {
            if (collection == null || index < 0) return null;
            try
            {
                if (collection is IList list) return index < list.Count ? list[index] : null;
                var type = collection.GetType();
                var item = type.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public);
                if (item != null) return item.GetValue(collection, new object[] { index });
                if (collection is IDictionary dictionary)
                {
                    if (dictionary.Contains(index)) return dictionary[index];
                    if (dictionary.Contains(index + 1)) return dictionary[index + 1];
                }
            }
            catch { }
            return null;
        }

        private static void AddCandidate(List<string> values, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
        }

        private static bool TryNativeSave(TaleWorlds.Engine.Texture texture, string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                texture.PreloadTexture(true);
                try { texture.SetTextureAsAlwaysValid(); } catch { }
                texture.SaveToFile(path, false);
                return IsValidPng(path);
            }
            catch { return false; }
        }

        private static bool CropPng(string sourcePath, string targetPath, int x, int y, int width, int height, bool flipY)
        {
            try
            {
                using (var source = new Bitmap(sourcePath))
                {
                    x = Math.Max(0, Math.Min(x, source.Width - 1));
                    y = Math.Max(0, Math.Min(y, source.Height - 1));
                    width = Math.Min(width, source.Width - x);
                    height = Math.Min(height, source.Height - y);
                    if (width <= 0 || height <= 0) return false;
                    using (var crop = source.Clone(new Rectangle(x, y, width, height), PixelFormat.Format32bppArgb))
                    {
                        if (flipY) crop.RotateFlip(RotateFlipType.RotateNoneFlipY);
                        crop.Save(targetPath, ImageFormat.Png);
                        return IsValidPng(targetPath);
                    }
                }
            }
            catch { return false; }
        }

        private enum PixelLayout { Rgba, Bgra, Argb }

        private static void WritePngCrop(string path, byte[] raw, int sourceWidth, int sourceHeight, int x, int y, int width, int height, PixelLayout layout, bool flipY)
        {
            if (x < 0 || y < 0 || x >= sourceWidth || y >= sourceHeight) throw new ArgumentOutOfRangeException();
            width = Math.Min(width, sourceWidth - x);
            height = Math.Min(height, sourceHeight - y);
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException();
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var converted = new byte[width * height * 4];
                    for (var dy = 0; dy < height; dy++)
                    for (var dx = 0; dx < width; dx++)
                    {
                        var sy = flipY ? y + (height - 1 - dy) : y + dy;
                        var src = (sy * sourceWidth + x + dx) * 4;
                        var dst = (dy * width + dx) * 4;
                        byte r, g, b, a;
                        switch (layout)
                        {
                            case PixelLayout.Rgba: r = raw[src]; g = raw[src + 1]; b = raw[src + 2]; a = raw[src + 3]; break;
                            case PixelLayout.Argb: a = raw[src]; r = raw[src + 1]; g = raw[src + 2]; b = raw[src + 3]; break;
                            default: b = raw[src]; g = raw[src + 1]; r = raw[src + 2]; a = raw[src + 3]; break;
                        }
                        converted[dst] = b; converted[dst + 1] = g; converted[dst + 2] = r; converted[dst + 3] = a;
                    }
                    Marshal.Copy(converted, 0, data.Scan0, converted.Length);
                }
                finally { bitmap.UnlockBits(data); }
                var directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static string DiagnosePixels(byte[] raw, int width, int height)
        {
            if (raw == null) return "raw=null";
            var sample = Math.Min(raw.Length, 4 * 4096);
            long[] min = { 255, 255, 255, 255 }, max = { 0, 0, 0, 0 }, sum = { 0, 0, 0, 0 };
            for (var i = 0; i + 3 < sample; i += 4)
            {
                for (var c = 0; c < 4; c++) { var v = raw[i + c]; if (v < min[c]) min[c] = v; if (v > max[c]) max[c] = v; sum[c] += v; }
            }
            var count = Math.Max(1, sample / 4);
            return "dimensions=" + width + "x" + height + ", bytes=" + raw.Length + ", samplePixels=" + count
                + ", min=[" + string.Join(",", min) + "]"
                + ", max=[" + string.Join(",", max) + "]"
                + ", avg=[" + string.Join(",", sum.Select(v => (v / (double)count).ToString("F1", System.Globalization.CultureInfo.InvariantCulture))) + "]";
        }

        private static bool AllZero(byte[] raw)
        {
            if (raw == null || raw.Length == 0) return true;
            for (var i = 0; i < raw.Length; i++) if (raw[i] != 0) return false;
            return true;
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
        private static bool? GetBool(object instance, string name)
        {
            var value = GetPropertyValue(instance, name);
            if (value == null) return null;
            try { return Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture); } catch { return null; }
        }
        private static int? GetCollectionCount(object collection)
        {
            if (collection == null) return null;
            try
            {
                if (collection is ICollection c) return c.Count;
                var property = collection.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
                return property == null ? (int?)null : Convert.ToInt32(property.GetValue(collection, null));
            }
            catch { return null; }
        }
        private static string BuildIdentity(string categoryName, string spriteName, int sheetId, int width, int height)
            => (categoryName ?? string.Empty) + "|" + (spriteName ?? string.Empty) + "|" + sheetId + "|" + width + "x" + height;
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