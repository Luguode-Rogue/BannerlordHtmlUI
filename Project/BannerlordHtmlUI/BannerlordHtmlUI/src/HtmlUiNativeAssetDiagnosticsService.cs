using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiNativeAssetDiagnosticsService
    {
        private const int MaxUiMatches = 100;
        private const int MaxSpriteDataMatches = 50;
        private const int MaxEntriesPerRoot = 5000;

        public static Task<object> RunAsync(JToken payload, CancellationToken cancellationToken)
        {
            var moduleDirectory = GetModuleDirectory();
            return Task.Run<object>(() => Run(moduleDirectory, cancellationToken), cancellationToken);
        }

        private static object Run(string moduleDirectory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gameRoot = FindGameRoot(moduleDirectory);
            var nativeDirectory = gameRoot == null
                ? null
                : System.IO.Path.Combine(gameRoot, "Modules", "Native");

            var assetPackageCandidates = new List<object>();
            var uiMatches = new List<object>();
            var spriteDataMatches = new List<object>();
            var tpacToolCandidates = new List<object>();
            var notes = new List<string>();

            AddDirectoryInfo(assetPackageCandidates, "AssetPackages",
                nativeDirectory == null ? null : System.IO.Path.Combine(nativeDirectory, "AssetPackages"));
            AddDirectoryInfo(assetPackageCandidates, "EmAssetPackages",
                nativeDirectory == null ? null : System.IO.Path.Combine(nativeDirectory, "EmAssetPackages"));
            AddDirectoryInfo(assetPackageCandidates, "AssetPackagesSub",
                nativeDirectory == null ? null : System.IO.Path.Combine(nativeDirectory, "AssetPackagesSub"));

            var rootsToScan = new List<string>();
            foreach (var root in new[] {
                nativeDirectory == null ? null : System.IO.Path.Combine(nativeDirectory, "AssetPackages"),
                nativeDirectory == null ? null : System.IO.Path.Combine(nativeDirectory, "EmAssetPackages"),
                nativeDirectory == null ? null : System.IO.Path.Combine(nativeDirectory, "AssetPackagesSub")
            })
            {
                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                    rootsToScan.Add(root);
            }

            foreach (var root in rootsToScan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ScanUiGroup1(root, uiMatches, cancellationToken);
                if (uiMatches.Count >= MaxUiMatches)
                    break;
            }

            if (nativeDirectory != null && Directory.Exists(nativeDirectory))
            {
                var guiCandidates = new[] {
                    System.IO.Path.Combine(nativeDirectory, "GUI"),
                    System.IO.Path.Combine(nativeDirectory, "GUI", "GauntletUI"),
                    System.IO.Path.Combine(nativeDirectory, "GUI", "SpriteParts")
                };

                foreach (var gui in guiCandidates)
                {
                    if (!Directory.Exists(gui)) continue;
                    ScanSpriteData(gui, spriteDataMatches, cancellationToken);
                    if (spriteDataMatches.Count >= MaxSpriteDataMatches)
                        break;
                }
            }

            AddToolCandidate(tpacToolCandidates, moduleDirectory, "TpacTool.Lib.dll");
            AddToolCandidate(tpacToolCandidates, moduleDirectory, "TpacTool.IO.dll");
            AddToolCandidate(tpacToolCandidates, AppDomain.CurrentDomain.BaseDirectory, "TpacTool.Lib.dll");
            AddToolCandidate(tpacToolCandidates, AppDomain.CurrentDomain.BaseDirectory, "TpacTool.IO.dll");
            if (gameRoot != null)
            {
                AddToolCandidate(tpacToolCandidates, gameRoot, "TpacTool.Lib.dll");
                AddToolCandidate(tpacToolCandidates, gameRoot, "TpacTool.IO.dll");
            }

            var filesUnderPackages = assetPackageCandidates
                .OfType<PackageDirectoryInfo>()
                .Where(x => x.Exists)
                .Sum(x => x.FileCount);

            if (gameRoot == null)
                notes.Add("Game root could not be inferred from the framework module directory.");
            else
                notes.Add("Game root inferred by locating an ancestor containing Modules\\Native.");

            if (uiMatches.Count == 0)
                notes.Add("No files whose name contains 'ui_group1' were found in the bounded native AssetPackages scan.");
            else
                notes.Add("Found native AssetPackages entries containing 'ui_group1'.");

            if (tpacToolCandidates.All(x => !GetBooleanProperty(x, "exists")))
                notes.Add("TpacTool DLLs were not found in the checked locations.");

            notes.Add("Diagnostics uses bounded/top-level scans only; it does not recursively enumerate the entire AssetPackages tree.");

            return new
            {
                status = "ok",
                frameworkModuleDirectory = moduleDirectory,
                processBaseDirectory = AppDomain.CurrentDomain.BaseDirectory,
                gameRoot = gameRoot,
                nativeModuleDirectory = nativeDirectory,
                assetPackageRoots = assetPackageCandidates,
                packageFileCount = filesUnderPackages,
                uiGroup1Matches = uiMatches,
                spriteDataMatches = spriteDataMatches,
                tpacToolCandidates = tpacToolCandidates,
                notes = notes
            };
        }

        private sealed class PackageDirectoryInfo
        {
            public string Name { get; set; }
            public string Path { get; set; }
            public bool Exists { get; set; }
            public int FileCount { get; set; }
            public int DirectoryCount { get; set; }
        }

        private static void AddDirectoryInfo(List<object> list, string name, string path)
        {
            var info = new PackageDirectoryInfo
            {
                Name = name,
                Path = path,
                Exists = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path),
                FileCount = 0,
                DirectoryCount = 0
            };

            if (info.Exists)
            {
                try
                {
                    info.FileCount = Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
                        .Take(MaxEntriesPerRoot)
                        .Count();
                }
                catch { }

                try
                {
                    info.DirectoryCount = Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly)
                        .Take(MaxEntriesPerRoot)
                        .Count();
                }
                catch { }
            }

            list.Add(info);
        }

        private static void ScanUiGroup1(string root, List<object> matches, CancellationToken cancellationToken)
        {
            try
            {
                var processed = 0;
                foreach (var file in EnumerateBounded(root, cancellationToken))
                {
                    processed++;
                    var name = System.IO.Path.GetFileName(file);
                    if (name.IndexOf("ui_group1", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    matches.Add(new
                    {
                        fileName = name,
                        extension = System.IO.Path.GetExtension(file),
                        fullPath = file,
                        sizeBytes = SafeLength(file)
                    });

                    if (matches.Count >= MaxUiMatches)
                        return;
                    if (processed >= MaxEntriesPerRoot)
                        return;
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }
        }

        private static void ScanSpriteData(string root, List<object> matches, CancellationToken cancellationToken)
        {
            try
            {
                var processed = 0;
                foreach (var file in EnumerateBounded(root, cancellationToken))
                {
                    processed++;
                    var name = System.IO.Path.GetFileName(file);
                    if (name.IndexOf("SpriteData", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    matches.Add(new
                    {
                        fileName = name,
                        extension = System.IO.Path.GetExtension(file),
                        fullPath = file,
                        sizeBytes = SafeLength(file)
                    });

                    if (matches.Count >= MaxSpriteDataMatches)
                        return;
                    if (processed >= MaxEntriesPerRoot)
                        return;
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }
        }

        private static IEnumerable<string> EnumerateBounded(string root, CancellationToken cancellationToken)
        {
            var files = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly);
            foreach (var file in files.Take(MaxEntriesPerRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
            }
        }

        private static void AddToolCandidate(List<object> list, string directory, string fileName)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            var path = System.IO.Path.Combine(directory, fileName);
            list.Add(new
            {
                name = fileName,
                directory = directory,
                path = path,
                exists = File.Exists(path),
                sizeBytes = File.Exists(path) ? SafeLength(path) : 0L
            });
        }

        private static bool GetBooleanProperty(object instance, string name)
        {
            var value = GetProperty(instance, name);
            return value is bool b && b;
        }

        private static long SafeLength(string path)
        {
            try { return new FileInfo(path).Length; } catch { return 0L; }
        }

        private static string GetModuleDirectory()
        {
            try
            {
                return System.IO.Path.GetDirectoryName(typeof(SubModule).Assembly.Location);
            }
            catch
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        private static string FindGameRoot(string moduleDirectory)
        {
            if (string.IsNullOrWhiteSpace(moduleDirectory)) return null;
            var current = new DirectoryInfo(moduleDirectory);
            while (current != null)
            {
                var native = System.IO.Path.Combine(current.FullName, "Modules", "Native");
                if (Directory.Exists(native)) return current.FullName;
                current = current.Parent;
            }
            return null;
        }

        private static object GetProperty(object instance, string name)
        {
            if (instance == null) return null;
            var property = instance.GetType().GetProperty(name);
            return property == null ? null : property.GetValue(instance, null);
        }
    }
}
