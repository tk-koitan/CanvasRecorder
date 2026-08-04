# Changelog

このファイルの書式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に、
バージョン番号は [Semantic Versioning](https://semver.org/lang/ja/) に従います。

## [0.2.0] - 2026-08-02

### Added

- Claude Code 用のスキルを同梱し、利用者プロジェクトの `.claude/skills/canvas-recorder/` へ
  自動配置する仕組みを追加しました。UPM 経由で導入したパッケージは
  `Library/PackageCache/` に展開され gitignore されるため、Claude Code から
  ソースを発見できません。スキルはその在処と読み方だけを伝えます。
  - 初回のみダイアログで同意を求めます。同意状態はプロジェクトごとに分けて保存します
  - パッケージのバージョンが変わったときだけ再配置します
  - `Tools/CanvasRecorder/Setup Claude Code Skill` で手動配置
  - `Tools/CanvasRecorder/Remove Claude Code Skill` で削除と同意の取り消し
  - バッチモードでは何もしません

## [0.1.0] - 2026-08-02

### Added

- ランタイムでの画面録画、プレビュー、保存の基本機能
- Web(WebGL) 向けの実装。ブラウザの `MediaRecorder` と `canvas.captureStream()` を使用
- Windows スタンドアロン向けの実装。Media Foundation を使うネイティブプラグインを同梱
- Windows エディタでの録画。ビルドと同じ Media Foundation 経路を使用
- Unity Recorder を使うエディタ向けバックエンド。`versionDefines` により
  `com.unity.recorder` が導入されている場合のみコンパイルされます
- ダミー映像モード。Unity Recorder を使わずテストパターンを生成します
- `VideoPlayer` によるプレビューと音量調整
- Web Share API による共有と、X の投稿画面を開くヘルパー
- 一連の流れを実装したサンプル
