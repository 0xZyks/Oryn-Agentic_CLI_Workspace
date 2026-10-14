using System.Text;
using Oryn.App;
using SDL;
using SkiaSharp;
using static SDL.SDL3;

namespace Oryn.Ui.Sdl;

/// <summary>Owns SDL resources and presents Skia raster frames; no Terminal.Gui dependency.</summary>
public sealed unsafe class SdlWorkspace : IDisposable
{
    public GraphicalWorkspaceController Controller { get; }
    private readonly WorkspacePainter _painter;
    private SDL_Window* _window;
    private SDL_Renderer* _renderer;
    private SDL_Texture* _texture;
    private SKBitmap? _bitmap;
    private SKCanvas? _canvas;
    private bool _initialized, _disposed, _textInput;
    private int _pixelWidth, _pixelHeight, _width, _height;
    private readonly int? _manualFps;
    private int _fps = 60;
    private long _nextDisplayCheck;
    private readonly SdlKeyboardInput _keyboard;
    private bool _hasKeyboardFocus = true;
    private SDL_Rect? _textArea;
    public int FramesPerSecond => _fps;
    public long RasterFrames { get; private set; }
    public long PresentedFrames { get; private set; }
    public uint WindowId => (uint)SDL_GetWindowID(_window);
    public event Action<SDL_KeyboardEvent>? KeyboardObserved;
    public string RendererName { get; private set; } = "";
    public SdlWorkspace(GraphicalWorkspaceController controller, bool color = true, bool unicode = true, int? fps = null)
    { Controller = controller; _keyboard = new(controller); _painter = new(controller, color, unicode); _manualFps = fps; }
    public void Run(Action<SdlWorkspace>? afterFrame = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) throw new InvalidOperationException("A window session can only run once.");
        try
        {
            SDL_SetMainReady();
            Require(SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO), "initialize SDL video"); _initialized = true;
            byte[] title = Encoding.UTF8.GetBytes("Oryn — local engineering workspace\0");
            fixed (byte* name = title) _window = SDL_CreateWindow(name, 1280, 836,
                SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY);
            if (_window == null) Fail("create window");
            SDL_SetWindowMinimumSize(_window, 360, 264);
            _renderer = SDL_CreateRenderer(_window, (byte*)null);
            if (_renderer == null) Fail("create renderer");
            RendererName = SDL_GetRendererName(_renderer) ?? "unknown";
            SDL_SetRenderVSync(_renderer, 0); // Explicit bounded pacing; Skia raster is not tied to GL.
            long lastFrame = 0, lastAnimation = -1;
            while (!Controller.State.QuitRequested)
            {
                long now = GraphicalWorkspaceController.Now;
                Controller.Tick(now);
                ReadSize(); UpdateCadence(now); UpdateTextInput();
                bool due = now - lastFrame >= Math.Max(1, 1000.0 / _fps);
                if (due || lastFrame == 0)
                {
                    // Rasterize only changed content, but keep presenting the cached texture.
                    // Event-driven swaps can leave the last frames queued on a composited desktop.
                    if (Controller.Dirty || Controller.Animating && now / 100 != lastAnimation)
                    { Draw(); lastAnimation = now / 100; }
                    Present(); lastFrame = now; afterFrame?.Invoke(this);
                }
                SDL_Event ev;
                int wait = Math.Clamp((int)Math.Ceiling(1000.0 / _fps - (GraphicalWorkspaceController.Now - lastFrame)), 1, 16);
                if (SDL_WaitEventTimeout(&ev, wait))
                {
                    Handle(ev);
                    // Bound event work so a noisy mouse cannot starve rendering.
                    for (int i = 0; i < 255 && SDL_PollEvent(&ev); i++) Handle(ev);
                }
            }
        }
        finally { Dispose(); }
    }
    private void ReadSize()
    {
        int w, h, pw, ph;
        Require(SDL_GetWindowSize(_window, &w, &h), "read window size");
        Require(SDL_GetRenderOutputSize(_renderer, &pw, &ph), "read output size");
        if (w < 1 || h < 1 || pw < 1 || ph < 1) return;
        if (_width == w && _height == h && _pixelWidth == pw && _pixelHeight == ph) return;
        // Bound raster allocations when display sizes or scale factors are unusual.
        if ((long)pw * ph > 32_000_000) throw new IOException("Window surface exceeds the 32 million pixel limit.");
        _width = w; _height = h; _pixelWidth = pw; _pixelHeight = ph;
        _canvas?.Dispose(); _bitmap?.Dispose();
        if (_texture != null) SDL_DestroyTexture(_texture);
        _texture = SDL_CreateTexture(_renderer, SDL_PIXELFORMAT_BGRA32,
            SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, pw, ph);
        if (_texture == null) Fail("create presentation texture");
        SDL_SetTextureBlendMode(_texture, SDL_BlendMode.SDL_BLENDMODE_NONE);
        _bitmap = new(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        _canvas = new(_bitmap); Controller.Resize(w, h);
    }
    private void UpdateCadence(long now)
    {
        if (_manualFps is { } rate) { _fps = rate; return; }
        if (now < _nextDisplayCheck) return;
        _nextDisplayCheck = now + 1000;
        SDL_DisplayMode* mode = SDL_GetCurrentDisplayMode(SDL_GetDisplayForWindow(_window));
        _fps = mode != null && float.IsFinite(mode->refresh_rate) && mode->refresh_rate >= 1
            ? Math.Clamp((int)Math.Round(mode->refresh_rate), 1, 500) : 60;
    }
    private void UpdateTextInput()
    {
        bool enabled = _hasKeyboardFocus && Controller.AcceptsText;
        if (enabled != _textInput)
        {
            if (enabled) Require(SDL_StartTextInput(_window), "start text input"); else SDL_StopTextInput(_window);
            _textInput = enabled; _textArea = null;
        }
        if (enabled)
        {
            SDL_Rect area = new() { x = 18, y = Math.Max(0, _height - 150), w = Math.Max(1, _width - 36), h = 100 };
            if (_textArea is not { } previous || previous.x != area.x || previous.y != area.y || previous.w != area.w || previous.h != area.h)
            { Require(SDL_SetTextInputArea(_window, &area, 0), "set text input area"); _textArea = area; }
        }
    }
    private void Draw()
    {
        if (_canvas is null || _bitmap is null) return;
        _painter.Draw(_canvas, _width, _height, (float)_pixelWidth / _width, (float)_pixelHeight / _height);
        _canvas.Flush();
        Require(SDL_UpdateTexture(_texture, null, _bitmap.GetPixels(), _bitmap.RowBytes), "upload frame");
        RasterFrames++;
    }
    private void Present()
    {
        Require(SDL_RenderClear(_renderer), "clear renderer");
        Require(SDL_RenderTexture(_renderer, _texture, null, null), "present texture");
        Require(SDL_RenderPresent(_renderer), "present frame");
        PresentedFrames++;
    }
    public void Capture(string path)
    {
        if (_bitmap is null) throw new InvalidOperationException("No frame has been drawn.");
        using SKImage image = SKImage.FromBitmap(_bitmap);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream file = File.Create(path); png.SaveTo(file);
    }
    public void ResizeWindow(int width, int height) => Require(SDL_SetWindowSize(_window, width, height), "resize window");
    private void Handle(SDL_Event ev)
    {
        long now = GraphicalWorkspaceController.Now;
        switch ((SDL_EventType)ev.type)
        {
            case SDL_EventType.SDL_EVENT_QUIT:
            case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED: Controller.State.Dispatch(WorkspaceCommand.Quit); break;
            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
                _hasKeyboardFocus = false; Controller.FocusLost(); _keyboard.Reset(); break;
            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED:
                _hasKeyboardFocus = true; _keyboard.Reset(); Controller.Dirty = true; break;
            case SDL_EventType.SDL_EVENT_WINDOW_EXPOSED:
            case SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
            case SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED: Controller.Dirty = true; break;
            case SDL_EventType.SDL_EVENT_KEY_DOWN:
                KeyboardObserved?.Invoke(ev.key);
                KeyboardInputResult result = _keyboard.KeyDown(ev.key, now);
                Clipboard(result.Clipboard);
                break;
            case SDL_EventType.SDL_EVENT_KEY_UP: KeyboardObserved?.Invoke(ev.key); _keyboard.KeyUp(ev.key); break;
            case SDL_EventType.SDL_EVENT_TEXT_INPUT:
                _keyboard.Text(PtrToStringUTF8(ev.text.text) ?? ""); break;
            case SDL_EventType.SDL_EVENT_TEXT_EDITING:
                Controller.Composition = PtrToStringUTF8(ev.edit.text) ?? ""; Controller.Dirty = true; break;
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
                if (ev.button.button is 1 or 3) _painter.PointerDown(ev.button.x, ev.button.y, ev.button.button == 3);
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP: if (ev.button.button == 1) Controller.PointerUp(); break;
            case SDL_EventType.SDL_EVENT_WINDOW_MOUSE_LEAVE: _painter.Hover(-1, -1); break;
            case SDL_EventType.SDL_EVENT_MOUSE_MOTION: _painter.Hover(ev.motion.x, ev.motion.y); Controller.PointerMove(ev.motion.x, ev.motion.y); break;
            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                int direction = ev.wheel.direction == SDL_MouseWheelDirection.SDL_MOUSEWHEEL_FLIPPED ? -1 : 1;
                Controller.Wheel((int)(ev.wheel.mouse_x / GraphicalWorkspaceController.CellWidth), (int)(ev.wheel.mouse_y / GraphicalWorkspaceController.CellHeight),
                    (int)Math.Sign(ev.wheel.x) * direction, (int)Math.Sign(ev.wheel.y) * direction); break;
        }
        // Activate/deactivate text input at the focus transition, not one event batch later.
        if (!Controller.State.QuitRequested) UpdateTextInput();
    }
    private void Clipboard(ClipboardRequest request)
    {
        if (request == ClipboardRequest.None) return;
        TextEditState editor = Controller.Overlay == WorkspaceOverlay.Keybindings ? Controller.BindingEditor : Controller.Editor;
        if (request == ClipboardRequest.Paste) Controller.Text(SDL_GetClipboardText() ?? "");
        else if (editor.HasSelection)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(editor.Selection + "\0");
            fixed (byte* text = bytes) Require(SDL_SetClipboardText(text), "set clipboard");
            if (request == ClipboardRequest.Cut) { editor.Insert(""); if (Controller.Overlay != WorkspaceOverlay.Keybindings) Controller.CurrentChat.Draft = editor.Text; }
        }
        Controller.Dirty = true;
    }
    public static bool Map(SDL_KeyboardEvent ev, out InputKey key) => SdlKeyboardInput.Map(ev, out key);
    private static void Require(SDLBool result, string action) { if (!result) Fail(action); }
    private static void Fail(string action) => throw new IOException($"Cannot {action}: {SDL_GetError()}");
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (_textInput && _window != null) SDL_StopTextInput(_window);
        _canvas?.Dispose(); _bitmap?.Dispose(); _painter.Dispose();
        if (_texture != null) SDL_DestroyTexture(_texture);
        if (_renderer != null) SDL_DestroyRenderer(_renderer);
        if (_window != null) SDL_DestroyWindow(_window);
        if (_initialized) SDL_Quit();
    }
}
