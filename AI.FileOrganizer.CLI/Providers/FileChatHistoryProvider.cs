using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace AI.FileOrganizer.CLI.Providers;

/// <summary>
/// A <see cref="ChatHistoryProvider"/> that persists chat history to a local JSON file.
/// </summary>
internal sealed class FileChatHistoryProvider : ChatHistoryProvider
{
    private const int MaxHistoryMessages = 20;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private List<ChatMessage>? _messages;

    public FileChatHistoryProvider(string? filePath = null)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AI.FileOrganizer");

        Directory.CreateDirectory(directory);

        _filePath = filePath ?? Path.Combine(directory, "chat_history.json");
    }

    public string FilePath => _filePath;

    /// <summary>
    /// Clears the persisted chat history file and in-memory cache.
    /// </summary>
    public void Clear()
    {
        _messages = null;
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        _messages ??= LoadFromFile();
        IEnumerable<ChatMessage> history = _messages.Count > MaxHistoryMessages
            ? _messages.Skip(_messages.Count - MaxHistoryMessages)
            : _messages;
        return new ValueTask<IEnumerable<ChatMessage>>(history);
    }

    protected override ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        _messages ??= LoadFromFile();

        var allNewMessages = context.RequestMessages.Concat(context.ResponseMessages ?? []);
        _messages.AddRange(allNewMessages);

        SaveToFile(_messages);

        return default;
    }

    private List<ChatMessage> LoadFromFile()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<ChatMessage>>(json, s_jsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void SaveToFile(List<ChatMessage> messages)
    {
        var json = JsonSerializer.Serialize(messages, s_jsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
