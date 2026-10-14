using System.Collections.ObjectModel;
using Oryn.App;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Oryn.Tui.Prototype;

internal static class KeybindingsDialog
{
    public static void Show(IApplication app, KeyboardRouter current, string path, Action<KeyboardRouter> apply)
    {
        KeyBindingSettings draft = current.Settings.DeepCopy();
        using Dialog dialog = new() { Title = "Keybindings", Width = Dim.Percent(95), Height = Dim.Percent(95) };
        TextField super = new() { X = 16, Y = 1, Width = 20, Text = draft.Super };
        Label guidance = new() { X = 2, Y = 2, Width = Dim.Fill(2), Height = 2,
            Text = "Ctrl/Alt: hold the modifier. Space or F12: press the leader, then the command.\nWin/Command needs a terminal remapping with this adapter." };
        ListView actions = new() { X = 2, Y = 6, Width = Dim.Fill(2), Height = Dim.Fill(9) };
        Label selected = new() { X = 2, Y = Pos.AnchorEnd(8), Width = Dim.Fill(2), Height = 1 };
        TextField bindings = new() { X = 2, Y = Pos.AnchorEnd(7), Width = Dim.Fill(2), Height = 1 };
        Label error = new() { X = 2, Y = Pos.AnchorEnd(5), Width = Dim.Fill(2), Height = 2 };
        KeyBindingDefinition[] definitions = [];
        int editing = -1;
        KeyBindingGroup group = KeyBindingGroup.Workspace;
        bool changing = false;
        void FocusActionsLater() => app.AddTimeout(TimeSpan.FromMilliseconds(1), () =>
        {
            if (ReferenceEquals(app.TopRunnableView, dialog)) actions.SetFocus();
            return false;
        });

        void Capture()
        {
            if (editing >= 0 && editing < definitions.Length)
                draft.Group(group)[definitions[editing].Action.ToString()] = bindings.Text.ToString()
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        void Select(int index)
        {
            if (index < 0 || index >= definitions.Length) return;
            editing = index;
            selected.Text = "_Bindings: " + KeyBindingSettings.Label(definitions[index].Action) + " — separate shortcuts with commas";
            bindings.Text = string.Join(", ", draft.Bindings(definitions[index]));
        }
        void ShowGroup(KeyBindingGroup next)
        {
            Capture();
            changing = true;
            group = next;
            definitions = KeyBindingSettings.Definitions.Where(definition => definition.Group == next).ToArray();
            actions.SetSource(new ObservableCollection<string>(definitions.Select(definition => KeyBindingSettings.Label(definition.Action))));
            actions.SelectedItem = 0;
            Select(0);
            error.Text = "Group: " + group;
            changing = false;
        }
        actions.ValueChanged += (_, _) =>
        {
            if (changing) return;
            Capture();
            Select(actions.SelectedItem ?? 0);
        };
        actions.KeyDown += (_, key) =>
        {
            if (key != Key.Tab) return;
            key.Handled = true;
            bindings.SetFocus();
        };
        bindings.KeyDown += (_, key) =>
        {
            if (key != Key.Tab.WithShift) return;
            key.Handled = true;
            actions.SetFocus();
        };
        dialog.Add(new Label { X = 2, Y = 1, Text = "Oryn S_uper:" }, super, guidance, actions, selected, bindings, error);
        int x = 2;
        foreach (KeyBindingGroup item in Enum.GetValues<KeyBindingGroup>())
        {
            KeyBindingGroup chosen = item;
            Button button = new() { X = x, Y = 4, Text = item == KeyBindingGroup.Content ? "C_ontent" : "_" + item };
            button.Accepting += (_, args) => { args.Handled = true; ShowGroup(chosen); FocusActionsLater(); };
            dialog.Add(button);
            x += item.ToString().Length + 5;
        }
        Button reset = new() { X = 2, Y = Pos.AnchorEnd(2), Text = "_Reset group" };
        reset.Accepting += (_, args) =>
        {
            args.Handled = true;
            editing = -1;
            draft.Group(group).Clear();
            ShowGroup(group);
            FocusActionsLater();
        };
        void Commit(bool save)
        {
            Capture();
            try
            {
                KeyBindingSettings candidate = draft with { Super = super.Text.ToString().Trim() };
                KeyboardRouter router = new(candidate);
                if (save) KeyBindingStore.Save(path, candidate);
                apply(router);
                app.RequestStop(dialog);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            { error.Text = TerminalText.Sanitize(ex.Message); }
        }
        Button session = new() { X = 22, Y = Pos.AnchorEnd(2), Text = "_Apply" };
        session.Accepting += (_, args) => { args.Handled = true; Commit(false); };
        Button save = new() { X = 36, Y = Pos.AnchorEnd(2), Text = "_Save" };
        save.Accepting += (_, args) => { args.Handled = true; Commit(true); };
        Button cancel = new() { X = 48, Y = Pos.AnchorEnd(2), Text = "_Cancel" };
        cancel.Accepting += (_, args) => { args.Handled = true; app.RequestStop(dialog); };
        dialog.Add(reset, session, save, cancel);
        // The form owns Enter; editing a field must not implicitly accept the dialog.
        super.Accepting += (_, args) => args.Handled = true;
        bindings.Accepting += (_, args) => args.Handled = true;
        ShowGroup(group);
        dialog.Initialized += (_, _) => super.SetFocus();
        app.Run(dialog);
    }
}
