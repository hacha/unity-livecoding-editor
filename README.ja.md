# LiveCoding Editor

[![openupm](https://img.shields.io/npm/v/com.hacha.livecoding-editor?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.hacha.livecoding-editor/)

シーン内（ワールドスペース Canvas）に置く小さなコードエディタ。ソフトウェアキーボードや OS のテキスト入力欄を通さず、ハードウェアキーボードを直接読む。複数行選択・行コメント・Undo/Redo など、コードを書くときに欲しい操作を備える。ゲーム内コンソールや、VR/XR 内でのライブコーディングなどに使える。

言語には依存せず、シンタックスハイライトと診断（エラー表示）は利用側が注入する。

English: [README.md](README.md)

## インストール

### OpenUPM（推奨）

[openupm-cli](https://github.com/openupm/openupm-cli) で:

```sh
openupm add com.hacha.livecoding-editor
```

または `Packages/manifest.json` に scoped registry を手で追加する。

```json
{
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": ["com.hacha.livecoding-editor"]
    }
  ],
  "dependencies": {
    "com.hacha.livecoding-editor": "0.1.0"
  }
}
```

### Git URL

Package Manager の「Add package from git URL」、または `Packages/manifest.json` に次を追加する。

```json
"com.hacha.livecoding-editor": "https://github.com/hacha/unity-livecoding-editor.git#v0.1.0"
```

### 必要なもの

Unity 6000.0 以降、Input System、uGUI（uGUI 2.0 経由の TextMeshPro）。このパッケージの EditMode テストを実行するには、利用側の `manifest.json` に `"testables": ["com.hacha.livecoding-editor"]` を追加する。

## キー操作

`EditorController.HandleKey` / `HandleText`（`Runtime/Core/EditorController.cs`）でのキー→操作の割り当て（組み込み分）。パッケージに「評価（submit）」は無く、Ctrl+Enter などは利用側が割り当てる。「ReadOnly時」列は `CodeEditorView.ReadOnly = true`（`EditorController.ReadOnly`）のときも動くかどうか。

### 改行

| キー | 動作 | 備考 |
|---|---|---|
| Enter / Shift+Enter | 改行を挿入（現在行のインデントを引き継ぐ） | ReadOnly時は無効。リピートする。Ctrl+Enter は割り当てが無ければ何もしない（改行は入らない）。評価などは下の「利用側で割り当てるキー」で割り当てる |

### カーソル移動・選択

| キー | 動作 | 備考 |
|---|---|---|
| ←→↑↓ | 1文字／1行移動 | Shift で選択を拡張。←→は Ctrl または Alt で単語単位移動。選択中に Shift なしで←→を押すと選択の端へキャレットが畳まれる。ReadOnly時も有効。リピートする |
| Home | 行頭へ（1回目はインデント直後、そこにいれば次で桁0） | Ctrl+Home は文書の先頭へ。Shift で選択拡張。ReadOnly時も有効。リピートする |
| End | 行末へ | Ctrl+End は文書の末尾へ。Shift で選択拡張。ReadOnly時も有効。リピートする |
| PageUp / PageDown | 表示行数ぶん上下移動 | Shift で選択拡張。ReadOnly時も有効。リピートする |
| Ctrl+A | 全選択 | ReadOnly時も有効。リピートしない |

### 編集

| キー | 動作 | 備考 |
|---|---|---|
| Backspace | 1文字削除（選択があれば選択を削除） | Ctrl/Alt で単語単位。インデント内では次のタブ位置までまとめて戻る。ReadOnly時は無効。リピートする |
| Delete | カーソル右の1文字を削除（選択があれば選択を削除） | Ctrl/Alt で単語単位。ReadOnly時は無効。リピートする |
| Tab | インデント。選択なし／単一行選択なら次のタブ位置まで空白を挿入、複数行選択なら各行の先頭に空白（TabSize分）を追加 | ReadOnly時は無効。リピートする |
| Shift+Tab | アウトデント。対象行の先頭の空白を TabSize 個まで削る | ReadOnly時は無効。リピートする |
| Ctrl+/ | 行コメントの切り替え（既定のプレフィックスは `//`、`CodeEditorView` で変更可） | 選択行（無ければキャレット行）が対象。空行以外が全てコメント済みなら外し、そうでなければ全行を同じ桁で `// ` 付与。ReadOnly時は無効。リピートしない |

### クリップボード・Undo

| キー | 動作 | 備考 |
|---|---|---|
| Ctrl+C | 選択をコピー | 選択が無い、またはクリップボード未設定なら何もしない。ReadOnly時も有効。リピートしない |
| Ctrl+X | 選択をカット | ReadOnly時は無効。リピートしない |
| Ctrl+V | クリップボードの内容を貼り付け | ReadOnly時は無効。リピートしない |
| Ctrl+Z | Undo | 連続したタイピングや同位置での Backspace は1つの undo にまとめられる。それ以外の編集操作は都度1ステップ。ReadOnly時は無効。押しっぱなしでリピートする |
| Ctrl+Shift+Z / Ctrl+Y | Redo | ReadOnly時は無効。押しっぱなしでリピートする |

## 利用側で割り当てるキー

組み込みの操作とは別に、利用側が任意のキーに処理を割り当てられる（`EditorController.Bind`）。ビューからは `CodeEditorView.Controller.Bind(...)`。

| API | 動作 | 備考 |
|---|---|---|
| `Bind(EditorKey key, EditorModifiers modifiers, Action action, bool repeat = false, bool whenUnfocused = false)` | キー＋修飾キーに処理を割り当てる | 修飾キーは完全一致（Ctrl+Enter の割り当ては Ctrl+Shift+Enter では動かない。両方欲しければ両方割り当てる）。同じ組を再度 Bind すると置き換え |
| `Unbind(key, modifiers)` | 割り当てを外す | |
| `IsBound(key, modifiers)` | 割り当て済みか | |

- 割り当ては組み込みの操作より先に判定するので、組み込みのキー（Ctrl+/ など）も上書きできる。
- `ReadOnly` 時も動く。
- `repeat`: 押しっぱなしのリピートでも呼ぶか（既定は呼ばない）。
- `whenUnfocused`: `HasFocus` が false の間も呼ぶか。エディタを隠していても効かせたいキー用。
- `EditorController.HasFocus`（`CodeEditorView.HasFocus` で設定）が false の間は文字入力と組み込みの操作を受けず、`whenUnfocused` 付きの割り当てだけが動く。
- 割り当てた処理が例外を投げても `CodeEditorView` が捕まえる（`Debug.LogException` と診断行に出す）ので、入力は止まらない。
- 割り当て専用に使えるキーとして `EditorKey` に A〜Z 全て・Digit0〜Digit9・F1〜F12・Escape・Space がある（組み込みの操作は無い）。

```csharp
var c = view.Controller;
c.Bind(EditorKey.Enter, EditorModifiers.Ctrl, Evaluate);
c.Bind(EditorKey.Enter, EditorModifiers.Ctrl | EditorModifiers.Shift, Evaluate);
c.Bind(EditorKey.H, EditorModifiers.Ctrl | EditorModifiers.Shift, ToggleHud, whenUnfocused: true);
```

例えばライブコーディング用途なら、Ctrl+Enter / Ctrl+Shift+Enter で評価、Ctrl+Shift+H（`whenUnfocused`）でエディタの表示切替、のように割り当てる。

## ステータス欄

| API | 出る場所 |
|---|---|
| `SetDiagnostic(line, message)` / `ClearDiagnostic()` | 1 段目。エラー行の強調とメッセージ（`Ln/Col` の代わりに出る） |
| `SetStatusNote(string)` | 1 段目。呼び出し側の短い計測値など。`Ln/Col` の後ろに続けて出し、エラー表示中はエラーの前に出す（欄は末尾から Ellipsis で切れるので、長いエラーで消えないようにするため）。エラー側は `<noparse>` の内側になるのでリッチテキストは書けない |
| `SetStatusAside(string)` | 1 段目の右端。常に見えていてほしい短い値。左側（`Ln/Col`・補足・エラー）とは別の文字列で、左側がこの手前で Ellipsis で切れる。色は `Ln/Col` と同じ。null / 空で消える |
| `SetStatusMeters(...)` / `ClearStatusMeters()` | 2 段目。ラベルの背後でバーが伸びるメーター。空なら 2 段目ごと消えて本文の領域が戻る |

いずれも中身が何かはパッケージ側では決めない（計測値・テンポ・フレームレートなどを出す想定）。

## 修飾キー

- Mac の Cmd（左右どちらも）は Ctrl と同じ扱い（`KeyboardInputSource.Modifiers`）。
- Alt は Ctrl と同様に「単語単位」の修飾として働く（←→の単語移動、Backspace/Delete の単語削除）。
- Ctrl（Cmd 含む）を押しながらの文字入力は `HandleText` で捨てる（キー側の処理と二重にならないように）。
- Meta Quest では Cmd と Option（Alt）は使えない（[プラットフォームごとの注意](#プラットフォームごとの注意-meta-quest)）。

## キーリピート

`KeyboardInputSource` が自前でリピートを刻む（Input System はキーリピートを出さないため）。初回まで 0.4秒、以降 0.05秒間隔。押しているキーは全てリピートとして通知し（`KeyPressed(EditorKey, EditorModifiers, bool repeat)`）、リピートするかどうかは `EditorController` が決める（組み込みは `IsRepeatable`、割り当ては `repeat` 引数）。

- リピートする: 矢印、Home/End/PageUp/PageDown、Backspace、Delete、Tab、Enter（Ctrl 押下時を除く）
- リピートしない: Ctrl+A/C/V/X、Ctrl+/
- Ctrl+Z / Ctrl+Y のみ Ctrl 押下時に限りリピートする
- 利用側の割り当て（`Bind`）は `repeat: true` を付けたものだけリピートする（既定はしない。評価の誤爆連打を防げる）

## 文字入力 / JIS配列

文字そのものは OS が確定させたもの（`onTextInput`）をそのまま使う。物理キーボードを常に US配列として解釈するプラットフォーム（Meta Quest など）で JIS配列キーボードを使う場合は、アプリ側で記号を JIS に変換する（`CodeEditorView` の `JisKeyboardLayout` / `KeyboardInputSource.UseJisLayout`）。変換が効くのは Android 実行時のみ。

## プラットフォームごとの注意: Meta Quest

- **Quest 3 では Cmd と Option（Alt）が使えない。** どちらも押した瞬間に OS に拾われ、アプリがフォーカスを失ってキーボードごと `Disabled` になる（Apple Magic Keyboard + `KeyboardProbe` で確認）。修飾キーは Ctrl（Magic Keyboard の control）だけを使う。単語単位の移動・削除も Ctrl で行う。
- Bluetooth の物理キーボードは常に US配列として解釈される。JIS配列キーボードでは `JisKeyboardLayout` を有効にする（上記）。
- キーリピートの間隔（初回まで 0.4秒、以降 0.05秒）は Quest 3 の OS が文字入力に掛けるリピートに合わせた値。

## ライセンス

コードは MIT ライセンス（[LICENSE.md](LICENSE.md)）。同梱の JetBrains Mono フォントと、そこから生成した TMP SDF アセットは SIL OFL 1.1（[Runtime/Fonts/JetBrainsMono-OFL.txt](Runtime/Fonts/JetBrainsMono-OFL.txt)）。
