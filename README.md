# CanvasRecorder

Unity のランタイムでゲーム画面を録画し、その場でプレビューして保存できるようにする Unity パッケージです。

Web(WebGL) ビルドではブラウザの MediaRecorder、Windows ではネイティブの Media Foundation を使い、
どちらも**録画中に音を聞きながら、音声入りの MP4 を書き出せます**。

## 特徴

- **録画中も音が聞こえる** — 音声出力を奪わずに分岐させる方式なので、プレイ中の音はそのまま流れます
- **停止と保存が別操作** — 停止した時点では保存されません。プレビューで確認してから保存できます
- **Unity 内でプレビュー** — `VideoPlayer` でデコードし、`Texture` として好きな UI に表示できます
- **エディタで動作確認できる** — ビルドせずに録画からプレビュー、保存までの流れを試せます
- **外部バイナリの同梱なし** — ffmpeg などを持ち込まず、ブラウザと OS の機能だけで完結します

## 対応環境

Unity 6000.0 以降（6000.0.58f2 で確認）

| プラットフォーム | 録画方式 | 音声 | 共有 |
|---|---|---|---|
| Web(WebGL) | `MediaRecorder` + `canvas.captureStream()` | 対応 | Web Share API |
| Windows スタンドアロン | Media Foundation（同梱プラグイン） | 対応 | 非対応 |
| Windows エディタ | Media Foundation（ビルドと同じ経路） | 対応 | 非対応 |
| その他のエディタ | Unity Recorder / ダミー映像 | 制限あり | 非対応 |

Mac / Linux スタンドアロンには未対応です。`IScreenRecorderBackend` を実装すれば追加できます。

## 導入

Unity の Package Manager で **Add package from git URL** を選び、次を入力します。

```
https://github.com/tk-koitan/CanvasRecorder.git?path=/Packages/com.koitan.canvas-recorder
```

バージョンを固定する場合はタグを付けます。

```
https://github.com/tk-koitan/CanvasRecorder.git?path=/Packages/com.koitan.canvas-recorder#v0.1.0
```

> **導入後は Unity を再起動してください。**
> Windows 用のネイティブプラグインを含みます。Unity はネイティブプラグインをエディタ起動時にのみ
> 読み込むため、再起動しないとエディタでの録画が別方式にフォールバックします。

## セットアップ

シーンに **`ScreenRecorder` という名前の空の GameObject** を作り、`ScreenRecorder` コンポーネントだけを
アタッチします。名前は必須です（JS から Unity への通知に `SendMessage` を使うため）。
他のコンポーネントとは同居させないでください。

プレビューを使う場合は、別の GameObject に `RecordingPreview` をアタッチします。

## 使い方

```csharp
using CanvasRecorder;
using UnityEngine;

public class MyRecorderUI : MonoBehaviour
{
    [SerializeField] private ScreenRecorder _screenRecorder;

    public void OnClickStart() => _screenRecorder.StartRecording();

    // 停止しただけでは保存されない
    public void OnClickStop() => _screenRecorder.StopRecording();

    public void OnClickSave() => _screenRecorder.SaveRecording();
}
```

停止は非同期です。保存できる状態になると `RecordingReady` が発火します。

```csharp
private void OnEnable() => _screenRecorder.RecordingReady += OnReady;
private void OnDisable() => _screenRecorder.RecordingReady -= OnReady;

private void OnReady(long sizeBytes) => Debug.Log($"{sizeBytes / 1024f:F1} KB");
```

## サンプル

Package Manager の画面から **Basic Sample** をインポートしてください。
録画の開始と停止、プレビュー、保存、共有までを IMGUI で実装した、結線済みのシーンが入っています。

## Claude Code を使う場合

UPM で導入したパッケージは `Library/PackageCache/` に展開されます。Unity 公式の .gitignore は
`Library/` を除外するため、**Claude Code はこのパッケージのソースを発見できません。**
その状態では API を推測して、存在しないメソッドを書いてしまいます。

これを防ぐため、本パッケージは Claude Code 用のスキルを
`.claude/skills/canvas-recorder/` に配置します。スキルにはソースの在処と読み方だけを書いてあり、
API の詳細は含みません（パッケージ更新時に古い情報が残らないようにするため）。

初回にエディタ上で同意を求めるダイアログが出ます。同意すると配置され、
以降はパッケージのバージョンが変わったときだけ更新されます。
`Assets/` の外に書き込むため、無断では配置しません。

メニューからも操作できます。

| メニュー | 動作 |
|---|---|
| `Tools/CanvasRecorder/Setup Claude Code Skill` | 手動で配置・再配置する |
| `Tools/CanvasRecorder/Remove Claude Code Skill` | 配置したものを削除し、同意を取り消す |

利用者の `.gitignore`、`.claude/settings.json`、`CLAUDE.md` は書き換えません。
`.claude/skills/` 内の他のスキルにも触れません。

## 主な制約

- 録画対象は Unity の描画のみです。キャンバス外の HTML 要素やブラウザ UI は含まれません
- 録画中はエンコードのぶんフレームレートが落ちます
- 停止するまで全データがメモリ上に蓄積されます。数分を超える録画には向きません
- X への動画添付は Web Share API 経由となるため、事実上モバイル限定です（デスクトップの共有シートに X が並ばないため）
- ビルド後のランタイムでは組み込みフォントに日本語グリフがありません

詳細な API リファレンスと制約の説明は
[パッケージ内の README](Packages/com.koitan.canvas-recorder/README.md) を参照してください。

## リポジトリ構成

このリポジトリは Unity プロジェクトそのもので、パッケージは `Packages/` 配下にあります。

```
Packages/com.koitan.canvas-recorder/   パッケージ本体
├── Runtime/                           録画・プレビュー・共有
├── Editor/                            エディタ用バックエンドと設定
├── Plugins/
│   ├── WebGL/                         Recorder.jslib
│   ├── Windows/x86_64/                CanvasRecorderMF.dll
│   └── Source~/                       プラグインの C++ ソース
└── Samples~/BasicSample/              サンプル

Assets/                                 開発・検証用のシーンとツール
Tools/mp4probe.py                       出力した MP4 の構造を検査するスクリプト
```

`Editor/Recorder/` の Unity Recorder バックエンドは `versionDefines` で切り離してあり、
`com.unity.recorder` が入っているプロジェクトでのみコンパイルされます。
**本パッケージは Unity Recorder を依存に含めません。**
