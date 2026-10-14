using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Oryn.App;
using Oryn.Ui.Sdl;
using SDL;
using static SDL.SDL3;

return args.Contains("--x11-keyboard") ? X11KeyboardSmoke.Run() : Smoke.Run(args);

static unsafe class Smoke
{
    private static readonly List<nint> Allocations = [];
    public static int Run(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "oryn-ui-smoke-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        string captures = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(root, ".captures");
        Directory.CreateDirectory(captures);
        try
        {
            KeyboardChecks.Run(root);
            ChatRenderingChecks.Run(root, captures);
            File.WriteAllText(Path.Combine(root, "example.cs"), "class Example\n{\n    // read-only preview\n}\n");
            LocalWorkspaceFiles files = new(root, new()); WorkspaceState state = new(root);
            state.LoadRoot(files.ListAsync(root, default).GetAwaiter().GetResult());
            using GraphicalWorkspaceController ui = new(state, files, new(new()), Path.Combine(root, "keys.json"));
            using SdlWorkspace window = new(ui, fps: 60);
            Stopwatch time = Stopwatch.StartNew(); int phase = 0; long idleRaster = 0, idlePresented = 0; System.Numerics.Vector2 dragGoal = default, dragOrigin = default; GraphBox beforeDrag = default; Guid firstMember = default;
            window.Run(host =>
            {
                if (time.Elapsed.TotalSeconds > 20) throw new TimeoutException($"Smoke timeout in phase {phase}");
                switch (phase)
                {
                    case 0:
                        host.Capture(Path.Combine(captures, "01-workspace.png"));
                        Key(SDL_Keycode.SDLK_TAB, SDL_Keymod.SDL_KMOD_CTRL); break;
                    case 1:
                        Check(state.Focus == WorkspacePanel.Center, "SDL Ctrl+Tab focus");
                        Text("Bonjour 👩‍💻"); break;
                    case 2:
                        Check(state.Chat.Draft == "Bonjour 👩‍💻", "SDL UTF-8 text input");
                        Key(SDL_Keycode.SDLK_RETURN, SDL_Keymod.SDL_KMOD_SHIFT); Text("Second line"); Key(SDL_Keycode.SDLK_RETURN); break;
                    case 3:
                        Check(state.Chat.Messages[0].Text == "Bonjour 👩‍💻\nSecond line", "Shift+Enter newline / Enter sends");
                        Check(state.Chat.IsThinking, "demo reply begins asynchronously");
                        host.Capture(Path.Combine(captures, "18-thinking-live.png")); Text("next draft"); Key(SDL_Keycode.SDLK_A, SDL_Keymod.SDL_KMOD_CTRL); Key(SDL_Keycode.SDLK_C, SDL_Keymod.SDL_KMOD_CTRL); Key(SDL_Keycode.SDLK_X, SDL_Keymod.SDL_KMOD_CTRL); Key(SDL_Keycode.SDLK_V, SDL_Keymod.SDL_KMOD_CTRL); break;
                    case 4:
                        if (state.Chat.IsThinking) return;
                        Check(state.Chat.Messages.Count == 2 && state.Chat.Draft == "next draft", "reply preserves draft");
                        host.Capture(Path.Combine(captures, "02-chat.png"));
                        Key(SDL_Keycode.SDLK_F1);
                        PanelBounds explorer = ui.Layout.Explorer; Mouse(SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN, (explorer.X + 2) * 9, (explorer.Y + 3) * 22 + 5); Mouse(SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP, (explorer.X + 2) * 9, (explorer.Y + 3) * 22 + 5); break;
                    case 5:
                        if (ui.Loading) return;
                        Check(state.Preview?.Content.Contains("class Example") == true, "async file preview");
                        host.Capture(Path.Combine(captures, "03-code.png")); Key(SDL_Keycode.SDLK_F4); break;
                    case 6:
                        host.Capture(Path.Combine(captures, "04-graph.png")); Key(SDL_Keycode.SDLK_RETURN); break;
                    case 7:
                        Check(state.Graph.SelectedMemberId is not null, "enter graph member"); firstMember = state.Graph.Target.Id;
                        host.Capture(Path.Combine(captures, "05-members.png")); Key(SDL_Keycode.SDLK_RETURN, SDL_Keymod.SDL_KMOD_ALT); break;
                    case 8:
                        Check(ui.Overlay == WorkspaceOverlay.Options, "component options"); Key(SDL_Keycode.SDLK_RETURN); break;
                    case 9:
                        Check(ui.Overlay == WorkspaceOverlay.Details && ui.OverlayTarget?.Id == firstMember, "exact UUID details");
                        host.Capture(Path.Combine(captures, "06-details.png"));
                        Key(SDL_Keycode.SDLK_ESCAPE); Key(SDL_Keycode.SDLK_RETURN, SDL_Keymod.SDL_KMOD_ALT); Key(SDL_Keycode.SDLK_DOWN); Key(SDL_Keycode.SDLK_RETURN); break;
                    case 10:
                        Check(ui.Overlay == WorkspaceOverlay.ElementChat, "component chat"); Text("member prompt"); Key(SDL_Keycode.SDLK_RETURN); break;
                    case 11:
                        if (ui.CurrentChat.IsThinking) return;
                        Check(state.Graph.ChatFor(firstMember).Messages.Count == 2 && state.Chat.Messages.Count == 2, "UUID chat isolation");
                        host.Capture(Path.Combine(captures, "07-element-chat.png"));
                        Key(SDL_Keycode.SDLK_ESCAPE); Key(SDL_Keycode.SDLK_K, SDL_Keymod.SDL_KMOD_CTRL); break;
                    case 12:
                        Check(ui.Overlay == WorkspaceOverlay.Keybindings, "keybinding editor");
                        Key(SDL_Keycode.SDLK_A, SDL_Keymod.SDL_KMOD_LCTRL); Text("Alt"); break;
                    case 13:
                        Key(SDL_Keycode.SDLK_S, SDL_Keymod.SDL_KMOD_ALT); break;
                    case 14:
                        Check(File.Exists(Path.Combine(root, "keys.json")) && ui.Keyboard.Settings.Super == "Alt", "atomic profile save");
                        Key(SDL_Keycode.SDLK_K, SDL_Keymod.SDL_KMOD_ALT); break;
                    case 15:
                        host.Capture(Path.Combine(captures, "08-keybindings.png")); Key(SDL_Keycode.SDLK_ESCAPE);
                        host.ResizeWindow(540, 352); Key(SDL_Keycode.SDLK_F2); break;
                    case 16:
                        if (ui.Columns > 60) return;
                        Check(ui.Layout.Compact, "compact resize"); host.Capture(Path.Combine(captures, "09-compact.png"));
                        host.ResizeWindow(1280, 836); Key(SDL_Keycode.SDLK_F4); break;
                    case 17:
                        if (ui.Columns < 100) return;
                        Key(SDL_Keycode.SDLK_D, SDL_Keymod.SDL_KMOD_LALT); break;
                    case 18:
                        Check(state.LayoutOptions.VisualizerPlacement == VisualizerPlacement.Right, "docking");
                        host.Capture(Path.Combine(captures, "10-dock.png"));
                        PanelBounds split = ui.Layout.ExplorerSplitter;
                        Mouse(SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN, split.X * 9 + 3, split.Y * 22 + 3); break;
                    case 19:
                        Check(state.ResizingSplitter is not null, "SDL splitter mouse down");
                        SDL_Event motion = new(); motion.motion.type = SDL_EventType.SDL_EVENT_MOUSE_MOTION; motion.motion.x = (ui.Layout.ExplorerSplitter.X + 3) * 9; Push(motion);
                        Key(SDL_Keycode.SDLK_ESCAPE); break;
                    case 20:
                        Check(state.ResizingSplitter is null, "Escape cancels drag");
                        Key(SDL_Keycode.SDLK_F2); Key(SDL_Keycode.SDLK_TAB); Key(SDL_Keycode.SDLK_F2, repeats: 5); break;
                    case 21:
                        Check(ui.AcceptsText, "F2 returns to composer"); Key(SDL_Keycode.SDLK_RETURN, SDL_Keymod.SDL_KMOD_LALT, repeats: 5); break;
                    case 22:
                        Check(state.FocusArea == PanelFocusArea.Tabs, "held Super+Enter toggles once");
                        Key(SDL_Keycode.SDLK_RIGHT); Key(SDL_Keycode.SDLK_RETURN, repeats: 5); break;
                    case 23:
                        Check(state.ActiveTab == WorkspaceTab.Code && state.FocusArea == PanelFocusArea.Content, "held Enter confirms once");
                        Key(SDL_Keycode.SDLK_F2); Text("@{}", SDL_Keymod.SDL_KMOD_LCTRL | SDL_Keymod.SDL_KMOD_RALT, SDL_Keycode.SDLK_2); break;
                    case 24:
                        Check(state.Chat.Draft.EndsWith("@{}"), "native AltGr text");
                        Down(SDL_Keycode.SDLK_K, SDL_Keymod.SDL_KMOD_LALT); TextEvent("k");
                        Down(SDL_Keycode.SDLK_LSHIFT, SDL_Keymod.SDL_KMOD_LALT | SDL_Keymod.SDL_KMOD_LSHIFT); TextEvent("k");
                        Up(SDL_Keycode.SDLK_LSHIFT); Up(SDL_Keycode.SDLK_K); break;
                    case 25:
                        Check(ui.Overlay == WorkspaceOverlay.Keybindings && ui.BindingEditor.Text == "Alt", "shortcut never leaks into editor");
                        Key(SDL_Keycode.SDLK_ESCAPE, repeats: 5); break;
                    case 26:
                        Check(ui.Overlay == WorkspaceOverlay.None && ui.AcceptsText, "held Escape closes modal once");
                        SDL_Event wheel = new(); wheel.wheel.type = SDL_EventType.SDL_EVENT_MOUSE_WHEEL;
                        wheel.wheel.y = 1; wheel.wheel.mouse_x = ui.Layout.Visualizer.X * 9 + 5; wheel.wheel.mouse_y = ui.Layout.Visualizer.Y * 22 + 5;
                        Push(wheel); Text(" typing"); break;
                    case 27:
                        Check(ui.AcceptsText && state.Focus == WorkspacePanel.Center && state.Chat.Draft.EndsWith(" typing"), "wheel does not steal typing focus");
                        Key(SDL_Keycode.SDLK_C, SDL_Keymod.SDL_KMOD_LCTRL); Key(SDL_Keycode.SDLK_RETURN, SDL_Keymod.SDL_KMOD_LSHIFT, repeats: 5); break;
                    case 28:
                        Check(!state.QuitRequested && !state.Chat.IsThinking && state.Chat.Draft.EndsWith(" typing\n"), "clipboard safe / held Shift+Enter inserts one newline");
                        Key(SDL_Keycode.SDLK_A, SDL_Keymod.SDL_KMOD_LCTRL);
                        foreach (char c in "Coucou") Text(c.ToString()); break;
                    case 29:
                        Check(state.Chat.Draft == "Coucou", "individual Coucou text commits");
                        idleRaster = host.RasterFrames; idlePresented = host.PresentedFrames; break;
                    case 30:
                        if (host.RasterFrames != idleRaster) { idleRaster = host.RasterFrames; idlePresented = host.PresentedFrames; return; }
                        if (host.PresentedFrames - idlePresented < 6) return;
                        Check(state.Chat.Draft == "Coucou" && ui.AcceptsText, "idle preserves the last typed characters");
                        host.Capture(Path.Combine(captures, "11-coucou-idle.png")); Key(SDL_Keycode.SDLK_F4); break;
                    case 31:
                        beforeDrag = state.Graph.Placement.Bounds(state.Graph.SelectedId);
                        PanelBounds pane = state.Focus == WorkspacePanel.Visualizer ? ui.Layout.Visualizer : ui.Layout.Center;
                        dragOrigin = new((pane.X + 1 - state.Graph.ScrollX) * 9, (pane.Y + 4 - state.Graph.ScrollY) * 22);
                        dragGoal = new(beforeDrag.X + 65, state.Graph.Nodes.Max(n => state.Graph.Placement.Bounds(n.Id).Bottom) + 80);
                        Mouse(SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN, beforeDrag.X + dragOrigin.X + 16, beforeDrag.Y + dragOrigin.Y + 16);
                        SDL_Event drag = new(); drag.motion.type = SDL_EventType.SDL_EVENT_MOUSE_MOTION;
                        drag.motion.x = dragGoal.X + dragOrigin.X + 16; drag.motion.y = dragGoal.Y + dragOrigin.Y + 16; Push(drag); break;
                    case 32:
                        Check(state.Graph.Placement.DraggedId == state.Graph.SelectedId, "left down starts card drag");
                        GraphBox moved = state.Graph.Placement.Bounds(state.Graph.SelectedId);
                        foreach (var node in state.Graph.Nodes.Where(n => n.Id != state.Graph.SelectedId))
                        {
                            GraphBox other = state.Graph.Placement.Bounds(node.Id);
                            Check(!(moved.X < other.Right - .01f && moved.Right > other.X + .01f && moved.Y < other.Bottom - .01f && moved.Bottom > other.Y + .01f), "drag frame has no card overlap");
                        }
                        if (state.Graph.Placement.Moving) return;
                        Check(System.Numerics.Vector2.Distance(new(moved.X, moved.Y), dragGoal) < .1f, "card contours obstacles and rejoins pointer");
                        host.Capture(Path.Combine(captures, "12-drag-links.png"));
                        Mouse(SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP, dragGoal.X + dragOrigin.X + 16, dragGoal.Y + dragOrigin.Y + 16); break;
                    case 33:
                        Check(state.Graph.Placement.DraggedId is null, "release ends drag");
                        host.Capture(Path.Combine(captures, "13-drag-released.png"));
                        Key(SDL_Keycode.SDLK_TAB, SDL_Keymod.SDL_KMOD_LSHIFT, repeats: 5); Key(SDL_Keycode.SDLK_LEFT); break;
                    case 34:
                        Check(ui.NavbarFocused && ui.NavbarIndex == 5 && ui.Overlay == WorkspaceOverlay.None, "held Shift+Tab selects navbar once, Left wraps to Settings");
                        host.Capture(Path.Combine(captures, "14-navbar-selection.png"));
                        Key(SDL_Keycode.SDLK_RETURN, repeats: 5); break;
                    case 35:
                        Check(ui.Overlay == WorkspaceOverlay.Menu && ui.MenuTitle == "Settings", "Enter opens selected category without invoking it on repeat");
                        host.Capture(Path.Combine(captures, "15-settings-menu.png"));
                        Key(SDL_Keycode.SDLK_ESCAPE); Key(SDL_Keycode.SDLK_ESCAPE); Key(SDL_Keycode.SDLK_Q, SDL_Keymod.SDL_KMOD_LALT); break;
                }
                phase++;
            });
            Check(phase == 36, "all SDL scenarios completed");
            Check((uint)SDL_WasInit(0) == 0, "SDL shutdown releases video subsystem");
            Console.WriteLine($"PASS: SDL {window.RendererName}, focus, UTF-8/multiline/clipboard input, reply, mouse file preview, graph/members, UUID chats, keybindings, resize, docking, mouse/cancel and shutdown. Captures: {captures}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        finally
        {
            foreach (nint p in Allocations) Marshal.FreeHGlobal(p); Allocations.Clear();
            Directory.Delete(root, true);
        }
    }
    private static void Key(SDL_Keycode key, SDL_Keymod modifiers = 0, int repeats = 0)
    {
        Down(key, modifiers);
        for (int i = 0; i < repeats; i++) Down(key, modifiers, repeat: true);
        Up(key, modifiers);
    }
    private static void Down(SDL_Keycode key, SDL_Keymod modifiers = 0, bool repeat = false)
    {
        SDL_Event ev = new(); ev.key.type = SDL_EventType.SDL_EVENT_KEY_DOWN; ev.key.key = key;
        ev.key.scancode = SDL_GetScancodeFromKey(key, null); ev.key.mod = modifiers; ev.key.down = true; ev.key.repeat = repeat; Push(ev);
    }
    private static void Up(SDL_Keycode key, SDL_Keymod modifiers = 0)
    {
        SDL_Event ev = new(); ev.key.type = SDL_EventType.SDL_EVENT_KEY_UP; ev.key.key = key;
        ev.key.scancode = SDL_GetScancodeFromKey(key, null); ev.key.mod = modifiers; ev.key.down = false; Push(ev);
    }
    private static void Text(string value, SDL_Keymod modifiers = 0, SDL_Keycode? key = null)
    {
        SDL_Keycode character = key ?? (value.Length > 0 && value[0] is >= ' ' and <= '~'
            ? (SDL_Keycode)char.ToLowerInvariant(value[0]) : SDL_Keycode.SDLK_UNKNOWN);
        Down(character, modifiers); TextEvent(value); Up(character, modifiers);
    }
    private static void TextEvent(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value + "\0"); nint p = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, p, bytes.Length); Allocations.Add(p);
        SDL_Event ev = new(); ev.text.type = SDL_EventType.SDL_EVENT_TEXT_INPUT; ev.text.text = (byte*)p; Push(ev);
    }
    private static void Mouse(SDL_EventType type, float x, float y)
    { SDL_Event ev = new(); ev.button.type = type; ev.button.button = 1; ev.button.x = x; ev.button.y = y; Push(ev); }
    private static void Push(SDL_Event ev) { if (!SDL_PushEvent(&ev)) throw new IOException(SDL_GetError()); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
