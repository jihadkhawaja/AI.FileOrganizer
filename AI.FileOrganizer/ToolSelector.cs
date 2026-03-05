using Microsoft.Extensions.AI;

namespace AI.FileOrganizer;

/// <summary>
/// Optimizes tool selection by classifying user intent and returning only relevant tools.
/// Reduces token usage and improves response quality by narrowing the tool set per request.
/// </summary>
public static class ToolSelector
{
    private static readonly Dictionary<IntentType, HashSet<string>> IntentToolMap = new()
    {
        [IntentType.Query] = [
            nameof(Tools.FileTools.ListFiles),
            nameof(Tools.FolderTools.ListFolders),
            nameof(Tools.FileTools.CategorizeByNameContext),
            nameof(Tools.FileTools.CategorizeByContentContext),
        ],
        [IntentType.FileManagement] = [
            nameof(Tools.FileTools.ListFiles),
            nameof(Tools.FileTools.MoveFile),
            nameof(Tools.FileTools.CopyFile),
            nameof(Tools.FileTools.DeleteFile),
        ],
        [IntentType.FolderManagement] = [
            nameof(Tools.FolderTools.ListFolders),
            nameof(Tools.FolderTools.MoveFolder),
            nameof(Tools.FolderTools.CopyFolder),
            nameof(Tools.FolderTools.DeleteFolder),
        ],
        [IntentType.FileOrganization] = [
            nameof(Tools.FileTools.ListFiles),
            nameof(Tools.FileTools.CategorizeByNameContext),
            nameof(Tools.FileTools.CategorizeByContentContext),
            nameof(Tools.FileTools.OrganizeByExtension),
            nameof(Tools.FileTools.OrganizeAllByType),
        ],
        [IntentType.FolderOrganization] = [
            nameof(Tools.FolderTools.ListFolders),
            nameof(Tools.FolderTools.OrganizeFoldersByNamePattern),
            nameof(Tools.FolderTools.OrganizeFoldersBySize),
        ],
    };

    private static readonly (IntentType Intent, string[] Keywords)[] IntentKeywords =
    [
        (IntentType.FileOrganization, ["organize files", "sort files", "group files", "organize by extension",
            "organize by type", "categorize files", "organize my files", "arrange files", "tidy files",
            "clean up files", "file cleanup"]),
        (IntentType.FolderOrganization, ["organize folders", "sort folders", "group folders", "organize by pattern",
            "organize by size", "arrange folders", "tidy folders", "clean up folders", "folder cleanup"]),
        (IntentType.FileManagement, ["move file", "copy file", "delete file", "remove file",
            "rename file", "move this file", "copy this file", "delete this file", "transfer file"]),
        (IntentType.FolderManagement, ["move folder", "copy folder", "delete folder", "remove folder",
            "rename folder", "move this folder", "copy this folder", "delete this folder",
            "move directory", "copy directory", "delete directory", "remove directory"]),
        (IntentType.Query, ["list files", "show files", "what files", "list folders", "show folders",
            "what folders", "what's in", "show me", "preview", "categorize", "what do i have",
            "how many files", "how many folders", "contents of"]),
    ];

    /// <summary>
    /// Detects the user's intent from their input message.
    /// </summary>
    public static IntentType DetectIntent(string userInput)
    {
        var normalized = userInput.ToLowerInvariant().Trim();

        foreach (var (intent, keywords) in IntentKeywords)
        {
            foreach (var keyword in keywords)
            {
                if (normalized.Contains(keyword))
                    return intent;
            }
        }

        return IntentType.General;
    }

    /// <summary>
    /// Filters the full tool list down to only the tools relevant for the detected intent.
    /// Returns all tools for <see cref="IntentType.General"/>.
    /// </summary>
    public static IList<AITool> SelectTools(IList<AITool> allTools, IntentType intent)
    {
        if (intent == IntentType.General)
            return allTools;

        if (!IntentToolMap.TryGetValue(intent, out var allowedNames))
            return allTools;

        return allTools
            .Where(tool => allowedNames.Contains(GetToolName(tool)))
            .ToList();
    }

    /// <summary>
    /// Convenience method: detect intent and select tools in one call.
    /// </summary>
    public static (IntentType Intent, IList<AITool> Tools) SelectToolsForInput(string userInput, IList<AITool> allTools)
    {
        var intent = DetectIntent(userInput);
        var tools = SelectTools(allTools, intent);
        return (intent, tools);
    }

    private static string GetToolName(AITool tool)
    {
        return tool switch
        {
            AIFunction fn => fn.Name,
            _ => tool.GetType().Name
        };
    }
}
