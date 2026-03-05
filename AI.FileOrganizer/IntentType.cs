namespace AI.FileOrganizer;

public enum IntentType
{
    /// <summary>Read-only queries: listing files/folders, categorizing, previewing.</summary>
    Query,
    /// <summary>Managing individual files: move, copy, delete.</summary>
    FileManagement,
    /// <summary>Managing individual folders: move, copy, delete.</summary>
    FolderManagement,
    /// <summary>Bulk file organization: by extension, type, content.</summary>
    FileOrganization,
    /// <summary>Bulk folder organization: by pattern, size.</summary>
    FolderOrganization,
    /// <summary>Ambiguous intent — provide all tools.</summary>
    General
}
