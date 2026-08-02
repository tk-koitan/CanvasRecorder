namespace CanvasRecorder
{
    /// <summary>
    /// <see cref="ScreenRecorder.ShareRecording"/> の結果。
    /// </summary>
    public enum RecordingShareResult
    {
        /// <summary>共有が完了した。</summary>
        Shared,

        /// <summary>ユーザーが共有をキャンセルした。</summary>
        Cancelled,

        /// <summary>この環境では共有できない。ダウンロードなどに切り替えること。</summary>
        Unsupported,

        /// <summary>共有に失敗した。</summary>
        Failed,
    }
}
