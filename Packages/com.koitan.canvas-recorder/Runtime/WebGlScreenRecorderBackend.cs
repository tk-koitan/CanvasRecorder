using System.Runtime.InteropServices;
using UnityEngine;

namespace CanvasRecorder
{
    /// <summary>
    /// ブラウザの MediaRecorder API を使う実装。Web ビルドの本番用。
    /// 非同期の完了通知は jslib から SendMessage で <see cref="ScreenRecorder"/> に直接届くため、
    /// このクラスは owner を保持するだけで通知は行わない。
    /// </summary>
    public class WebGlScreenRecorderBackend : IScreenRecorderBackend
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int CanvasRecorder_Start(int fps, int bitsPerSecond, int includeAudio);
        [DllImport("__Internal")] private static extern void CanvasRecorder_Stop();
        [DllImport("__Internal")] private static extern int CanvasRecorder_Save(string fileName);
        [DllImport("__Internal")] private static extern void CanvasRecorder_Discard();
        [DllImport("__Internal")] private static extern int CanvasRecorder_IsRecording();
        [DllImport("__Internal")] private static extern int CanvasRecorder_HasRecording();
        [DllImport("__Internal")] private static extern int CanvasRecorder_RequestPreviewUrl();
        [DllImport("__Internal")] private static extern void CanvasRecorder_ReleasePreviewUrl();
        [DllImport("__Internal")] private static extern int CanvasRecorder_InstallAudioTap();
        [DllImport("__Internal")] private static extern int CanvasRecorder_HasAudio();
        [DllImport("__Internal")] private static extern int CanvasRecorder_CanShare();
        [DllImport("__Internal")] private static extern int CanvasRecorder_Share(string text, string fileName);
        [DllImport("__Internal")] private static extern int CanvasRecorder_IsLikelyMobile();
#else
        // Web ビルド以外ではブラウザ API が無いのでダミー実装にする。
        private static int CanvasRecorder_Start(int fps, int bitsPerSecond, int includeAudio) => 0;
        private static void CanvasRecorder_Stop() { }
        private static int CanvasRecorder_Save(string fileName) => 0;
        private static void CanvasRecorder_Discard() { }
        private static int CanvasRecorder_IsRecording() => 0;
        private static int CanvasRecorder_HasRecording() => 0;
        private static int CanvasRecorder_RequestPreviewUrl() => 0;
        private static void CanvasRecorder_ReleasePreviewUrl() { }
        private static int CanvasRecorder_InstallAudioTap() => 0;
        private static int CanvasRecorder_HasAudio() => 0;
        private static int CanvasRecorder_CanShare() => 0;
        private static int CanvasRecorder_Share(string text, string fileName) => 0;
        private static int CanvasRecorder_IsLikelyMobile() => 0;
#endif

        public bool IsRecording => CanvasRecorder_IsRecording() != 0;

        public bool HasRecording => CanvasRecorder_HasRecording() != 0;

        public bool IsAudioAvailable => CanvasRecorder_HasAudio() != 0;

        public bool CanShare => CanvasRecorder_CanShare() != 0;

        public bool IsLikelyMobile => CanvasRecorder_IsLikelyMobile() != 0;

        public void Initialize(ScreenRecorder owner)
        {
            // 音声フックは早く仕掛けるほど取りこぼしが減る。
            CanvasRecorder_InstallAudioTap();
        }

        public bool StartRecording(int fps, int bitsPerSecond, bool includeAudio)
        {
            // 音声が初期化される前に Initialize が走っていた場合に備えて再試行する。
            if (includeAudio && !IsAudioAvailable) CanvasRecorder_InstallAudioTap();

            if (CanvasRecorder_Start(fps, bitsPerSecond, includeAudio ? 1 : 0) == 0)
            {
                Debug.LogWarning("録画を開始できませんでした。Web ビルドのブラウザ上でのみ動作します。");
                return false;
            }

            return true;
        }

        public void StopRecording() => CanvasRecorder_Stop();

        public bool SaveRecording(string fileName) => CanvasRecorder_Save(fileName ?? string.Empty) != 0;

        public void DiscardRecording() => CanvasRecorder_Discard();

        public bool RequestPreviewUrl() => CanvasRecorder_RequestPreviewUrl() != 0;

        public void ReleasePreviewUrl() => CanvasRecorder_ReleasePreviewUrl();

        public bool ShareRecording(string text, string fileName)
            => CanvasRecorder_Share(text ?? string.Empty, fileName ?? string.Empty) != 0;
    }
}
