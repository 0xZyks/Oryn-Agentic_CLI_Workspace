using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Oryn.App;
using Oryn.Ui.Sdl;
using SDL;
using static SDL.SDL3;

// Send platform key events only to this test window; never changes desktop focus or uses global XTest input.
internal static unsafe class X11KeyboardSmoke
{
    public static int Run()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("X11 keyboard smoke is Linux-only.");
        string root = Path.Combine(Path.GetTempPath(), "oryn-platform-keys-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            string dir = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName;
            File.WriteAllText(Path.Combine(dir, "child.txt"), "child");
            LocalWorkspaceFiles files = new(root, new()); WorkspaceState state = new(root);
            state.LoadRoot(files.ListAsync(root, default).GetAwaiter().GetResult());
            using GraphicalWorkspaceController ui = new(state, files, new(new()), Path.Combine(root, "keys.json"));
            using SdlWorkspace host = new(ui, fps: 60);
            int phase = 0, observed = 0, sentAt = 0, cycles = 0; Stopwatch watch = Stopwatch.StartNew(); long next = 0;
            host.KeyboardObserved += key =>
            {
                if (key.type == SDL_EventType.SDL_EVENT_KEY_DOWN) observed++;

            };
            host.Run(window =>
            {
                if (watch.Elapsed.TotalSeconds > 15) throw new TimeoutException($"X11 phase {phase}, events {observed}");
                if (watch.ElapsedMilliseconds < next) return;
                next = watch.ElapsedMilliseconds + 100;
                switch (phase)
                {
                    case 0:
                        if (watch.ElapsedMilliseconds < 800 || (SDL_GetModState() & (SDL_Keymod.SDL_KMOD_GUI | SDL_Keymod.SDL_KMOD_CTRL | SDL_Keymod.SDL_KMOD_ALT | SDL_Keymod.SDL_KMOD_SHIFT)) != 0) return;
                        Send(window, 0xff09, ctrl: true); sentAt = observed; break;
                    case 1:
                        if (observed <= sentAt) return;
                        Check(state.Focus == (cycles % 2 == 0 ? WorkspacePanel.Center : WorkspacePanel.Explorer), "OS Ctrl+Tab cycle " + cycles);
                        if (++cycles < 40) { Send(window, 0xff09, ctrl: true); sentAt = observed; return; }
                        // Ctrl+Enter must work directly from Explorer as well as from central content.
                        Check(state.Focus == WorkspacePanel.Explorer, "40 panel switches");
                        Send(window, 0xff0d, ctrl: true); sentAt = observed; break;
                    case 2:
                        if (observed <= sentAt) return;
                        Check(state.FocusArea == PanelFocusArea.Tabs, "OS Ctrl+Enter enters tabs"); Send(window, 0xff53); sentAt = observed; break;
                    case 3:
                        if (observed <= sentAt) return;
                        Check(state.Focus == WorkspacePanel.Center && state.ActiveTab == WorkspaceTab.Code && state.FocusArea == PanelFocusArea.Tabs, "OS Right selects Code only");
                        Send(window, 0xff0d); sentAt = observed; break;
                    case 4:
                        if (observed <= sentAt) return;
                        Check(state.FocusArea == PanelFocusArea.Content, "OS Enter confirms tab"); Send(window, 0xff09, ctrl: true); sentAt = observed; break;
                    case 5:
                        if (observed <= sentAt) return;
                        Check(state.Focus == WorkspacePanel.Explorer, "OS Ctrl+Tab returns to explorer"); Send(window, 0xff53); sentAt = observed; break;
                    case 6:
                        if (observed <= sentAt || ui.Loading) return;
                        Check(state.ExpandedDirectories.Contains(dir) && state.Rows.Count == 2 && state.SelectedIndex == 0, "OS Right expands directory without moving selection");
                        Send(window, 0xff51); sentAt = observed; break;
                    case 7:
                        if (observed <= sentAt) return;
                        Check(!state.ExpandedDirectories.Contains(dir) && state.Rows.Count == 1, "OS Left collapses directory");
                        Send(window, 0xff09, shift: true); sentAt = observed; break;
                    case 8:
                        if (observed <= sentAt) return;
                        Check(ui.NavbarFocused, "OS Shift+Tab selects navbar"); Send(window, 0xff53); sentAt = observed; break;
                    case 9:
                        if (observed <= sentAt) return;
                        Check(ui.NavbarIndex == 1 && state.Focus == WorkspacePanel.Explorer, "OS navbar arrows stay out of explorer");
                        Send(window, 0xff0d); sentAt = observed; break;
                    case 10:
                        if (observed <= sentAt) return;
                        Check(ui.Overlay == WorkspaceOverlay.Menu && ui.MenuTitle == "Project", "OS Enter opens navbar category");
                        state.Dispatch(WorkspaceCommand.Quit); break;
                }
                phase++;
            });
            Check(phase == 11, "all platform steps completed"); Console.WriteLine("PASS: platform X11 40 consecutive Ctrl+Tab presses, Ctrl+Enter, tab arrows/confirmation, explorer Right/Left, Shift+Tab/navbar arrows/Enter."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        finally { Directory.Delete(root, true); }
    }
    private static void Send(SdlWorkspace host, nuint symbol, bool ctrl = false, bool alt = false, bool shift = false)
    {
        SDL_Window* window = SDL_GetWindowFromID((SDL_WindowID)host.WindowId);
        SDL_PropertiesID props = SDL_GetWindowProperties(window);
        byte[] displayKey = Encoding.UTF8.GetBytes("SDL.window.x11.display\0"), windowKey = Encoding.UTF8.GetBytes("SDL.window.x11.window\0");
        nint display; nuint xid;
        fixed (byte* name = displayKey) display = SDL_GetPointerProperty(props, name, 0);
        fixed (byte* name = windowKey) xid = (nuint)SDL_GetNumberProperty(props, name, 0);
        if (display == 0 || xid == 0) throw new InvalidOperationException("Use SDL_VIDEODRIVER=x11 for platform smoke.");
        uint modifiers = ctrl ? 4u : alt ? 8u : shift ? 1u : 0;
        nuint modifier = ctrl ? 0xffe3u : alt ? 0xffe9u : 0xffe1u;
        if (ctrl || alt || shift) Emit(display, xid, modifier, 2, 0);
        Emit(display, xid, symbol, 2, modifiers);
        Emit(display, xid, symbol, 3, modifiers);
        if (ctrl || alt || shift) Emit(display, xid, modifier, 3, modifiers);
        XFlush(display);
    }
    private static void Emit(nint display, nuint xid, nuint symbol, int type, uint state)
    {
        XEvent ev = new(); ev.Key = new() { Type = type, Display = display, Window = xid, Root = XDefaultRootWindow(display),
            Keycode = XKeysymToKeycode(display, symbol), State = state, SameScreen = 1, X = 1, Y = 1 };
        if (XSendEvent(display, xid, 0, type == 2 ? 1 : 2, ref ev) == 0) throw new IOException("XSendEvent failed");
    }
    [StructLayout(LayoutKind.Sequential)] private struct XKeyEvent
    { public int Type; public nuint Serial; public int SendEvent; public nint Display; public nuint Window, Root, Subwindow, Time; public int X, Y, XRoot, YRoot; public uint State, Keycode; public int SameScreen; }
    [StructLayout(LayoutKind.Explicit, Size = 192)] private struct XEvent { [FieldOffset(0)] public XKeyEvent Key; }
    [DllImport("libX11.so.6")] private static extern int XSendEvent(nint display, nuint window, int propagate, nint mask, ref XEvent ev);
    [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
    [DllImport("libX11.so.6")] private static extern nuint XDefaultRootWindow(nint display);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(nint display, nuint key);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
