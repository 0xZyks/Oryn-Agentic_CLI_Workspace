using Oryn.App;
using SDL;

namespace Oryn.Ui.Sdl;

/// <summary>Routes strokes and committed Unicode independently; modifiers are not text strokes.</summary>
public sealed class SdlKeyboardInput(GraphicalWorkspaceController controller)
{
    private readonly HashSet<(SDL_KeyboardID Device, SDL_Scancode Scan, SDL_Keycode Key)> _pressed = [];
    private bool _suppressText;
    private SDL_KeyboardEvent? _stroke;
    public KeyboardInputResult KeyDown(SDL_KeyboardEvent ev, long now)
    {
        if (IsModifier(ev)) return default;
        bool repeat = ev.repeat || !_pressed.Add(Identity(ev));
        KeyboardInputResult result = Map(ev, out InputKey key) ? controller.Key(key, now, repeat) : default;
        _stroke = ev; _suppressText = result.Handled && !result.AllowCommittedText;
        return result;
    }
    public void KeyUp(SDL_KeyboardEvent ev)
    {
        _pressed.Remove(Identity(ev));
        if (_stroke is not { } stroke || IsModifier(ev)) return;
        bool same = stroke.which == ev.which && (stroke.scancode != SDL_Scancode.SDL_SCANCODE_UNKNOWN
            ? stroke.scancode == ev.scancode : stroke.key == ev.key);
        if (same) { _stroke = null; _suppressText = false; }
    }
    public void Text(string text) { if (!_suppressText) controller.Text(text); }
    public void Reset() { _pressed.Clear(); _stroke = null; _suppressText = false; }
    private static (SDL_KeyboardID, SDL_Scancode, SDL_Keycode) Identity(SDL_KeyboardEvent ev) =>
        (ev.which, ev.scancode, ev.scancode == SDL_Scancode.SDL_SCANCODE_UNKNOWN ? ev.key : 0);
    private static bool IsModifier(SDL_KeyboardEvent ev) => ev.scancode is
        >= SDL_Scancode.SDL_SCANCODE_LCTRL and <= SDL_Scancode.SDL_SCANCODE_RGUI
        || ev.key is SDL_Keycode.SDLK_LCTRL or SDL_Keycode.SDLK_RCTRL or SDL_Keycode.SDLK_LSHIFT or SDL_Keycode.SDLK_RSHIFT
            or SDL_Keycode.SDLK_LALT or SDL_Keycode.SDLK_RALT or SDL_Keycode.SDLK_LGUI or SDL_Keycode.SDLK_RGUI or SDL_Keycode.SDLK_MODE;
    public static bool Map(SDL_KeyboardEvent ev, out InputKey key)
    {
        InputModifiers mods = InputModifiers.None;
        if ((ev.mod & SDL_Keymod.SDL_KMOD_CTRL) != 0) mods |= InputModifiers.Ctrl;
        if ((ev.mod & SDL_Keymod.SDL_KMOD_ALT) != 0) mods |= InputModifiers.Alt;
        if ((ev.mod & SDL_Keymod.SDL_KMOD_SHIFT) != 0) mods |= InputModifiers.Shift;
        if ((ev.mod & SDL_Keymod.SDL_KMOD_GUI) != 0) mods |= InputModifiers.System;
        if ((ev.mod & SDL_Keymod.SDL_KMOD_MODE) != 0
            || (ev.mod & SDL_Keymod.SDL_KMOD_RALT) != 0 && (ev.mod & SDL_Keymod.SDL_KMOD_CTRL) != 0)
            mods = (mods & ~(InputModifiers.Ctrl | InputModifiers.Alt)) | InputModifiers.AltGraph;
        // Navigation keys use physical identities; printable shortcuts keep layout-based keycodes.
        string? name = ev.scancode switch
        {
            SDL_Scancode.SDL_SCANCODE_TAB => "TAB",
            SDL_Scancode.SDL_SCANCODE_RETURN or SDL_Scancode.SDL_SCANCODE_KP_ENTER => "ENTER",
            SDL_Scancode.SDL_SCANCODE_ESCAPE => "ESC", SDL_Scancode.SDL_SCANCODE_BACKSPACE => "BACKSPACE",
            SDL_Scancode.SDL_SCANCODE_DELETE => "DELETE",
            SDL_Scancode.SDL_SCANCODE_LEFT => "LEFT", SDL_Scancode.SDL_SCANCODE_RIGHT => "RIGHT",
            SDL_Scancode.SDL_SCANCODE_UP => "UP", SDL_Scancode.SDL_SCANCODE_DOWN => "DOWN",
            SDL_Scancode.SDL_SCANCODE_HOME => "HOME", SDL_Scancode.SDL_SCANCODE_END => "END",
            SDL_Scancode.SDL_SCANCODE_PAGEUP => "PAGEUP", SDL_Scancode.SDL_SCANCODE_PAGEDOWN => "PAGEDOWN",
            _ => null
        };
        name ??= ev.key switch
        {
            SDL_Keycode.SDLK_RETURN or SDL_Keycode.SDLK_KP_ENTER => "ENTER", SDL_Keycode.SDLK_ESCAPE => "ESC",
            SDL_Keycode.SDLK_TAB => "TAB", SDL_Keycode.SDLK_SPACE => "SPACE", SDL_Keycode.SDLK_BACKSPACE => "BACKSPACE", SDL_Keycode.SDLK_DELETE => "DELETE",
            SDL_Keycode.SDLK_UP => "UP", SDL_Keycode.SDLK_DOWN => "DOWN", SDL_Keycode.SDLK_LEFT => "LEFT", SDL_Keycode.SDLK_RIGHT => "RIGHT",
            SDL_Keycode.SDLK_INSERT => "INSERT", SDL_Keycode.SDLK_HOME => "HOME", SDL_Keycode.SDLK_END => "END", SDL_Keycode.SDLK_PAGEUP => "PAGEUP", SDL_Keycode.SDLK_PAGEDOWN => "PAGEDOWN",
            SDL_Keycode.SDLK_PLUS or SDL_Keycode.SDLK_KP_PLUS => "PLUS", SDL_Keycode.SDLK_MINUS or SDL_Keycode.SDLK_KP_MINUS => "MINUS",
            >= SDL_Keycode.SDLK_F1 and <= SDL_Keycode.SDLK_F12 => "F" + ((uint)ev.key - (uint)SDL_Keycode.SDLK_F1 + 1),
            >= SDL_Keycode.SDLK_F13 and <= SDL_Keycode.SDLK_F24 => "F" + ((uint)ev.key - (uint)SDL_Keycode.SDLK_F13 + 13),
            _ => (uint)ev.key is >= 32 and <= 126 ? ((char)ev.key).ToString().ToUpperInvariant() : null
        };
        // Equals is not Plus. Shift+Equals is a US-layout fallback; actual Plus keycodes are preferred.
        if (ev.key == SDL_Keycode.SDLK_EQUALS && mods.HasFlag(InputModifiers.Shift)) name = "PLUS";
        key = new(name ?? "", mods); return name is not null;
    }
}
