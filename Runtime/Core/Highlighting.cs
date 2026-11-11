using System.Collections.Generic;

namespace hacha.LiveCodingEditor
{
    /// <summary>色分けの種類。実際の色は表示側のパレットが決める。</summary>
    public enum TokenStyle
    {
        Default,
        Comment,
        Number,
        Identifier,
        Function,
        Keyword,
        Punctuation,
        Error,
    }

    /// <summary>文書全体の文字インデックスで表した色分け区間。</summary>
    public struct StyleSpan
    {
        public int Start;
        public int Length;
        public TokenStyle Style;

        public StyleSpan(int start, int length, TokenStyle style)
        {
            Start = start;
            Length = length;
            Style = style;
        }
    }

    /// <summary>
    /// 言語ごとの色分け。エディタは言語を知らないので、利用側が実装して注入する。
    /// 編集途中の壊れたテキストが渡るのが普通なので、例外を投げずに分かる範囲だけ返すこと。
    /// </summary>
    public interface ISyntaxHighlighter
    {
        /// <param name="text">文書全体（改行は \n）</param>
        /// <param name="spans">結果の追加先。呼び出し側がクリア済み。区間に入らない文字は Default。</param>
        void Highlight(string text, List<StyleSpan> spans);
    }

    public interface IClipboard
    {
        string Text { get; set; }
    }
}
