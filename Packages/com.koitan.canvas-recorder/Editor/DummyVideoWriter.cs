using System.Collections;
using Unity.Collections;
using UnityEditor.Media;
using UnityEngine;

namespace CanvasRecorder.Editor
{
    /// <summary>
    /// テストパターンの MP4 を生成する。
    ///
    /// Unity Recorder を使わず <c>UnityEditor.Media.MediaEncoder</c> だけで書き出すため、
    /// Recorder パッケージにも Game View の描画にも依存しない。
    /// 録画〜プレビュー〜保存の流れを軽量に確認するためのもの。
    ///
    /// 実装上の注意:
    /// Texture2D を渡すオーバーロードは1フレームごとに GPU へのアップロードが発生する。
    /// それをメインスレッドの同期ループで数百回連続実行すると GPU のウォッチドッグに
    /// 引っかかり、D3D12 のデバイスロストでエディタごと落ちる。
    /// そのため CPU 側のバッファを渡すオーバーロードを使い、さらに一定間隔で
    /// フレームを跨いでメインスレッドを解放する。
    /// </summary>
    internal static class DummyVideoWriter
    {
        // ダミーなので解像度は低くてよい。生成コストは面積に比例する。
        private const int Width = 320;

        /// <summary>一度に処理するフレーム数。これを超えたら次の Unity フレームまで待つ。</summary>
        private const int FramesPerYield = 10;

        /// <summary>生成するフレーム数の上限。長時間の録画でも生成が暴走しないようにする。</summary>
        private const int MaxFrameCount = 900;

        /// <summary>
        /// 指定した長さのダミー動画を書き出す。コルーチンとして実行すること。
        /// </summary>
        /// <param name="path">出力先。拡張子まで含めた絶対パス。</param>
        /// <param name="fps">フレームレート。</param>
        /// <param name="durationSeconds">動画の長さ（秒）。</param>
        /// <param name="aspect">縦横比（幅 / 高さ）。0 以下なら 16:9 とする。</param>
        public static IEnumerator Write(string path, int fps, float durationSeconds, float aspect)
        {
            if (aspect <= 0f) aspect = 16f / 9f;

            // H.264 は偶数の寸法を要求する。
            var height = Mathf.Max(2, Mathf.RoundToInt(Width / aspect)) & ~1;
            var frameCount = Mathf.Clamp(Mathf.RoundToInt(durationSeconds * fps), 1, MaxFrameCount);

            var attributes = new VideoTrackAttributes
            {
                frameRate = new MediaRational(fps),
                width = (uint)Width,
                height = (uint)height,
                includeAlpha = false,
            };

            // AddFrame が受け取るのは NativeArray<byte>。RGBA32 なので 1 ピクセル 4 バイト。
            // エディタでは NativeArray の要素アクセスに安全性チェックが入り、
            // 1 ピクセルずつ書くと非常に遅い。マネージド配列に組み立ててから一括コピーする。
            var buffer = new byte[Width * height * 4];
            var pixels = new NativeArray<byte>(buffer.Length, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);

            try
            {
                using (var encoder = new MediaEncoder(path, attributes))
                {
                    for (var frame = 0; frame < frameCount; frame++)
                    {
                        FillFrame(buffer, Width, height, frame, frameCount);
                        pixels.CopyFrom(buffer);
                        encoder.AddFrame(Width, height, Width * 4, TextureFormat.RGBA32, pixels);

                        // メインスレッドを詰まらせないよう定期的に手を離す。
                        if (frame % FramesPerYield == FramesPerYield - 1) yield return null;
                    }
                }
            }
            finally
            {
                pixels.Dispose();
            }

            Debug.Log($"ダミー動画を生成しました: {Width}x{height} {frameCount}フレーム @ {fps}fps → {path}");
        }

        /// <summary>
        /// 背景色が徐々に変わり、縦帯が左右に動き、下部の進捗バーが伸びる絵を作る。
        /// プレビューでのシーク確認ができるよう、時間経過が一目で分かる構成にしている。
        /// </summary>
        private static void FillFrame(byte[] pixels, int width, int height, int frame, int frameCount)
        {
            var progress = frameCount > 1 ? (float)frame / (frameCount - 1) : 0f;

            var background = (Color32)Color.HSVToRGB(Mathf.Repeat(progress * 2f, 1f), 0.55f, 0.65f);
            var bar = new Color32(255, 220, 60, 255);
            var track = new Color32(30, 30, 34, 255);

            var barWidth = Mathf.Max(8, width / 12);
            var barX = Mathf.RoundToInt(Mathf.PingPong(frame * 6f, width - barWidth));
            var progressHeight = Mathf.Max(4, height / 14);
            var progressWidth = Mathf.RoundToInt(width * progress);

            for (var y = 0; y < height; y++)
            {
                var isProgressRow = y < progressHeight;
                var rowOffset = y * width * 4;

                for (var x = 0; x < width; x++)
                {
                    Color32 color;
                    if (isProgressRow) color = x < progressWidth ? bar : track;
                    else if (x >= barX && x < barX + barWidth) color = bar;
                    else color = background;

                    var index = rowOffset + x * 4;
                    pixels[index] = color.r;
                    pixels[index + 1] = color.g;
                    pixels[index + 2] = color.b;
                    pixels[index + 3] = color.a;
                }
            }
        }
    }
}
