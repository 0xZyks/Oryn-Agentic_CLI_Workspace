using Oryn.App;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Oryn.Tui.Prototype;

internal enum PreviewComponentAction { None, Details, Chat }

internal static class GraphPreviewDialogs
{
    public static PreviewComponentAction Options(IApplication app, PreviewGraphTarget target)
    {
        PreviewComponentAction result = PreviewComponentAction.None;
        using Dialog dialog = new() { Title = "Options: " + target.Name, Width = Dim.Percent(75), Height = 14 };
        dialog.Add(new Label { X = 2, Y = 1, Width = Dim.Fill(2), Height = 2,
            Text = target.Kind + " / DEMO component\nChoose an action for this exact element." });
        Button details = new() { X = 2, Y = 4, Text = "Show _details" };
        Button chat = new() { X = 2, Y = 7, Text = "Show _chat (demo)" };
        Button back = new() { X = 2, Y = 10, Text = "_Back" };
        details.Accepting += (_, args) => { args.Handled = true; result = PreviewComponentAction.Details; app.RequestStop(dialog); };
        chat.Accepting += (_, args) => { args.Handled = true; result = PreviewComponentAction.Chat; app.RequestStop(dialog); };
        back.Accepting += (_, args) => { args.Handled = true; app.RequestStop(dialog); };
        dialog.Add(details, chat, back);
        dialog.Initialized += (_, _) => details.SetFocus();
        app.Run(dialog);
        return result;
    }

    public static void Details(IApplication app, VisualizerPreviewState graph, PreviewGraphTarget target)
    {
        using Dialog dialog = new() { Title = "Details: " + target.Name, Width = Dim.Percent(85), Height = Dim.Percent(85) };
        string parent = target.ParentId is { } id ? graph.Nodes.First(node => node.Id == id).Name : "None (graph card)";
#pragma warning disable CS0618
        TextView body = new() { X = 2, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(4), ReadOnly = true,
            WordWrap = true, BorderStyle = LineStyle.None, Text = $"Component: {target.Name}\nKind: {target.Kind}\nState: {target.Status} (simulated)\nUUID: {target.Id}\nParent: {parent}\n\nOrigin: fictitious M0 template.\nNo source analysis or implementation validation.\n\nShow chat targets this UUID, independently of its parent." };
#pragma warning restore CS0618
        dialog.Add(body, Close(app, dialog));
        app.Run(dialog);
    }

    public static void Chat(IApplication app, PreviewGraphTarget target, ChatPreviewState chat, bool unicode)
    {
        using Dialog dialog = new() { Title = "Chat: " + target.Name, Width = Dim.Percent(85), Height = Dim.Percent(95) };
        dialog.Add(new Label { X = 2, Y = 1, Width = Dim.Fill(2), Height = 1,
            Text = "Component chat / DEMO / session only / Enter sends" });
#pragma warning disable CS0618
        TextView history = new() { X = 2, Y = 3, Width = Dim.Fill(2), Height = Dim.Fill(7), ReadOnly = true, WordWrap = false, BorderStyle = LineStyle.None };
        TextView input = new() { X = 2, Y = Pos.AnchorEnd(6), Width = Dim.Fill(2), Height = 3, Multiline = true,
            WordWrap = true, TabKeyAddsTab = false, BorderStyle = LineStyle.None, Text = chat.Draft };
#pragma warning restore CS0618
        int renderedWidth = -1;
        void Render()
        {
            renderedWidth = history.Viewport.Width;
            history.Text = ChatPreviewTranscript.Render(chat, Math.Max(1, history.Viewport.Width), unicode);
            history.MoveEnd();
        }
        input.TextChanged += (_, _) => chat.Draft = input.Text;
        input.ContentsChanged += (_, _) => chat.Draft = input.Text;
        input.KeyBindings.ReplaceCommands(Key.A.WithCtrl, Command.SelectAll);
        input.KeyDown += (_, key) =>
        {
            if (key == Key.Enter)
            {
                key.Handled = true;
                if (!chat.Send()) return;
                chat.CompleteReply();
                input.Text = chat.Draft;
                Render();
            }
            else if (key == Key.Enter.WithShift) { key.Handled = true; input.InvokeCommand(Command.NewLine); }
        };
        dialog.Add(history, input, Close(app, dialog));
        dialog.Initialized += (_, _) => { Render(); input.SetFocus(); };
        dialog.SubViewsLaidOut += (_, _) => { if (renderedWidth != history.Viewport.Width) Render(); };
        app.Run(dialog);
    }

    private static Button Close(IApplication app, Dialog dialog)
    {
        Button close = new() { X = 2, Y = Pos.AnchorEnd(2), Text = "_Close" };
        close.Accepting += (_, args) => { args.Handled = true; app.RequestStop(dialog); };
        return close;
    }
}
