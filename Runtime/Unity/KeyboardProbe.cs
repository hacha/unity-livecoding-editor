using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace hacha.LiveCodingEditor
{
    /// <summary>
    /// 実機でハードウェアキーボードの入力がどこまで届くかを目で確かめるための診断表示。
    /// 空の GameObject に付けるだけで、カメラの前にワールドスペースのパネルを出す。
    /// 見るもの: デバイスが認識されるか / onTextInput が来るか・押しっぱなしでリピートするか /
    /// 修飾キー（特に Ctrl+Enter, Ctrl+/, Shift+矢印）が OS に奪われず届くか。
    /// </summary>
    public sealed class KeyboardProbe : MonoBehaviour
    {
        [Tooltip("カメラからの距離(m)。描画順だけ手前にして実際の位置が奥だと立体視が破綻するので、視野を覆う面より手前に置く")]
        [SerializeField] float _distance = 0.8f;
        [SerializeField] Vector2 _size = new Vector2(1000f, 700f);
        [SerializeField] float _unitsToMeters = 0.001f;
        [Tooltip("半透明の描画順。距離より先に効くので、視野を覆う半透明の面（映像用の Quad など）より後に描かせて手前に見せる")]
        [SerializeField] int _sortingOrder = 1000;
        [SerializeField] int _maxLines = 18;
        [SerializeField] bool _alsoLogToConsole = true;
        [Tooltip("頭のトラッキングが始まる（カメラが原点から動く）のを待つ上限。過ぎたらその時点の位置に置く")]
        [SerializeField] float _placementTimeout = 3f;

        readonly Queue<string> _log = new Queue<string>();
        readonly StringBuilder _sb = new StringBuilder();
        readonly List<Key> _held = new List<Key>();
        readonly Dictionary<Keyboard, System.Action<char>> _textSources = new Dictionary<Keyboard, System.Action<char>>();
        RectTransform _canvasRect;
        bool _placed;
        float _startTime;
        TextMeshProUGUI _text;
        Keyboard _bound;
        int _textInputCount;
        bool _dirty = true;

        void Start()
        {
            var canvasGo = new GameObject("KeyboardProbeCanvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = _sortingOrder;
            var rect = (RectTransform)canvasGo.transform;
            rect.sizeDelta = _size;
            rect.localScale = Vector3.one * _unitsToMeters;

            _canvasRect = rect;
            _startTime = Time.unscaledTime;

            var bg = new GameObject("Background", typeof(RectTransform)).AddComponent<Image>();
            bg.transform.SetParent(rect, false);
            bg.color = new Color(0f, 0f, 0f, 0.8f);
            bg.raycastTarget = false;
            Fill(bg.rectTransform, 0f);

            _text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _text.transform.SetParent(rect, false);
            _text.fontSize = 26f;
            _text.richText = false;
            _text.raycastTarget = false;
            _text.alignment = TextAlignmentOptions.TopLeft;
            Fill(_text.rectTransform, 20f);
        }

        /// <summary>
        /// 実機では起動直後は頭のトラッキングが始まっておらず、カメラが原点（床の高さ）にいることがある。
        /// その位置に置くと足元に出て見えないので、カメラが動き出すまで待ってから一度だけ置く。
        /// </summary>
        void TryPlace()
        {
            if (_placed || _canvasRect == null) return;
            var cam = Camera.main;
            bool timedOut = Time.unscaledTime - _startTime > _placementTimeout;
            if (cam == null)
            {
                if (timedOut)
                {
                    _placed = true;
                    Add("placement: Camera.main not found, panel left at world origin");
                }
                return;
            }
            bool tracking = cam.transform.position.sqrMagnitude > 0.0001f;
            if (!tracking && !timedOut) return;

            _placed = true;
            var forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            _canvasRect.position = cam.transform.position + forward * _distance;
            _canvasRect.rotation = Quaternion.LookRotation(forward, Vector3.up);
            Add($"placement: camera={cam.transform.position:F2} panel={_canvasRect.position:F2} waited={Time.unscaledTime - _startTime:F2}s tracking={tracking}");
        }

        static void Fill(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            foreach (var device in InputSystem.devices)
                if (device is Keyboard keyboard) AddTextSource(keyboard);
        }

        void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            foreach (var pair in _textSources) pair.Key.onTextInput -= pair.Value;
            _textSources.Clear();
            _bound = null;
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!(device is Keyboard keyboard)) return;
            Add($"device {change}: {keyboard.displayName}");
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected) AddTextSource(keyboard);
            else if (change == InputDeviceChange.Removed && _textSources.TryGetValue(keyboard, out var handler))
            {
                keyboard.onTextInput -= handler;
                _textSources.Remove(keyboard);
            }
        }

        // 文字入力は全キーボードから受ける（Keyboard.current だけだと、切り替わる瞬間の1文字を落とす）
        void AddTextSource(Keyboard keyboard)
        {
            if (_textSources.ContainsKey(keyboard)) return;
            System.Action<char> handler = c => OnTextInput(keyboard, c);
            _textSources.Add(keyboard, handler);
            keyboard.onTextInput += handler;
        }

        void Bind(Keyboard keyboard)
        {
            if (keyboard == _bound) return;
            _bound = keyboard;
            Add(_bound != null ? $"keyboard current: {_bound.displayName} ({_bound.description.interfaceName})" : "keyboard lost");
        }

        void OnTextInput(Keyboard source, char c)
        {
            _textInputCount++;
            string shown = c < ' ' || c == 127 ? "ctrl" : c.ToString();
            string from = source == _bound ? "current" : source.displayName;
            Add($"text  '{shown}' U+{(int)c:X4}  mods={Mods()}  from={from}  #{_textInputCount}");
        }

        string Mods()
        {
            if (_bound == null) return "-";
            _sb.Clear();
            if (_bound.shiftKey.isPressed) _sb.Append("Shift ");
            if (_bound.ctrlKey.isPressed) _sb.Append("Ctrl ");
            if (_bound.altKey.isPressed) _sb.Append("Alt ");
            if (_bound.leftMetaKey.isPressed || _bound.rightMetaKey.isPressed) _sb.Append("Meta ");
            return _sb.Length == 0 ? "-" : _sb.ToString().TrimEnd();
        }

        void Add(string line)
        {
            if (_alsoLogToConsole) Debug.Log("[KeyboardProbe] " + line);
            _log.Enqueue($"{Time.unscaledTime,7:0.00}  {line}");
            while (_log.Count > _maxLines) _log.Dequeue();
            _dirty = true;
        }

        void Update()
        {
            TryPlace();
            Bind(Keyboard.current);

            if (_bound != null)
            {
                foreach (var control in _bound.allKeys)
                {
                    if (control.wasPressedThisFrame) Add($"down  {control.keyCode}  mods={Mods()}");
                    if (control.wasReleasedThisFrame) Add($"up    {control.keyCode}");
                }

                _held.Clear();
                foreach (var control in _bound.allKeys)
                    if (control.isPressed) _held.Add(control.keyCode);
                _dirty = true;
            }

            if (!_dirty || _text == null) return;
            _dirty = false;

            _sb.Clear();
            _sb.Append("Keyboard.current: ").Append(_bound != null ? _bound.displayName : "NONE").Append('\n');
            _sb.Append("devices: ");
            foreach (var device in InputSystem.devices)
                if (device is Keyboard) _sb.Append(device.displayName).Append("  ");
            _sb.Append('\n');
            _sb.Append("held: ");
            foreach (var key in _held) _sb.Append(key).Append(' ');
            _sb.Append("\n--------\n");
            foreach (var line in _log) _sb.Append(line).Append('\n');
            _text.text = _sb.ToString();
        }
    }
}
