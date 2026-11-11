using NUnit.Framework;
using hacha.LiveCodingEditor;

namespace hacha.LiveCodingEditor.Tests
{
    /// <summary>テスト用の単純なクリップボード。</summary>
    class FakeClipboard : IClipboard
    {
        public string Text { get; set; } = "";
    }

    public class EditorControllerTests
    {
        static EditorController Make(string text = "")
        {
            var c = new EditorController();
            if (!string.IsNullOrEmpty(text)) c.Document.SetText(text);
            return c;
        }

        // ---- 利用側の割り当て（Bind） ----

        [Test]
        public void Bind_FiresOnExactModifiers()
        {
            var c = Make("ab");
            int fired = 0;
            c.Bind(EditorKey.Enter, EditorModifiers.Ctrl, () => fired++);

            Assert.IsTrue(c.HandleKey(EditorKey.Enter, EditorModifiers.Ctrl));
            Assert.AreEqual(1, fired);
            Assert.AreEqual("ab", c.Document.Text);

            // 修飾キーは完全一致。Ctrl+Shift+Enter には別に割り当てが要る
            Assert.IsFalse(c.HandleKey(EditorKey.Enter, EditorModifiers.Ctrl | EditorModifiers.Shift));
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void UnboundCtrlEnter_DoesNothing()
        {
            // 評価のつもりの Ctrl+Enter で改行が入らないように
            var c = Make("ab");
            c.Document.SetCaret(new TextPos(0, 2));

            Assert.IsFalse(c.HandleKey(EditorKey.Enter, EditorModifiers.Ctrl));
            Assert.AreEqual("ab", c.Document.Text);
        }

        [Test]
        public void PlainEnter_InsertsNewLine_EvenWhenCtrlEnterIsBound()
        {
            var c = Make("ab");
            c.Document.SetCaret(new TextPos(0, 2));
            bool fired = false;
            c.Bind(EditorKey.Enter, EditorModifiers.Ctrl, () => fired = true);

            c.HandleKey(EditorKey.Enter, EditorModifiers.None);

            Assert.IsFalse(fired);
            Assert.AreEqual("ab\n", c.Document.Text);
        }

        [Test]
        public void ShiftEnter_InsertsNewLine()
        {
            var c = Make("ab");
            c.Document.SetCaret(new TextPos(0, 2));

            c.HandleKey(EditorKey.Enter, EditorModifiers.Shift);

            Assert.AreEqual("ab\n", c.Document.Text);
        }

        [Test]
        public void Bind_OverridesBuiltIn()
        {
            var c = Make("foo()");
            bool fired = false;
            c.Bind(EditorKey.Slash, EditorModifiers.Ctrl, () => fired = true);

            c.HandleKey(EditorKey.Slash, EditorModifiers.Ctrl);

            Assert.IsTrue(fired);
            Assert.AreEqual("foo()", c.Document.Text);
        }

        [Test]
        public void Bind_Rebind_Replaces_And_Unbind_Removes()
        {
            var c = Make();
            string fired = null;
            c.Bind(EditorKey.T, EditorModifiers.Ctrl, () => fired = "first");
            c.Bind(EditorKey.T, EditorModifiers.Ctrl, () => fired = "second");

            c.HandleKey(EditorKey.T, EditorModifiers.Ctrl);
            Assert.AreEqual("second", fired);

            Assert.IsTrue(c.Unbind(EditorKey.T, EditorModifiers.Ctrl));
            Assert.IsFalse(c.IsBound(EditorKey.T, EditorModifiers.Ctrl));
            fired = null;
            Assert.IsFalse(c.HandleKey(EditorKey.T, EditorModifiers.Ctrl));
            Assert.IsNull(fired);
        }

        [Test]
        public void Bind_FiresWhenReadOnly()
        {
            var c = Make("ab");
            c.ReadOnly = true;
            bool fired = false;
            c.Bind(EditorKey.Enter, EditorModifiers.Ctrl, () => fired = true);

            c.HandleKey(EditorKey.Enter, EditorModifiers.Ctrl);

            Assert.IsTrue(fired);
        }

        [Test]
        public void Bind_Unfocused_FiresOnlyWhenAllowed()
        {
            var c = Make();
            c.HasFocus = false;
            bool focusedOnly = false, always = false;
            c.Bind(EditorKey.Enter, EditorModifiers.Ctrl, () => focusedOnly = true);
            c.Bind(EditorKey.H, EditorModifiers.Ctrl | EditorModifiers.Shift, () => always = true, whenUnfocused: true);

            Assert.IsFalse(c.HandleKey(EditorKey.Enter, EditorModifiers.Ctrl));
            Assert.IsTrue(c.HandleKey(EditorKey.H, EditorModifiers.Ctrl | EditorModifiers.Shift));

            Assert.IsFalse(focusedOnly);
            Assert.IsTrue(always);
        }

        [Test]
        public void Unfocused_IgnoresBuiltInsAndText()
        {
            var c = Make("ab");
            c.Document.SetCaret(new TextPos(0, 2));
            c.HasFocus = false;

            Assert.IsFalse(c.HandleKey(EditorKey.Backspace, EditorModifiers.None));
            Assert.IsFalse(c.HandleKey(EditorKey.Enter, EditorModifiers.None));
            Assert.IsFalse(c.HandleText('x', EditorModifiers.None));
            Assert.AreEqual("ab", c.Document.Text);
        }

        [Test]
        public void Bind_Repeat_FollowsFlag()
        {
            var c = Make();
            int once = 0, repeated = 0;
            c.Bind(EditorKey.T, EditorModifiers.Ctrl, () => once++);
            c.Bind(EditorKey.F5, EditorModifiers.None, () => repeated++, repeat: true);

            c.HandleKey(EditorKey.T, EditorModifiers.Ctrl);
            c.HandleKey(EditorKey.T, EditorModifiers.Ctrl, repeat: true);
            c.HandleKey(EditorKey.T, EditorModifiers.Ctrl, repeat: true);
            c.HandleKey(EditorKey.F5, EditorModifiers.None);
            c.HandleKey(EditorKey.F5, EditorModifiers.None, repeat: true);
            c.HandleKey(EditorKey.F5, EditorModifiers.None, repeat: true);

            Assert.AreEqual(1, once);
            Assert.AreEqual(3, repeated);
        }

        // ---- 組み込みの操作のキーリピート ----

        static bool Repeats(EditorKey key, EditorModifiers mods, string text = "abc def")
        {
            var c = Make(text);
            c.Document.SetCaret(new TextPos(0, 3));
            c.Clipboard = new FakeClipboard { Text = "z" };
            c.Document.TypeChar('q'); // undo を積んでおく
            string before = c.Document.Text;
            var caret = c.Document.Caret;
            bool handled = c.HandleKey(key, mods, repeat: true);
            return handled && (c.Document.Text != before || c.Document.Caret != caret || c.Document.HasSelection);
        }

        [Test]
        public void Repeat_NavigationAndDeletion_Repeat()
        {
            Assert.IsTrue(Repeats(EditorKey.Left, EditorModifiers.None));
            Assert.IsTrue(Repeats(EditorKey.Right, EditorModifiers.None));
            Assert.IsTrue(Repeats(EditorKey.Down, EditorModifiers.None, "abc\ndef"));
            Assert.IsTrue(Repeats(EditorKey.Up, EditorModifiers.None, "abc\ndef"));
            Assert.IsTrue(Repeats(EditorKey.Backspace, EditorModifiers.None));
            Assert.IsTrue(Repeats(EditorKey.Delete, EditorModifiers.None));
        }

        [Test]
        public void Repeat_EnterAndTab_RepeatOnlyWithoutCtrl()
        {
            Assert.IsTrue(Repeats(EditorKey.Enter, EditorModifiers.None));
            Assert.IsTrue(Repeats(EditorKey.Tab, EditorModifiers.None));
            Assert.IsFalse(Repeats(EditorKey.Enter, EditorModifiers.Ctrl));
            Assert.IsFalse(Repeats(EditorKey.Tab, EditorModifiers.Ctrl));
        }

        [Test]
        public void Repeat_UndoRedo_RepeatOnlyWithCtrl()
        {
            Assert.IsTrue(Repeats(EditorKey.Z, EditorModifiers.Ctrl));
            Assert.IsFalse(Repeats(EditorKey.Z, EditorModifiers.None));
            Assert.IsFalse(Repeats(EditorKey.Y, EditorModifiers.None));
        }

        [Test]
        public void Repeat_ClipboardSelectAllComment_DoNotRepeat()
        {
            Assert.IsFalse(Repeats(EditorKey.A, EditorModifiers.Ctrl));
            Assert.IsFalse(Repeats(EditorKey.V, EditorModifiers.Ctrl));
            Assert.IsFalse(Repeats(EditorKey.X, EditorModifiers.Ctrl));
            Assert.IsFalse(Repeats(EditorKey.C, EditorModifiers.Ctrl));
            Assert.IsFalse(Repeats(EditorKey.Slash, EditorModifiers.Ctrl));
        }

        [Test]
        public void CtrlSlash_TogglesLineComment()
        {
            var c = Make("foo()");
            c.Document.SetCaret(new TextPos(0, 0));

            bool handled = c.HandleKey(EditorKey.Slash, EditorModifiers.Ctrl);

            Assert.IsTrue(handled);
            Assert.AreEqual("// foo()", c.Document.Text);
        }

        [Test]
        public void CtrlZ_Undo_And_CtrlShiftZ_Redo()
        {
            var c = Make("");
            c.Document.TypeChar('a');

            bool undone = c.HandleKey(EditorKey.Z, EditorModifiers.Ctrl);
            Assert.IsTrue(undone);
            Assert.AreEqual("", c.Document.Text);

            bool redone = c.HandleKey(EditorKey.Z, EditorModifiers.Ctrl | EditorModifiers.Shift);
            Assert.IsTrue(redone);
            Assert.AreEqual("a", c.Document.Text);
        }

        [Test]
        public void CtrlY_Redo()
        {
            var c = Make("");
            c.Document.TypeChar('a');
            c.Document.Undo();
            Assert.AreEqual("", c.Document.Text);

            c.HandleKey(EditorKey.Y, EditorModifiers.Ctrl);
            Assert.AreEqual("a", c.Document.Text);
        }

        [Test]
        public void CtrlA_SelectsAll()
        {
            var c = Make("abc\ndef");
            c.HandleKey(EditorKey.A, EditorModifiers.Ctrl);
            Assert.AreEqual(new TextPos(0, 0), c.Document.Anchor);
            Assert.AreEqual(new TextPos(1, 3), c.Document.Caret);
        }

        [Test]
        public void CtrlC_CopiesSelectionToClipboard()
        {
            var c = Make("abcdef");
            c.Clipboard = new FakeClipboard();
            c.Document.SetCaret(new TextPos(0, 1));
            c.Document.SetCaret(new TextPos(0, 4), extend: true);

            c.HandleKey(EditorKey.C, EditorModifiers.Ctrl);

            Assert.AreEqual("bcd", c.Clipboard.Text);
        }

        [Test]
        public void CtrlX_CutsSelectionToClipboard()
        {
            var c = Make("abcdef");
            c.Clipboard = new FakeClipboard();
            c.Document.SetCaret(new TextPos(0, 1));
            c.Document.SetCaret(new TextPos(0, 4), extend: true);

            c.HandleKey(EditorKey.X, EditorModifiers.Ctrl);

            Assert.AreEqual("bcd", c.Clipboard.Text);
            Assert.AreEqual("aef", c.Document.Text);
        }

        [Test]
        public void CtrlV_PastesClipboardText()
        {
            var c = Make("ac");
            c.Clipboard = new FakeClipboard { Text = "b" };
            c.Document.SetCaret(new TextPos(0, 1));

            c.HandleKey(EditorKey.V, EditorModifiers.Ctrl);

            Assert.AreEqual("abc", c.Document.Text);
        }

        [Test]
        public void ShiftTab_Outdents()
        {
            var c = Make("    x");
            c.Document.SetCaret(new TextPos(0, 4));

            c.HandleKey(EditorKey.Tab, EditorModifiers.Shift);

            Assert.AreEqual("  x", c.Document.Text);
        }

        [Test]
        public void Tab_WithoutShift_Indents()
        {
            var c = Make("x");
            c.Document.SetCaret(new TextPos(0, 0));

            c.HandleKey(EditorKey.Tab, EditorModifiers.None);

            Assert.AreEqual("  x", c.Document.Text);
        }

        [Test]
        public void ReadOnly_BlocksEditingKeys()
        {
            var c = Make("abc");
            c.ReadOnly = true;
            c.Document.SetCaret(new TextPos(0, 3));

            bool backspaceHandled = c.HandleKey(EditorKey.Backspace, EditorModifiers.None);
            bool deleteHandled = c.HandleKey(EditorKey.Delete, EditorModifiers.None);
            bool tabHandled = c.HandleKey(EditorKey.Tab, EditorModifiers.None);

            Assert.IsFalse(backspaceHandled);
            Assert.IsFalse(deleteHandled);
            Assert.IsFalse(tabHandled);
            Assert.AreEqual("abc", c.Document.Text);
        }

        [Test]
        public void ReadOnly_AllowsMoveCopyAndBindings()
        {
            var c = Make("abc");
            c.ReadOnly = true;
            c.Clipboard = new FakeClipboard();
            string submitted = null;
            c.Bind(EditorKey.Enter, EditorModifiers.Ctrl, () => submitted = c.Document.Text);

            c.Document.SetCaret(new TextPos(0, 0));

            bool moveHandled = c.HandleKey(EditorKey.Right, EditorModifiers.None);
            Assert.IsTrue(moveHandled);
            Assert.AreEqual(new TextPos(0, 1), c.Document.Caret);

            c.Document.SetCaret(new TextPos(0, 0));
            c.Document.SetCaret(new TextPos(0, 2), extend: true);
            bool copyHandled = c.HandleKey(EditorKey.C, EditorModifiers.Ctrl);
            Assert.IsTrue(copyHandled);
            Assert.AreEqual("ab", c.Clipboard.Text);

            bool submitHandled = c.HandleKey(EditorKey.Enter, EditorModifiers.Ctrl);
            Assert.IsTrue(submitHandled);
            Assert.AreEqual("abc", submitted);
        }

        [Test]
        public void ReadOnly_BlocksUndoAndToggleComment()
        {
            // ReadOnly のチェックが Ctrl+Z/Slash より手前にあるため、これらは無視される
            var c = Make("abc");
            c.Document.SetCaret(new TextPos(0, 3));
            c.Document.TypeChar('d'); // "abcd" にして undo できる状態を作る
            c.ReadOnly = true;

            bool undoHandled = c.HandleKey(EditorKey.Z, EditorModifiers.Ctrl);
            bool commentHandled = c.HandleKey(EditorKey.Slash, EditorModifiers.Ctrl);

            Assert.IsFalse(undoHandled);
            Assert.IsFalse(commentHandled);
            Assert.AreEqual("abcd", c.Document.Text);
        }

        [Test]
        public void HandleText_InsertsPrintableChar()
        {
            var c = Make("");
            bool handled = c.HandleText('a', EditorModifiers.None);
            Assert.IsTrue(handled);
            Assert.AreEqual("a", c.Document.Text);
        }

        [Test]
        public void HandleText_DiscardsControlCharacters()
        {
            var c = Make("");
            bool handled = c.HandleText('\b', EditorModifiers.None); // 0x08 < ' '
            Assert.IsFalse(handled);
            Assert.AreEqual("", c.Document.Text);
        }

        [Test]
        public void HandleText_DiscardsWhenCtrlHeld()
        {
            var c = Make("");
            bool handled = c.HandleText('a', EditorModifiers.Ctrl);
            Assert.IsFalse(handled);
            Assert.AreEqual("", c.Document.Text);
        }

        [Test]
        public void HandleText_DiscardsPrivateUseAreaChars()
        {
            var c = Make("");
            bool handled = c.HandleText((char)0xF700, EditorModifiers.None);
            Assert.IsFalse(handled);
            Assert.AreEqual("", c.Document.Text);
        }

        [Test]
        public void HandleText_ReadOnly_Discards()
        {
            var c = Make("");
            c.ReadOnly = true;
            bool handled = c.HandleText('a', EditorModifiers.None);
            Assert.IsFalse(handled);
            Assert.AreEqual("", c.Document.Text);
        }
    }
}
