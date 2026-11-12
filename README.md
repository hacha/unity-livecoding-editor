# LiveCoding Editor

[![openupm](https://img.shields.io/npm/v/com.hacha.livecoding-editor?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.hacha.livecoding-editor/)

A small code editor that lives in your scene (world-space Canvas) and reads a hardware keyboard directly, without going through a soft keyboard or an OS text field. It supports the editing operations you expect when writing code (multi-line selection, line comments, undo/redo, ...). Useful for in-game consoles or live coding in VR/XR.

It is language-agnostic: syntax highlighting and diagnostics (error display) are injected by the host application.

日本語: [README.ja.md](README.ja.md)

## Installation

### OpenUPM (recommended)

With [openupm-cli](https://github.com/openupm/openupm-cli):

```sh
openupm add com.hacha.livecoding-editor
```

Or add the scoped registry to `Packages/manifest.json` by hand:

```json
{
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": ["com.hacha.livecoding-editor"]
    }
  ],
  "dependencies": {
    "com.hacha.livecoding-editor": "0.1.0"
  }
}
```

### Git URL

Use "Add package from git URL" in the Package Manager, or add this to `Packages/manifest.json`:

```json
"com.hacha.livecoding-editor": "https://github.com/hacha/unity-livecoding-editor.git#v0.1.0"
```

### Requirements

Unity 6000.0 or later, Input System, and uGUI (TextMeshPro via uGUI 2.0).

To run this package's EditMode tests, add `"testables": ["com.hacha.livecoding-editor"]` to your project's `manifest.json`.

## Key bindings

Built-in key-to-action mapping in `EditorController.HandleKey` / `HandleText` (`Runtime/Core/EditorController.cs`). The package has no "evaluate (submit)" action; the host application assigns Ctrl+Enter and similar keys. The "ReadOnly" notes say whether the key still works when `CodeEditorView.ReadOnly = true` (`EditorController.ReadOnly`).

### Newline

| Key | Action | Notes |
|---|---|---|
| Enter / Shift+Enter | Insert a newline (inherits the current line's indentation) | Disabled when ReadOnly. Repeats. Ctrl+Enter does nothing unless the host binds it (no newline is inserted). Bind evaluation and similar actions as described in "Host-assigned keys" below |

### Caret movement and selection

| Key | Action | Notes |
|---|---|---|
| Left / Right / Up / Down | Move by one character / one line | Shift extends the selection. Left/Right move by word with Ctrl or Alt. Pressing Left/Right without Shift while a selection exists collapses the caret to the selection edge. Works when ReadOnly. Repeats |
| Home | Go to the start of the line (first press: just after the indentation; if already there, column 0) | Ctrl+Home goes to the start of the document. Shift extends the selection. Works when ReadOnly. Repeats |
| End | Go to the end of the line | Ctrl+End goes to the end of the document. Shift extends the selection. Works when ReadOnly. Repeats |
| PageUp / PageDown | Move up / down by the number of visible lines | Shift extends the selection. Works when ReadOnly. Repeats |
| Ctrl+A | Select all | Works when ReadOnly. Does not repeat |

### Editing

| Key | Action | Notes |
|---|---|---|
| Backspace | Delete one character (or the selection, if any) | Ctrl/Alt deletes by word. Inside indentation it goes back to the previous tab stop. Disabled when ReadOnly. Repeats |
| Delete | Delete the character to the right of the caret (or the selection, if any) | Ctrl/Alt deletes by word. Disabled when ReadOnly. Repeats |
| Tab | Indent. With no selection or a single-line selection, inserts spaces up to the next tab stop; with a multi-line selection, adds spaces (TabSize) at the start of each line | Disabled when ReadOnly. Repeats |
| Shift+Tab | Outdent. Removes up to TabSize leading spaces from the target lines | Disabled when ReadOnly. Repeats |
| Ctrl+/ | Toggle line comments (default prefix `//`, configurable on `CodeEditorView`) | Targets the selected lines, or the caret line if there is no selection. If every non-empty line is already commented, the comments are removed; otherwise all lines get `// ` at the same column. Disabled when ReadOnly. Does not repeat |

### Clipboard and undo

| Key | Action | Notes |
|---|---|---|
| Ctrl+C | Copy the selection | Does nothing if there is no selection or no clipboard is set. Works when ReadOnly. Does not repeat |
| Ctrl+X | Cut the selection | Disabled when ReadOnly. Does not repeat |
| Ctrl+V | Paste the clipboard contents | Disabled when ReadOnly. Does not repeat |
| Ctrl+Z | Undo | Consecutive typing, and Backspace at the same position, are merged into one undo step. Every other edit is one step each. Disabled when ReadOnly. Repeats while held |
| Ctrl+Shift+Z / Ctrl+Y | Redo | Disabled when ReadOnly. Repeats while held |

## Host-assigned keys

Besides the built-in actions, the host can assign an action to any key with `EditorController.Bind`. From a view, use `CodeEditorView.Controller.Bind(...)`.

| API | Action | Notes |
|---|---|---|
| `Bind(EditorKey key, EditorModifiers modifiers, Action action, bool repeat = false, bool whenUnfocused = false)` | Assign an action to a key plus modifiers | Modifiers must match exactly (a Ctrl+Enter binding does not fire on Ctrl+Shift+Enter; bind both if you want both). Binding the same combination again replaces the previous one |
| `Unbind(key, modifiers)` | Remove a binding | |
| `IsBound(key, modifiers)` | Whether a binding exists | |

- Bindings are checked before the built-in actions, so built-in keys (such as Ctrl+/) can be overridden.
- Bindings also work when `ReadOnly`.
- `repeat`: whether the action also fires on key repeat while the key is held (default: no).
- `whenUnfocused`: whether the action fires while `HasFocus` is false. Use it for keys that should work even when the editor is hidden.
- While `EditorController.HasFocus` (set via `CodeEditorView.HasFocus`) is false, text input and built-in actions are ignored, and only bindings with `whenUnfocused` run.
- If a bound action throws, `CodeEditorView` catches it (logging with `Debug.LogException` and showing it on the diagnostic line), so input keeps working.
- `EditorKey` includes A-Z, Digit0-Digit9, F1-F12, Escape and Space, which have no built-in action and are meant for bindings.

```csharp
var c = view.Controller;
c.Bind(EditorKey.Enter, EditorModifiers.Ctrl, Evaluate);
c.Bind(EditorKey.Enter, EditorModifiers.Ctrl | EditorModifiers.Shift, Evaluate);
c.Bind(EditorKey.H, EditorModifiers.Ctrl | EditorModifiers.Shift, ToggleHud, whenUnfocused: true);
```

For example, a live-coding host might bind Ctrl+Enter / Ctrl+Shift+Enter to evaluate the code, and Ctrl+Shift+H (`whenUnfocused`) to show or hide the editor.

## Status bar

| API | Where it appears |
|---|---|
| `SetDiagnostic(line, message)` / `ClearDiagnostic()` | First row. Highlights the error line and shows the message (replacing `Ln/Col`) |
| `SetStatusNote(string)` | First row. A short caller-supplied note such as a measurement. Shown after `Ln/Col`; while an error is displayed it is shown before the error (the field is truncated from the end with an ellipsis, so this keeps the note from being pushed out by a long error). The error part is wrapped in `<noparse>`, so it cannot contain rich text |
| `SetStatusAside(string)` | Right end of the first row. For a short value that should always be visible. It is a separate string from the left side (`Ln/Col`, note, error); the left side is truncated with an ellipsis before reaching it. Same color as `Ln/Col`. Null or empty clears it |
| `SetStatusMeters(...)` / `ClearStatusMeters()` | Second row. Meters whose bars extend behind their labels. When empty, the whole second row disappears and the text area expands back |

The package does not decide what these show; use them for things like performance counters, tempo or frame rate.

## Modifier keys

- Mac Cmd (left or right) is treated the same as Ctrl (`KeyboardInputSource.Modifiers`).
- Alt, like Ctrl, acts as a "by word" modifier (word movement with Left/Right, word deletion with Backspace/Delete).
- Character input while Ctrl (including Cmd) is held is discarded in `HandleText`, so it is not handled twice alongside the key path.
- On Meta Quest, Cmd and Option (Alt) cannot be used; see [Platform notes](#platform-notes-meta-quest).

## Key repeat

`KeyboardInputSource` generates key repeats itself, because the Input System does not emit them. The first repeat comes after 0.4 s, then every 0.05 s. Every held key is reported as a repeat (`KeyPressed(EditorKey, EditorModifiers, bool repeat)`); `EditorController` decides whether to act on it (built-in actions via `IsRepeatable`, bindings via the `repeat` argument).

- Repeats: arrows, Home/End/PageUp/PageDown, Backspace, Delete, Tab, Enter (except with Ctrl held)
- Does not repeat: Ctrl+A/C/V/X, Ctrl+/
- Ctrl+Z / Ctrl+Y repeat only while Ctrl is held
- Host bindings (`Bind`) repeat only if given `repeat: true` (default is no, which prevents accidental repeated evaluation)

## Text input and JIS layout

Characters themselves are taken as the OS has resolved them (`onTextInput`). On platforms that interpret every physical keyboard as US layout (such as Meta Quest), a JIS keyboard needs its symbols converted to JIS in the app: enable `JisKeyboardLayout` on `CodeEditorView` (`KeyboardInputSource.UseJisLayout`). The conversion only takes effect at runtime on Android.

## Platform notes: Meta Quest

- **Cmd and Option (Alt) are unusable on Quest 3.** The OS captures either key the moment it is pressed, the app loses focus, and the whole keyboard becomes `Disabled` (observed with an Apple Magic Keyboard and `KeyboardProbe`). Use only Ctrl (the Magic Keyboard's control key), including for word-wise movement and deletion.
- A Bluetooth physical keyboard is always interpreted as a US layout. With a JIS keyboard, enable `JisKeyboardLayout` (see above).
- The key repeat timing (0.4 s, then every 0.05 s) matches the repeat the Quest 3 OS applies to text input.

## License

The code is MIT licensed (see [LICENSE.md](LICENSE.md)). The bundled JetBrains Mono font and the TMP SDF asset generated from it are under the SIL Open Font License 1.1 (see [Runtime/Fonts/JetBrainsMono-OFL.txt](Runtime/Fonts/JetBrainsMono-OFL.txt)).
