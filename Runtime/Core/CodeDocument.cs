using System;
using System.Collections.Generic;
using System.Text;

namespace hacha.LiveCodingEditor
{
    public enum MoveKind
    {
        Left,
        Right,
        Up,
        Down,
        WordLeft,
        WordRight,
        LineStart,
        LineEnd,
        DocStart,
        DocEnd,
        PageUp,
        PageDown,
    }

    /// <summary>
    /// テキスト本体・キャレット・選択・undo を持つ文書モデル。Unity にも言語にも依存しない。
    /// 対象は数十〜数百行のライブコーディング用コードなので、行のリストと丸ごとのスナップショット undo で足りる。
    /// </summary>
    public sealed class CodeDocument
    {
        const int MaxUndo = 500;

        enum EditKind
        {
            None,
            Typing,
            Backspace,
            Delete,
            Other,
        }

        struct Snapshot
        {
            public string[] Lines;
            public TextPos Caret;
            public TextPos Anchor;
        }

        readonly List<string> _lines = new List<string> { "" };
        readonly List<Snapshot> _undo = new List<Snapshot>();
        readonly List<Snapshot> _redo = new List<Snapshot>();

        TextPos _caret;
        TextPos _anchor;
        int _preferredCol = -1;

        EditKind _lastKind = EditKind.None;
        TextPos _caretAfterLastEdit;
        bool _lastTypedWasSpace;

        string _textCache;
        int _textCacheVersion = -1;

        /// <summary>行コメントの開始記号。空なら ToggleLineComment は何もしない。</summary>
        public string LineCommentPrefix = "//";
        public int TabSize = 2;
        public int PageSize = 20;

        /// <summary>テキストが変わるたびに増える。表示側の再描画判定用。</summary>
        public int Version { get; private set; }

        public TextPos Caret => _caret;
        public TextPos Anchor => _anchor;
        public bool HasSelection => _caret != _anchor;
        public TextPos SelectionStart => TextPos.Min(_caret, _anchor);
        public TextPos SelectionEnd => TextPos.Max(_caret, _anchor);
        public int LineCount => _lines.Count;
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public string GetLine(int line) => _lines[line];

        public string Text
        {
            get
            {
                if (_textCacheVersion != Version)
                {
                    _textCache = string.Join("\n", _lines);
                    _textCacheVersion = Version;
                }
                return _textCache;
            }
        }

        public string SelectedText => HasSelection ? GetRange(SelectionStart, SelectionEnd) : "";

        // ---- 全体の入れ替え ----

        public void SetText(string text, bool clearHistory = true)
        {
            if (clearHistory)
            {
                _undo.Clear();
                _redo.Clear();
                _lastKind = EditKind.None;
            }
            else
            {
                BeginEdit(EditKind.Other);
            }

            _lines.Clear();
            _lines.AddRange(Normalize(text).Split('\n'));
            _caret = _anchor = new TextPos(0, 0);
            _preferredCol = -1;
            Version++;
            if (!clearHistory) EndEdit(EditKind.Other);
        }

        // ---- 位置の変換 ----

        public int ToIndex(TextPos pos)
        {
            pos = Clamp(pos);
            int index = 0;
            for (int l = 0; l < pos.Line; l++) index += _lines[l].Length + 1;
            return index + pos.Col;
        }

        public TextPos FromIndex(int index)
        {
            if (index <= 0) return new TextPos(0, 0);
            for (int l = 0; l < _lines.Count; l++)
            {
                int len = _lines[l].Length;
                if (index <= len) return new TextPos(l, index);
                index -= len + 1;
            }
            return DocEnd;
        }

        public TextPos Clamp(TextPos pos)
        {
            int line = Math.Max(0, Math.Min(pos.Line, _lines.Count - 1));
            int col = Math.Max(0, Math.Min(pos.Col, _lines[line].Length));
            return new TextPos(line, col);
        }

        TextPos DocEnd => new TextPos(_lines.Count - 1, _lines[_lines.Count - 1].Length);

        // ---- 移動と選択 ----

        public void SetCaret(TextPos pos, bool extend = false)
        {
            _caret = Clamp(pos);
            if (!extend) _anchor = _caret;
            _preferredCol = -1;
            _lastKind = EditKind.None;
        }

        public void SelectAll()
        {
            _anchor = new TextPos(0, 0);
            _caret = DocEnd;
            _preferredCol = -1;
            _lastKind = EditKind.None;
        }

        public void Move(MoveKind kind, bool extend)
        {
            _lastKind = EditKind.None;

            // 選択中に Shift なしで左右へ動いたら、選択の端に畳むだけ
            if (HasSelection && !extend && (kind == MoveKind.Left || kind == MoveKind.Right))
            {
                _caret = _anchor = kind == MoveKind.Left ? SelectionStart : SelectionEnd;
                _preferredCol = -1;
                return;
            }

            bool vertical = kind == MoveKind.Up || kind == MoveKind.Down || kind == MoveKind.PageUp || kind == MoveKind.PageDown;
            if (vertical && _preferredCol < 0) _preferredCol = _caret.Col;

            _caret = Target(kind, _caret);
            if (!vertical) _preferredCol = -1;
            if (!extend) _anchor = _caret;
        }

        TextPos Target(MoveKind kind, TextPos p)
        {
            string line = _lines[p.Line];
            switch (kind)
            {
                case MoveKind.Left:
                    if (p.Col > 0) return new TextPos(p.Line, p.Col - 1);
                    return p.Line > 0 ? new TextPos(p.Line - 1, _lines[p.Line - 1].Length) : p;
                case MoveKind.Right:
                    if (p.Col < line.Length) return new TextPos(p.Line, p.Col + 1);
                    return p.Line < _lines.Count - 1 ? new TextPos(p.Line + 1, 0) : p;
                case MoveKind.Up: return Vertical(p, -1);
                case MoveKind.Down: return Vertical(p, 1);
                case MoveKind.PageUp: return Vertical(p, -PageSize);
                case MoveKind.PageDown: return Vertical(p, PageSize);
                case MoveKind.WordLeft: return WordLeft(p);
                case MoveKind.WordRight: return WordRight(p);
                case MoveKind.LineStart:
                {
                    // 1回目はインデントの直後、そこに居たら行頭へ
                    int indent = IndentOf(line);
                    return new TextPos(p.Line, p.Col == indent ? 0 : indent);
                }
                case MoveKind.LineEnd: return new TextPos(p.Line, line.Length);
                case MoveKind.DocStart: return new TextPos(0, 0);
                case MoveKind.DocEnd: return DocEnd;
                default: return p;
            }
        }

        TextPos Vertical(TextPos p, int delta)
        {
            int line = p.Line + delta;
            if (line < 0) return new TextPos(0, 0);
            if (line > _lines.Count - 1) return DocEnd;
            return new TextPos(line, Math.Min(_preferredCol, _lines[line].Length));
        }

        static int CharClass(char c)
        {
            if (char.IsWhiteSpace(c)) return 0;
            if (char.IsLetterOrDigit(c) || c == '_' || c == '$') return 1;
            return 2;
        }

        TextPos WordLeft(TextPos p)
        {
            if (p.Col == 0) return Target(MoveKind.Left, p);
            string line = _lines[p.Line];
            int c = p.Col;
            while (c > 0 && CharClass(line[c - 1]) == 0) c--;
            if (c > 0)
            {
                int cls = CharClass(line[c - 1]);
                while (c > 0 && CharClass(line[c - 1]) == cls) c--;
            }
            return new TextPos(p.Line, c);
        }

        TextPos WordRight(TextPos p)
        {
            string line = _lines[p.Line];
            if (p.Col == line.Length) return Target(MoveKind.Right, p);
            int c = p.Col;
            while (c < line.Length && CharClass(line[c]) == 0) c++;
            if (c < line.Length)
            {
                int cls = CharClass(line[c]);
                while (c < line.Length && CharClass(line[c]) == cls) c++;
            }
            return new TextPos(p.Line, c);
        }

        // ---- 編集 ----

        /// <summary>1文字の入力。連続したタイプは単語単位でひとつの undo にまとめる。</summary>
        public void TypeChar(char c)
        {
            if (c == '\n' || c == '\r')
            {
                NewLine();
                return;
            }
            if (c == '\t')
            {
                Indent();
                return;
            }

            bool isSpace = char.IsWhiteSpace(c);
            // 単語の切れ目（非空白→空白）で undo のまとまりを区切る
            if (isSpace && !_lastTypedWasSpace && _lastKind == EditKind.Typing) _lastKind = EditKind.None;

            BeginEdit(EditKind.Typing);
            DeleteSelectionRaw();
            _caret = _anchor = InsertRaw(_caret, c.ToString());
            _lastTypedWasSpace = isSpace;
            EndEdit(EditKind.Typing);
        }

        /// <summary>貼り付けなど、まとまったテキストの挿入。</summary>
        public void InsertText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            BeginEdit(EditKind.Other);
            DeleteSelectionRaw();
            _caret = _anchor = InsertRaw(_caret, Normalize(text));
            EndEdit(EditKind.Other);
        }

        /// <summary>改行。現在行のインデントを引き継ぐ。</summary>
        public void NewLine()
        {
            BeginEdit(EditKind.Other);
            DeleteSelectionRaw();
            string line = _lines[_caret.Line];
            int indent = Math.Min(IndentOf(line), _caret.Col);
            _caret = _anchor = InsertRaw(_caret, "\n" + line.Substring(0, indent));
            EndEdit(EditKind.Other);
        }

        public void Backspace(bool word = false)
        {
            if (HasSelection)
            {
                DeleteSelection();
                return;
            }
            if (_caret == new TextPos(0, 0)) return;

            TextPos from;
            if (word)
            {
                from = WordLeft(_caret);
            }
            else
            {
                from = Target(MoveKind.Left, _caret);
                // インデントの中ではタブ位置まで一気に戻す
                string line = _lines[_caret.Line];
                if (_caret.Col > 0 && TabSize > 0 && IndentOf(line) >= _caret.Col)
                {
                    bool onlySpaces = true;
                    for (int i = 0; i < _caret.Col; i++) onlySpaces &= line[i] == ' ';
                    if (onlySpaces) from = new TextPos(_caret.Line, _caret.Col - ((_caret.Col - 1) % TabSize + 1));
                }
            }

            var kind = word ? EditKind.Other : EditKind.Backspace;
            BeginEdit(kind);
            DeleteRaw(from, _caret);
            _caret = _anchor = from;
            EndEdit(kind);
        }

        public void Delete(bool word = false)
        {
            if (HasSelection)
            {
                DeleteSelection();
                return;
            }
            if (_caret == DocEnd) return;

            TextPos to = word ? WordRight(_caret) : Target(MoveKind.Right, _caret);
            var kind = word ? EditKind.Other : EditKind.Delete;
            BeginEdit(kind);
            DeleteRaw(_caret, to);
            EndEdit(kind);
        }

        public void DeleteSelection()
        {
            if (!HasSelection) return;
            BeginEdit(EditKind.Other);
            DeleteSelectionRaw();
            EndEdit(EditKind.Other);
        }

        /// <summary>Tab。複数行選択なら行ごとにインデント、それ以外は次のタブ位置まで空白を入れる。</summary>
        public void Indent()
        {
            if (TabSize <= 0) return;
            if (!HasSelection || SelectionStart.Line == SelectionEnd.Line)
            {
                BeginEdit(EditKind.Other);
                DeleteSelectionRaw();
                int n = TabSize - _caret.Col % TabSize;
                _caret = _anchor = InsertRaw(_caret, new string(' ', n));
                EndEdit(EditKind.Other);
                return;
            }

            BeginEdit(EditKind.Other);
            GetSelectedLineRange(out int first, out int last);
            string pad = new string(' ', TabSize);
            for (int l = first; l <= last; l++)
            {
                if (IsBlank(_lines[l])) continue;
                _lines[l] = pad + _lines[l];
                ShiftCarets(l, 0, TabSize);
            }
            EndEdit(EditKind.Other);
        }

        /// <summary>Shift+Tab。選択の有無にかかわらず、対象行の先頭の空白を TabSize 個まで削る。</summary>
        public void Outdent()
        {
            if (TabSize <= 0) return;
            GetSelectedLineRange(out int first, out int last);

            bool any = false;
            for (int l = first; l <= last && !any; l++) any = _lines[l].Length > 0 && _lines[l][0] == ' ';
            if (!any) return;

            BeginEdit(EditKind.Other);
            for (int l = first; l <= last; l++)
            {
                int n = 0;
                while (n < TabSize && n < _lines[l].Length && _lines[l][n] == ' ') n++;
                if (n == 0) continue;
                _lines[l] = _lines[l].Substring(n);
                RemoveFromCarets(l, 0, n);
            }
            EndEdit(EditKind.Other);
        }

        /// <summary>
        /// 選択範囲（なければキャレット行）の行コメントを切り替える。
        /// 空行以外がすべてコメント済みなら外し、そうでなければ全行を同じ桁でコメントアウトする。
        /// </summary>
        public void ToggleLineComment()
        {
            string prefix = LineCommentPrefix;
            if (string.IsNullOrEmpty(prefix)) return;

            GetSelectedLineRange(out int first, out int last);

            bool anyContent = false;
            bool allCommented = true;
            int minIndent = int.MaxValue;
            for (int l = first; l <= last; l++)
            {
                string line = _lines[l];
                if (IsBlank(line)) continue;
                anyContent = true;
                int indent = IndentOf(line);
                minIndent = Math.Min(minIndent, indent);
                if (string.CompareOrdinal(line, indent, prefix, 0, prefix.Length) != 0) allCommented = false;
            }

            BeginEdit(EditKind.Other);
            if (!anyContent)
            {
                // 空行しかない: キャレット行にコメント記号だけ置く
                int l = _caret.Line;
                int col = _lines[l].Length;
                _lines[l] += prefix + " ";
                ShiftCarets(l, col, prefix.Length + 1);
            }
            else if (allCommented)
            {
                for (int l = first; l <= last; l++)
                {
                    string line = _lines[l];
                    if (IsBlank(line)) continue;
                    int indent = IndentOf(line);
                    int n = prefix.Length;
                    if (indent + n < line.Length && line[indent + n] == ' ') n++;
                    _lines[l] = line.Remove(indent, n);
                    RemoveFromCarets(l, indent, n);
                }
            }
            else
            {
                string insert = prefix + " ";
                for (int l = first; l <= last; l++)
                {
                    if (IsBlank(_lines[l])) continue;
                    _lines[l] = _lines[l].Insert(minIndent, insert);
                    ShiftCarets(l, minIndent, insert.Length);
                }
            }
            EndEdit(EditKind.Other);
        }

        // ---- undo / redo ----

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            _redo.Add(Capture());
            Restore(Pop(_undo));
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            _undo.Add(Capture());
            Restore(Pop(_redo));
            return true;
        }

        static Snapshot Pop(List<Snapshot> stack)
        {
            var s = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            return s;
        }

        Snapshot Capture() => new Snapshot { Lines = _lines.ToArray(), Caret = _caret, Anchor = _anchor };

        void Restore(Snapshot s)
        {
            _lines.Clear();
            _lines.AddRange(s.Lines);
            _caret = s.Caret;
            _anchor = s.Anchor;
            _preferredCol = -1;
            _lastKind = EditKind.None;
            Version++;
        }

        void BeginEdit(EditKind kind)
        {
            bool coalesce = kind != EditKind.Other
                            && kind == _lastKind
                            && !HasSelection
                            && _caret == _caretAfterLastEdit;
            if (!coalesce)
            {
                _undo.Add(Capture());
                if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
            }
            _redo.Clear();
        }

        void EndEdit(EditKind kind)
        {
            _lastKind = kind;
            _caretAfterLastEdit = _caret;
            _preferredCol = -1;
            Version++;
        }

        // ---- 下請け ----

        /// <summary>
        /// 選択がかかっている行の範囲。選択の終端が行頭にあるとき、その行は含めない
        /// （Shift+↓ で行を選ぶと終端が次の行の先頭に来るため）。
        /// </summary>
        void GetSelectedLineRange(out int first, out int last)
        {
            first = SelectionStart.Line;
            last = SelectionEnd.Line;
            if (last > first && SelectionEnd.Col == 0) last--;
        }

        string GetRange(TextPos from, TextPos to)
        {
            if (from.Line == to.Line) return _lines[from.Line].Substring(from.Col, to.Col - from.Col);
            var sb = new StringBuilder();
            sb.Append(_lines[from.Line], from.Col, _lines[from.Line].Length - from.Col);
            for (int l = from.Line + 1; l < to.Line; l++) sb.Append('\n').Append(_lines[l]);
            sb.Append('\n').Append(_lines[to.Line], 0, to.Col);
            return sb.ToString();
        }

        void DeleteSelectionRaw()
        {
            if (!HasSelection) return;
            var start = SelectionStart;
            DeleteRaw(start, SelectionEnd);
            _caret = _anchor = start;
        }

        void DeleteRaw(TextPos from, TextPos to)
        {
            string head = _lines[from.Line].Substring(0, from.Col);
            string tail = _lines[to.Line].Substring(to.Col);
            _lines[from.Line] = head + tail;
            if (to.Line > from.Line) _lines.RemoveRange(from.Line + 1, to.Line - from.Line);
        }

        /// <summary>改行を含みうるテキストを挿入し、挿入後の末尾位置を返す。</summary>
        TextPos InsertRaw(TextPos at, string text)
        {
            string line = _lines[at.Line];
            string head = line.Substring(0, at.Col);
            string tail = line.Substring(at.Col);
            string[] parts = text.Split('\n');

            if (parts.Length == 1)
            {
                _lines[at.Line] = head + parts[0] + tail;
                return new TextPos(at.Line, at.Col + parts[0].Length);
            }

            _lines[at.Line] = head + parts[0];
            for (int i = 1; i < parts.Length - 1; i++) _lines.Insert(at.Line + i, parts[i]);
            string lastPart = parts[parts.Length - 1];
            _lines.Insert(at.Line + parts.Length - 1, lastPart + tail);
            return new TextPos(at.Line + parts.Length - 1, lastPart.Length);
        }

        /// <summary>
        /// line の atCol に delta 文字入ったぶん、キャレットと選択始点をずらす。
        /// 選択の始点がちょうど挿入位置にあるときは動かさない（挿入した記号ごと選択に残すため）。
        /// </summary>
        void ShiftCarets(int line, int atCol, int delta)
        {
            bool caretIsStart = HasSelection && _caret < _anchor;
            bool anchorIsStart = HasSelection && _anchor < _caret;
            _caret = ShiftPos(_caret, line, atCol, delta, caretIsStart);
            _anchor = ShiftPos(_anchor, line, atCol, delta, anchorIsStart);
        }

        static TextPos ShiftPos(TextPos p, int line, int atCol, int delta, bool isSelectionStart)
        {
            if (p.Line != line || p.Col < atCol) return p;
            if (p.Col == atCol && isSelectionStart) return p;
            return new TextPos(line, p.Col + delta);
        }

        /// <summary>line の atCol から count 文字消えたぶん、キャレットと選択始点を詰める。</summary>
        void RemoveFromCarets(int line, int atCol, int count)
        {
            _caret = RemoveFromPos(_caret, line, atCol, count);
            _anchor = RemoveFromPos(_anchor, line, atCol, count);
        }

        static TextPos RemoveFromPos(TextPos p, int line, int atCol, int count)
        {
            if (p.Line != line || p.Col <= atCol) return p;
            return new TextPos(line, Math.Max(atCol, p.Col - count));
        }

        static int IndentOf(string line)
        {
            int i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
            return i;
        }

        static bool IsBlank(string line) => IndentOf(line) == line.Length;

        string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            // タブは桁の計算を狂わせるので空白に開く
            if (text.IndexOf('\t') >= 0) text = text.Replace("\t", new string(' ', Math.Max(1, TabSize)));
            return text;
        }
    }
}
