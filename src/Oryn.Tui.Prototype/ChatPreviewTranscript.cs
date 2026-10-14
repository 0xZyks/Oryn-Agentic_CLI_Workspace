using System.Text;
using Oryn.App;
using Terminal.Gui.Text;

namespace Oryn.Tui.Prototype;

internal static class ChatPreviewTranscript
{
    public static string Render(ChatPreviewState chat, int width, bool unicode, int thinkingFrame = 0)
    {
        width = Math.Max(1, width);
        if (chat.Messages.Count == 0) return "Write a prompt below to start a conversation.";
        int bubbleWidth = Math.Max(1, width * 3 / 4);
        StringBuilder result = new();
        foreach (ChatPreviewMessage message in chat.Messages)
        {
            bool user = message.Role == ChatPreviewRole.User;
            string title = (unicode ? user ? "● " : "◆ " : "* ") + (user ? "User" : "Oryn");
            Append(title, user);
            string text = TerminalText.Sanitize(message.Text);
            StringBuilder line = new();
            int columns = 0;
            foreach (Rune rune in text.EnumerateRunes())
            {
                int size = Math.Max(0, rune.GetColumns());
                if (rune.Value == '\n' || columns + size > bubbleWidth && columns > 0)
                { Append(line.ToString(), user); line.Clear(); columns = 0; }
                if (rune.Value == '\n') continue;
                line.Append(rune.ToString()); columns += size;
            }
            Append(line.ToString(), user);
            result.AppendLine();
        }
        if (chat.IsThinking)
        {
            Append((unicode ? "◆ " : "* ") + "Oryn", false);
            if (unicode)
            {
                const string spinner = "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏";
                Append(spinner[thinkingFrame % spinner.Length] + "  Thinking...", false);
            }
            else
            {
                int frame = thinkingFrame % 4;
                Append(frame == 0 ? " *" : " .", false);
                Append((frame == 3 ? "* " : ". ") + (frame == 1 ? "*" : ".") + "   Thinking...", false);
                Append(frame == 2 ? " *" : " .", false);
            }
        }
        return result.ToString().TrimEnd('\r', '\n');

        void Append(string text, bool right)
        {
            if (right) result.Append(' ', Math.Max(0, width - text.GetColumns()));
            result.AppendLine(text);
        }
    }
}
