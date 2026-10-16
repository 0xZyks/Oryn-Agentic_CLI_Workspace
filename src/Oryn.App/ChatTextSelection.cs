using System.Globalization;

namespace Oryn.App;

public readonly record struct ChatTextPosition(Guid MessageId, int Offset);

/// <summary>Read-only history selection, anchored to message IDs and original UTF-16 grapheme boundaries.</summary>
public sealed class ChatTextSelection
{
    public ChatTextPosition? Anchor { get; private set; }
    public ChatTextPosition? Focus { get; private set; }
    public bool Dragging { get; private set; }
    public void Clear() { Anchor = Focus = null; Dragging = false; }
    public void End() => Dragging = false;
    public void Begin(IReadOnlyList<ChatPreviewMessage> messages, Guid id, int offset)
    {
        if (Position(messages, id, offset) is not { } point) return;
        Anchor = Focus = point; Dragging = true;
    }
    public void Extend(IReadOnlyList<ChatPreviewMessage> messages, Guid id, int offset)
    { if (Anchor is not null && Position(messages, id, offset) is { } point) Focus = point; }
    public void SelectAll(IReadOnlyList<ChatPreviewMessage> messages)
    {
        Clear();
        if (messages.Count == 0) return;
        Anchor = new(messages[0].Id, 0); Focus = new(messages[^1].Id, messages[^1].Text.Length);
    }
    public (int Start, int End)? Range(IReadOnlyList<ChatPreviewMessage> messages, Guid id)
    {
        var bounds = Bounds(messages);
        if (bounds is not { } b) return null;
        int index = Index(messages, id);
        if (index < b.First || index > b.Last) return null;
        int start = index == b.First ? b.Start : 0;
        int end = index == b.Last ? b.End : messages[index].Text.Length;
        return (start, end);
    }
    public string Text(IReadOnlyList<ChatPreviewMessage> messages, Func<string, int, int, string>? projection = null)
    {
        if (Bounds(messages) is not { } b) return "";
        List<string> parts = [];
        for (int i = b.First; i <= b.Last; i++)
        {
            int start = i == b.First ? b.Start : 0, end = i == b.Last ? b.End : messages[i].Text.Length;
            parts.Add(projection is null ? messages[i].Text[start..end] : projection(messages[i].Text, start, end));
        }
        return string.Join("\n\n", parts);
    }
    private (int First, int Start, int Last, int End)? Bounds(IReadOnlyList<ChatPreviewMessage> messages)
    {
        if (Anchor is not { } a || Focus is not { } f) return null;
        int first = Index(messages, a.MessageId), last = Index(messages, f.MessageId);
        if (first < 0 || last < 0) return null;
        int start = Math.Clamp(a.Offset, 0, messages[first].Text.Length), end = Math.Clamp(f.Offset, 0, messages[last].Text.Length);
        if (first > last || first == last && start > end) return (last, end, first, start);
        return (first, start, last, end);
    }
    private static ChatTextPosition? Position(IReadOnlyList<ChatPreviewMessage> messages, Guid id, int offset)
    {
        int index = Index(messages, id);
        if (index < 0) return null;
        string text = messages[index].Text;
        int bounded = Math.Clamp(offset, 0, text.Length);
        int boundary = StringInfo.ParseCombiningCharacters(text).Append(text.Length).Last(i => i <= bounded);
        return new(id, boundary);
    }
    private static int Index(IReadOnlyList<ChatPreviewMessage> messages, Guid id)
    {
        for (int i = 0; i < messages.Count; i++) if (messages[i].Id == id) return i;
        return -1;
    }
}
