using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace AI.FileOrganizer.Tools;

[Description("Tools for organizing files in directories")]
public static class FileTools
{
    [Description("Lists all files in the specified directory")]
    public static string ListFiles(
        [Description("The directory path to list files from")] string directory)
    {
        if (!Directory.Exists(directory))
            return "Directory does not exist.";

        var files = Directory.GetFiles(directory);
        if (files.Length == 0)
            return "No files found.";

        return string.Join(Environment.NewLine, files.Select(Path.GetFileName));
    }

    [Description("Moves a file to a new directory")]
    public static string MoveFile(
        [Description("The full path to the source file")] string sourceFilePath,
        [Description("The target directory path")] string destinationDirectory)
    {
        if (!File.Exists(sourceFilePath))
            return "Source file does not exist.";
        if (!Directory.Exists(destinationDirectory))
            return "Destination directory does not exist.";

        var fileName = Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(destinationDirectory, fileName);
        File.Move(sourceFilePath, destPath, overwrite: true);
        return $"Moved {fileName} to {destinationDirectory}";
    }

    [Description("Copies a file to a new directory")]
    public static string CopyFile(
        [Description("The full path to the source file")] string sourceFilePath,
        [Description("The target directory path")] string destinationDirectory)
    {
        if (!File.Exists(sourceFilePath))
            return "Source file does not exist.";
        if (!Directory.Exists(destinationDirectory))
            return "Destination directory does not exist.";

        var fileName = Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(destinationDirectory, fileName);
        File.Copy(sourceFilePath, destPath, overwrite: true);
        return $"Copied {fileName} to {destinationDirectory}";
    }

    [Description("Deletes a file")]
    public static string DeleteFile(
        [Description("The full path to the file to delete")] string filePath)
    {
        if (!File.Exists(filePath))
            return "File does not exist.";

        File.Delete(filePath);
        return $"Deleted {Path.GetFileName(filePath)}";
    }

    [Description("Organizes files in a directory into subdirectories grouped by file extension. Set preview to true to only see the categorization without moving files.")]
    public static string OrganizeByExtension(
        [Description("The directory path to organize files in")] string directory,
        [Description("If true, only shows categorization without moving files")] bool preview = false)
    {
        if (!Directory.Exists(directory))
            return "Directory does not exist.";

        var files = Directory.GetFiles(directory);

        if (preview)
        {
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

    [Description("Categorizes files in a directory by context of file name using detected keywords or patterns")]
    public static string CategorizeByNameContext(
        [Description("The directory path to categorize files in")] string directory)
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

    [Description("Categorizes text files in a directory by their content, grouping by the most frequent keyword")]
    public static string CategorizeByContentContext(
        [Description("The directory path to categorize text files in")] string directory)
    {
        if (!Directory.Exists(directory))
            return "Directory does not exist.";

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "for", "with", "that", "this", "from", "are",
            "was", "but", "not", "you", "all", "can", "has", "have"
        };

        var files = Directory.GetFiles(directory, "*.txt");
        var keywordGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string content;
            try { content = File.ReadAllText(file); }
            catch { content = ""; }

            var keyword = Regex.Matches(content.ToLowerInvariant(), @"\b[a-z]{3,}\b")
                .Select(m => m.Value)
                .Where(w => !stopWords.Contains(w))
                .GroupBy(w => w)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? "uncategorized";

            if (!keywordGroups.TryGetValue(keyword, out var list))
            {
                list = [];
                keywordGroups[keyword] = list;
            }
            list.Add(Path.GetFileName(file));
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

    [Description("Organizes all items in a directory by type, moving files and folders into separate subfolders. Set preview to true to only see the categorization without moving.")]
    public static string OrganizeAllByType(
        [Description("The directory path to organize")] string directory,
        [Description("If true, only shows categorization without moving")] bool preview = false)
    {
        if (!Directory.Exists(directory))
            return "Directory does not exist.";

        var files = Directory.GetFiles(directory);
        var folders = Directory.GetDirectories(directory);

        if (preview)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Files:");
            foreach (var file in files)
                sb.AppendLine($"  {Path.GetFileName(file)}");
            sb.AppendLine("Folders:");
            foreach (var folder in folders)
                sb.AppendLine($"  {Path.GetFileName(folder)}");
            return sb.ToString();
        }

        var filesDir = Path.Combine(directory, "all_files");
        var foldersDir = Path.Combine(directory, "all_folders");
        Directory.CreateDirectory(filesDir);
        Directory.CreateDirectory(foldersDir);

        int movedFiles = 0, movedFolders = 0;
        foreach (var file in files)
        {
            var destPath = Path.Combine(filesDir, Path.GetFileName(file));
            if (!File.Exists(destPath))
            {
                File.Move(file, destPath);
                movedFiles++;
            }
        }
        foreach (var folder in folders)
        {
            var folderName = Path.GetFileName(folder);
            if (folderName is "all_files" or "all_folders")
                continue;

            var destPath = Path.Combine(foldersDir, folderName);
            if (!Directory.Exists(destPath))
            {
                Directory.Move(folder, destPath);
                movedFolders++;
            }
        }
        return $"Organized {movedFiles} files and {movedFolders} folders by type in {directory}.";
    }
}
