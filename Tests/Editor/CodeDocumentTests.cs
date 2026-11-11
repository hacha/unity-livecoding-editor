using NUnit.Framework;
using hacha.LiveCodingEditor;

namespace hacha.LiveCodingEditor.Tests
{
    public class CodeDocumentTests
    {
        static CodeDocument Doc(string text)
        {
            var d = new CodeDocument();
            d.SetText(text);
            return d;
        }

        // ---- InsertText / TypeChar / NewLine ----

        [Test]
        public void InsertText_AtCaret_MovesCaretToEnd()
        {
            var d = Doc("ab");
            d.SetCaret(new TextPos(0, 1));
            d.InsertText("XY");
            Assert.AreEqual("aXYb", d.Text);
            Assert.AreEqual(new TextPos(0, 3), d.Caret);
        }

        [Test]
        public void InsertText_MultiLine_SplitsLines()
        {
            var d = Doc("ab");
            d.SetCaret(new TextPos(0, 1));
            d.InsertText("1\n2");
            Assert.AreEqual("a1\n2b", d.Text);
            Assert.AreEqual(new TextPos(1, 1), d.Caret);
        }

        [Test]
        public void InsertText_ReplacesSelection()
        {
            var d = Doc("abcde");
            d.SetCaret(new TextPos(0, 1));
            d.SetCaret(new TextPos(0, 4), extend: true);
            d.InsertText("X");
            Assert.AreEqual("aXe", d.Text);
        }

        [Test]
        public void TypeChar_InsertsAtCaret()
        {
            var d = Doc("");
            d.TypeChar('a');
            d.TypeChar('b');
            Assert.AreEqual("ab", d.Text);
            Assert.AreEqual(new TextPos(0, 2), d.Caret);
        }

        [Test]
        public void TypeChar_Newline_DelegatesToNewLine()
        {
            var d = Doc("  ab");
            d.SetCaret(new TextPos(0, 4));
            d.TypeChar('\n');
            Assert.AreEqual("  ab\n  ", d.Text);
        }

        [Test]
        public void NewLine_InheritsIndent_CappedByCaretColumn()
        {
            var d = Doc("    x");
            d.SetCaret(new TextPos(0, 4));
            d.NewLine();
            // インデント4文字を引き継ぐ
            Assert.AreEqual("    \n    x", d.Text);
            Assert.AreEqual(new TextPos(1, 4), d.Caret);
        }

        [Test]
        public void NewLine_CaretInsideIndent_InheritsUpToCaretOnly()
        {
            var d = Doc("    x");
            d.SetCaret(new TextPos(0, 2));
            d.NewLine();
            Assert.AreEqual("  \n  " + "  x", d.Text);
            Assert.AreEqual(new TextPos(1, 2), d.Caret);
        }

        // ---- Backspace ----

        [Test]
        public void Backspace_RemovesPrecedingChar()
        {
            var d = Doc("abc");
            d.SetCaret(new TextPos(0, 3));
            d.Backspace();
            Assert.AreEqual("ab", d.Text);
            Assert.AreEqual(new TextPos(0, 2), d.Caret);
        }

        [Test]
        public void Backspace_AtDocStart_NoOp()
        {
            var d = Doc("abc");
            d.SetCaret(new TextPos(0, 0));
            d.Backspace();
            Assert.AreEqual("abc", d.Text);
        }

        [Test]
        public void Backspace_AtLineStart_JoinsWithPreviousLine()
        {
            var d = Doc("ab\ncd");
            d.SetCaret(new TextPos(1, 0));
            d.Backspace();
            Assert.AreEqual("abcd", d.Text);
            Assert.AreEqual(new TextPos(0, 2), d.Caret);
        }

        [Test]
        public void Backspace_WithSelection_DeletesSelection()
        {
            var d = Doc("abcde");
            d.SetCaret(new TextPos(0, 1));
            d.SetCaret(new TextPos(0, 4), extend: true);
            d.Backspace();
            Assert.AreEqual("ae", d.Text);
            Assert.AreEqual(new TextPos(0, 1), d.Caret);
        }

        [Test]
        public void Backspace_InsideIndent_JumpsToTabStop()
        {
            // TabSize = 2 既定。4スペースのインデント直後から1回で2文字戻る
            var d = Doc("    x");
            d.SetCaret(new TextPos(0, 4));
            d.Backspace();
            Assert.AreEqual("  x", d.Text);
            Assert.AreEqual(new TextPos(0, 2), d.Caret);
        }

        [Test]
        public void Backspace_InsideIndent_OddColumn_GoesToLowerTabStop()
        {
            var d = Doc("    x");
            d.SetCaret(new TextPos(0, 3));
            d.Backspace();
            Assert.AreEqual("   x", d.Text); // 4スペースのうち1文字だけ消えてcol=2(タブ位置)
            Assert.AreEqual(new TextPos(0, 2), d.Caret);
        }

        [Test]
        public void Backspace_Word_DeletesPrecedingWord()
        {
            var d = Doc("foo bar");
            d.SetCaret(new TextPos(0, 7));
            d.Backspace(word: true);
            Assert.AreEqual("foo ", d.Text);
            Assert.AreEqual(new TextPos(0, 4), d.Caret);
        }

        // ---- Delete ----

        [Test]
        public void Delete_RemovesFollowingChar()
        {
            var d = Doc("abc");
            d.SetCaret(new TextPos(0, 1));
            d.Delete();
            Assert.AreEqual("ac", d.Text);
            Assert.AreEqual(new TextPos(0, 1), d.Caret);
        }

        [Test]
        public void Delete_AtDocEnd_NoOp()
        {
            var d = Doc("abc");
            d.SetCaret(new TextPos(0, 3));
            d.Delete();
            Assert.AreEqual("abc", d.Text);
        }

        [Test]
        public void Delete_AtLineEnd_JoinsWithNextLine()
        {
            var d = Doc("ab\ncd");
            d.SetCaret(new TextPos(0, 2));
            d.Delete();
            Assert.AreEqual("abcd", d.Text);
            Assert.AreEqual(new TextPos(0, 2), d.Caret);
        }

        [Test]
        public void Delete_WithSelection_DeletesSelection()
        {
            var d = Doc("abcde");
            d.SetCaret(new TextPos(0, 1));
            d.SetCaret(new TextPos(0, 4), extend: true);
            d.Delete();
            Assert.AreEqual("ae", d.Text);
        }

        [Test]
        public void Delete_Word_DeletesFollowingWord()
        {
            var d = Doc("foo bar");
            d.SetCaret(new TextPos(0, 0));
            d.Delete(word: true);
            Assert.AreEqual(" bar", d.Text);
            Assert.AreEqual(new TextPos(0, 0), d.Caret);
        }

        // ---- Move / selection ----

        [Test]
        public void Move_Right_AdvancesAcrossLineBreak()
        {
            var d = Doc("ab\ncd");
            d.SetCaret(new TextPos(0, 2));
            d.Move(MoveKind.Right, extend: false);
            Assert.AreEqual(new TextPos(1, 0), d.Caret);
        }

        [Test]
        public void Move_Left_RetreatsAcrossLineBreak()
        {
            var d = Doc("ab\ncd");
            d.SetCaret(new TextPos(1, 0));
            d.Move(MoveKind.Left, extend: false);
            Assert.AreEqual(new TextPos(0, 2), d.Caret);
        }

        [Test]
        public void Move_ShiftDown_MultipleTimes_SelectsMultipleLines()
        {
            var d = Doc("aaa\nbbb\nccc\nddd");
            d.SetCaret(new TextPos(0, 1));
            d.Move(MoveKind.Down, extend: true);
            d.Move(MoveKind.Down, extend: true);
            Assert.AreEqual(new TextPos(0, 1), d.Anchor);
            Assert.AreEqual(new TextPos(2, 1), d.Caret);
            Assert.AreEqual("aa\nbbb\nc", d.SelectedText);
        }

        [Test]
        public void Move_UpDown_KeepsPreferredColumn()
        {
            // 長い行で桁4に移動してから短い行→長い行と往復しても桁4を覚えている
            var d = Doc("aaaaaa\nbb\ncccccc");
            d.SetCaret(new TextPos(0, 4));
            d.Move(MoveKind.Down, extend: false); // 短い行 "bb" -> col clamp 2
            Assert.AreEqual(new TextPos(1, 2), d.Caret);
            d.Move(MoveKind.Down, extend: false); // 長い行に戻ったら preferredCol=4 を復元
            Assert.AreEqual(new TextPos(2, 4), d.Caret);
        }

        [Test]
        public void Move_PlainLeftRight_WhileSelected_CollapsesToSelectionEdge()
        {
            var d = Doc("abcdef");
            d.SetCaret(new TextPos(0, 1));
            d.SetCaret(new TextPos(0, 4), extend: true);
            d.Move(MoveKind.Left, extend: false);
            Assert.IsFalse(d.HasSelection);
            Assert.AreEqual(new TextPos(0, 1), d.Caret);

            d.SetCaret(new TextPos(0, 1));
            d.SetCaret(new TextPos(0, 4), extend: true);
            d.Move(MoveKind.Right, extend: false);
            Assert.IsFalse(d.HasSelection);
            Assert.AreEqual(new TextPos(0, 4), d.Caret);
        }

        [Test]
        public void Move_WordLeftRight()
        {
            var d = Doc("foo bar baz");
            d.SetCaret(new TextPos(0, 11));
            d.Move(MoveKind.WordLeft, extend: false);
            Assert.AreEqual(new TextPos(0, 8), d.Caret);
            d.Move(MoveKind.WordLeft, extend: false);
            Assert.AreEqual(new TextPos(0, 4), d.Caret);
            d.Move(MoveKind.WordRight, extend: false);
            Assert.AreEqual(new TextPos(0, 7), d.Caret);
        }

        [Test]
        public void Move_LineStart_TogglesBetweenIndentAndColumnZero()
        {
            var d = Doc("  abc");
            d.SetCaret(new TextPos(0, 4));
            d.Move(MoveKind.LineStart, extend: false);
            Assert.AreEqual(new TextPos(0, 2), d.Caret); // インデント直後
            d.Move(MoveKind.LineStart, extend: false);
            Assert.AreEqual(new TextPos(0, 0), d.Caret); // 行頭
        }

        [Test]
        public void Move_LineEnd_DocStart_DocEnd()
        {
            var d = Doc("aa\nbbb\nc");
            d.SetCaret(new TextPos(1, 1));
            d.Move(MoveKind.LineEnd, extend: false);
            Assert.AreEqual(new TextPos(1, 3), d.Caret);
            d.Move(MoveKind.DocEnd, extend: false);
            Assert.AreEqual(new TextPos(2, 1), d.Caret);
            d.Move(MoveKind.DocStart, extend: false);
            Assert.AreEqual(new TextPos(0, 0), d.Caret);
        }

        // ---- ToggleLineComment ----

        [Test]
        public void ToggleLineComment_OnlyBlankLines_PutsPrefixOnCaretLineOnly()
        {
            // 空行しか選んでいないときは、キャレット行にコメント記号だけを置く
            var d = Doc("\n\n");
            d.SetCaret(new TextPos(0, 0));
            d.SetCaret(new TextPos(1, 0), extend: true);
            d.ToggleLineComment();
            Assert.AreEqual("\n// \n", d.Text);
            Assert.AreEqual(new TextPos(1, 3), d.Caret);
            Assert.IsTrue(d.Undo());
            Assert.AreEqual("\n\n", d.Text);
        }

        [Test]
        public void ToggleLineComment_NoSelection_RoundTrips()
        {
            var d = Doc("let x = 1");
            d.SetCaret(new TextPos(0, 4));
            d.ToggleLineComment();
            Assert.AreEqual("// let x = 1", d.Text);
            Assert.AreEqual(new TextPos(0, 7), d.Caret);

            d.ToggleLineComment();
            Assert.AreEqual("let x = 1", d.Text);
            Assert.AreEqual(new TextPos(0, 4), d.Caret);
        }

        [Test]
        public void ToggleLineComment_MultiLineSelection_RoundTripsExactly()
        {
            var original = "if (x) {\n  foo()\n}";
            var d = Doc(original);
            d.SetCaret(new TextPos(0, 0));
            d.SetCaret(new TextPos(2, 1), extend: true);

            d.ToggleLineComment();
            Assert.AreEqual("// if (x) {\n//   foo()\n// }", d.Text);

            d.ToggleLineComment();
            Assert.AreEqual(original, d.Text);
        }

        [Test]
        public void ToggleLineComment_DifferentIndent_AlignsToMinIndentColumn()
        {
            var d = Doc("  a()\n    b()");
            d.SetCaret(new TextPos(0, 0));
            d.SetCaret(new TextPos(1, 6), extend: true);
            d.ToggleLineComment();
            Assert.AreEqual("  // a()\n  //   b()", d.Text);
        }

        [Test]
        public void ToggleLineComment_BlankLines_AreSkipped()
        {
            var d = Doc("a\n\nb");
            d.SetCaret(new TextPos(0, 0));
            d.SetCaret(new TextPos(2, 1), extend: true);
            d.ToggleLineComment();
            Assert.AreEqual("// a\n\n// b", d.Text);
        }

        [Test]
        public void ToggleLineComment_MixedCommentedAndPlain_CommentsAllLines()
        {
            var d = Doc("// a()\nb()");
            d.SetCaret(new TextPos(0, 0));
            d.SetCaret(new TextPos(1, 3), extend: true);
            d.ToggleLineComment();
            // 既にコメント済みの行にも重ねてコメントが付く
            Assert.AreEqual("// // a()\n// b()", d.Text);
        }

        [Test]
        public void ToggleLineComment_SelectionEndingAtNextLineStart_ExcludesThatLine()
        {
            var d = Doc("aaa\nbbb\nccc");
            d.SetCaret(new TextPos(0, 0));
            d.SetCaret(new TextPos(1, 0), extend: true); // Shift+Down 相当、1行だけ選択
            d.ToggleLineComment();
            Assert.AreEqual("// aaa\nbbb\nccc", d.Text);
        }

        [Test]
        public void ToggleLineComment_KeepsSelectionAndAdjustsAnchorCaretColumns()
        {
            var d = Doc("foo()\nbar()");
            d.SetCaret(new TextPos(0, 1));
            d.SetCaret(new TextPos(1, 2), extend: true);
            d.ToggleLineComment();
            Assert.AreEqual(new TextPos(0, 4), d.Anchor);
            Assert.AreEqual(new TextPos(1, 5), d.Caret);
            Assert.IsTrue(d.HasSelection);
        }

        [Test]
        public void ToggleLineComment_SingleUndo_RevertsMultiLineToggle()
        {
            var original = "a()\nb()";
            var d = Doc(original);
            d.SetCaret(new TextPos(0, 0));
            d.SetCaret(new TextPos(1, 3), extend: true);
            d.ToggleLineComment();
            Assert.AreNotEqual(original, d.Text);
            bool undone = d.Undo();
            Assert.IsTrue(undone);
            Assert.AreEqual(original, d.Text);
        }

        [Test]
        public void ToggleLineComment_NoSpaceAfterMarker_StillUncomments()
        {
            var d = Doc("//foo");
            d.SetCaret(new TextPos(0, 0));
            d.ToggleLineComment();
            Assert.AreEqual("foo", d.Text);
        }

        // ---- Indent / Outdent ----

        [Test]
        public void Indent_NoSelection_InsertsToNextTabStop()
        {
            var d = Doc("x");
            d.SetCaret(new TextPos(0, 0));
            d.Indent();
            Assert.AreEqual("  x", d.Text); // TabSize=2
            Assert.AreEqual(new TextPos(0, 2), d.Caret);
        }

        [Test]
        public void Indent_MultiLineSelection_IndentsEachNonBlankLine()
        {
            var d = Doc("a\n\nb");
            d.SetCaret(new TextPos(0, 0));
            d.SetCaret(new TextPos(2, 1), extend: true);
            d.Indent();
            Assert.AreEqual("  a\n\n  b", d.Text);
        }

        [Test]
        public void Outdent_RemovesLeadingSpacesUpToTabSize()
        {
            var d = Doc("    x");
            d.SetCaret(new TextPos(0, 4));
            d.Outdent();
            Assert.AreEqual("  x", d.Text);
        }

        [Test]
        public void Outdent_MultiLineSelection()
        {
            var d = Doc("  a\n    b");
            d.SetCaret(new TextPos(0, 0));
            d.SetCaret(new TextPos(1, 5), extend: true);
            d.Outdent();
            Assert.AreEqual("a\n  b", d.Text);
        }

        [Test]
        public void Outdent_NoLeadingSpace_NoOpAndNoUndoEntry()
        {
            var d = Doc("x");
            d.SetCaret(new TextPos(0, 1));
            bool canUndoBefore = d.CanUndo;
            d.Outdent();
            Assert.AreEqual("x", d.Text);
            Assert.AreEqual(canUndoBefore, d.CanUndo);
        }

        // ---- Undo / Redo ----

        [Test]
        public void Undo_Typing_GroupsByWord()
        {
            var d = Doc("");
            foreach (char c in "ab cd") d.TypeChar(c);
            Assert.AreEqual("ab cd", d.Text);

            d.Undo();
            Assert.AreEqual("ab", d.Text);

            d.Undo();
            Assert.AreEqual("", d.Text);
        }

        [Test]
        public void Undo_MoveBetweenEdits_SplitsUndoGroup()
        {
            var d = Doc("");
            d.TypeChar('a');
            d.TypeChar('b');
            d.Move(MoveKind.Left, extend: false);
            d.TypeChar('c');
            Assert.AreEqual("acb", d.Text);

            d.Undo();
            Assert.AreEqual("ab", d.Text);
            d.Undo();
            Assert.AreEqual("", d.Text);
        }

        [Test]
        public void Undo_ConsecutivePlainBackspaces_Coalesce()
        {
            var d = Doc("abc");
            d.SetCaret(new TextPos(0, 3));
            d.Backspace();
            d.Backspace();
            d.Backspace();
            Assert.AreEqual("", d.Text);

            d.Undo();
            Assert.AreEqual("abc", d.Text);
            Assert.AreEqual(new TextPos(0, 3), d.Caret);
        }

        [Test]
        public void Redo_RestoresUndoneEdit()
        {
            var d = Doc("");
            d.TypeChar('a');
            d.Undo();
            Assert.AreEqual("", d.Text);
            bool redone = d.Redo();
            Assert.IsTrue(redone);
            Assert.AreEqual("a", d.Text);
        }

        [Test]
        public void Redo_ClearedByNewEdit()
        {
            var d = Doc("");
            d.TypeChar('a');
            d.Undo();
            Assert.IsTrue(d.CanRedo);
            d.TypeChar('b');
            Assert.IsFalse(d.CanRedo);
            Assert.AreEqual("b", d.Text);
        }

        // ---- ToIndex / FromIndex / SelectedText ----

        [Test]
        public void ToIndex_FromIndex_RoundTrip()
        {
            var d = Doc("aa\nbbb\nc");
            var pos = new TextPos(1, 2);
            int index = d.ToIndex(pos);
            Assert.AreEqual(pos, d.FromIndex(index));
        }

        [Test]
        public void SelectedText_SpansMultipleLines()
        {
            var d = Doc("aaa\nbbb\nccc");
            d.SetCaret(new TextPos(0, 1));
            d.SetCaret(new TextPos(2, 2), extend: true);
            Assert.AreEqual("aa\nbbb\ncc", d.SelectedText);
        }
    }
}
