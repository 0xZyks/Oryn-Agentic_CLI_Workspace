namespace Oryn.App;

public enum ChatPreviewRole { User, Oryn }
public enum ChatMessageStatus { Complete, Generating, Cancelled, Interrupted, Failed, Truncated }
public sealed record ChatPreviewMessage(ChatPreviewRole Role, string Text)
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public long Sequence { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public ChatMessageStatus Status { get; init; } = ChatMessageStatus.Complete;
    public IReadOnlyList<string> ContextSources { get; init; } = [];
}

/// <summary>Global or element conversation state.</summary>
public sealed class ChatPreviewState
{
    private readonly List<ChatPreviewMessage> _messages = [];
    public IReadOnlyList<ChatPreviewMessage> Messages => _messages.AsReadOnly();
    public string Draft { get; set; } = "";
    public bool IsThinking { get; private set; }
    public string Error { get; private set; } = "";
    public string ContextNotice { get; set; } = "";
    public ChatTextSelection Selection { get; } = new();
    private long _sequence;

    public void Load(IEnumerable<ChatPreviewMessage> messages)
    { Selection.Clear(); _messages.Clear(); _messages.AddRange(messages); _sequence = _messages.Count == 0 ? 0 : _messages.Max(m => m.Sequence); }
    public void Prepend(IEnumerable<ChatPreviewMessage> messages)
    { var known = _messages.Select(m => m.Id).ToHashSet(); _messages.InsertRange(0, messages.Where(m => known.Add(m.Id))); }
    public void BeginReply()
    { IsThinking = true; Error = ""; _messages.Add(new(ChatPreviewRole.Oryn, "") { Sequence = ++_sequence, Status = ChatMessageStatus.Generating }); }
    public void AttachSources(IReadOnlyList<string> sources)
    { if (_messages.Count > 0 && _messages[^1].Role == ChatPreviewRole.User) _messages[^1] = _messages[^1] with { ContextSources = sources }; }
    public void AppendReply(string text)
    {
        if (IsThinking && _messages.Count > 0) _messages[^1] = _messages[^1] with { Text = _messages[^1].Text + text };
    }
    public void FailReply(string error) { IsThinking = false; Error = error; }
    public void ClearErrorIf(string expected) { if (Error == expected) Error = ""; }
    public void FinishReply(string text, ChatMessageStatus status, string error = "")
    {
        if (_messages.Count > 0 && _messages[^1].Role == ChatPreviewRole.Oryn)
            _messages[^1] = _messages[^1] with { Text = text, Status = status };
        IsThinking = false; Error = error;
    }
    public void Rollback(Guid userId, Guid assistantId, string prompt, string error)
    {
        _messages.RemoveAll(m => m.Id == userId || m.Id == assistantId);
        Draft = Draft.Length == 0 ? prompt : prompt + "\n" + Draft;
        IsThinking = false; Error = error;
    }
    public void EndReply() { IsThinking = false; }

    public bool Send()
    {
        string prompt = Draft.Trim();
        if (IsThinking || prompt.Length == 0) return false;
        _messages.Add(new(ChatPreviewRole.User, prompt) { Sequence = ++_sequence });
        IsThinking = true;
        Error = "";
        Draft = "";
        return true;
    }

    public bool CompleteReply()
    {
        if (!IsThinking) return false;
        _messages.Add(new(ChatPreviewRole.Oryn, "Aucun modèle d’inférence n’est connecté.") { Sequence = ++_sequence, Status = ChatMessageStatus.Failed });
        IsThinking = false;
        return true;
    }
}
