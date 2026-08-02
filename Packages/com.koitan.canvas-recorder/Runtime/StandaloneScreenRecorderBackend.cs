using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CanvasRecorder
{
    /// <summary>
    /// Windows スタンドアロンビルド用の実装。
    ///
    /// Unity にはランタイムで使える動画エンコーダが無いため、Windows の Media Foundation を
    /// 薄くラップしたネイティブプラグイン（CanvasRecorderMF.dll）へ映像と音声を流し込む。
    /// 出力はフラグメントではない通常の MP4 なので、再生時間もシークも最初から正しく入る。
    ///
    /// 映像は <see cref="StandaloneCaptureDriver"/> が AsyncGPUReadback で取得し、
    /// 音声は <see cref="StandaloneAudioCapture"/> が OnAudioFilterRead で取得する。
    /// 音声のタップはバッファを書き換えないため、録画中も再生音はそのまま聞こえる。
    /// </summary>
    public class StandaloneScreenRecorderBackend : IScreenRecorderBackend
    {
        private const string PluginName = "CanvasRecorderMF";
        private const string OutputDirectoryName = "CanvasRecorder";

        /// <summary>
        /// キャプチャしたフレームを上下反転して書き込むかどうか。
        /// グラフィックス API によって <c>ScreenCapture.CaptureScreenshotIntoRenderTexture</c> の
        /// 縦向きが変わるため、録画結果が上下逆になる場合はここを切り替える。
        /// </summary>
        public static bool FlipCapturedFrames = true;

        // Windows のスタンドアロンビルドに加え、Windows エディタでも使う。
        // エディタは Windows プロセスなので同じプラグインがそのまま動作し、
        // Unity Recorder と違って録画中も音がスピーカーから聞こえる。
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport(PluginName, CharSet = CharSet.Unicode)]
        private static extern int CanvasRecorderMF_Open(string path, int width, int height, int fps,
            int videoBitrate, int audioChannels, int audioSampleRate, int flipVertically);

        [DllImport(PluginName)]
        private static extern int CanvasRecorderMF_WriteVideoFrame(byte[] data, int size, long timeHns);

        [DllImport(PluginName)]
        private static extern int CanvasRecorderMF_WriteAudio(float[] samples, int count);

        [DllImport(PluginName)]
        private static extern int CanvasRecorderMF_Close();

        [DllImport(PluginName)]
        private static extern int CanvasRecorderMF_IsOpen();
#else
        // Windows 以外ではプラグインが存在しないのでダミー実装にする。
        private static int CanvasRecorderMF_Open(string path, int width, int height, int fps,
            int videoBitrate, int audioChannels, int audioSampleRate, int flipVertically) => -1;

        private static int CanvasRecorderMF_WriteVideoFrame(byte[] data, int size, long timeHns) => -1;
        private static int CanvasRecorderMF_WriteAudio(float[] samples, int count) => -1;
        private static int CanvasRecorderMF_Close() => -1;
        private static int CanvasRecorderMF_IsOpen() => 0;
#endif

        private static bool? _pluginAvailable;

        /// <summary>
        /// ネイティブプラグインを読み込めるかどうか。
        /// 呼べるかを一度だけ実際に試して結果を保持する。
        /// </summary>
        public static bool IsPluginAvailable
        {
            get
            {
                if (_pluginAvailable.HasValue) return _pluginAvailable.Value;

                try
                {
                    CanvasRecorderMF_IsOpen();
                    _pluginAvailable = true;
                }
                catch (DllNotFoundException)
                {
                    _pluginAvailable = false;
                }
                catch (EntryPointNotFoundException)
                {
                    _pluginAvailable = false;
                }

                return _pluginAvailable.Value;
            }
        }

        private ScreenRecorder _owner;
        private StandaloneCaptureDriver _driver;
        private string _outputFilePath;
        private bool _hasRecording;

        public bool IsRecording => _driver != null && _driver.IsCapturing;

        public bool HasRecording => _hasRecording;

        public bool IsAudioAvailable => true;

        /// <summary>スタンドアロンには Web Share API が無い。</summary>
        public bool CanShare => false;

        public bool IsLikelyMobile => false;

        public void Initialize(ScreenRecorder owner)
        {
            _owner = owner;

            // 録画対象と同じ GameObject に載せると SendMessage の配送先が増えるため、専用の GameObject を作る。
            var driverObject = new GameObject("CanvasRecorderCaptureDriver");
            UnityEngine.Object.DontDestroyOnLoad(driverObject);
            _driver = driverObject.AddComponent<StandaloneCaptureDriver>();
            _driver.Initialize(WriteVideoFrame, WriteAudio, Abort);
        }

        /// <summary>
        /// 録画中にドライバが破棄された場合の後始末。
        /// エディタでは Play モードを抜けてもプラグイン側の状態が残るため、確実に閉じる。
        /// </summary>
        private void Abort()
        {
            if (CanvasRecorderMF_IsOpen() == 0) return;

            CanvasRecorderMF_Close();
            Debug.LogWarning("録画中に中断されたため、エンコーダを閉じました。");
        }

        public bool StartRecording(int fps, int bitsPerSecond, bool includeAudio)
        {
            if (IsRecording)
            {
                Debug.LogWarning("すでに録画中です。");
                return false;
            }

            DiscardRecording();

            var directory = Path.Combine(Application.persistentDataPath, OutputDirectoryName);
            Directory.CreateDirectory(directory);
            _outputFilePath = Path.Combine(directory, $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.mp4");

            // H.264 は偶数の寸法を要求する。
            var width = Mathf.Max(2, Screen.width) & ~1;
            var height = Mathf.Max(2, Screen.height) & ~1;

            var channels = includeAudio ? StandaloneAudioCapture.ResolveChannelCount() : 0;
            var sampleRate = includeAudio ? AudioSettings.outputSampleRate : 0;

            var result = CanvasRecorderMF_Open(_outputFilePath, width, height, fps, bitsPerSecond,
                channels, sampleRate, FlipCapturedFrames ? 1 : 0);

            if (result != 0)
            {
                Debug.LogWarning($"録画を開始できませんでした。プラグインの初期化に失敗しました（0x{result:X8}）。");
                _outputFilePath = null;
                return false;
            }

            _driver.StartCapture(width, height, fps, includeAudio);
            return true;
        }

        public void StopRecording()
        {
            if (!IsRecording) return;

            _driver.StopCapture(OnCaptureFinished);
        }

        private void OnCaptureFinished()
        {
            var result = CanvasRecorderMF_Close();
            if (result != 0)
            {
                Debug.LogWarning($"録画の終了処理に失敗しました（0x{result:X8}）。");
                return;
            }

            if (!File.Exists(_outputFilePath))
            {
                Debug.LogWarning($"録画ファイルが生成されませんでした: {_outputFilePath}");
                return;
            }

            _hasRecording = true;
            _owner.OnRecordingReady(new FileInfo(_outputFilePath).Length);
        }

        private void WriteVideoFrame(byte[] data, int size, long timeHns)
        {
            var result = CanvasRecorderMF_WriteVideoFrame(data, size, timeHns);
            if (result != 0) Debug.LogWarning($"映像フレームの書き込みに失敗しました（0x{result:X8}）。");
        }

        private void WriteAudio(float[] samples, int count)
        {
            CanvasRecorderMF_WriteAudio(samples, count);
        }

        public bool SaveRecording(string fileName)
        {
            if (!HasRecording) return false;

            if (string.IsNullOrEmpty(fileName)) fileName = Path.GetFileName(_outputFilePath);

            // スタンドアロンにはブラウザのダウンロードが無いので、ビデオフォルダへコピーして場所を開く。
            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            var destinationDirectory = string.IsNullOrEmpty(videos)
                ? Path.Combine(Application.persistentDataPath, OutputDirectoryName)
                : Path.Combine(videos, "CanvasRecorder");

            Directory.CreateDirectory(destinationDirectory);
            var destination = Path.Combine(destinationDirectory, fileName);

            try
            {
                File.Copy(_outputFilePath, destination, true);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"録画を保存できませんでした: {e.Message}");
                return false;
            }

            Debug.Log($"録画を保存しました: {destination}");
            RevealInFileBrowser(destination);
            return true;
        }

        private static void RevealInFileBrowser(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception e)
            {
                // 保存自体は成功しているので、フォルダを開けなくても失敗扱いにはしない。
                Debug.LogWarning($"保存先を開けませんでした: {e.Message}");
            }
        }

        public void DiscardRecording()
        {
            _hasRecording = false;

            if (string.IsNullOrEmpty(_outputFilePath)) return;

            try
            {
                if (File.Exists(_outputFilePath)) File.Delete(_outputFilePath);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"録画ファイルを削除できませんでした: {e.Message}");
            }

            _outputFilePath = null;
        }

        public bool RequestPreviewUrl()
        {
            if (!HasRecording) return false;

            // VideoPlayer はローカルの絶対パスをそのまま再生できる。
            _owner.OnPreviewUrlReady(_outputFilePath);
            return true;
        }

        public void ReleasePreviewUrl()
        {
            // ローカルファイルなので解放するものは無い。
        }

        public bool ShareRecording(string text, string fileName)
        {
            _owner.OnShareResult("unsupported");
            return false;
        }
    }
}
