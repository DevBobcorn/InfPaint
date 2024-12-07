using NaturalSort.Extension;
using System.Text.Json;

namespace InfiniCore.FileTree
{
    public static class FileTreeHelper
    {
        private static readonly Dictionary<string, string> nameOverrideTable = new();

        public static void LoadFileNameOverrides(string rootPath, string[] overrideFiles)
        {
            Console.WriteLine("Loading file name overrides...");

            // Clear loaded stuffs
            nameOverrideTable.Clear();

            foreach (string file in overrideFiles)
            {
                var fileFullPath = Path.Combine(rootPath, file);
                var baseFullPath = Path.GetDirectoryName(fileFullPath);

                if (File.Exists(fileFullPath))
                {
                    var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(fileFullPath))!;

                    foreach (var entry in entries)
                    {
                        var entryPath = Path.Combine(baseFullPath!, entry.Key).Replace('\\', '/');

                        nameOverrideTable.Add(entryPath, entry.Value);
                    }
                }
            }

            Console.WriteLine("Loaded file name overrides");
        }

        private static FileTreeNode GetFileNode(string basePath, string fullPath)
        {
            nameOverrideTable.TryGetValue(fullPath, out string? nameOverride);

            if (Directory.Exists(fullPath))
            {
                return new FileTreeNode(FileTreeNodeType.Folder, basePath,
                    nameOverride, !Directory.EnumerateFileSystemEntries(fullPath).Any());
            }

            if (File.Exists(fullPath))
            {
                return new FileTreeNode(FileTreeNodeType.File, basePath, nameOverride, false);
            }

            throw new FileNotFoundException($"No file or folder found at {fullPath}");
        }

        public static Dictionary<string, FileTreeNode> GetFileTree(string fileServerRoot, string path, Func<string, bool> filter)
        {
            if (Directory.Exists(fileServerRoot))
            {
                try
                {
                    if (path.StartsWith('/') || path.StartsWith('\\'))
                    {
                        path = path[1..];
                    }

                    Console.WriteLine($"Requesting entries under {fileServerRoot}/{path}");

                    var entries = Directory.EnumerateFileSystemEntries(Path.Combine(fileServerRoot, path), "*", SearchOption.TopDirectoryOnly)
                        .OrderBy(x => Path.GetFileName(x), StringComparison.OrdinalIgnoreCase.WithNaturalSort())
                        .Where(x => filter(x)).ToDictionary(x => Path.GetFileName(x), x => GetFileNode(path, Path.GetFullPath(x).Replace('\\', '/')));

                    return entries;
                }
                catch (Exception)
                {
                    return [];
                }
            }

            throw new FileNotFoundException($"No folder found at {fileServerRoot}");
        }
    }
}
