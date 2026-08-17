using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.ScreenSystem;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Native UI sprite asset proof-of-concept.
    /// Primary route: original TPAC texture -> PNG -> SpriteData crop.
    /// GPU Texture readback is intentionally not used here.
    /// </summary>
    internal static class HtmlUiNativeAtlasAssetService
    {
        private sealed class SpriteRequest
        {
            public string BrushName;
            public string SpriteName;
            public string CategoryName;
            public int SheetId;
            public int SheetX;
            public int SheetY;
            public int Width;
            public int Height;
        }

        private sealed class AtlasResult
        {
            public string Provider;
            public string AtlasName;
            public string AtlasPath;
            public string AtlasUrl;
            public string SpriteUrl;
            public string Status;
            public string Error;
            public int AtlasWidth;
            public int AtlasHeight;
        }

        private static readonly ConcurrentDictionary<string, Task<AtlasResult>> Cache =
            new ConcurrentDictionary<string, Task<AtlasResult>>(StringComparer.OrdinalIgnoreCase);

        private static string _cacheDirectory;
        private static string _publicHost;
        private static string _gameRoot;
        private static string _tpacToolDirectory;
        private static Assembly _tpacLibAssembly;
        private static Assembly _tpacIoAssembly;
        private static bool _initialized;
        private static readonly object InitSync = new object();

        public static void Initialize(HtmlUiHost host)
        {
            if (_initialized || host == null) return;
            lock (InitSync)
            {
                if (_initialized) return;
                _cacheDirectory = Path.Combine(Path.GetTempPath(), "BannerlordHtmlUI", "NativeAtlasCache");
                Directory.CreateDirectory(_cacheDirectory);
                host.RegisterContentRoot("framework-native-atlas-cache", _cacheDirectory);
                _publicHost = "https://bannerlord-htmlui-framework-native-atlas-cache.local";
                _gameRoot = FindGameRoot();
                _initialized = true;
                HtmlUiLogger.Info("Native Atlas asset service initialized. GameRoot=" + (_gameRoot ?? "<null>"));
            }
        }

        public static void Dispose()
        {
            Cache.Clear();
            _initialized = false;
            _cacheDirectory = null;
            _publicHost = null;
            _gameRoot = null;
        }

        public static async Task<object> ProbeAsync(JToken payload, CancellationToken cancellationToken)
        {
            EnsureInitialized();
            var request = CaptureRequest(payload);
            if (request == null)
                throw new ArgumentException("Brush name is required.");

            var candidates = BuildAtlasCandidates(request.CategoryName, request.SheetId);
            var result = await GetOrExtractAsync(candidates, request, cancellationToken).ConfigureAwait(false);
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
                provider = result.Provider,
                atlasName = result.AtlasName,
                atlasPath = result.AtlasPath,
                atlasUrl = result.AtlasUrl,
                spriteUrl = result.SpriteUrl,
                status = result.Status,
                error = result.Error,
                atlasWidth = result.AtlasWidth,
                atlasHeight = result.AtlasHeight
            };
        }

        private static async Task<AtlasResult> GetOrExtractAsync(List<string> candidates, SpriteRequest request, CancellationToken cancellationToken)
        {
            var key = string.Join("|", candidates) + "|" + request.SheetX + ":" + request.SheetY + ":" + request.Width + ":" + request.Height;
            var task = Cache.GetOrAdd(key, _ => Task.Run(() => Extract(candidates, request, cancellationToken), cancellationToken));
            return await task.ConfigureAwait(false);
        }

        private static AtlasResult Extract(List<string> candidates, SpriteRequest request, CancellationToken cancellationToken)
        {
            var loose = TryFindLooseAtlas(candidates);
            if (loose != null)
            {
                return ExportAndCropLoose(loose, request, cancellationToken);
            }

            try
            {
                return ExportAndCropWithTpacTool(candidates, request, cancellationToken);
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Warn("Native Atlas TPAC extraction failed: " + ex.GetType().Name + ": " + ex.Message);
                return new AtlasResult
                {
                    Provider = "none",
                    Status = "missing",
                    Error = ex.GetType().Name + ": " + ex.Message
                };
            }
        }

        private static AtlasResult ExportAndCropLoose(string atlasPath, SpriteRequest request, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(Path.GetExtension(atlasPath), ".png", StringComparison.OrdinalIgnoreCase))
                {
                    return new AtlasResult
                    {
                        Provider = "loose-file",
                        AtlasName = Path.GetFileNameWithoutExtension(atlasPath),
                        AtlasPath = atlasPath,
                        Status = "found",
                        Error = "Loose atlas found but is not PNG. TPAC extraction is required for DDS/TGA."
                    };
                }

                var hash = SafeHash(atlasPath + "|" + request.SpriteName + "|" + request.SheetX + ":" + request.SheetY);
                var cachedAtlas = Path.Combine(_cacheDirectory, "atlas-" + hash + ".png");
                if (!File.Exists(cachedAtlas)) File.Copy(atlasPath, cachedAtlas, true);
                var crop = CropPng(cachedAtlas, request, hash);
                return new AtlasResult
                {
                    Provider = "loose-file",
                    AtlasName = Path.GetFileNameWithoutExtension(atlasPath),
                    AtlasPath = cachedAtlas,
                    AtlasUrl = PublicUrl(cachedAtlas),
                    SpriteUrl = crop == null ? null : PublicUrl(crop),
                    Status = crop == null ? "error" : "ready",
                    Error = crop == null ? "Unable to crop atlas." : null,
                    AtlasWidth = GetPngWidth(cachedAtlas),
                    AtlasHeight = GetPngHeight(cachedAtlas)
                };
            }
            catch (Exception ex)
            {
                return new AtlasResult { Provider = "loose-file", Status = "error", Error = ex.GetType().Name + ": " + ex.Message };
            }
        }

        private static AtlasResult ExportAndCropWithTpacTool(List<string> candidates, SpriteRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(_gameRoot))
                throw new DirectoryNotFoundException("Bannerlord game root could not be located from BannerlordHtmlUI assembly path.");

            EnsureTpacToolLoaded();
            if (_tpacLibAssembly == null || _tpacIoAssembly == null)
                throw new FileNotFoundException("TpacTool.Lib.dll / TpacTool.IO.dll not found. Install TpacTool or provide its DLLs beside the game/tool executable.");

            var assetDirectory = FindAssetPackageDirectory();
            if (assetDirectory == null)
                throw new DirectoryNotFoundException("Native AssetPackages / EmAssetPackages directory not found.");

            var assetManagerType = _tpacLibAssembly.GetType("TpacTool.Lib.AssetManager", true);
            var manager = Activator.CreateInstance(assetManagerType);
            var load = assetManagerType.GetMethod("Load", new[] { typeof(DirectoryInfo) });
            if (load == null) throw new MissingMethodException("TpacTool.Lib.AssetManager.Load(DirectoryInfo)");

            HtmlUiLogger.Info("Native Atlas: loading TPAC packages from " + assetDirectory);
            load.Invoke(manager, new object[] { new DirectoryInfo(assetDirectory) });

            var loadedAssets = assetManagerType.GetProperty("LoadedAssets")?.GetValue(manager, null) as IEnumerable;
            if (loadedAssets == null) throw new InvalidOperationException("TpacTool AssetManager.LoadedAssets is unavailable.");

            var texture = FindTextureAsset(loadedAssets, candidates);
            if (texture == null)
                throw new FileNotFoundException("Native atlas texture not found. Tried: " + string.Join(", ", candidates));

            var textureName = GetString(texture, "Name") ?? candidates[0];
            var hash = SafeHash(textureName + "|" + request.SpriteName + "|" + request.SheetX + ":" + request.SheetY);
            var atlasPath = Path.Combine(_cacheDirectory, "atlas-" + hash + ".png");
            ExportTextureToPng(texture, atlasPath);
            if (!IsValidPng(atlasPath)) throw new IOException("TpacTool exported texture but PNG validation failed.");

            var crop = CropPng(atlasPath, request, hash);
            return new AtlasResult
            {
                Provider = "tpactool",
                AtlasName = textureName,
                AtlasPath = atlasPath,
                AtlasUrl = PublicUrl(atlasPath),
                SpriteUrl = crop == null ? null : PublicUrl(crop),
                Status = crop == null ? "error" : "ready",
                Error = crop == null ? "TpacTool Atlas export succeeded, but SpriteData crop failed." : null,
                AtlasWidth = GetPngWidth(atlasPath),
                AtlasHeight = GetPngHeight(atlasPath)
            };
        }

        private static object FindTextureAsset(IEnumerable loadedAssets, List<string> candidates)
        {
            foreach (var asset in loadedAssets)
            {
                if (asset == null) continue;
                var typeName = asset.GetType().FullName ?? string.Empty;
                if (typeName.IndexOf("Texture", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var name = GetString(asset, "Name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                foreach (var candidate in candidates)
                {
                    if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetFileNameWithoutExtension(name), candidate, StringComparison.OrdinalIgnoreCase))
                        return asset;
                }
            }
            return null;
        }

        private static void ExportTextureToPng(object texture, string path)
        {
            var exporterType = _tpacIoAssembly.GetType("TpacTool.IO.TextureExporter", true);
            var textureType = _tpacLibAssembly.GetType("TpacTool.Lib.Texture", true);
            var methods = exporterType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "ExportToFile" && !m.IsGenericMethodDefinition)
                .Where(m => m.GetParameters().Length == 3)
                .Where(m => m.GetParameters()[0].ParameterType == typeof(string));

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (!parameters[1].ParameterType.IsAssignableFrom(texture.GetType()) && !parameters[1].ParameterType.IsAssignableFrom(textureType)) continue;
                if (!parameters[2].ParameterType.IsEnum) continue;
                var option = Enum.ToObject(parameters[2].ParameterType, 0);
                method.Invoke(null, new[] { (object)path, texture, option });
                return;
            }
            throw new MissingMethodException("TpacTool.IO.TextureExporter.ExportToFile(string, Texture, TextureExportOption)");
        }

        private static List<string> BuildAtlasCandidates(string categoryName, int sheetId)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(categoryName)) return list;
            var ids = new[] { sheetId, sheetId + 1, sheetId - 1 };
            foreach (var id in ids)
            {
                if (id <= 0) continue;
                AddUnique(list, "ui_" + categoryName + "_" + id);
                AddUnique(list, categoryName + "_" + id);
            }
            return list;
        }

        private static string TryFindLooseAtlas(List<string> candidates)
        {
            if (string.IsNullOrWhiteSpace(_gameRoot)) return null;
            var roots = new[]
            {
                Path.Combine(_gameRoot, "Modules", "Native", "Assets", "GauntletUI"),
                Path.Combine(_gameRoot, "Modules", "Native", "AssetSources", "GauntletUI"),
                Path.Combine(_gameRoot, "Modules", "Native", "GUI"),
                Path.Combine(_gameRoot, "Modules", "Native")
            };
            var extensions = new[] { ".png", ".dds", ".tga" };
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var candidate in candidates)
                foreach (var ext in extensions)
                {
                    var exact = Path.Combine(root, candidate + ext);
                    if (File.Exists(exact)) return exact;
                }
            }
            return null;
        }

        private static void EnsureTpacToolLoaded()
        {
            if (_tpacLibAssembly != null && _tpacIoAssembly != null) return;
            var candidates = new List<string>();
            var env = Environment.GetEnvironmentVariable("TPACTOOL_HOME");
            if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env);
            if (!string.IsNullOrWhiteSpace(_gameRoot))
            {
                candidates.Add(Path.Combine(_gameRoot, "TpacTool"));
                candidates.Add(Path.Combine(_gameRoot, "Tools", "TpacTool"));
                candidates.Add(Path.Combine(_gameRoot, "Tools"));
            }
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TpacTool"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "TpacTool"));

            foreach (var directory in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(directory)) continue;
                var lib = Directory.GetFiles(directory, "TpacTool.Lib.dll", SearchOption.AllDirectories).FirstOrDefault();
                var io = Directory.GetFiles(directory, "TpacTool.IO.dll", SearchOption.AllDirectories).FirstOrDefault();
                if (lib == null || io == null) continue;
                _tpacToolDirectory = Path.GetDirectoryName(lib);
                _tpacLibAssembly = Assembly.LoadFrom(lib);
                _tpacIoAssembly = Assembly.LoadFrom(io);
                HtmlUiLogger.Info("Native Atlas: TpacTool detected at " + _tpacToolDirectory);
                return;
            }
        }

        private static string FindAssetPackageDirectory()
        {
            var candidates = new[]
            {
                Path.Combine(_gameRoot, "Modules", "Native", "EmAssetPackages"),
                Path.Combine(_gameRoot, "Modules", "Native", "AssetPackages")
            };
            return candidates.FirstOrDefault(Directory.Exists);
        }

        private static SpriteRequest CaptureRequest(JToken payload)
        {
            var brushName = payload?["name"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(brushName)) return null;
            var context = FindActiveUiContext();
            if (context == null) throw new InvalidOperationException("No active Gauntlet UIContext was found.");
            var brush = context.GetBrush(brushName);
            if (brush == null) throw new KeyNotFoundException("Brush not found: " + brushName);
            var sprite = brush.Sprite;
            if (sprite == null) throw new InvalidOperationException("Brush has no Sprite: " + brushName);
            var part = GetPropertyValue(sprite, "SpritePart") ?? GetPropertyValue(sprite, "BaseSprite");
            var category = GetPropertyValue(part, "Category");
            return new SpriteRequest
            {
                BrushName = brushName,
                SpriteName = GetString(sprite, "Name") ?? string.Empty,
                CategoryName = GetString(category, "Name") ?? string.Empty,
                SheetId = GetInt(part, "SheetID") ?? -1,
                SheetX = GetInt(part, "SheetX") ?? 0,
                SheetY = GetInt(part, "SheetY") ?? 0,
                Width = GetInt(sprite, "Width") ?? GetInt(part, "Width") ?? 0,
                Height = GetInt(sprite, "Height") ?? GetInt(part, "Height") ?? 0
            };
        }

        private static UIContext FindActiveUiContext()
        {
            var top = ScreenManager.TopScreen;
            if (top == null) return null;
            foreach (var layer in top.Layers)
                if (layer is GauntletLayer gauntlet && gauntlet.IsActive) return gauntlet.UIContext;
            foreach (var layer in top.Layers)
                if (layer is GauntletLayer fallback) return fallback.UIContext;
            return null;
        }

        private static string CropPng(string source, SpriteRequest request, string hash)
        {
            if (!IsValidPng(source)) return null;
            var target = Path.Combine(_cacheDirectory, "sprite-" + hash + ".png");
            try
            {
                using (var bitmap = new Bitmap(source))
                {
                    var x = Math.Max(0, Math.Min(request.SheetX, bitmap.Width - 1));
                    var y = Math.Max(0, Math.Min(request.SheetY, bitmap.Height - 1));
                    var w = Math.Max(1, Math.Min(request.Width, bitmap.Width - x));
                    var h = Math.Max(1, Math.Min(request.Height, bitmap.Height - y));
                    using (var crop = bitmap.Clone(new Rectangle(x, y, w, h), PixelFormat.Format32bppArgb))
                    {
                        crop.Save(target, ImageFormat.Png);
                    }
                }
                return IsValidPng(target) ? target : null;
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Warn("Native Atlas crop failed: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static int GetPngWidth(string path)
        {
            try { using (var b = new Bitmap(path)) return b.Width; } catch { return 0; }
        }

        private static int GetPngHeight(string path)
        {
            try { using (var b = new Bitmap(path)) return b.Height; } catch { return 0; }
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

        private static string FindGameRoot()
        {
            try
            {
                var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(HtmlUiNativeAtlasAssetService).Assembly.Location));
                for (var i = 0; i < 8 && dir != null; i++)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, "Modules", "Native"))) return dir.FullName;
                    dir = dir.Parent;
                }
            }
            catch { }
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

        private static void AddUnique(List<string> list, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !list.Contains(value, StringComparer.OrdinalIgnoreCase)) list.Add(value);
        }

        private static string SafeHash(string value)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var ch in value ?? string.Empty) hash = (hash ^ ch) * 16777619u;
                return hash.ToString("x8");
            }
        }

        private static string PublicUrl(string path) => _publicHost + "/" + Path.GetFileName(path);
    }
}
