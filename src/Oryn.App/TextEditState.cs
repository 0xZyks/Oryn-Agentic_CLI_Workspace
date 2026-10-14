using System.Globalization;

namespace Oryn.App;

/// <summary>Bounded, grapheme-aware composer editing independent of a UI toolkit.</summary>
public sealed class TextEditState
{
    public const int MaximumLength = 65536;
    public string Text { get; private set; } = "";
    public int Cursor { get; private set; }
    public int Anchor { get; private set; }
    public bool HasSelection => Cursor != Anchor;
    public string Selection => Text[Math.Min(Cursor, Anchor)..Math.Max(Cursor, Anchor)];
    public void Set(string text) { Text = text; Cursor = Anchor = text.Length; }
    public void SelectAll() { Anchor = 0; Cursor = Text.Length; }
    public void Insert(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = string.Concat(text.Where(c => !char.IsControl(c) || c is '\n' or '\t'));
        int start = Math.Min(Cursor, Anchor), end = Math.Max(Cursor, Anchor);
        if (Text.Length - (end - start) + text.Length > MaximumLength) return;
        Text = Text[..start] + text + Text[end..]; Cursor = Anchor = start + text.Length;
    }
    private int Previous(int index) => StringInfo.ParseCombiningCharacters(Text).LastOrDefault(i => i < index);
    private int Next(int index) => StringInfo.ParseCombiningCharacters(Text).FirstOrDefault(i => i > index, Text.Length);
    public void Backspace() { if (!HasSelection && Cursor > 0) Anchor = Previous(Cursor); Insert(""); }
    public void Delete() { if (!HasSelection) Anchor = Next(Cursor); Insert(""); }
    public void Move(string key, bool extend)
    {
        int start = Text.LastIndexOf('\n', Math.Max(0, Cursor - 1), Cursor) + 1;
        int end = Text.IndexOf('\n', Cursor); if (end < 0) end = Text.Length;
        Cursor = key switch
        {
            "LEFT" => !extend && HasSelection ? Math.Min(Cursor, Anchor) : Previous(Cursor),
            "RIGHT" => !extend && HasSelection ? Math.Max(Cursor, Anchor) : Next(Cursor),
            "HOME" => start, "END" => end,
            "UP" => start == 0 ? 0 : PreviousLine(start, Cursor - start),
            "DOWN" => end == Text.Length ? Text.Length : Math.Min(NextLineEnd(end + 1), end + 1 + Cursor - start),
            _ => Cursor
        };
        // Vertical moves must also land on a grapheme boundary.
        Cursor = StringInfo.ParseCombiningCharacters(Text).Append(Text.Length).Last(i => i <= Cursor);
        if (!extend) Anchor = Cursor;
    }
    private int PreviousLine(int start, int column)
    {
        int previous = Text.LastIndexOf('\n', Math.Max(0, start - 2), Math.Max(0, start - 1)) + 1;
        return Math.Min(start - 1, previous + column);
    }
    private int NextLineEnd(int start) { int end = Text.IndexOf('\n', start); return end < 0 ? Text.Length : end; }
    public void Place(int index, bool extend = false)
    {
        Cursor = StringInfo.ParseCombiningCharacters(Text).Append(Text.Length).Last(i => i <= Math.Clamp(index, 0, Text.Length));
        if (!extend) Anchor = Cursor;
    }
}
