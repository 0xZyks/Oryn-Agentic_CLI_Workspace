using Oryn.App;
using Oryn.Ui.Sdl;
using SDL;

internal static class KeyboardChecks
{
    public static void Run(string root)
    {
        using GraphicalWorkspaceController ui = Create(root);
        SdlKeyboardInput input = new(ui);
        SDL_KeyboardEvent tab = Stroke(SDL_Keycode.SDLK_TAB, SDL_Keymod.SDL_KMOD_LCTRL, SDL_Scancode.SDL_SCANCODE_TAB);
        input.KeyDown(tab, 0); tab.repeat = true;
        for (int i = 1; i < 8; i++) input.KeyDown(tab, i);
        Check(ui.State.Focus == WorkspacePanel.Center, "held Ctrl+Tab must switch once"); input.KeyUp(tab);
        input.KeyDown(Stroke(SDL_Keycode.SDLK_2, SDL_Keymod.SDL_KMOD_LCTRL | SDL_Keymod.SDL_KMOD_RALT), 10);
        input.Text("@{}"); Check(ui.Editor.Text == "@{}", "AltGr must remain Unicode text");
        input.KeyUp(Stroke(SDL_Keycode.SDLK_2));
        input.KeyDown(Stroke(SDL_Keycode.SDLK_E, SDL_Keymod.SDL_KMOD_MODE), 11); input.Text("€"); input.KeyUp(Stroke(SDL_Keycode.SDLK_E));
        Check(ui.Editor.Text == "@{}€", "SDL MODE text must not be Ctrl/Alt shortcut");
        input.KeyDown(Stroke(SDL_Keycode.SDLK_K, SDL_Keymod.SDL_KMOD_RCTRL, SDL_Scancode.SDL_SCANCODE_K), 12);
        Check(ui.Overlay == WorkspaceOverlay.Keybindings, "right Ctrl binding");
        input.Text("k");
        input.KeyDown(Stroke(SDL_Keycode.SDLK_LSHIFT, SDL_Keymod.SDL_KMOD_LSHIFT, SDL_Scancode.SDL_SCANCODE_LSHIFT), 13);
        input.Text("k"); Check(ui.BindingEditor.Text == "Ctrl", "shortcut text must stay suppressed across modifier transitions");
        input.KeyUp(Stroke(SDL_Keycode.SDLK_K, 0, SDL_Scancode.SDL_SCANCODE_K));
        input.Text("IME"); Check(ui.BindingEditor.Text == "CtrlIME", "released stroke must not swallow independent committed text");
        input.KeyUp(Stroke(SDL_Keycode.SDLK_LSHIFT, 0, SDL_Scancode.SDL_SCANCODE_LSHIFT));
        ui.CloseOverlay();
        input.KeyDown(Stroke(SDL_Keycode.SDLK_RETURN, SDL_Keymod.SDL_KMOD_LCTRL), 14);
        SDL_KeyboardEvent held = Stroke(SDL_Keycode.SDLK_RETURN, SDL_Keymod.SDL_KMOD_LCTRL); held.repeat = true;
        input.KeyDown(held, 15); Check(ui.State.FocusArea == PanelFocusArea.Tabs, "held Ctrl+Enter must not toggle back");
        input.KeyUp(held);
        input.KeyDown(Stroke(SDL_Keycode.SDLK_C, SDL_Keymod.SDL_KMOD_LCTRL), 16);
        Check(!ui.State.QuitRequested, "Ctrl+C on tab header must not quit");
        input.KeyUp(Stroke(SDL_Keycode.SDLK_C)); ui.Execute(KeyboardAction.FocusVisualizer, 17);
        Guid selected = ui.State.Graph.SelectedId;
        input.KeyDown(Stroke(SDL_Keycode.SDLK_RIGHT, SDL_Keymod.SDL_KMOD_LGUI), 18);
        Check(ui.State.Graph.SelectedId == selected, "GUI modifier must not become plain Right");
        Check(SdlKeyboardInput.Map(Stroke(SDL_Keycode.SDLK_EQUALS), out InputKey equals) && equals.Name == "=", "equals is not graph Plus");
        Check(SdlKeyboardInput.Map(Stroke(SDL_Keycode.SDLK_TAB, SDL_Keymod.SDL_KMOD_RCTRL | SDL_Keymod.SDL_KMOD_CAPS | SDL_Keymod.SDL_KMOD_NUM), out InputKey locked)
            && locked == InputKey.Parse("Ctrl+Tab"), "lock bits must not alter shortcuts");
        input.Reset();
        using GraphicalWorkspaceController leader = Create(root, new() { Super = "F12" }); SdlKeyboardInput prefix = new(leader);
        leader.Execute(KeyboardAction.FocusChat, 20); leader.Text("draft");
        prefix.KeyDown(Stroke(SDL_Keycode.SDLK_F12), 21); prefix.KeyUp(Stroke(SDL_Keycode.SDLK_F12));
        prefix.KeyDown(Stroke(SDL_Keycode.SDLK_RETURN), 22); prefix.KeyUp(Stroke(SDL_Keycode.SDLK_RETURN));
        Check(leader.State.FocusArea == PanelFocusArea.Tabs && leader.State.Chat.Messages.Count == 0, "F12 leader Enter cannot send chat");
        using GraphicalWorkspaceController alt = Create(root, new() { Super = "Alt" }); SdlKeyboardInput altInput = new(alt);
        alt.Execute(KeyboardAction.FocusChat, 30);
        altInput.KeyDown(Stroke(SDL_Keycode.SDLK_K, SDL_Keymod.SDL_KMOD_LALT), 31); altInput.Text("k");
        Check(alt.Overlay == WorkspaceOverlay.Keybindings && alt.BindingEditor.Text == "Alt", "Alt shortcut cannot type into its newly opened field");
        using GraphicalWorkspaceController ime = Create(root); SdlKeyboardInput imeInput = new(ime);
        ime.Execute(KeyboardAction.FocusChat, 40); ime.Text("a"); ime.Composition = "é";
        imeInput.KeyDown(Stroke(SDL_Keycode.SDLK_RETURN), 41); imeInput.Text("é");
        Check(ime.State.Chat.Draft == "aé" && !ime.State.Chat.IsThinking, "IME Enter must accept committed Unicode without submitting");
        Check(SdlKeyboardInput.Map(Stroke(SDL_Keycode.SDLK_F24), out InputKey f24) && f24.Name == "F24", "declared F24 binding must map");
        input.Reset();
        ui.Execute(KeyboardAction.FocusFiles, 50);
        SDL_KeyboardEvent physicalTab = Stroke(SDL_Keycode.SDLK_UNKNOWN, SDL_Keymod.SDL_KMOD_LCTRL, SDL_Scancode.SDL_SCANCODE_TAB);
        input.KeyDown(physicalTab, 51); input.KeyDown(physicalTab, 52);
        Check(ui.State.Focus == WorkspacePanel.Center, "duplicate down without repeat bit must not toggle twice");
        input.KeyUp(physicalTab); input.KeyDown(physicalTab, 53); input.KeyUp(physicalTab);
        Check(ui.State.Focus == WorkspacePanel.Explorer, "release permits a new Ctrl+Tab press even with a nonstandard keycode");
        for (int i = 0; i < 100; i++) { input.KeyDown(physicalTab, 54 + i); input.KeyUp(physicalTab); }
        Check(ui.State.Focus == WorkspacePanel.Explorer, "100 adapter press/release cycles");
        Console.WriteLine("PASS: SDL stroke mapping, held shortcuts, AltGr/MODE, left/right modifiers, release/text ownership, GUI/lock bits, Equals, modal and leader routing.");
    }
    private static GraphicalWorkspaceController Create(string root, KeyBindingSettings? keys = null) =>
        new(new(root), new LocalWorkspaceFiles(root, new()), new(keys ?? new()), Path.Combine(root, "unused-key-test.json"));
    private static SDL_KeyboardEvent Stroke(SDL_Keycode key, SDL_Keymod mods = 0, SDL_Scancode scan = SDL_Scancode.SDL_SCANCODE_UNKNOWN) =>
        new() { type = SDL_EventType.SDL_EVENT_KEY_DOWN, key = key, mod = mods, scancode = scan, down = true };
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
