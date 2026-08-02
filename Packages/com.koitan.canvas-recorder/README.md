# CanvasRecorder

Unity の Web(WebGL) ビルドで、ランタイムにゲーム画面を録画し、プレビューして、ファイルとして保存するためのパッケージです。

実体はブラウザの `canvas.captureStream()` と `MediaRecorder` API で、Unity Recorder パッケージ（エディタ専用）とは無関係です。

## 動作要件

- Unity 6000.0 以降（6000.0.58f2 で確認）
- プレビュー機能を使う場合は `com.unity.modules.video` が必要です

対応プラットフォームと録画方式は次のとおりです。

| プラットフォーム | 録画方式 | 録画中に音が聞こえるか | 共有 |
|---|---|---|---|
| **Web(WebGL)** | ブラウザの `MediaRecorder` + `canvas.captureStream()` | 聞こえる | Web Share API |
| **Windows スタンドアロン** | Media Foundation（同梱のネイティブプラグイン） | 聞こえる | 非対応 |
| **Windows エディタ** | Media Foundation（ビルドと同じ経路） | 聞こえる | 非対応 |
| その他のエディタ | Unity Recorder / ダミー映像 | 制限あり（下記参照） | 非対応 |

Web ビルドは Chrome / Edge で確認しています。
Windows ビルドは Unity 6000.0.58f2 / D3D12 / Mono バックエンドで確認しています。

**Windows エディタでの録画には Unity の再起動が必要です**（ネイティブプラグインの読み込みタイミングのため）。
詳細は「エディタでの録画方式」を参照してください。

Mac / Linux スタンドアロンには未対応です。`IScreenRecorderBackend` を実装すれば追加できます。

## Windows スタンドアロンビルド

Unity にはランタイムで使える動画エンコーダが無いため、Windows の **Media Foundation** を薄くラップした
ネイティブプラグイン（`Plugins/Windows/x86_64/CanvasRecorderMF.dll`）を同梱しています。
OS の機能を使うので外部バイナリの同梱は不要で、追加のライセンス対応もありません。

- **映像**: `ScreenCapture.CaptureScreenshotIntoRenderTexture` + `AsyncGPUReadback`
- **音声**: `AudioListener` への `OnAudioFilterRead` によるタップ。バッファを書き換えないため
  **録画中も再生音はそのまま聞こえます**
- **出力**: H.264 + AAC のプログレッシブ MP4。ブラウザの `MediaRecorder` と違い、
  再生時間とシーク索引が最初から正しく入ります

録画中の一時ファイルは `Application.persistentDataPath/CanvasRecorder/` に作られ、
`SaveRecording()` でユーザーのビデオフォルダへコピーしてエクスプローラーを開きます。

`ShareRecording()` は非対応で `Unsupported` を返します。共有導線が必要な場合は
保存してからファイルをユーザーに扱ってもらう形にしてください。

### 映像の上下が逆になる場合

`ScreenCapture.CaptureScreenshotIntoRenderTexture` の縦向きはグラフィックス API によって変わります。
既定は D3D12 で正しい向きになる設定にしてあります。逆になる環境では次の値を切り替えてください。

```csharp
StandaloneScreenRecorderBackend.FlipCapturedFrames = false;
```

### プラグインを再ビルドする場合

ソースは `Plugins/Source~/CanvasRecorderMF.cpp` にあります（`~` 付きのフォルダなので Unity は無視します）。
Visual Studio の C++ ツールと Windows SDK が必要です。

```bash
cl /nologo /utf-8 /LD /O2 /EHsc /std:c++17 CanvasRecorderMF.cpp /Fe:CanvasRecorderMF.dll
```

**`/utf-8` は必須です。** 付けないと日本語コメントが CP932 として解釈され、構文エラーになります。

DLL を差し替えたら **Unity を再起動してください。** 起動中の Unity は読み込んだ DLL を掴んでおり、
差し替えても反映されません（そもそもファイルを上書きできないことがあります）。

インポート設定は Editor と Standalone Windows x64 の両方を有効にし、CPU を x86_64 にします。
この設定は `.meta` に含めてコミットしてください。Git 経由で導入したパッケージは読み取り専用になり、
利用側でインポート設定を変更できません。

## エディタでの動作確認

エディタの Play モードでは Unity Recorder を使ったバックエンドに自動で切り替わり、
ビルドせずに録画からプレビュー、保存までの流れを確認できます。

| 機能 | エディタ | 備考 |
|---|---|---|
| 録画 / 停止 | 動く | **既定ではダミー映像**。実映像に切り替えも可（下記参照） |
| プレビュー | 動く | `VideoPlayer` がローカルファイルを再生 |
| 音声 | ダミー映像では無し | 実録画に切り替えれば入る。ただし録画中は無音になる |
| 保存 | 動く | `<プロジェクト>/Recordings/` にコピーしてフォルダを開く |
| 共有 | **動かない** | `Unsupported` を返す。Web Share API はブラウザのみ |

使うには `com.unity.recorder`（5.1.2 で確認）が必要です。`Editor/` フォルダを含めずに配布した場合は、
エディタでは何も動かない従来どおりの挙動になります。

**エディタで確認できるのは C# の状態遷移と UI の配線までです。** ブラウザ固有の挙動、
すなわち MP4 コンテナの仕上げ、`MediaRecorder` の対応コーデック、WebAudio からの音声取り込み、
Web Share の可否は再現されません。このパッケージで実際に問題になったのはいずれもこの層なので、
**最終確認は必ず Web ビルドで行ってください。**

### エディタでの録画方式

Windows エディタでは、Windows ビルドと同じ Media Foundation のプラグインを使います。
Unity Recorder は使いません。これにより **録画中も音がスピーカーから聞こえ、
ファイルにも音声が入ります。**

優先順位は次のとおりです。

| 条件 | 使われる方式 |
|---|---|
| Tools > CanvasRecorder > Use Dummy Video が有効 | ダミー映像 |
| Tools > CanvasRecorder > Force Unity Recorder が有効 | Unity Recorder |
| Windows エディタでプラグインが読み込める | **Media Foundation（既定）** |
| 上記以外 | Unity Recorder。無ければダミー映像 |

Windows 以外のエディタでは Media Foundation が使えないため、Unity Recorder かダミー映像になります。

#### 初回は Unity の再起動が必要です

**Unity はネイティブプラグインをエディタ起動時にのみ読み込みます。** そのため次の場合は
再起動するまで Media Foundation 経路が使われず、Unity Recorder かダミー映像にフォールバックします。

- パッケージを導入した直後
- `CanvasRecorderMF.dll` を差し替えた直後
- プラグインの `.meta`（プラットフォーム設定）を変更した直後

Console に `UnityRecorderBackend を使用します` と出ている場合はプラグインが読めていません。
Unity を再起動してください。正しく読めていれば録画中も音がスピーカーから聞こえます。

同じ理由で、**一度読み込まれた DLL は Unity が掴んで離しません。**
プラグイン自体を改修する場合、差し替えのたびに Unity の再起動が必要になります。

### Unity Recorder を使う場合の音声について

Unity Recorder の音声取り込みは `UnityEngine.AudioRenderer` を使っており、**動作中は Unity の音声出力が
キャプチャ側へリダイレクトされ、録画中はスピーカーから聞こえなくなります。**
`AudioRenderer.Start()` に回避オプションは無く、「聞きながら録る」ことはできません。
録画ファイルには正しく音声が入ります。

両立させるには、`OnAudioFilterRead` で音声を自前でタップして WAV に書き出し、
ffmpeg で映像と結合する構成が必要になります（本パッケージには含めていません）。

**この制約はエディタ限定です。** Web ビルドでは `AudioNode.connect` にフックを入れて
`destination` への接続を録音用ノードに**分岐**させており、差し替えではないため
スピーカー出力はそのまま残ります。録画中も音は聞こえ、ファイルにも入ります。

### ダミー映像モード（エディタの既定）

エディタでの録画は**既定でダミー映像**になります。Game View の内容ではなく、
テストパターンの動画（320x180）が生成されます。Unity Recorder を一切使わず、
Editor 標準の `UnityEditor.Media.MediaEncoder` だけで書き出すため軽量です。

生成される動画は背景色が時間とともに変化し、縦帯が左右に動き、下部の進捗バーが伸びます。
プレビューのシーク動作もこれで確認できます。**音声は含まれません。**

録画〜プレビュー〜保存の流れや UI の配線を確認するには、これで十分なはずです。

Game View の実際の映像を録りたい場合は **Tools > CanvasRecorder > Use Dummy Video**
を無効にしてください。Unity Recorder による実録画に切り替わります。ただし次の制約があります。

- 録画中は音声がスピーカーから聞こえなくなります（`AudioRenderer` の仕様）
- Game View のキャプチャとエンコードのぶん重くなります

なお Unity Recorder を使う実録画で**音声を無効にすると、数秒後に極端に重くなる事象**を
確認しています。原因は未特定です。そのため実録画では音声を常に有効にしています。
ダミー映像モードはこの経路を通らないため影響を受けません。

### バックエンドの差し替え

録画の実処理は `IScreenRecorderBackend` に切り出されています。
既定では Web ビルド用の実装が使われ、エディタでは `CanvasRecorder.Editor` アセンブリが
起動時に `ScreenRecorderBackendProvider.Factory` を差し替えます。
独自のバックエンド（テスト用のフェイクなど）を使いたい場合も、この `Factory` を差し替えてください。

## 導入

Package Manager の **Add package from git URL** に次を入力します。

```
https://github.com/<user>/<repo>.git?path=/Packages/com.koitan.canvas-recorder
```

バージョンを固定する場合はタグを付けます。

```
https://github.com/<user>/<repo>.git?path=/Packages/com.koitan.canvas-recorder#v0.1.0
```

Git URL からのインストールには、利用側の PATH に git が通っている必要があります。

> **導入後は Unity を再起動してください。**
> 本パッケージには Windows 用のネイティブプラグインが含まれます。Unity はネイティブプラグインを
> **エディタ起動時にのみ読み込む**ため、インポート直後のエディタでは認識されません。
> 再起動しないと、Windows エディタでの録画が Media Foundation ではなく
> Unity Recorder かダミー映像にフォールバックします。

### 構成

| フォルダ | 内容 | アセンブリ |
|---|---|---|
| `Runtime/` | 本体。`ScreenRecorder`、`RecordingPreview`、バックエンド | `CanvasRecorder` |
| `Plugins/WebGL/` | `Recorder.jslib`（ブラウザ API の呼び出し） | なし（プラグイン） |
| `Plugins/Windows/` | `CanvasRecorderMF.dll`（Media Foundation ラッパー） | なし（プラグイン） |
| `Editor/` | エディタ用バックエンドと設定メニュー | `CanvasRecorder.Editor` |
| `Editor/Recorder/` | Unity Recorder バックエンド。Recorder がある場合のみコンパイル | `CanvasRecorder.Editor.Recorder` |
| `Samples~/` | サンプル。Package Manager からインポート | `CanvasRecorder.Samples` |

### 名前空間とアセンブリ定義

すべての型は `CanvasRecorder` 名前空間にあります。

```csharp
using CanvasRecorder;
```

ランタイムは `CanvasRecorder.asmdef`（アセンブリ名 `CanvasRecorder`）にまとまっています。
`autoReferenced` が有効なので、アセンブリ定義を使っていないコード（`Assembly-CSharp`）からは
`using` を書くだけで参照できます。

自分のコードをアセンブリ定義に分けている場合は、その `.asmdef` の Assembly Definition References に
`CanvasRecorder` を追加してください。

サンプルは `Samples~/` にあり、Package Manager の画面からインポートします。
別アセンブリ（`CanvasRecorder.Samples`）なので、インポートしなくても本体に影響しません。

Unity Recorder バックエンドは `versionDefines` で切り離してあり、`com.unity.recorder` が
入っているプロジェクトでのみコンパイルされます。**本パッケージは Recorder を依存に含めません。**

## セットアップ

シーンに **`ScreenRecorder` という名前の空の GameObject** を作り、`ScreenRecorder` コンポーネントだけをアタッチしてください。

GameObject の名前は必須です。jslib から Unity への通知に `SendMessage` を使っており、その宛先がこの名前になっているためです。

また **`ScreenRecorder` は他のコンポーネントと同居させないでください**。`SendMessage` は同じ GameObject の全コンポーネントに配送されるため、受け取れない側で `MissingMethodException` が発生します。

プレビューを使う場合は、**別の** GameObject に `RecordingPreview` をアタッチしてください。

## 使い方

### 最小構成

```csharp
using CanvasRecorder;
using UnityEngine;

public class MyRecorderUI : MonoBehaviour
{
    [SerializeField] private ScreenRecorder _screenRecorder;

    public void OnClickStart() => _screenRecorder.StartRecording();

    public void OnClickStop() => _screenRecorder.StopRecording();

    // 停止しただけでは保存されない。保存は明示的に呼ぶ。
    public void OnClickSave() => _screenRecorder.SaveRecording();
}
```

停止は非同期です。保存できる状態になると `RecordingReady` が発火します。

```csharp
private void OnEnable() => _screenRecorder.RecordingReady += OnReady;
private void OnDisable() => _screenRecorder.RecordingReady -= OnReady;

private void OnReady(long sizeBytes)
{
    Debug.Log($"{sizeBytes / 1024f:F1} KB の録画を保存できます");
}
```

### プレビュー

`RecordingPreview.Open()` を呼ぶと、保持中の録画を `VideoPlayer` でデコードします。準備が完了すると `Prepared` が発火し、`Texture` プロパティから映像を取得できます。

```csharp
[SerializeField] private RecordingPreview _preview;
[SerializeField] private RawImage _rawImage;

private void OnEnable() => _preview.Prepared += OnPrepared;
private void OnDisable() => _preview.Prepared -= OnPrepared;

public void OnClickPreview() => _preview.Open();

private void OnPrepared() => _rawImage.texture = _preview.Texture;

// 閉じるときは必ず Close を呼ぶ。内部で blob URL を解放している。
public void OnClickClose() => _preview.Close();
```

### X などへの共有

`ShareRecording()` は Web Share API で録画ファイルを OS の共有シートに渡します。
モバイルなら共有先に X アプリが並び、動画が添付された状態で投稿画面に入れます。

**X の Web Intent は仕様上、動画や画像を添付できません。** 添付まで行うには Web Share API か、
サーバを立てて X API を使うかのどちらかが必要です。本パッケージは前者を提供しています。

#### デスクトップでは X が共有先に出ません（実測）

`navigator.share({files})` はデスクトップでも動作しますが、Windows では **OS の共有シート**に処理が渡り、
そこに並ぶのは共有ターゲットとして登録された Windows アプリだけです。X は通常登録されていないため、
共有シートは開くものの X が選べません。

したがって**動画を添付した投稿は事実上モバイル限定**です。

| 環境 | 動画添付 |
|---|---|
| Android Chrome / iOS Safari（X アプリあり） | できる。共有シートから X を選ぶと動画付きで投稿画面に入る |
| デスクトップ | できない。共有シートに X が並ばない |

デスクトップ向けには、動画を保存させてから X の投稿画面を開き、ユーザーに手動で添付してもらう形に
フォールバックしてください。クリップボード経由は動画が非対応のため代替になりません。
ワンクリックでの動画付き投稿をデスクトップでも実現するには、サーバを立てて X API を使う必要があります。

判定には `CanShareToApps`（`CanShare` かつ `IsLikelyMobile`）を使えます。
`IsLikelyMobile` は UA ベースの推測なので確実ではありません。両方の導線をユーザーに見せておくのが安全です。

`CanShare` が `false` になる環境（`allow="web-share"` の無い cross-origin iframe など）でも同じく
フォールバックしてください。

```csharp
// モバイルなら共有シート、デスクトップなら保存 + 投稿画面。
// 判定を外した場合に行き止まりにならないよう、両方の導線を出しておくのが安全。
public void OnClickShare()
{
    if (!_screenRecorder.ShareRecording("スコア更新しました！")) SaveAndOpenX();
}

public void OnClickSaveAndOpenX() => SaveAndOpenX();

private void SaveAndOpenX()
{
    // 保存させてから投稿画面を開く（添付はユーザーが手動で行う）
    _screenRecorder.SaveRecording();
    XPost.OpenPostIntent("スコア更新しました！", "https://unityroom.com/games/xxxx");
}

private void OnEnable() => _screenRecorder.ShareCompleted += OnShareCompleted;
private void OnDisable() => _screenRecorder.ShareCompleted -= OnShareCompleted;

private void OnShareCompleted(RecordingShareResult result)
{
    // Shared / Cancelled / Unsupported / Failed
}
```

`ShareRecording()` も `SaveRecording()` と同様に、ユーザー操作のハンドラから直接呼んでください。

## API

### ScreenRecorder

| メンバー | 説明 |
|---|---|
| `bool StartRecording(int fps = 30, int bitsPerSecond = 8_000_000, bool includeAudio = true)` | 録画を開始する。保持中の前回結果は破棄される |
| `void StopRecording()` | 録画を停止する。**保存は行わない** |
| `bool SaveRecording(string fileName = null)` | 保持中の結果をダウンロードさせる。省略時は日時ベースのファイル名 |
| `void DiscardRecording()` | 保持中の結果を保存せずに破棄する |
| `bool RequestPreviewUrl()` | 再生用の blob URL を要求する。通常は `RecordingPreview` 経由で使う |
| `void ReleasePreviewUrl()` | 再生用 URL を解放する |
| `bool IsRecording` | 録画中かどうか |
| `bool HasRecording` | 保存できる結果を保持しているかどうか |
| `bool IsAudioAvailable` | 音声を録音できる状態かどうか |
| `bool ShareRecording(string text = null, string fileName = null)` | 録画ファイルを OS の共有シートに渡す（Web Share API） |
| `bool CanShare` | ファイル共有に対応しているかどうか |
| `bool IsLikelyMobile` | モバイル環境らしいかどうか（UA ベースの推測） |
| `bool CanShareToApps` | 動画添付での SNS 共有が見込めるか（`CanShare` かつ `IsLikelyMobile`） |
| `event Action<RecordingShareResult> ShareCompleted` | 共有の結果を通知（`Shared` / `Cancelled` / `Unsupported` / `Failed`） |

### XPost

| メンバー | 説明 |
|---|---|
| `static void OpenPostIntent(string text, string url, params string[] hashtags)` | X の投稿画面を本文入りで開く。**動画は添付されない** |
| `event Action<long> RecordingReady` | 停止が完了し保存可能になったときにバイト数を通知 |
| `event Action<string> PreviewUrlReady` | 再生用 URL が用意できたときに通知 |

### RecordingPreview

| メンバー | 説明 |
|---|---|
| `bool Open()` | プレビューを開く。準備完了は非同期 |
| `void Close()` | プレビューを閉じ、URL を解放する |
| `void TogglePlay()` | 再生と一時停止を切り替える |
| `void Seek(double seconds)` | 再生位置を秒で指定する |
| `Texture Texture` | デコードされた映像。準備完了前は `null` |
| `bool IsOpen` / `bool IsPrepared` / `bool IsPlaying` | 状態 |
| `double Length` / `double Time` | 動画の長さと現在位置（秒） |
| `event Action Prepared` | 再生準備が完了したときに発火 |
| `event Action<string> Failed` | 再生に失敗したときにメッセージ付きで発火 |

## サンプル

`Samples/CanvasRecorderSample.unity` を開いて Web ビルドしてください。
録画開始 → 停止 → プレビュー → 保存 / 破棄 の一連の流れを IMGUI で実装してあります。

ローカルで確認する場合は、ビルド結果を静的サーバで配信してください。`file://` では動作しません。

```bash
python -m http.server 8000 --directory <ビルド出力先>
```

## 制約と注意点

### 録画対象は Unity のキャンバスのみ

キャンバス外の HTML 要素は録画されません。ブラウザ UI ごと録りたい場合は `getDisplayMedia` を使う別の実装が必要です。

### 音声について

Unity が再生している音声を録音できます。`StartRecording` の `includeAudio` は既定で `true` です。

Unity 6 の WebGL 実装は、各サウンドチャンネルの gain ノードを個別に `audioContext.destination` へ
直結しており、まとめて取得できるマスターノードがありません。そのため本パッケージは
`AudioNode.prototype.connect` にフックを仕掛け、`destination` へ接続されるノードを
録音用の `MediaStreamAudioDestinationNode` にも分岐させています。

この方式には次の制約があります。

- **フックを仕掛けるより前から鳴り続けている音は録音されません。** フックは `ScreenRecorder.Awake`
  で仕掛けているため、シーン開始時から途切れずループしている BGM などが該当する可能性があります。
  Unity は再生のたびに接続をやり直すので、鳴り直された時点で拾われるようになります。
- ブラウザの `AudioContext` はユーザー操作があるまで suspended になります。録画開始を
  クリック起点にしていれば問題になりません。
- マイク入力は含みません。録音されるのは Unity が出力している音だけです。

音声を含めたくない場合は明示的に無効化してください。

```csharp
_screenRecorder.StartRecording(includeAudio: false);
```

音声を取得できる状態かどうかは `IsAudioAvailable` で確認できます。
`includeAudio: true` でも音声トラックを取得できなかった場合は、警告を出して映像のみで録画を続行します。

### 解像度はキャンバスのバッキングストアサイズ

CSS 上の表示サイズではなく、`devicePixelRatio` を掛けた実解像度で録画されます。

### 録画中はフレームレートが落ちます

エンコードで CPU を消費します。`StartRecording` の `fps` を下げると軽くなります。

### 長時間録画のメモリ

停止するまで全データがブラウザのメモリ上に蓄積されます。数分を超える録画を想定する場合は、File System Access API へ逐次書き出す実装への変更を検討してください。

### 保存されたファイルの再生時間

`MediaRecorder` の MP4 出力はフラグメント MP4 です。録画が複数フラグメントに分かれると `mvhd.duration` が 0 になり、シーク索引（`mfra`）も付かないため、再生時間が表示されずシークできないファイルになることがあります。

`MediaRecorder.start()` に timeslice を渡さないことで発生頻度は下がりますが、完全には保証できません。なお Discord や X に投稿すると再エンコードされて再生時間が正しく付きます。

確実にシーク可能なファイルが必要な場合は、`MediaRecorder` ではなく WebCodecs (`VideoEncoder`) と JS の muxer で自前に多重化する実装が必要です。

### 日本語フォント

ビルド後のランタイムでは組み込みフォントに日本語グリフが無く、`GUI.Label` などの日本語が表示されません。日本語を出す場合は TextMeshPro などで日本語フォントアセットを用意してください。

### iframe 内での配布

iframe に埋め込まれるサイト（unityroom など）では、`sandbox` 属性や user activation の条件によりダウンロードがブロックされることがあります。
`SaveRecording()` はユーザーのクリックハンドラから直接呼ぶようにしてください。停止と保存を分けてあるのはこのためです。

### モバイル

iOS Safari の `MediaRecorder` と `canvas.captureStream()` の組み合わせは不安定です。`StartRecording()` が `false` を返した場合に録画 UI を隠すなどのフォールバックを用意してください。

### preserveDrawingBuffer は不要

`canvas.captureStream()` は `preserveDrawingBuffer` の有無に影響されません（Chromium で実測）。カスタム WebGL テンプレートは不要です。
`preserveDrawingBuffer` が必要になるのは `drawImage` や `toDataURL` による同期リードバックの場合です。

## 発展させる場合の候補

- WebCodecs ベースの実装への差し替え（シーク可能なファイルを保証したい場合）
- 音声の取り込み（Unity の WebAudio マスターノードを `MediaStreamAudioDestinationNode` に接続する）
