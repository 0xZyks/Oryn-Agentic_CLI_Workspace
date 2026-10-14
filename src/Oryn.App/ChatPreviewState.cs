namespace Oryn.App;

public enum ChatPreviewRole { User, Oryn }
public sealed record ChatPreviewMessage(ChatPreviewRole Role, string Text);

/// <summary>Session-only conversation for the M0 layout preview; no inference or storage.</summary>
public sealed class ChatPreviewState
{
    private readonly List<ChatPreviewMessage> _messages = [];
    public IReadOnlyList<ChatPreviewMessage> Messages => _messages.AsReadOnly();
    public string Draft { get; set; } = "";
    public bool IsThinking { get; private set; }

    public bool Send()
    {
        string prompt = Draft.Trim();
        if (IsThinking || prompt.Length == 0) return false;
        _messages.Add(new(ChatPreviewRole.User, prompt));
        IsThinking = true;
        Draft = "";
        return true;
    }

    public bool CompleteReply()
    {
        if (!IsThinking) return false;
        _messages.Add(new(ChatPreviewRole.Oryn, "Hello World!"));
        IsThinking = false;
        return true;
    }
}
