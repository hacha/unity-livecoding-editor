using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace hacha.LiveCodingEditor
{
    /// <summary>
    /// Canvas（ワールドスペース想定）上のコードエディタ。RectTransform に付けるだけで子要素は自分で組み立てる。
    /// TMP は表示専用に使い、編集は CodeDocument、キー入力は Input System から直接受ける
    /// （TMP_InputField は Android でソフトキーボード前提の経路に入るので使わない）。
    /// 文字は mspace で等幅に強制するので、桁×行の格子でキャレットと選択を置ける。
    /// </summary>
    /// <summary>ステータス欄の 2 段目に並べるメーター 1 個。</summary>
    public readonly struct StatusMeter
    {
        public readonly string Label;
        /// <summary>0〜1 でバーが伸びる。1 を超えたらバーは満杯のまま超過色になる。</summary>
        public readonly float Ratio;

        public StatusMeter(string label, float ratio)
        {
            Label = label;
            Ratio = ratio;
        }
    }

    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class CodeEditorView : MonoBehaviour
    {
        [Header("Text")]
        [Tooltip("未指定なら同梱の JetBrains Mono SemiBold（ASCII のみ）。等幅でなくても mspace で等幅に並べる")]
        [SerializeField] TMP_FontAsset _font;
        [SerializeField] float _fontSize = 28f;
        [SerializeField] float _charWidthEm = 0.6f;
        [SerializeField] float _lineHeightEm = 1.35f;
        [SerializeField] Vector2 _padding = new Vector2(16f, 12f);
        [SerializeField] bool _showLineNumbers = true;
        [SerializeField, TextArea(3, 12)] string _initialText = "";

        [Header("Editing")]
        [SerializeField] string _lineCommentPrefix = "//";
        [SerializeField] int _tabSize = 2;
        [SerializeField] bool _hasFocus = true;
        [Tooltip("JIS 配列のキーボードを使うとき ON。Android（Quest）は物理キーボードを US 配列として解釈するので、記号をアプリ側で JIS に直す。他の OS は OS 側で既にレイアウトを適用しているので、この設定は Android 実行時のみ効く")]
        [SerializeField] bool _jisKeyboardLayout;

        [Header("Colors")]
        [SerializeField] Color _background = new Color(0.04f, 0.05f, 0.07f, 0.82f);
        [SerializeField] Color _caretColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] Color _selectionColor = new Color(0.25f, 0.5f, 1f, 0.4f);
        [SerializeField] Color _errorLineColor = new Color(1f, 0.2f, 0.25f, 0.3f);
        [SerializeField] Color _errorTextColor = new Color(1f, 0.45f, 0.45f, 1f);
        [SerializeField] Color _gutterColor = new Color(1f, 1f, 1f, 0.3f);
        [SerializeField] Color _gutterBackground = new Color(1f, 1f, 1f, 0.05f);
        [SerializeField] Color _flashColor = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField] Color _meterTrackColor = new Color(1f, 1f, 1f, 0.06f);
        [SerializeField] Color _meterFillColor = new Color(0.47f, 0.82f, 1.00f, 0.35f);
        [SerializeField] Color _meterWarnColor = new Color(1f, 0.74f, 0.3f, 0.45f);
        [SerializeField] Color _meterOverColor = new Color(1f, 0.25f, 0.3f, 0.6f);
        [Tooltip("この割合からバーを注意色にする")]
        [SerializeField, Range(0f, 1f)] float _meterWarnRatio = 0.8f;
        [Tooltip("TokenStyle の並び順: Default, Comment, Number, Identifier, Function, Keyword, Punctuation, Error")]
        [SerializeField] Color[] _palette =
        {
            new Color(0.88f, 0.90f, 0.93f),
            new Color(0.45f, 0.52f, 0.58f),
            new Color(0.98f, 0.74f, 0.42f),
            new Color(0.88f, 0.90f, 0.93f),
            new Color(0.47f, 0.82f, 1.00f),
            new Color(0.80f, 0.58f, 1.00f),
            new Color(0.62f, 0.68f, 0.75f),
            new Color(1.00f, 0.40f, 0.42f),
        };

        const float CaretBlinkPeriod = 1.0f;
        const float FlashDuration = 0.25f;
        const string NoParse = "<noparse>";
        // 同梱フォント。パススルー映像の上では Regular だと線が細くて負けるので SemiBold にしてある
        const string DefaultFontResource = "LiveCodingEditor/JetBrainsMono-SemiBold SDF";

        readonly EditorController _controller = new EditorController();
        readonly KeyboardInputSource _input = new KeyboardInputSource();
        readonly List<StyleSpan> _spans = new List<StyleSpan>();
        readonly List<Image> _selectionPool = new List<Image>();
        readonly StringBuilder _sb = new StringBuilder();

        ISyntaxHighlighter _highlighter;
        byte[] _styles = new byte[0];
        string _shownText = "";
        int _prefixLength;

        bool _built;
        RectTransform _viewport;
        RectTransform _content;
        RectTransform _scroller;
        RectTransform _back;
        RectTransform _front;
        TextMeshProUGUI _text;
        TextMeshProUGUI _gutter;
        TextMeshProUGUI _status;
        TextMeshProUGUI _statusAside;
        RectTransform _meterRow;
        readonly List<MeterCell> _meterCells = new List<MeterCell>();
        StatusMeter[] _meters = new StatusMeter[0];
        TMP_FontAsset _resolvedFont;
        RectTransform _gutterBack;
        Image _caret;
        Image _errorLine;
        Image _flash;

        int _shownVersion = -1;
        int _shownLineCount = -1;
        TextPos _shownCaret = new TextPos(-1, -1);
        TextPos _shownAnchor = new TextPos(-1, -1);
        float _shownViewportHeight = -1f;
        float _scroll;
        float _scrollX;
        int _shownMaxLineLength = -1;
        float _gutterWidth;
        float _blinkStart;
        float _flashStart = -10f;
        Color _flashNow;

        int _errorLineIndex = -1;
        int _diagnosticVersion = -1;
        int _pendingScrollLine = -1;
        string _message;
        string _statusNote;
        string _statusAsideText;

        float CharWidth => _charWidthEm * _fontSize;
        float LineHeight => _lineHeightEm * _fontSize;
        bool HasMessage => !string.IsNullOrEmpty(_message);
        bool HasMeters => _meters.Length > 0;
        // ステータス欄の文字は本文の 3/4。1 行目は本文と同じ行送りを取り、2 段目はそれを詰めた高さにする
        float MeterRowHeight => LineHeight * 0.75f;

        sealed class MeterCell
        {
            public RectTransform Root;
            public Image Fill;
            public TextMeshProUGUI Label;
        }

        public CodeDocument Document => _controller.Document;
        public EditorController Controller => _controller;

        public bool HasFocus
        {
            get => _hasFocus;
            set
            {
                _hasFocus = value;
                _controller.HasFocus = value;
            }
        }

        public bool ReadOnly
        {
            get => _controller.ReadOnly;
            set => _controller.ReadOnly = value;
        }

        public string Text
        {
            get => Document.Text;
            set => Document.SetText(value);
        }

        public ISyntaxHighlighter Highlighter
        {
            get => _highlighter;
            set
            {
                _highlighter = value;
                _shownVersion = -1;
            }
        }

        // ---- 診断とフィードバック ----

        /// <summary>エラーの表示。line は 0 始まり、負なら行の強調なしでメッセージだけ出す。</summary>
        public void SetDiagnostic(int line, string message)
        {
            _errorLineIndex = line;
            _message = message;
            // この診断がどの版のテキストに対するものか。同じフレームに編集と評価が重なっても、
            // 「テキストが変わったから行の強調を消す」処理でこの診断を消さないために覚えておく
            _diagnosticVersion = Document.Version;
            // 評価に失敗した行が画面外だと帯が見えないので、そこまで送る。キャレットは打っている場所に残す。
            // キャレット追従のスクロールに上書きされないよう、LateUpdate の最後で適用する
            _pendingScrollLine = line;
            RefreshDiagnostic();
        }

        /// <summary>
        /// ステータス欄 1 段目に並べる短い補足（呼び出し側の計測値など）。
        /// エラー表示中も消えないよう、どちらの表示にも付ける（欄は末尾から切れるので、
        /// エラー側では補足を先に出す）。null / 空で消える。
        /// エラー側は &lt;noparse&gt; の内側になるので、ここにリッチテキストは書けない。
        /// </summary>
        public void SetStatusNote(string note)
        {
            if (_statusNote == note) return;
            _statusNote = note;
            if (_built) RefreshStatus();
        }

        /// <summary>
        /// ステータス欄 1 段目の右端に寄せる短い表示（フレームレートなど、常に見えていてほしい値）。
        /// 左側の Ln/Col・補足・エラーとは別の文字列で、左側の方がこの手前で Ellipsis で切れる。
        /// 色は Ln/Col と同じ。null / 空で消え、左側が右端まで戻る。
        /// </summary>
        public void SetStatusAside(string text)
        {
            if (_statusAsideText == text) return;
            _statusAsideText = text;
            if (_built) RefreshStatusAside();
        }

        public void ClearDiagnostic()
        {
            _errorLineIndex = -1;
            _message = null;
            RefreshDiagnostic();
        }

        /// <summary>
        /// ステータス欄の 2 段目（Ln/Col やエラーの下）に、ラベルの背後でバーが伸びるメーターを横に等分して並べる。
        /// 呼び出し側の資源の使用率（描画コストなど）をざっと掴むため。エラー表示とは独立して残る。
        /// 空で 2 段目が消え、本文の領域がその分戻る。
        /// </summary>
        public void SetStatusMeters(params StatusMeter[] meters)
        {
            bool layoutChanged = HasMeters != (meters != null && meters.Length > 0);
            _meters = meters ?? new StatusMeter[0];
            if (!_built) return;
            RefreshMeters();
            if (layoutChanged) LayoutStatus();
        }

        public void ClearStatusMeters() => SetStatusMeters();

        /// <summary>評価が通ったことを一瞬の光で返す。</summary>
        public void Flash() => Flash(_flashColor);

        public void Flash(Color color)
        {
            _flashNow = color;
            _flashStart = Time.unscaledTime;
        }

        // ---- ライフサイクル ----

        void Awake()
        {
            Build();
            Document.LineCommentPrefix = _lineCommentPrefix;
            Document.TabSize = _tabSize;
            if (!string.IsNullOrEmpty(_initialText)) Document.SetText(_initialText);

            _controller.Clipboard = new SystemClipboard();
            _controller.HasFocus = _hasFocus;
            ApplyJisLayout();
            _input.KeyPressed += OnKey;
            _input.TextEntered += OnTextEntered;
        }

        void OnEnable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTmpTextChanged);
            _shownVersion = -1;
        }

        void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTmpTextChanged);
            _input.Dispose();
        }

        void OnDestroy()
        {
            _input.KeyPressed -= OnKey;
            _input.TextEntered -= OnTextEntered;
            _input.Dispose();
        }

        /// <summary>JIS 配列のキーボード向けに記号を直すか。実行中に切り替えてよい。Android 実行時のみ実際に効く（他の OS は OS 側で既に適用済みのため）。</summary>
        public bool JisKeyboardLayout
        {
            get => _jisKeyboardLayout;
            set
            {
                _jisKeyboardLayout = value;
                ApplyJisLayout();
            }
        }

        void OnValidate()
        {
            // Play 中に Inspector で切り替えたときも効かせる
            ApplyJisLayout();
            _controller.HasFocus = _hasFocus;
        }

        /// <summary>
        /// JIS リマップは Android（Quest）が物理キーボードを US 配列として解釈するために必要な処理。
        /// macOS の Editor などでは OS が onTextInput の時点で既に JIS 配列を適用しているので、二重変換を避けるため Android 実行時のみ有効にする。
        /// Editor でも Android ビルドターゲット時は Application.platform が OSXEditor のままなので、実機と違い誤爆しない。
        /// </summary>
        void ApplyJisLayout()
        {
            _input.UseJisLayout = _jisKeyboardLayout && Application.platform == RuntimePlatform.Android;
        }

        void Update()
        {
            _input.Update(Time.unscaledDeltaTime);
        }

        void LateUpdate()
        {
            var doc = Document;
            float viewportHeight = _viewport.rect.height;
            bool textChanged = doc.Version != _shownVersion;
            bool caretChanged = doc.Caret != _shownCaret || doc.Anchor != _shownAnchor;
            bool resized = !Mathf.Approximately(viewportHeight, _shownViewportHeight);

            if (textChanged) RefreshText();
            if (textChanged || caretChanged || resized)
            {
                doc.PageSize = Mathf.Max(1, Mathf.FloorToInt(viewportHeight / LineHeight) - 1);
                if (textChanged || caretChanged) ScrollToCaret(viewportHeight);
                RefreshCaretAndSelection(viewportHeight);
                if (!HasMessage) RefreshStatus();
                _shownCaret = doc.Caret;
                _shownAnchor = doc.Anchor;
                _shownViewportHeight = viewportHeight;
                _blinkStart = Time.unscaledTime;
            }

            if (_pendingScrollLine >= 0)
            {
                if (_pendingScrollLine < doc.LineCount)
                {
                    ScrollToLine(_pendingScrollLine);
                    RefreshCaretAndSelection(viewportHeight);
                    RefreshDiagnostic();
                }
                _pendingScrollLine = -1;
            }

            // キャレットの点滅。操作直後は必ず見えている側から始まる
            float phase = (Time.unscaledTime - _blinkStart) % CaretBlinkPeriod;
            _caret.enabled = _hasFocus && phase < CaretBlinkPeriod * 0.5f;

            float flashT = (Time.unscaledTime - _flashStart) / FlashDuration;
            if (flashT < 1f)
            {
                var c = _flashNow;
                c.a *= 1f - flashT;
                _flash.color = c;
                _flash.enabled = true;
            }
            else if (_flash.enabled)
            {
                _flash.enabled = false;
            }
        }

        // ---- 入力 ----

        void OnKey(EditorKey key, EditorModifiers mods, bool repeat)
        {
            // フォーカスの有無は Controller が見る（フォーカスが無くても動かす割り当てがあるため）。
            // 割り当てた処理（利用側）の例外でエディタごと止まらないようにする
            try
            {
                _controller.HandleKey(key, mods, repeat);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                SetDiagnostic(-1, e.Message);
            }
        }

        void OnTextEntered(char c, EditorModifiers mods)
        {
            _controller.HandleText(c, mods);
        }

        // ---- 表示の更新 ----

        void RefreshText()
        {
            var doc = Document;
            string source = doc.Text;

            _sb.Clear();
            _sb.Append("<mspace=").Append(_charWidthEm.ToString("0.###", CultureInfo.InvariantCulture)).Append("em>");
            _sb.Append("<line-height=").Append(_lineHeightEm.ToString("0.###", CultureInfo.InvariantCulture)).Append("em>");
            string layoutPrefix = _sb.ToString();
            _sb.Append(NoParse);
            _prefixLength = _sb.Length;
            _sb.Append(source);

            ComputeStyles(source);
            _shownText = source;
            _text.text = _sb.ToString();
            _text.ForceMeshUpdate();
            ApplyColors();

            int maxLength = 0;
            for (int l = 0; l < doc.LineCount; l++) maxLength = Mathf.Max(maxLength, doc.GetLine(l).Length);
            if (doc.LineCount != _shownLineCount || maxLength != _shownMaxLineLength)
            {
                // マスクのカリングは矩形で判定されるので、はみ出して描くのではなく矩形を内容に合わせておく
                _shownMaxLineLength = maxLength;
                _text.rectTransform.sizeDelta = new Vector2((maxLength + 2) * CharWidth, (doc.LineCount + 1) * LineHeight);
            }

            if (doc.LineCount != _shownLineCount)
            {
                _shownLineCount = doc.LineCount;
                float height = doc.LineCount * LineHeight;
                _content.sizeDelta = new Vector2(0f, height);

                if (_showLineNumbers)
                {
                    _sb.Clear();
                    _sb.Append(layoutPrefix);
                    for (int l = 1; l <= doc.LineCount; l++)
                    {
                        if (l > 1) _sb.Append('\n');
                        _sb.Append(l);
                    }
                    _gutter.text = _sb.ToString();
                }
            }

            // 診断の後にテキストが動いたら、エラー行の位置は信用できない。メッセージだけ残す
            if (_errorLineIndex >= 0 && doc.Version != _diagnosticVersion)
            {
                _errorLineIndex = -1;
                RefreshDiagnostic();
            }
            _shownVersion = doc.Version;
        }

        void ComputeStyles(string source)
        {
            if (_styles.Length < source.Length) _styles = new byte[Mathf.NextPowerOfTwo(source.Length + 1)];
            Array.Clear(_styles, 0, _styles.Length);
            if (_highlighter == null) return;

            _spans.Clear();
            try
            {
                _highlighter.Highlight(source, _spans);
            }
            catch (Exception e)
            {
                // 色が付かないだけで編集は続けられる
                Debug.LogException(e, this);
                return;
            }

            foreach (var span in _spans)
            {
                int start = Mathf.Max(0, span.Start);
                int end = Mathf.Min(source.Length, span.Start + span.Length);
                for (int i = start; i < end; i++) _styles[i] = (byte)span.Style;
            }
        }

        void OnTmpTextChanged(UnityEngine.Object obj)
        {
            // レイアウトの都合で TMP がメッシュを作り直すと頂点カラーが消えるので塗り直す
            if (obj == _text) ApplyColors();
        }

        void ApplyColors()
        {
            var info = _text.textInfo;
            if (info == null || _palette == null || _palette.Length == 0) return;

            int count = info.characterCount;
            // 空のとき TMP はメッシュを外すだけで、textInfo には前の頂点が残っている。
            // ここで UpdateVertexData を呼ぶとその古い頂点を載せ直してしまい、消したはずの文字が画面に残る
            if (count == 0) return;
            for (int k = 0; k < count; k++)
            {
                var ch = info.characterInfo[k];
                if (!ch.isVisible) continue;

                // 元テキスト上の位置。食い違ったら「k 番目の文字」として扱う（noparse 内は1文字1エントリ）
                int idx = ch.index - _prefixLength;
                if (idx < 0 || idx >= _shownText.Length || _shownText[idx] != ch.character) idx = k;
                if (idx < 0 || idx >= _shownText.Length) continue;

                int style = _styles[idx];
                Color32 color = _palette[style < _palette.Length ? style : 0];
                var colors = info.meshInfo[ch.materialReferenceIndex].colors32;
                int v = ch.vertexIndex;
                if (colors == null || v + 3 >= colors.Length) continue;
                colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = color;
            }
            _text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
        }

        void ScrollToCaret(float viewportHeight)
        {
            float lh = LineHeight;
            float top = Document.Caret.Line * lh;
            float bottom = top + lh;
            if (top < _scroll) _scroll = top;
            if (bottom > _scroll + viewportHeight) _scroll = bottom - viewportHeight;
            float max = Mathf.Max(0f, Document.LineCount * lh - viewportHeight);
            _scroll = Mathf.Clamp(_scroll, 0f, max);

            // 横方向。行番号は動かさず、文字の層だけをずらす。端に張り付かないよう2桁の余白を見る
            float cw = CharWidth;
            float width = Mathf.Max(cw, _viewport.rect.width - _gutterWidth);
            float margin = Mathf.Min(cw * 2f, width * 0.25f);
            float caretX = Document.Caret.Col * cw;
            if (caretX - margin < _scrollX) _scrollX = caretX - margin;
            if (caretX + margin > _scrollX + width) _scrollX = caretX + margin - width;
            _scrollX = Mathf.Max(0f, _scrollX);

            ApplyScroll();
        }

        /// <summary>指定行が見える位置まで縦にスクロールする。キャレットは動かさない。</summary>
        void ScrollToLine(int line)
        {
            float lh = LineHeight;
            float viewportHeight = _viewport.rect.height;
            float top = line * lh;
            if (top < _scroll) _scroll = top;
            if (top + lh > _scroll + viewportHeight) _scroll = top + lh - viewportHeight;
            _scroll = Mathf.Clamp(_scroll, 0f, Mathf.Max(0f, Document.LineCount * lh - viewportHeight));
            ApplyScroll();
        }

        /// <summary>
        /// 縦に lines 行ぶん送る（正で下へ、小数可）。キャレットは動かさないので、打つか動かせばキャレットの位置へ戻る。
        /// コントローラーのスティックなど、利用側の入力で見る範囲だけ動かすためのもの。
        /// </summary>
        public void ScrollLines(float lines)
        {
            if (!_built || lines == 0f) return;
            float lh = LineHeight;
            float viewportHeight = _viewport.rect.height;
            float max = Mathf.Max(0f, Document.LineCount * lh - viewportHeight);
            float next = Mathf.Clamp(_scroll + lines * lh, 0f, max);
            if (Mathf.Approximately(next, _scroll)) return;
            _scroll = next;
            ApplyScroll();
            // 選択の矩形は見えている行だけ作っているので作り直す
            RefreshCaretAndSelection(viewportHeight);
        }

        void ApplyScroll()
        {
            _content.anchoredPosition = new Vector2(0f, _scroll);
            _scroller.anchoredPosition = new Vector2(-_scrollX, 0f);
        }

        /// <summary>行の上端。グリフは行送りより背が低いので、その差の半分だけ上に寄せてセルの中央に見せる。</summary>
        float LineTop(int line)
        {
            var face = _text.font != null ? _text.font.faceInfo : default;
            float glyphHeight = face.pointSize > 0
                ? (face.ascentLine - face.descentLine) * face.scale * _fontSize / face.pointSize
                : _fontSize;
            return line * LineHeight - (LineHeight - glyphHeight) * 0.5f;
        }

        void RefreshCaretAndSelection(float viewportHeight)
        {
            var doc = Document;
            float cw = CharWidth;
            float lh = LineHeight;

            var caretRect = _caret.rectTransform;
            caretRect.anchoredPosition = new Vector2(doc.Caret.Col * cw, -LineTop(doc.Caret.Line));
            caretRect.sizeDelta = new Vector2(Mathf.Max(1.5f, _fontSize * 0.08f), lh);

            int used = 0;
            if (doc.HasSelection)
            {
                var start = doc.SelectionStart;
                var end = doc.SelectionEnd;
                // 見えている行だけ矩形を作る
                int firstVisible = Mathf.Max(start.Line, Mathf.FloorToInt(_scroll / lh) - 1);
                int lastVisible = Mathf.Min(end.Line, Mathf.CeilToInt((_scroll + viewportHeight) / lh) + 1);
                for (int l = firstVisible; l <= lastVisible; l++)
                {
                    int c0 = l == start.Line ? start.Col : 0;
                    // 行末をまたぐ選択は改行ぶん1桁はみ出させて、空行でも選択が見えるようにする
                    int c1 = l == end.Line ? end.Col : doc.GetLine(l).Length + 1;
                    if (c1 <= c0) continue;

                    var rect = GetSelectionRect(used++).rectTransform;
                    rect.anchoredPosition = new Vector2(c0 * cw, -LineTop(l));
                    rect.sizeDelta = new Vector2((c1 - c0) * cw, lh);
                }
            }
            for (int i = 0; i < _selectionPool.Count; i++) _selectionPool[i].enabled = i < used;
        }

        Image GetSelectionRect(int index)
        {
            while (_selectionPool.Count <= index)
                _selectionPool.Add(CreateBox("Selection", _back, _selectionColor));
            return _selectionPool[index];
        }

        void RefreshDiagnostic()
        {
            if (!_built) return;
            bool showLine = _errorLineIndex >= 0 && _errorLineIndex < Document.LineCount;
            _errorLine.enabled = showLine;
            if (showLine)
            {
                var rect = _errorLine.rectTransform;
                rect.anchoredPosition = new Vector2(0f, -LineTop(_errorLineIndex));
                rect.sizeDelta = new Vector2(4096f, LineHeight);
            }
            RefreshStatus();
        }

        void RefreshStatus()
        {
            bool hasNote = !string.IsNullOrEmpty(_statusNote);
            if (HasMessage)
            {
                _status.color = _errorTextColor;
                // 欄は Ellipsis で末尾から切れる。長いエラーで補足が消えないよう、こちらでは先に出す
                _status.text = "<noparse>" + (hasNote ? _statusNote + "   " : "") + _message;
            }
            else
            {
                _status.color = _gutterColor;
                _status.text = $"Ln {Document.Caret.Line + 1}, Col {Document.Caret.Col + 1}"
                    + (hasNote ? "   " + _statusNote : "");
            }
        }

        void RefreshStatusAside()
        {
            bool has = !string.IsNullOrEmpty(_statusAsideText);
            _statusAside.gameObject.SetActive(has);
            _statusAside.text = has ? "<noparse>" + _statusAsideText : "";
            LayoutStatusRow();
        }

        // ---- 階層の組み立て ----

        void Build()
        {
            if (_built) return;
            _built = true;

            var root = (RectTransform)transform;
            var font = _font != null ? _font : Resources.Load<TMP_FontAsset>(DefaultFontResource);
            if (font == null) font = TMP_Settings.defaultFontAsset;
            _resolvedFont = font;
            float gutterWidth = _gutterWidth = _showLineNumbers ? CharWidth * 4f : 0f;

            var background = CreateBox("Background", root, _background);
            Stretch(background.rectTransform, Vector2.zero, Vector2.zero);

            if (_showLineNumbers)
            {
                // 行番号の列の背景。縦スクロールで動かないよう root に置き、右端は行番号と本文の間の空き（1文字分）の中央
                _gutterBack = CreateBox("GutterBackground", root, _gutterBackground).rectTransform;
                _gutterBack.anchorMin = new Vector2(0f, 0f);
                _gutterBack.anchorMax = new Vector2(0f, 1f);
                _gutterBack.offsetMax = new Vector2(_padding.x + gutterWidth - CharWidth * 0.5f, 0f);
            }

            _viewport = CreateRect("Viewport", root);
            Stretch(_viewport, Vector2.zero, Vector2.zero);
            _viewport.gameObject.AddComponent<RectMask2D>();

            _content = CreateRect("Content", _viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0f, 1f);
            _content.sizeDelta = Vector2.zero;
            _content.anchoredPosition = Vector2.zero;

            _gutter = CreateText("LineNumbers", _content, font, _gutterColor, TextAlignmentOptions.TopRight);
            var gutterRect = _gutter.rectTransform;
            gutterRect.anchorMin = new Vector2(0f, 0f);
            gutterRect.anchorMax = new Vector2(0f, 1f);
            gutterRect.pivot = new Vector2(0f, 1f);
            gutterRect.anchoredPosition = Vector2.zero;
            gutterRect.sizeDelta = new Vector2(Mathf.Max(0f, gutterWidth - CharWidth), 0f);
            _gutter.gameObject.SetActive(_showLineNumbers);

            // 行番号の右側だけを切り抜く窓。横スクロールした文字が行番号に重ならないようにする
            var textClip = CreateRect("TextClip", _content);
            textClip.anchorMin = new Vector2(0f, 0f);
            textClip.anchorMax = new Vector2(1f, 1f);
            textClip.pivot = new Vector2(0f, 1f);
            textClip.offsetMin = new Vector2(gutterWidth, 0f);
            textClip.offsetMax = Vector2.zero;
            textClip.gameObject.AddComponent<RectMask2D>();

            // 横スクロールで動かす層。テキストと重ね合わせ（後ろに選択、手前にキャレット）が同じ原点（左上）を共有する
            _scroller = CreateOrigin("Scroller", textClip, 0f);
            _back = CreateOrigin("Back", _scroller, 0f);

            _text = CreateText("Text", _scroller, font, _palette.Length > 0 ? _palette[0] : Color.white, TextAlignmentOptions.TopLeft);
            var textRect = _text.rectTransform;
            textRect.anchorMin = textRect.anchorMax = textRect.pivot = new Vector2(0f, 1f);
            textRect.anchoredPosition = Vector2.zero;

            _front = CreateOrigin("Front", _scroller, 0f);

            _errorLine = CreateBox("ErrorLine", _back, _errorLineColor);
            _errorLine.enabled = false;
            _caret = CreateBox("Caret", _front, _caretColor);

            _status = CreateText("Status", root, font, _gutterColor, TextAlignmentOptions.BottomLeft);
            _status.fontSize = _fontSize * 0.75f;
            _status.overflowMode = TextOverflowModes.Ellipsis;
            _status.rectTransform.anchorMin = new Vector2(0f, 0f);
            _status.rectTransform.anchorMax = new Vector2(1f, 0f);
            _status.rectTransform.pivot = new Vector2(0f, 0f);

            _statusAside = CreateText("StatusAside", root, font, _gutterColor, TextAlignmentOptions.BottomRight);
            _statusAside.fontSize = _fontSize * 0.75f;
            _statusAside.rectTransform.anchorMin = new Vector2(0f, 0f);
            _statusAside.rectTransform.anchorMax = new Vector2(1f, 0f);
            _statusAside.rectTransform.pivot = new Vector2(1f, 0f);

            _meterRow = CreateRect("Meters", root);
            _meterRow.anchorMin = new Vector2(0f, 0f);
            _meterRow.anchorMax = new Vector2(1f, 0f);
            _meterRow.pivot = new Vector2(0f, 0f);

            RefreshMeters();
            RefreshStatusAside();
            LayoutStatus();

            _flash = CreateBox("Flash", root, _flashColor);
            Stretch(_flash.rectTransform, Vector2.zero, Vector2.zero);
            _flash.enabled = false;
        }

        /// <summary>
        /// ステータス欄の高さを 2 段目の有無に合わせ、本文の領域をその上に詰める。
        /// 2 段目は最下段に置き、Ln/Col（とエラー）の行をその分持ち上げる。
        /// </summary>
        void LayoutStatus()
        {
            float lh = LineHeight;
            float detail = HasMeters ? MeterRowHeight : 0f;
            float statusHeight = lh + detail + _padding.y;
            float bottom = _padding.y * 0.5f;

            _meterRow.gameObject.SetActive(HasMeters);
            _meterRow.offsetMin = new Vector2(_padding.x, bottom);
            _meterRow.offsetMax = new Vector2(-_padding.x, bottom + detail);
            LayoutStatusRow();

            if (_gutterBack != null) _gutterBack.offsetMin = new Vector2(0f, statusHeight);
            _viewport.offsetMin = new Vector2(_padding.x, statusHeight);
            _viewport.offsetMax = new Vector2(-_padding.x, -_padding.y);
        }

        /// <summary>
        /// 1 段目の左（Ln/Col・エラー）と右端（<see cref="SetStatusAside"/>）の横幅を割り振る。
        /// 右端は文字の幅だけ取り、左はその手前（1 文字分空ける）までにする。
        /// </summary>
        void LayoutStatusRow()
        {
            float lh = LineHeight;
            float bottom = _padding.y * 0.5f + (HasMeters ? MeterRowHeight : 0f);
            float aside = 0f;
            if (!string.IsNullOrEmpty(_statusAsideText))
                aside = _statusAside.GetPreferredValues(_statusAside.text).x + CharWidth;

            _statusAside.rectTransform.offsetMin = new Vector2(_padding.x, bottom);
            _statusAside.rectTransform.offsetMax = new Vector2(-_padding.x, bottom + lh);
            _status.rectTransform.offsetMin = new Vector2(_padding.x, bottom);
            _status.rectTransform.offsetMax = new Vector2(-_padding.x - aside, bottom + lh);
        }

        /// <summary>メーターの数に合わせてセルを出し入れし、バーの長さと色、ラベルを更新する。</summary>
        void RefreshMeters()
        {
            while (_meterCells.Count < _meters.Length) _meterCells.Add(CreateMeterCell(_meterCells.Count));

            int n = _meters.Length;
            float gap = CharWidth;
            for (int i = 0; i < _meterCells.Count; i++)
            {
                var cell = _meterCells[i];
                bool used = i < n;
                cell.Root.gameObject.SetActive(used);
                if (!used) continue;

                var m = _meters[i];
                cell.Root.anchorMin = new Vector2(i / (float)n, 0f);
                cell.Root.anchorMax = new Vector2((i + 1) / (float)n, 1f);
                // セルの間だけ空ける（左端と右端は本文の余白に揃える）
                cell.Root.offsetMin = new Vector2(i == 0 ? 0f : gap * 0.5f, 0f);
                cell.Root.offsetMax = new Vector2(i == n - 1 ? 0f : -gap * 0.5f, 0f);

                float ratio = float.IsNaN(m.Ratio) ? 0f : m.Ratio;
                cell.Fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
                cell.Fill.color = ratio > 1f ? _meterOverColor
                    : ratio >= _meterWarnRatio ? _meterWarnColor
                    : _meterFillColor;
                cell.Label.text = NoParse + m.Label;
            }
        }

        MeterCell CreateMeterCell(int index)
        {
            var root = CreateRect("Meter" + index, _meterRow);

            var track = CreateBox("Track", root, _meterTrackColor);
            Stretch(track.rectTransform, Vector2.zero, Vector2.zero);

            var fill = CreateBox("Fill", root, _meterFillColor);
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;

            var label = CreateText("Label", root, _resolvedFont, _palette.Length > 0 ? _palette[0] : Color.white, TextAlignmentOptions.MidlineLeft);
            label.fontSize = _fontSize * 0.6f;
            label.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(label.rectTransform, new Vector2(CharWidth * 0.5f, 0f), new Vector2(-CharWidth * 0.5f, 0f));

            return new MeterCell { Root = root, Fill = fill, Label = label };
        }

        static RectTransform CreateRect(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        static RectTransform CreateOrigin(string name, RectTransform parent, float x)
        {
            var rect = CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = Vector2.zero;
            return rect;
        }

        /// <summary>左上原点の単色の箱。位置は anchoredPosition（y は下向きが負）、大きさは sizeDelta で決める。</summary>
        static Image CreateBox(string name, RectTransform parent, Color color)
        {
            var rect = CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        TextMeshProUGUI CreateText(string name, RectTransform parent, TMP_FontAsset font, Color color, TextAlignmentOptions alignment)
        {
            var rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = _fontSize;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = true;
            text.raycastTarget = false;
            text.margin = Vector4.zero;
            text.text = "";
            return text;
        }

        static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }
}
