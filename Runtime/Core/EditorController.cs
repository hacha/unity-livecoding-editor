using System;
using System.Collections.Generic;

namespace hacha.LiveCodingEditor
{
    /// <summary>
    /// エディタが受けるキー。文字そのものの入力は HandleText で別に受ける。
    /// 組み込みの操作に使うキーに加えて、利用側が <see cref="EditorController.Bind"/> で割り当てられるキーを並べてある。
    /// </summary>
    public enum EditorKey
    {
        None,
        Left,
        Right,
        Up,
        Down,
        Home,
        End,
        PageUp,
        PageDown,
        Backspace,
        Delete,
        Enter,
        Tab,
        A,
        C,
        V,
        X,
        Y,
        Z,
        Slash,
        // ここから下は組み込みの操作を持たない。利用側の割り当て用（既存の値を変えないよう末尾に足す）
        B,
        D,
        E,
        F,
        G,
        H,
        I,
        J,
        K,
        L,
        M,
        N,
        O,
        P,
        Q,
        R,
        S,
        T,
        U,
        W,
        Digit0,
        Digit1,
        Digit2,
        Digit3,
        Digit4,
        Digit5,
        Digit6,
        Digit7,
        Digit8,
        Digit9,
        F1,
        F2,
        F3,
        F4,
        F5,
        F6,
        F7,
        F8,
        F9,
        F10,
        F11,
        F12,
        Escape,
        Space,
    }

    [Flags]
    public enum EditorModifiers
    {
        None = 0,
        Shift = 1,
        /// <summary>Ctrl または Cmd。</summary>
        Ctrl = 2,
        Alt = 4,
    }

    /// <summary>
    /// キー入力を文書操作に割り当てる。入力デバイスには依存しない。
    /// 組み込みの操作（移動・編集・コメント・undo など）に加えて、利用側が <see cref="Bind"/> でキーに処理を割り当てられる。
    /// 割り当ては組み込みの操作より先に見るので、組み込みのキーを上書きすることもできる。
    /// </summary>
    public sealed class EditorController
    {
        sealed class Binding
        {
            public Action Action;
            public bool Repeat;
            public bool WhenUnfocused;
        }

        public CodeDocument Document { get; }
        public IClipboard Clipboard;
        public bool ReadOnly;

        /// <summary>
        /// false の間は文字入力と組み込みの操作を受けない。<see cref="Bind"/> で whenUnfocused を付けた割り当てだけが動く。
        /// </summary>
        public bool HasFocus = true;

        readonly Dictionary<(EditorKey, EditorModifiers), Binding> _bindings = new Dictionary<(EditorKey, EditorModifiers), Binding>();

        public EditorController(CodeDocument document = null)
        {
            Document = document ?? new CodeDocument();
        }

        /// <summary>
        /// キーと修飾キーの組に処理を割り当てる。修飾キーは完全一致で見る（Ctrl+Enter の割り当ては Ctrl+Shift+Enter では動かない）。
        /// 同じ組に割り当て直すと置き換わる。ReadOnly の間も動く。
        /// </summary>
        /// <param name="repeat">押しっぱなしでキーリピートに合わせて繰り返し呼ぶか。</param>
        /// <param name="whenUnfocused">フォーカスが無い間（<see cref="HasFocus"/> が false）も呼ぶか。エディタを隠していても効かせたいキー用。</param>
        public void Bind(EditorKey key, EditorModifiers modifiers, Action action, bool repeat = false, bool whenUnfocused = false)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            _bindings[(key, modifiers)] = new Binding { Action = action, Repeat = repeat, WhenUnfocused = whenUnfocused };
        }

        /// <returns>割り当てがあって外したら true</returns>
        public bool Unbind(EditorKey key, EditorModifiers modifiers) => _bindings.Remove((key, modifiers));

        public bool IsBound(EditorKey key, EditorModifiers modifiers) => _bindings.ContainsKey((key, modifiers));

        /// <param name="repeat">キーリピートで来た 2 回目以降の押下なら true。</param>
        /// <returns>キーを消費したら true</returns>
        public bool HandleKey(EditorKey key, EditorModifiers mods, bool repeat = false)
        {
            if (_bindings.TryGetValue((key, mods), out var binding))
            {
                if (!HasFocus && !binding.WhenUnfocused) return false;
                if (repeat && !binding.Repeat) return true;
                binding.Action();
                return true;
            }

            if (!HasFocus) return false;
            if (repeat && !IsRepeatable(key, mods)) return false;

            var doc = Document;
            bool shift = (mods & EditorModifiers.Shift) != 0;
            bool ctrl = (mods & EditorModifiers.Ctrl) != 0;
            bool word = ctrl || (mods & EditorModifiers.Alt) != 0;

            switch (key)
            {
                case EditorKey.Left: doc.Move(word ? MoveKind.WordLeft : MoveKind.Left, shift); return true;
                case EditorKey.Right: doc.Move(word ? MoveKind.WordRight : MoveKind.Right, shift); return true;
                case EditorKey.Up: doc.Move(MoveKind.Up, shift); return true;
                case EditorKey.Down: doc.Move(MoveKind.Down, shift); return true;
                case EditorKey.Home: doc.Move(ctrl ? MoveKind.DocStart : MoveKind.LineStart, shift); return true;
                case EditorKey.End: doc.Move(ctrl ? MoveKind.DocEnd : MoveKind.LineEnd, shift); return true;
                case EditorKey.PageUp: doc.Move(MoveKind.PageUp, shift); return true;
                case EditorKey.PageDown: doc.Move(MoveKind.PageDown, shift); return true;

                case EditorKey.Enter:
                    // Ctrl+Enter は割り当てが無ければ何もしない（改行にもしない。評価のつもりで改行が入らないように）
                    if (ctrl) return false;
                    if (!ReadOnly) doc.NewLine();
                    return true;
            }

            if (ctrl)
            {
                switch (key)
                {
                    case EditorKey.A: doc.SelectAll(); return true;
                    case EditorKey.C: Copy(); return true;
                }
            }

            if (ReadOnly) return false;

            switch (key)
            {
                case EditorKey.Backspace: doc.Backspace(word); return true;
                case EditorKey.Delete: doc.Delete(word); return true;
                case EditorKey.Tab:
                    if (shift) doc.Outdent();
                    else doc.Indent();
                    return true;
            }

            if (!ctrl) return false;

            switch (key)
            {
                case EditorKey.X:
                    if (Copy()) doc.DeleteSelection();
                    return true;
                case EditorKey.V:
                    if (Clipboard != null) doc.InsertText(Clipboard.Text);
                    return true;
                case EditorKey.Z:
                    if (shift) doc.Redo();
                    else doc.Undo();
                    return true;
                case EditorKey.Y: doc.Redo(); return true;
                case EditorKey.Slash: doc.ToggleLineComment(); return true;
            }
            return false;
        }

        /// <summary>組み込みの操作のうち、押しっぱなしで繰り返すもの。</summary>
        static bool IsRepeatable(EditorKey key, EditorModifiers mods)
        {
            bool ctrl = (mods & EditorModifiers.Ctrl) != 0;
            switch (key)
            {
                case EditorKey.Left:
                case EditorKey.Right:
                case EditorKey.Up:
                case EditorKey.Down:
                case EditorKey.Home:
                case EditorKey.End:
                case EditorKey.PageUp:
                case EditorKey.PageDown:
                case EditorKey.Backspace:
                case EditorKey.Delete:
                    return true;
                case EditorKey.Enter:
                case EditorKey.Tab:
                    return !ctrl;
                case EditorKey.Z:
                case EditorKey.Y:
                    return ctrl;
                default:
                    return false;
            }
        }

        /// <summary>OS が確定させた文字の入力。制御文字と Ctrl 併用時の文字は捨てる（キー側で処理済み）。</summary>
        public bool HandleText(char c, EditorModifiers mods)
        {
            if (!HasFocus || ReadOnly) return false;
            if ((mods & EditorModifiers.Ctrl) != 0) return false;
            if (c < ' ' || c == 127) return false;
            // macOS が矢印キー等に割り当てている私用領域
            if (c >= 0xF700 && c <= 0xF8FF) return false;
            Document.TypeChar(c);
            return true;
        }

        bool Copy()
        {
            if (Clipboard == null || !Document.HasSelection) return false;
            Clipboard.Text = Document.SelectedText;
            return true;
        }
    }
}
