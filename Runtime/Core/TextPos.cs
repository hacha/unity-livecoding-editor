using System;

namespace hacha.LiveCodingEditor
{
    /// <summary>文書内の位置（0始まり）。Col は行内の文字数で数え、行末は Col == 行の長さ。</summary>
    public readonly struct TextPos : IEquatable<TextPos>, IComparable<TextPos>
    {
        public readonly int Line;
        public readonly int Col;

        public TextPos(int line, int col)
        {
            Line = line;
            Col = col;
        }

        public int CompareTo(TextPos other) => Line != other.Line ? Line.CompareTo(other.Line) : Col.CompareTo(other.Col);
        public bool Equals(TextPos other) => Line == other.Line && Col == other.Col;
        public override bool Equals(object obj) => obj is TextPos p && Equals(p);
        public override int GetHashCode() => (Line * 397) ^ Col;
        public override string ToString() => $"{Line}:{Col}";

        public static bool operator ==(TextPos a, TextPos b) => a.Equals(b);
        public static bool operator !=(TextPos a, TextPos b) => !a.Equals(b);
        public static bool operator <(TextPos a, TextPos b) => a.CompareTo(b) < 0;
        public static bool operator >(TextPos a, TextPos b) => a.CompareTo(b) > 0;
        public static bool operator <=(TextPos a, TextPos b) => a.CompareTo(b) <= 0;
        public static bool operator >=(TextPos a, TextPos b) => a.CompareTo(b) >= 0;

        public static TextPos Min(TextPos a, TextPos b) => a <= b ? a : b;
        public static TextPos Max(TextPos a, TextPos b) => a >= b ? a : b;
    }
}
