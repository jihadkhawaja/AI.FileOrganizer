using System.ComponentModel;
using System.Text;

namespace AI.FileOrganizer.Tools;

[Description("Tools for organizing folders in directories")]
public static class FolderTools
{
    [Description("Lists all folders in the specified directory")]
    public static string ListFolders(
        [Description("The directory path to list folders from")] string directory)
    {
        if (!Directory.Exists(directory))
            return "Directory does not exist.";

        var folders = Directory.GetDirectories(directory);
        if (folders.Length == 0)
            return "No folders found.";

        return string.Join(Environment.NewLine, folders.Select(Path.GetFileName));
    }

    [Description("Moves a folder to a new parent directory")]
    public static string MoveFolder(
        [Description("The full path of the folder to move")] string sourceFolderPath,
        [Description("The target parent directory to move the folder into")] string destinationDirectory)
    {
        if (!Directory.Exists(sourceFolderPath))
            return "Source folder does not exist.";
        if (!Directory.Exists(destinationDirectory))
            return "Destination directory does not exist.";

        var folderName = Path.GetFileName(sourceFolderPath);
        var destPath = Path.Combine(destinationDirectory, folderName);
        if (Directory.Exists(destPath))
            return $"A folder named '{folderName}' already exists in the destination.";

        Directory.Move(sourceFolderPath, destPath);
        return $"Moved '{folderName}' to {destinationDirectory}";
    }

    [Description("Copies a folder and all its contents to a new parent directory")]
    public static string CopyFolder(
        [Description("The full path of the folder to copy")] string sourceFolderPath,
        [Description("The target parent directory to copy the folder into")] string destinationDirectory)
    {
        if (!Directory.Exists(sourceFolderPath))
            return "Source folder does not exist.";
        if (!Directory.Exists(destinationDirectory))
            return "Destination directory does not exist.";

        var folderName = Path.GetFileName(sourceFolderPath);
        var destPath = Path.Combine(destinationDirectory, folderName);
        if (Directory.Exists(destPath))
            return $"A folder named '{folderName}' already exists in the destination.";

        CopyDirectoryRecursive(sourceFolderPath, destPath);
        return $"Copied '{folderName}' to {destinationDirectory}";
    }

    [Description("Deletes a folder and all its contents")]
    public static string DeleteFolder(
        [Description("The full path of the folder to delete")] string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return "Folder does not exist.";

        Directory.Delete(folderPath, recursive: true);
        return $"Deleted '{Path.GetFileName(folderPath)}'";
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));

        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }

    [Description("Organizes folders in a directory into subfolders based on whether their name matches a pattern. Set preview to true to only see the categorization without moving.")]
    public static string OrganizeFoldersByNamePattern(
        [Description("The directory path to organize folders in")] string directory,
        [Description("The pattern to match folder names against")] string pattern,
        [Description("If true, only shows categorization without moving folders")] bool preview = false)
    {
        if (!Directory.Exists(directory))
            return "Directory does not exist.";

        var folders = Directory.GetDirectories(directory);

        if (preview)
        {
            var matching = folders.Where(f => Path.GetFileName(f)!.Contains(pattern, StringComparison.OrdinalIgnoreCase)).ToList();
            var nonMatching = folders.Except(matching).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Folders matching '{pattern}':");
            foreach (var folder in matching)
                sb.AppendLine($"  {Path.GetFileName(folder)}");
            sb.AppendLine($"Folders not matching '{pattern}':");
            foreach (var folder in nonMatching)
                sb.AppendLine($"  {Path.GetFileName(folder)}");
            return sb.ToString();
        }

        int moved = 0;
        var matchDir = Path.Combine(directory, $"folders_pattern_{pattern}");
        var otherDir = Path.Combine(directory, "folders_other");
        Directory.CreateDirectory(matchDir);
        Directory.CreateDirectory(otherDir);

        foreach (var folder in folders)
        {
            var folderName = Path.GetFileName(folder)!;
            if (folderName == $"folders_pattern_{pattern}" || folderName == "folders_other")
                continue;

            string destDir = folderName.Contains(pattern, StringComparison.OrdinalIgnoreCase) ? matchDir : otherDir;
            var destPath = Path.Combine(destDir, folderName);
            if (!Directory.Exists(destPath))
            {
                Directory.Move(folder, destPath);
                moved++;
            }
        }
        return $"Organized {moved} folders by name pattern '{pattern}' in {directory}.";
    }

    [Description("Organizes folders in a directory into subfolders based on how many files they contain (empty, small, large). Set preview to true to only see the categorization without moving.")]
    public static string OrganizeFoldersBySize(
        [Description("The directory path to organize folders in")] string directory,
        [Description("Threshold for small folders (default: 5)")] int smallThreshold = 5,
        [Description("Threshold for large folders (default: 20)")] int largeThreshold = 20,
        [Description("If true, only shows categorization without moving folders")] bool preview = false)
    {
        if (!Directory.Exists(directory))
            return "Directory does not exist.";

        var folders = Directory.GetDirectories(directory);

        if (preview)
        {
            var sb = new StringBuilder();
            foreach (var folder in folders)
            {
                int fileCount = Directory.GetFiles(folder).Length;
                sb.AppendLine($"{Path.GetFileName(folder)}: {fileCount} files");
            }
            return sb.ToString();
        }

        int moved = 0;
        var emptyDir = Path.Combine(directory, "folders_empty");
        var smallDir = Path.Combine(directory, "folders_small");
        var largeDir = Path.Combine(directory, "folders_large");
        Directory.CreateDirectory(emptyDir);
        Directory.CreateDirectory(smallDir);
        Directory.CreateDirectory(largeDir);

        foreach (var folder in folders)
        {
            var folderName = Path.GetFileName(folder)!;
            if (folderName is "folders_empty" or "folders_small" or "folders_large")
                continue;

            int fileCount = Directory.GetFiles(folder).Length;
            string destDir = fileCount == 0 ? emptyDir
                : fileCount <= smallThreshold ? smallDir
                : fileCount >= largeThreshold ? largeDir
                : smallDir;

            var destPath = Path.Combine(destDir, folderName);
            if (!Directory.Exists(destPath))
            {
                Directory.Move(folder, destPath);
                moved++;
            }
        }
        return $"Organized {moved} folders by size in {directory}.";
    }
}
