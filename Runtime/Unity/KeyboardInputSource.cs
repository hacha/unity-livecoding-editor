using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace hacha.LiveCodingEditor
{
    /// <summary>
    /// Input System のキーボードを EditorKey と文字入力に変換する。
    /// 文字は OS が確定させたもの（onTextInput）を使い、レイアウトや Shift の解釈は OS に任せる。
    /// 矢印や Backspace などのキーリピートは Input System が出さないので、ここで自前で刻む。
    /// </summary>
    public sealed class KeyboardInputSource : IDisposable
    {
        static readonly (Key key, EditorKey editorKey)[] Map = BuildMap();

        static (Key, EditorKey)[] BuildMap()
        {
            var map = new List<(Key, EditorKey)>
            {
                (Key.LeftArrow, EditorKey.Left),
                (Key.RightArrow, EditorKey.Right),
                (Key.UpArrow, EditorKey.Up),
                (Key.DownArrow, EditorKey.Down),
                (Key.Home, EditorKey.Home),
                (Key.End, EditorKey.End),
                (Key.PageUp, EditorKey.PageUp),
                (Key.PageDown, EditorKey.PageDown),
                (Key.Backspace, EditorKey.Backspace),
                (Key.Delete, EditorKey.Delete),
                (Key.Enter, EditorKey.Enter),
                (Key.NumpadEnter, EditorKey.Enter),
                (Key.Tab, EditorKey.Tab),
                (Key.Slash, EditorKey.Slash),
                (Key.Escape, EditorKey.Escape),
                (Key.Space, EditorKey.Space),
            };
            // 英字・数字・F キーは Input System の Key と EditorKey で名前が揃っている
            for (char c = 'A'; c <= 'Z'; c++) Add(map, c.ToString());
            for (int i = 0; i <= 9; i++) Add(map, "Digit" + i);
            for (int i = 1; i <= 12; i++) Add(map, "F" + i);
            return map.ToArray();
        }

        static void Add(List<(Key, EditorKey)> map, string name)
        {
            map.Add(((Key)Enum.Parse(typeof(Key), name), (EditorKey)Enum.Parse(typeof(EditorKey), name)));
        }

        /// <summary>
        /// JIS 配列のキーボードを繋いでいるとき true。OS が US 配列として送ってくる記号を JIS に直す。
        /// 呼び出し側（CodeEditorView）が Android 実行時だけ true を渡す想定。他の OS は onTextInput の時点で
        /// OS 側が既に JIS 配列を適用しているので、ここで true にすると二重変換になる。
        /// </summary>
        public bool UseJisLayout;

        public float RepeatDelay = 0.4f;
        // Quest 3 の OS が文字に掛けるリピート（初回 約0.4秒、以後 約0.05秒）に合わせてある
        public float RepeatInterval = 0.05f;

        /// <summary>キーの押下。3 つ目の引数はキーリピートで来た 2 回目以降なら true（繰り返すかどうかは受け手が決める）。</summary>
        public event Action<EditorKey, EditorModifiers, bool> KeyPressed;
        public event Action<char, EditorModifiers> TextEntered;

        readonly List<Keyboard> _textSources = new List<Keyboard>();
        bool _listening;
        Keyboard _bound;
        Key _repeatKey = Key.None;
        EditorKey _repeatEditorKey;
        float _repeatTimer;

        public EditorModifiers Modifiers
        {
            get
            {
                var kb = _bound;
                if (kb == null) return EditorModifiers.None;
                var mods = EditorModifiers.None;
                if (kb.shiftKey.isPressed) mods |= EditorModifiers.Shift;
                if (kb.ctrlKey.isPressed || kb.leftMetaKey.isPressed || kb.rightMetaKey.isPressed) mods |= EditorModifiers.Ctrl;
                if (kb.altKey.isPressed) mods |= EditorModifiers.Alt;
                return mods;
            }
        }

        /// <summary>毎フレーム呼ぶ。Bluetooth キーボードは後から繋がるので、接続先もここで追いかける。</summary>
        public void Update(float deltaTime)
        {
            StartListening();
            Bind(Keyboard.current);
            var kb = _bound;
            if (kb == null) return;

            var mods = Modifiers;
            foreach (var (key, editorKey) in Map)
            {
                if (!kb[key].wasPressedThisFrame) continue;
                KeyPressed?.Invoke(editorKey, mods, false);
                _repeatKey = key;
                _repeatEditorKey = editorKey;
                _repeatTimer = RepeatDelay;
            }

            if (_repeatKey == Key.None) return;
            if (!kb[_repeatKey].isPressed)
            {
                _repeatKey = Key.None;
                return;
            }

            _repeatTimer -= deltaTime;
            while (_repeatTimer <= 0f)
            {
                KeyPressed?.Invoke(_repeatEditorKey, mods, true);
                _repeatTimer += Math.Max(0.005f, RepeatInterval);
            }
        }

        /// <summary>キーの押下と修飾キーは「現在のキーボード」から読む。</summary>
        void Bind(Keyboard keyboard)
        {
            if (keyboard == _bound) return;
            _bound = keyboard;
            _repeatKey = Key.None;
        }

        /// <summary>
        /// 文字入力は接続中のすべてのキーボードから受ける。Quest では起動直後の Keyboard.current が
        /// On-Screen Keyboard で、物理キーボードへ切り替わるのは最初の打鍵を処理した後なので、
        /// current だけを聞いていると最初の1文字を取りこぼす。
        /// </summary>
        void StartListening()
        {
            if (_listening) return;
            _listening = true;
            InputSystem.onDeviceChange += OnDeviceChange;
            foreach (var device in InputSystem.devices)
                if (device is Keyboard keyboard) AddTextSource(keyboard);
        }

        void StopListening()
        {
            if (!_listening) return;
            _listening = false;
            InputSystem.onDeviceChange -= OnDeviceChange;
            foreach (var keyboard in _textSources) keyboard.onTextInput -= OnTextInput;
            _textSources.Clear();
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!(device is Keyboard keyboard)) return;
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected) AddTextSource(keyboard);
            else if (change == InputDeviceChange.Removed) RemoveTextSource(keyboard);
        }

        void AddTextSource(Keyboard keyboard)
        {
            if (_textSources.Contains(keyboard)) return;
            _textSources.Add(keyboard);
            keyboard.onTextInput += OnTextInput;
        }

        void RemoveTextSource(Keyboard keyboard)
        {
            if (!_textSources.Remove(keyboard)) return;
            keyboard.onTextInput -= OnTextInput;
        }

        void OnTextInput(char c)
        {
            if (UseJisLayout) c = JisLayout.FromUs(c);
            if (c == '\0') return;
            TextEntered?.Invoke(c, Modifiers);
        }

        public void Dispose()
        {
            StopListening();
            Bind(null);
        }
    }
}
