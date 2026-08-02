using System;

namespace CanvasRecorder
{
    /// <summary>
    /// <see cref="ScreenRecorder"/> が使うバックエンドの供給元。
    ///
    /// 既定では <see cref="WebGlScreenRecorderBackend"/> を返す。
    /// エディタでは CanvasRecorder.Editor アセンブリが起動時に <see cref="Factory"/> を差し替え、
    /// Unity Recorder を使う実装に切り替わる。
    /// Runtime アセンブリから Editor アセンブリは参照できないため、この向きの依存にしている。
    /// </summary>
    public static class ScreenRecorderBackendProvider
    {
        /// <summary>
        /// バックエンドの生成方法。null なら Web ビルド用の実装が使われる。
        /// </summary>
        public static Func<IScreenRecorderBackend> Factory { get; set; }

        /// <summary>
        /// バックエンドを生成する。
        /// </summary>
        public static IScreenRecorderBackend Create()
        {
            if (Factory != null) return Factory();

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            return new StandaloneScreenRecorderBackend();
#else
            return new WebGlScreenRecorderBackend();
#endif
        }
    }
}
