namespace hacha.LiveCodingEditor
{
    /// <summary>
    /// OS が US 配列として解釈した文字を、同じ物理キーを JIS 配列で打ったときの文字に置き換える。
    /// Quest では物理キーボードの配列設定がアプリの文字入力（onTextInput）に反映されないため、
    /// JIS キーボードを繋ぐとアプリ側で直す必要がある。英字と数字は両配列で同じなので素通し。
    /// </summary>
    public static class JisLayout
    {
        /// <summary>
        /// US 解釈の文字を JIS の文字に変換する。JIS では何も出ないキー（Shift+0）は '\0' を返す。
        /// </summary>
        public static char FromUs(char us)
        {
            switch (us)
            {
                // 数字段の Shift
                case '@': return '"';   // Shift+2
                case '^': return '&';   // Shift+6
                case '&': return '\'';  // Shift+7
                case '*': return '(';   // Shift+8
                case '(': return ')';   // Shift+9
                case ')': return '\0';  // Shift+0（JIS では何も出ない）
                case '_': return '=';   // Shift+-
                // 数字段右端（JIS の ^ キー）
                case '=': return '^';
                case '+': return '~';
                // P の右（JIS の @ と [）
                case '[': return '@';
                case '{': return '`';
                case ']': return '[';
                case '}': return '{';
                // Enter 左（JIS の ] キー。OS はこれを US の \ として送ってくる）
                case '\\': return ']';
                case '|': return '}';
                // L の右（JIS の ; と :）
                case ':': return '+';   // Shift+;
                case '\'': return ':';
                case '"': return '*';   // Shift+:
                default: return us;
            }
        }
    }
}
