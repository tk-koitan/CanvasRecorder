---
name: canvas-recorder
description: Unity パッケージ Canvas Recorder (com.koitan.canvas-recorder) を使うプロジェクトで、ScreenRecorder / RecordingPreview / IScreenRecorderBackend を扱うコードを書くとき、録画・プレビュー・保存・SNS 共有の実装や不具合調査を行うとき、WebGL の MediaRecorder 経路や Windows の Media Foundation プラグイン経由の録画について調べるときに使う。このパッケージのソースは Library/PackageCache に展開され gitignore されているため通常の検索では見つからない。実体の場所を解決してから読むこと。
---

# Canvas Recorder のソースを読む

## なぜこのスキルが必要か

このパッケージは UPM 経由で導入されると `Library/PackageCache/` に展開される。
`Library/` は Unity 公式の .gitignore で除外されているため、
プロジェクト内を検索してもソースが見つからない。

見つからないまま API を推測して書くと、存在しないメソッドや誤った引数を使ったコードになる。
**必ず実体を読んでから書くこと。**

## ソースの場所を解決する

導入形態が2通りあるので、両方を確認する。

```bash
# UPM 経由（ディレクトリ名にハッシュが付くのでワイルドカードで解決する）
ls -d Library/PackageCache/com.koitan.canvas-recorder@*/

# ローカルに埋め込まれている場合
ls -d Packages/com.koitan.canvas-recorder/
```

どちらか存在した方が実体のルートになる。以降このパスを `<PKG>` と呼ぶ。

見つからない場合はパッケージが導入されていない。
`Packages/manifest.json` に `com.koitan.canvas-recorder` があるか確認する。

## 読むべきファイル

必要な範囲だけ読む。全部を読む必要はない。

| 目的 | 読む場所 |
|---|---|
| 公開 API の把握 | `<PKG>/Runtime/` の `.cs` |
| 導入・制約・注意点 | `<PKG>/README.md` |
| プラットフォーム別の実装差 | `<PKG>/Runtime/` の `IScreenRecorderBackend` 実装 |
| エディタでの挙動 | `<PKG>/Editor/` |
| ブラウザ側の実装 | `<PKG>/Plugins/WebGL/` の `.jslib` |
| Windows ネイティブ側 | `<PKG>/Plugins/Source~/` |
| 変更履歴 | `<PKG>/CHANGELOG.md` |
| 使用例 | `<PKG>/Samples~/` |

公開されているクラスと、そのメンバのシグネチャは
`<PKG>/Runtime/` のファイルを直接読んで確認する。

## 守ること

- **推測で API を書かない。** メソッド名・引数・戻り値は必ず実体を読んで確認する
- このパッケージは**プラットフォームごとに実装が切り替わる**。
  「動くかどうか」を判断する前に、対象プラットフォームのバックエンドを読む
- `Library/PackageCache/` 配下は UPM が管理する領域なので**編集しない**。
  変更が必要ならパッケージのリポジトリ側で行う
- README には実測にもとづく制約が書かれている。
  挙動がおかしいと感じたら、まず README の該当箇所を確認する

## バージョンの確認

```bash
cat <PKG>/package.json
```

`version` が実際に使われている版。会話の途中で更新された可能性があるときは読み直す。
