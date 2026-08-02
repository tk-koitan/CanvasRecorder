namespace CanvasRecorder
{
    /// <summary>
    /// 録画の実処理を担う実装。
    /// Web ビルドではブラウザの MediaRecorder、エディタでは Unity Recorder が使われる。
    /// </summary>
    public interface IScreenRecorderBackend
    {
        /// <summary>録画中かどうか。</summary>
        bool IsRecording { get; }

        /// <summary>保存できる録画結果を保持しているかどうか。</summary>
        bool HasRecording { get; }

        /// <summary>音声を録音できる状態かどうか。</summary>
        bool IsAudioAvailable { get; }

        /// <summary>録画結果をファイルとして共有できるかどうか。</summary>
        bool CanShare { get; }

        /// <summary>モバイル環境らしいかどうか。</summary>
        bool IsLikelyMobile { get; }

        /// <summary>
        /// 非同期の完了通知を返すために、呼び出し元の <see cref="ScreenRecorder"/> を受け取る。
        /// </summary>
        void Initialize(ScreenRecorder owner);

        /// <summary>録画を開始する。</summary>
        bool StartRecording(int fps, int bitsPerSecond, bool includeAudio);

        /// <summary>録画を停止する。ファイルの保存は行わない。</summary>
        void StopRecording();

        /// <summary>保持している録画結果をファイルとして保存する。</summary>
        bool SaveRecording(string fileName);

        /// <summary>保持している録画結果を破棄する。</summary>
        void DiscardRecording();

        /// <summary>再生用の URL を要求する。取得できたら owner に通知される。</summary>
        bool RequestPreviewUrl();

        /// <summary>再生用の URL を解放する。</summary>
        void ReleasePreviewUrl();

        /// <summary>録画結果を共有する。結果は owner に通知される。</summary>
        bool ShareRecording(string text, string fileName);
    }
}
