using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace AI.FileOrganizer
{
    [Description("Plugin for organizing files in directories")]
    public class Organize
    {
        [KernelFunction, Description("Lists all files in the specified directory")]
        public string[] ListFiles(string directory)
        {
            if (!Directory.Exists(directory))
                return Array.Empty<string>();
            return Directory.GetFiles(directory);
        }

        [KernelFunction, Description("Moves a file to a new directory")]
        public string MoveFile(string sourceFilePath, string destinationDirectory)
        {
            if (!File.Exists(sourceFilePath) || !Directory.Exists(destinationDirectory))
                return "Source file or destination directory does not exist.";

            var fileName = Path.GetFileName(sourceFilePath);
            var destPath = Path.Combine(destinationDirectory, fileName);

            File.Move(sourceFilePath, destPath, overwrite: true);
            return $"Moved {fileName} to {destinationDirectory}";
        }

        [KernelFunction, Description("Categorizes files in a directory by extension")]
        public string CategorizeByExtension(string directory)
        {
            if (!Directory.Exists(directory))
                return "Directory does not exist.";

            var files = Directory.GetFiles(directory);
            var groups = files.GroupBy(f => Path.GetExtension(f).ToLowerInvariant());
            var result = new StringBuilder();

            foreach (var group in groups)
            {
                result.AppendLine($"{group.Key}:");
                foreach (var file in group)
                    result.AppendLine($"  {Path.GetFileName(file)}");
            }
            return result.ToString();
        }

        [KernelFunction, Description("Organizes files in the specified directory into subdirectories by extension")]
        public string OrganizeByExtension(string directory)
        {
            if (!Directory.Exists(directory))
                return "Directory does not exist.";

            var files = Directory.GetFiles(directory);
            int moved = 0;
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(ext))
                    ext = "no_extension";
                var subDir = Path.Combine(directory, ext);
                Directory.CreateDirectory(subDir);
                var destPath = Path.Combine(subDir, Path.GetFileName(file));
                if (!File.Exists(destPath))
                {
                    File.Move(file, destPath);
                    moved++;
                }
            }
            return $"Organized {moved} files by extension in {directory}.";
        }

        [KernelFunction, Description("Categorizes files in a directory by context of file name (e.g., by detected keywords or patterns)")]
        public string CategorizeByNameContext(string directory)
        {
            if (!Directory.Exists(directory))
                return "Directory does not exist.";

            var files = Directory.GetFiles(directory);

            var groups = files.GroupBy(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                var match = Regex.Match(name, @"^[A-Za-z0-9]+");
                return match.Success ? match.Value.ToLowerInvariant() : "other";
            });

            var sb = new StringBuilder();
            foreach (var group in groups)
            {
                sb.AppendLine($"{group.Key}:");
                foreach (var file in group)
                    sb.AppendLine($"  {Path.GetFileName(file)}");
            }
            return sb.ToString();
        }

        [KernelFunction, Description("Categorizes text files in a directory by context of file content (e.g., by detected keywords or topics)")]
        public string CategorizeByContentContext(string directory)
        {
            if (!Directory.Exists(directory))
                return "Directory does not exist.";

            var files = Directory.GetFiles(directory, "*.txt");
            var keywordGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                string content;
                try
                {
                    content = File.ReadAllText(file);
                }
                catch
                {
                    content = "";
                }

                var words = Regex.Matches(content.ToLowerInvariant(), @"\b[a-z]{3,}\b")
                    .Select(m => m.Value)
                    .Where(w => !new[] { "the", "and", "for", "with", "that", "this", "from", "are", "was", "but", "not", "you", "all", "can", "has", "have" }.Contains(w))
                    .GroupBy(w => w)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .ToList();

                var keyword = words.FirstOrDefault() ?? "uncategorized";
                if (!keywordGroups.ContainsKey(keyword))
                    keywordGroups[keyword] = new List<string>();
                keywordGroups[keyword].Add(Path.GetFileName(file));
            }

            var sb = new StringBuilder();
            foreach (var group in keywordGroups)
            {
                sb.AppendLine($"{group.Key}:");
                foreach (var file in group.Value)
                    sb.AppendLine($"  {file}");
            }
            return sb.ToString();
        }

        [KernelFunction, Description("Categorizes image files in a directory by context. If the model is not multimodal, categorizes by extension.")]
        public string CategorizeImagesByContext(string directory, bool isMultimodal = false)
        {
            if (!Directory.Exists(directory))
                return "Directory does not exist.";

            // Common image extensions
            var imageExts = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tiff" };
            var files = Directory.GetFiles(directory)
                .Where(f => imageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToArray();

            if (files.Length == 0)
                return "No image files found.";

            if (!isMultimodal)
            {
                // Fallback: group by extension
                var groups = files.GroupBy(f => Path.GetExtension(f).ToLowerInvariant());
                var sb = new StringBuilder();
                foreach (var group in groups)
                {
                    sb.AppendLine($"{group.Key}:");
                    foreach (var file in group)
                        sb.AppendLine($"  {Path.GetFileName(file)}");
                }
                return sb.ToString();
            }
            else
            {
                // For multimodal: return image file paths for further processing
                var sb = new StringBuilder();
                sb.AppendLine("Image files for context analysis:");
                foreach (var file in files)
                    sb.AppendLine(file);
                return sb.ToString();
            }
        }

        [KernelFunction, Description("Organizes image files in a directory into subfolders based on provided context labels. Provide a mapping of image file path to context label.")]
        public string OrganizeImagesByContext(string directory, IDictionary<string, string> imageContextMap)
        {
            if (!Directory.Exists(directory))
                return "Directory does not exist.";

            int moved = 0;
            foreach (var kvp in imageContextMap)
            {
                var file = kvp.Key;
                var label = string.IsNullOrWhiteSpace(kvp.Value) ? "unknown" : kvp.Value.Trim();
                if (!File.Exists(file)) continue;

                var subDir = Path.Combine(directory, label);
                Directory.CreateDirectory(subDir);
                var destPath = Path.Combine(subDir, Path.GetFileName(file));
                if (!File.Exists(destPath))
                {
                    File.Move(file, destPath);
                    moved++;
                }
            }
            return $"Organized {moved} images by context in {directory}.";
        }
    }
}