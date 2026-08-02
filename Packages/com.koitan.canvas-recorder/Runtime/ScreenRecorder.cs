using System;
using UnityEngine;

namespace CanvasRecorder
{
    /// <summary>
    /// ランタイムでキャンバス映像を録画し、プレビューやファイル保存を行う。
    ///
    /// 実処理は <see cref="IScreenRecorderBackend"/> に委譲している。
    /// Web ビルドではブラウザの MediaRecorder API、エディタの Play モードでは
    /// Unity Recorder が使われるため、ビルドしなくても一連の流れを確認できる。
    /// ただしブラウザ固有の挙動（コンテナの仕上げ、共有シート、音声の取り込み経路）は
    /// エディタでは再現されないため、最終確認は必ず Web ビルドで行うこと。
    ///
    /// 録画の停止（<see cref="StopRecording"/>）とファイルの保存（<see cref="SaveRecording"/>）は
    /// 分かれている。停止した時点で結果は保持されるだけで、
    /// <see cref="SaveRecording"/> を呼ぶまで保存は発生しない。
    ///
    /// JS から SendMessage を受け取るため、このコンポーネントが載る GameObject の名前は
    /// "ScreenRecorder" である必要がある。
    /// また SendMessage は同じ GameObject の全コンポーネントに配送されるので、
    /// このコンポーネントは他のコンポーネントと同居させず専用の GameObject に載せること。
    /// （同居させると受け取れない側で MissingMethodException が出る）
    /// </summary>
    public class ScreenRecorder : MonoBehaviour
    {
        /// <summary>この GameObject の名前。jslib 側の SendMessage の宛先と一致させる。</summary>
        public const string GameObjectName = "ScreenRecorder";

        private IScreenRecorderBackend _backend;

        /// <summary>
        /// 録画が停止し、保存できる状態になったときにバイト数付きで発火する。
        /// この時点ではまだファイルは保存されていない。
        /// </summary>
        public event Action<long> RecordingReady;

        /// <summary>
        /// <see cref="RequestPreviewUrl"/> で再生用 URL が用意できたときに発火する。
        /// </summary>
        public event Action<string> PreviewUrlReady;

        /// <summary>
        /// <see cref="ShareRecording"/> の結果を通知する。
        /// </summary>
        public event Action<RecordingShareResult> ShareCompleted;

        /// <summary>実処理を担うバックエンド。</summary>
        public IScreenRecorderBackend Backend => _backend;

        /// <summary>録画中かどうか。</summary>
        public bool IsRecording => _backend.IsRecording;

        /// <summary>保存できる録画結果を保持しているかどうか。</summary>
        public bool HasRecording => _backend.HasRecording;

        /// <summary>音声を録音できる状態かどうか。</summary>
        public bool IsAudioAvailable => _backend.IsAudioAvailable;

        /// <summary>
        /// 録画結果をファイルとして共有できるかどうか（Web Share API の対応状況）。
        /// cross-origin の iframe 内では allow="web-share" が無いと false になる。
        /// </summary>
        public bool CanShare => _backend.CanShare;

        /// <summary>
        /// モバイル環境らしいかどうか。
        ///
        /// Web Share API はデスクトップでも動作するが、デスクトップの共有シートには
        /// X などの SNS アプリが並ばないため、動画を添付した投稿は事実上モバイル限定になる。
        /// UA ベースの推測なので確実ではない。
        /// </summary>
        public bool IsLikelyMobile => _backend.IsLikelyMobile;

        /// <summary>
        /// 動画を添付した状態で SNS へ共有できる見込みがあるかどうか。
        /// </summary>
        public bool CanShareToApps => CanShare && IsLikelyMobile;

        private void Awake()
        {
            if (name != GameObjectName)
            {
                Debug.LogWarning($"{nameof(ScreenRecorder)} の GameObject 名は \"{GameObjectName}\" である必要があります。" +
                                 "現在の名前では録画完了の通知を受け取れません。");
            }

            _backend = ScreenRecorderBackendProvider.Create();
            _backend.Initialize(this);
        }

        /// <summary>
        /// 録画を開始する。保持している前回の録画結果は破棄される。
        /// </summary>
        /// <param name="fps">キャプチャするフレームレート。</param>
        /// <param name="bitsPerSecond">映像ビットレート。</param>
        /// <param name="includeAudio">
        /// 再生中の音声も録音するかどうか。
        /// 音声を取得できない場合は警告を出して映像のみで録画を続行する。
        /// </param>
        /// <returns>開始できたら true。</returns>
        public bool StartRecording(int fps = 30, int bitsPerSecond = 8_000_000, bool includeAudio = true)
            => _backend.StartRecording(fps, bitsPerSecond, includeAudio);

        /// <summary>
        /// 録画を停止する。結果は保持されるだけで、ファイルの保存は行わない。
        /// 停止処理は非同期なので、保存できるようになると <see cref="RecordingReady"/> が発火する。
        /// </summary>
        public void StopRecording() => _backend.StopRecording();

        /// <summary>
        /// 保持している録画結果をファイルとして保存する。
        /// </summary>
        /// <param name="fileName">
        /// 保存するファイル名。null または空なら日時ベースの名前を自動生成する。
        /// </param>
        /// <returns>保存を開始できたら true。保持している録画が無ければ false。</returns>
        public bool SaveRecording(string fileName = null)
        {
            if (!_backend.SaveRecording(fileName))
            {
                Debug.LogWarning("保存できる録画結果がありません。");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 保持している録画結果を保存せずに破棄する。プレビュー用 URL も解放される。
        /// </summary>
        public void DiscardRecording() => _backend.DiscardRecording();

        /// <summary>
        /// 保持している録画結果の再生用 URL を要求する。
        /// 取得できると <see cref="PreviewUrlReady"/> が発火する。
        /// 使い終わったら <see cref="ReleasePreviewUrl"/> を呼ぶこと。
        /// </summary>
        /// <returns>要求できたら true。保持している録画が無ければ false。</returns>
        public bool RequestPreviewUrl()
        {
            if (!_backend.RequestPreviewUrl())
            {
                Debug.LogWarning("プレビューできる録画結果がありません。");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 再生用 URL を解放する。
        /// </summary>
        public void ReleasePreviewUrl() => _backend.ReleasePreviewUrl();

        /// <summary>
        /// 保持している録画結果を OS の共有機能に渡す（Web Share API）。
        /// モバイルでは共有先に X アプリなどが並ぶ。
        /// ユーザー操作のハンドラから直接呼ぶこと。そうしないとブラウザに拒否される。
        /// 結果は <see cref="ShareCompleted"/> で通知される。
        /// </summary>
        /// <param name="text">共有時に添える本文。共有先によっては無視される。</param>
        /// <param name="fileName">共有するファイル名。null または空なら自動生成する。</param>
        /// <returns>共有を開始できたら true。</returns>
        public bool ShareRecording(string text = null, string fileName = null)
            => _backend.ShareRecording(text, fileName);

        /// <summary>
        /// バックエンドから呼び出される。直接呼ばないこと。
        /// Web ビルドでは jslib から SendMessage 経由で届く。
        /// </summary>
        /// <param name="sizeBytes">録画結果のバイト数。SendMessage の制約で float で渡ってくる。</param>
        public void OnRecordingReady(float sizeBytes)
        {
            var bytes = (long)sizeBytes;
            Debug.Log($"録画を停止しました（保存可能）: {bytes} bytes");
            RecordingReady?.Invoke(bytes);
        }

        /// <summary>
        /// バックエンドから呼び出される。直接呼ばないこと。
        /// </summary>
        /// <param name="url">録画結果の再生用 URL。</param>
        public void OnPreviewUrlReady(string url)
        {
            Debug.Log($"プレビュー用 URL を取得しました: {url}");
            PreviewUrlReady?.Invoke(url);
        }

        /// <summary>
        /// バックエンドから呼び出される。直接呼ばないこと。
        /// </summary>
        public void OnShareResult(string result)
        {
            var parsed = result switch
            {
                "shared" => RecordingShareResult.Shared,
                "cancelled" => RecordingShareResult.Cancelled,
                "unsupported" => RecordingShareResult.Unsupported,
                _ => RecordingShareResult.Failed,
            };

            ShareCompleted?.Invoke(parsed);
        }
    }
}
