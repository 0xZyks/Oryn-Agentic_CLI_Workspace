# ADR 0011 — Menu-bar keyboard selection

Status: implemented in M0.

Update (2026-10-14): the author confirmed physical Alt+Tab was intercepted by the desktop. Navbar entry now uses Shift+Tab/F9. Exact previous defaults migrate in memory, releasing Shift+Tab from PreviousNavbar, PreviousTab and ToggleHistory. Left remains reverse selection; Tab remains forward selection. Shift+Tab still traverses backward inside modal dialogs. The original decision/validation below is historical.

## Context and decision

The author requests Alt+Tab to enter top menu-bar selection, following central tab behavior, with Enter opening the chosen category. Previously the six menu labels all opened the generic workspace menu.

Introduce a Navbar scope and configurable Navbar actions in the existing compiled keyboard router. Alt+Tab/F9 toggles selection; Left/Right and Shift+Tab/Tab wrap categories; Enter opens the selected menu. Held Tab/Enter do not repeat toggles or execute a newly opened menu action. Escape from a navbar menu returns to bar selection; Escape or a second Alt+Tab leaves selection and restores the underlying pane/tab/history focus. Unicode input is inactive in bar selection. Navbar state and category/action data are independent of SDL/Skia.

Expose only existing actions: File/Quit, View/docking and width reset, Settings/keybindings. Project/Agents/Tools show a disabled no-actions indication. F10 retains its six-action generic workspace menu. Mouse clicks select the corresponding category, and entering pane/tab controls exits bar focus. Keep the selected top label visible in narrow windows via horizontal clipping/offset.

Alt+Tab conflicts with default panel traversal when Super=Alt. Those exact default panel bindings resolve to Ctrl+Tab/Ctrl+Shift+Tab; all other Super actions retain Alt. Explicit custom overlapping bindings are rejected with the existing conflict diagnostics. Older profiles omit Navbar and receive its defaults; no settings file is rewritten on load. F9 provides an alternate entry if a desktop reserves Alt+Tab. No compositor/terminal configuration is modified.

## Verification and limits

118 App tests passed, including eight additional cases for all panels, navigation/wrap, modal isolation, held confirmation, text exclusion, caret/draft/focus restoration, category counts, inactive entries, Alt-Super fallback and custom remaps/conflicts. Solution build passed without warnings/errors. Expanded 36-stage SDL queue smoke passed on dummy/software and native Wayland, capturing selected Settings and its menu. The X11 platform smoke passed its 40 panel cycles and additional Alt+Tab/Right/Enter category sequence targeted to its own window. Linux self-contained Release and smoke with its exact packaged App/SDL assemblies passed. Native Settings-selection capture inspected.

These checks do not establish global desktop shortcut delivery from the author's physical keyboard or Windows/macOS operation. New project/agent/tool capabilities are not implemented by adding their menu headings.
