using System.Globalization;
using Oryn.App;
using SkiaSharp;

namespace Oryn.Ui.Sdl;

public sealed partial class WorkspacePainter
{
    private enum ChatRowKind { Text, Label, Divider, Copy, Thinking, Space, CodeHeader }
    private sealed record ChatVisualLine(string Text, int Start, int End, int BreakEnd, int[] Offsets, float[] Advances,
        IReadOnlyList<ChatGlyph>? Styled = null, float Indent = 0, string Prefix = "", int Quote = 0, bool Code = false,
        string? CodeHeader = null, bool Rule = false, bool TableHeader = false, IReadOnlyList<float>? Columns = null);
    private sealed record ChatRow(ChatRowKind Kind, string Text = "", Guid? MessageId = null,
        ChatVisualLine? Body = null, bool User = false, bool Error = false);
    private sealed record ChatGeometry(ChatPreviewState Chat, SKRect Viewport, List<ChatRow> Rows, int Top, int Scroll, float TextX);
    private ChatGeometry? _chatGeometry;
    private ChatGeometry? _previousChatGeometry;
    private ChatPreviewState? _wrappedChat;
    private readonly Dictionary<Guid, (string Text, float Width, List<ChatVisualLine> Lines)> _chatWrapCache = [];
    private long _lastChatDragScroll;
    private readonly record struct ChatGlyph(string Text, int Start, int End, float Width, bool Newline, bool Break,
        MarkdownStyle Style = MarkdownStyle.None, string? Url = null);

    private List<ChatVisualLine> WrapChat(string text, float width)
    {
        List<ChatGlyph> glyphs = [];
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            string value = elements.GetTextElement();
            bool newline = value is "\n" or "\r\n" or "\r";
            string visible = newline ? "" : TerminalText.Sanitize(value);
            glyphs.Add(new(visible, elements.ElementIndex, elements.ElementIndex + value.Length,
                _fonts.MeasureText(visible, _font), newline, !newline && string.IsNullOrWhiteSpace(value)));
        }
        List<ChatVisualLine> lines = [];
        int start = 0;
        while (start < glyphs.Count)
        {
            int end = start, lastBreak = -1; float used = 0;
            while (end < glyphs.Count && !glyphs[end].Newline)
            {
                if (end > start && used + glyphs[end].Width > width) break;
                used += glyphs[end].Width;
                if (glyphs[end].Break) lastBreak = end + 1;
                end++;
            }
            if (end < glyphs.Count && !glyphs[end].Newline && lastBreak > start) end = lastBreak;
            bool newline = end < glyphs.Count && glyphs[end].Newline;
            int firstOffset = glyphs[start].Start, lastOffset = end == start ? firstOffset : glyphs[end - 1].End;
            int[] offsets = new int[end - start + 1]; float[] advances = new float[offsets.Length];
            offsets[0] = firstOffset;
            for (int i = start; i < end; i++)
            { offsets[i - start + 1] = glyphs[i].End; advances[i - start + 1] = advances[i - start] + glyphs[i].Width; }
            lines.Add(new(string.Concat(glyphs.Skip(start).Take(end - start).Select(g => g.Text)), firstOffset, lastOffset,
                newline ? glyphs[end].End : lastOffset, offsets, advances));
            start = newline ? end + 1 : end;
        }
        if (glyphs.Count == 0 || glyphs[^1].Newline)
            lines.Add(new("", text.Length, text.Length, text.Length, [text.Length], [0]));
        return lines;
    }

    private void DrawChat(PanelBounds b, ChatPreviewState chat, float titleInset = 0)
    {
        if (b.Width < 6 || b.Height < 3) { Text("Enlarge window", b.X, b.Y, Muted); return; }
        bool interactive = ReferenceEquals(chat, _ui.CurrentChat) && _ui.Overlay is WorkspaceOverlay.None or WorkspaceOverlay.ElementChat;
        _canvas.Save(); _canvas.Translate(0, titleInset);
        UiText(interactive && _ui.Overlay == WorkspaceOverlay.ElementChat ? "Discussion / " + (_ui.OverlayTarget?.Name ?? "element") : "Discussion du projet",
            b.X + 2, b.Y, TextColor, b.Width - 4, title: true);
        _canvas.Restore();
        UiText(_ui.BackendStatus, b.X + 2, b.Y + 1, Muted, b.Width - 4, small: true);
        int inner = Math.Max(1, b.Width - 5);
        List<Line> draft = Wrap(_ui.Editor.Text, inner);
        int inputHeight = Math.Min(Math.Max(3, draft.Count + 2), Math.Max(3, b.Height / 2));
        PanelBounds input = new(b.X + 1, b.Y + b.Height - inputHeight, b.Width - 2, inputHeight);
        int bottomGap = b.Height >= 7 ? 10 + Spacing.Gap : Spacing.Gap;
        float textX = (b.X + 2) * CW, textWidth = Math.Max(CW, (b.Width - 4) * CW);
        if (interactive && !ReferenceEquals(chat, _wrappedChat)) { _chatWrapCache.Clear(); _wrappedChat = chat; }
        List<ChatRow> history = [];
        if (chat.Messages.Count == 0)
        {
            history.Add(new(ChatRowKind.Text, "Clarifier une idée. Explorer le système."));
            history.Add(new(ChatRowKind.Text, "Relier les concepts et discuter les choix."));
            history.Add(new(ChatRowKind.Text, $"Profil : {_ui.ActiveModelProfile} · Agents / setup pour configurer"));
        }
        foreach (ChatPreviewMessage message in chat.Messages)
        {
            bool user = message.Role == ChatPreviewRole.User;
            if (user) history.Add(new(ChatRowKind.Divider));
            history.Add(new(ChatRowKind.Label, user ? message.ContextSources.Count > 0 ? "Vous / source jointe" : "Vous"
                : message.Status == ChatMessageStatus.Complete ? "Oryn" : "Oryn / " + message.Status, User: user));
            if (!_chatWrapCache.TryGetValue(message.Id, out var cached) || !ReferenceEquals(cached.Text, message.Text) || cached.Width != textWidth)
            { cached = (message.Text, textWidth, LayoutMarkdown(message.Text, textWidth)); _chatWrapCache[message.Id] = cached; }
            if (message.Text.Length > 0 || message.Status != ChatMessageStatus.Generating)
                foreach (ChatVisualLine line in cached.Lines)
                    history.Add(new(line.CodeHeader is not null ? ChatRowKind.CodeHeader : line.Rule ? ChatRowKind.Divider : ChatRowKind.Text,
                        line.CodeHeader ?? line.Text, message.Id, line));
            if (message.Status == ChatMessageStatus.Generating && chat.IsThinking)
                history.Add(new(ChatRowKind.Thinking, "Réflexion…"));
            if (user) history.Add(new(ChatRowKind.Divider));
            if (message.Text.Length > 0) history.Add(new(ChatRowKind.Copy, MessageId: message.Id));
            history.Add(new(ChatRowKind.Space));
        }
        if (!string.IsNullOrWhiteSpace(chat.ContextNotice))
            foreach (var line in WrapChat(chat.ContextNotice, textWidth)) history.Add(new(ChatRowKind.Text, line.Text));
        if (!string.IsNullOrWhiteSpace(chat.Error))
        {
            foreach (var line in WrapChat("Erreur : " + chat.Error, textWidth)) history.Add(new(ChatRowKind.Text, line.Text, Error: true));
            history.Add(new(ChatRowKind.Copy, Error: true));
        }
        int space = Math.Max(0, input.Y - b.Y - 3 - (bottomGap > 0 ? 1 : 0));
        SKRect viewport = Pixels(new(b.X, b.Y + 2, b.Width, space));
        int maximum = Math.Max(0, history.Count - space), scroll = Math.Clamp(interactive ? _ui.ChatScroll : 0, 0, maximum);
        // Keep the same visible message while new streamed rows arrive during reading/selection.
        if (interactive && _previousChatGeometry is { } previous && ReferenceEquals(previous.Chat, chat)
            && previous.Viewport.Width == viewport.Width && previous.Scroll == _ui.ChatScroll
            && previous.Rows.Count != history.Count && (_ui.HistoryFocused || scroll > 0))
        {
            int oldIndex = previous.Rows.FindIndex(previous.Top, row => row.Body is not null && row.MessageId is not null);
            if (oldIndex >= 0)
            {
                ChatRow anchor = previous.Rows[oldIndex];
                int newIndex = history.FindIndex(row => row.MessageId == anchor.MessageId && row.Body?.Start == anchor.Body!.Start);
                if (newIndex >= 0) scroll = Math.Clamp(maximum - (newIndex - (oldIndex - previous.Top)), 0, maximum);
            }
        }
        if (interactive && chat.Selection.Dragging && _animationTime - _lastChatDragScroll >= 75)
        {
            if (_pointer.Y < viewport.Top) scroll = Math.Min(maximum, scroll + 1);
            else if (_pointer.Y >= viewport.Bottom) scroll = Math.Max(0, scroll - 1);
            _lastChatDragScroll = _animationTime;
        }
        if (interactive && scroll != _ui.ChatScroll) _ui.SetChatScroll(scroll, maximum);
        int top = maximum - scroll;
        if (interactive)
        {
            _chatGeometry = _previousChatGeometry = new(chat, viewport, history, top, scroll, textX);
            if (chat.Selection.Dragging) SelectChatAt(_pointer.X, _pointer.Y, extend: true);
            _hits.Add((viewport, (x, y) => SelectChatAt(x, y, extend: false)));
        }
        _canvas.Save(); _canvas.ClipRect(viewport);
        for (int i = 0; i < space && top + i < history.Count; i++)
        {
            ChatRow row = history[top + i]; int y = b.Y + 2 + i;
            if (row.Kind == ChatRowKind.Divider)
                LineSegment(textX, y * CH + CH / 2f, textX + textWidth, y * CH + CH / 2f, Border);
            else if (row.Kind == ChatRowKind.CodeHeader)
            {
                Rect(textX, y * CH, textWidth, CH, Raised);
                UiPixels(row.Text, textX + CW, y * CH + 16, Muted, _smallFont);
            }
            else if (row.Kind == ChatRowKind.Copy)
            {
                bool error = row.Error; Guid? id = row.MessageId;
                SKRect button = SKRect.Create(textX - 3, y * CH, 28, CH);
                DrawCopyIcon(button);
                _hits.Add((button, (_, _) => { if (error) _ui.CopyChatError(); else if (id is { } messageId) _ui.CopyChatMessage(messageId); }));
                if (button.Contains(_pointer.X, _pointer.Y))
                    UiPixels(error ? "Copier l'erreur" : "Copier le message", textX + 32, y * CH + 16, Muted, _smallFont);
            }
            else if (row.Kind == ChatRowKind.Label)
            {
                if (row.User) Circle(textX + 5, y * CH + 11, 2.3f, Accent);
                else Star(textX + 5, y * CH + 11, 5.5f, Accent);
                UiText(row.Text, b.X + 4, y, Accent, b.Width - 6);
            }
            else if (row.Kind == ChatRowKind.Thinking)
            {
                ThinkingSpinner(textX + 5, y * CH + 11);
                UiText(row.Text, b.X + 4, y, Muted, b.Width - 6);
            }
            else if (row.Kind == ChatRowKind.Text)
            {
                if (row.Body is { Styled: not null } rich)
                { DrawMarkdownLine(rich, row.MessageId, chat, interactive, textX, textWidth, y); continue; }
                if (interactive && row.MessageId is { } id && row.Body is { } line && _ui.HistoryFocused
                    && chat.Selection.Range(chat.Messages, id) is { } range)
                {
                    int a = Array.FindIndex(line.Offsets, offset => offset >= Math.Max(line.Start, range.Start));
                    int z = Array.FindLastIndex(line.Offsets, offset => offset <= Math.Min(line.End, range.End));
                    if (a >= 0 && z >= a && range.End > line.Start && range.Start < line.BreakEnd)
                    {
                        float startX = line.Advances[a], endX = line.Advances[z];
                        if (line.BreakEnd > line.End && range.End > line.End) endX += CW;
                        Rect(textX + startX, y * CH, Math.Max(0, endX - startX), CH, Accent.WithAlpha(75));
                    }
                }
                _canvas.Save(); _canvas.ClipRect(SKRect.Create(textX, y * CH, textWidth, CH));
                UiPixels(row.Text, textX, y * CH + 16, row.Error ? Accent : TextColor, _font);
                _canvas.Restore();
            }
        }
        _canvas.Restore();
        _canvas.Save(); _canvas.Translate(0, -bottomGap);
        Round(input, Raised, 7);
        Outline(input, _ui.AcceptsText && _ui.ActiveEditor == _ui.Editor ? Accent.WithAlpha(150) : Border, 7);
        DrawEditor(input, _ui.Editor, draft, inner);
        if (input.Width > 30) UiText("Enter send / Shift+Enter newline", input.X + input.Width - 31, input.Y + input.Height - 1, Muted, 29, small: true);
        _canvas.Restore();
        Hit(input, (x, y) => { _ui.FocusHistory(false); PlaceEditor(_ui.Editor, draft, input, x, y); }, offsetY: -bottomGap);
    }

    private void DrawCopyIcon(SKRect bounds)
    {
        if (bounds.Contains(_pointer.X, _pointer.Y)) RoundedPixels(bounds, Selection, 3);
        _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 1.3f; _paint.Color = Muted;
        _canvas.DrawRoundRect(SKRect.Create(bounds.Left + 6, bounds.Top + 4, 10, 11), 1.5f, 1.5f, _paint);
        _paint.Style = SKPaintStyle.Fill; _paint.Color = bounds.Contains(_pointer.X, _pointer.Y) ? Selection : Active;
        SKRect front = SKRect.Create(bounds.Left + 10, bounds.Top + 8, 10, 11);
        _canvas.DrawRoundRect(front, 1.5f, 1.5f, _paint);
        _paint.Style = SKPaintStyle.Stroke; _paint.Color = Accent;
        _canvas.DrawRoundRect(front, 1.5f, 1.5f, _paint); _paint.Style = SKPaintStyle.Fill;
    }

    private void SelectChatAt(float x, float y, bool extend)
    {
        if (_chatGeometry is not { } geometry || geometry.Viewport.Height <= 0
            || !ReferenceEquals(geometry.Chat, _ui.CurrentChat) || _ui.Overlay is not (WorkspaceOverlay.None or WorkspaceOverlay.ElementChat)) return;
        if (extend && !geometry.Chat.Selection.Dragging) return;
        int visible = Math.Max(1, (int)(geometry.Viewport.Height / CH));
        int row = Math.Clamp(geometry.Top + (int)MathF.Floor((y - geometry.Viewport.Top) / CH), geometry.Top,
            Math.Min(geometry.Rows.Count - 1, geometry.Top + visible - 1));
        var candidates = geometry.Rows.Select((value, index) => (value, index)).Where(pair => pair.value.Kind == ChatRowKind.Text && pair.value.Body is not null && pair.value.MessageId is not null).ToList();
        if (candidates.Count == 0)
        { if (!extend) { _ui.FocusHistory(true); geometry.Chat.Selection.Clear(); } return; }
        var nearest = candidates.MinBy(pair => Math.Abs(pair.index - row));
        if (nearest.value?.Body is not { } line || nearest.value.MessageId is not { } id)
        { if (!extend) { _ui.FocusHistory(true); geometry.Chat.Selection.Clear(); } return; }
        float local = x - geometry.TextX - line.Indent;
        int boundary = row < nearest.index ? 0 : row > nearest.index ? line.Offsets.Length - 1
            : Enumerable.Range(0, line.Advances.Length).MinBy(i => Math.Abs(line.Advances[i] - local));
        if (extend) _ui.ExtendChatSelection(id, line.Offsets[boundary]);
        else { _pointer = new(x, y); _ui.BeginChatSelection(id, line.Offsets[boundary]); }
    }
}
