using System.Collections;
using System.IO;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace CanvasRecorder.Editor.Recorder
{
    /// <summary>
    /// エディタの Play モードで Unity Recorder を使い、Game View を MP4 に録画する。
    ///
    /// このアセンブリは com.unity.recorder が入っているときだけコンパイルされる。
    /// 起動時に <see cref="EditorBackendRegistry.RecorderBackendFactory"/> へ自分を登録し、
    /// ダミー映像モードが無効なときに使われる。
    ///
    /// ブラウザ固有の挙動（MP4 コンテナの仕上げ、Web Share、WebAudio からの音声取り込み）は
    /// 再現されないため、最終確認は必ず Web ビルドで行うこと。
    /// </summary>
    public class UnityRecorderBackend : EditorScreenRecorderBackendBase
    {
        private RecorderController _controller;
        private RecorderControllerSettings _controllerSettings;
        private bool _startRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            EditorBackendRegistry.RecorderBackendFactory = () => new UnityRecorderBackend();
        }

        public override bool IsRecording =>
            _startRequested || (_controller != null && _controller.IsRecording());

        /// <summary>
        /// Unity Recorder は Unity の出力音声を取り込める。
        /// ただし録画中は AudioRenderer が出力を奪うため、スピーカーからは聞こえなくなる。
        /// </summary>
        public override bool IsAudioAvailable => true;

        public override void Initialize(ScreenRecorder owner)
        {
            base.Initialize(owner);
            Debug.Log($"{nameof(UnityRecorderBackend)} を使用します。" +
                      "録画中は音声がスピーカーから聞こえなくなります（AudioRenderer の仕様）。");
        }

        public override bool StartRecording(int fps, int bitsPerSecond, bool includeAudio)
        {
            if (IsRecording)
            {
                Debug.LogWarning("すでに録画中です。");
                return false;
            }

            // OnGUI などの描画コールバック中に RecorderController を起動すると
            // Recorder 自身のレンダーループ用フックと再入する恐れがある。
            // 実際の開始は次の Update まで遅らせる。
            _startRequested = true;
            Owner.StartCoroutine(StartRecordingDeferred(fps, includeAudio));
            return true;
        }

        private IEnumerator StartRecordingDeferred(int fps, bool includeAudio)
        {
            yield return null;

            try
            {
                StartRecordingNow(fps, includeAudio);
            }
            finally
            {
                _startRequested = false;
            }
        }

        private void StartRecordingNow(int fps, bool includeAudio)
        {
            DiscardRecording();

            var pathWithoutExtension = PrepareOutputPath();

            // 切り分け用。Recorder がフレームごとの実測 fps や待ち時間を出力する。
            RecorderOptions.VerboseMode = CanvasRecorderEditorSettings.VerboseLogging;

            var movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movieSettings.name = "CanvasRecorder";
            movieSettings.Enabled = true;
            movieSettings.OutputFile = pathWithoutExtension;
            // H.264 は偶数の寸法を要求するため、奇数なら切り下げる。
            movieSettings.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = Mathf.Max(2, Screen.width) & ~1,
                OutputHeight = Mathf.Max(2, Screen.height) & ~1,
            };
            movieSettings.AudioInputSettings.PreserveAudio = includeAudio;
            movieSettings.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            };

            _controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            _controllerSettings.AddRecorderSettings(movieSettings);
            _controllerSettings.SetRecordModeToManual();

            // 既定の Constant は Time.captureDeltaTime を設定して時間を実時間から切り離す、
            // オフラインレンダリング向けのモード。実際のプレイを実時間で録るのが目的なので
            // Variable を使う。
            _controllerSettings.FrameRatePlayback = FrameRatePlayback.Variable;
            _controllerSettings.FrameRate = fps;
            _controllerSettings.CapFrameRate = true;
            _controllerSettings.ExitPlayMode = false;

            _controller = new RecorderController(_controllerSettings);
            _controller.PrepareRecording();

            if (!_controller.StartRecording())
            {
                Debug.LogWarning("Unity Recorder の録画を開始できませんでした。");
                OutputFilePath = null;
            }
        }

        public override void StopRecording()
        {
            if (_controller == null || !_controller.IsRecording()) return;

            // 開始と同じ理由で、停止も描画コールバックから抜けてから行う。
            Owner.StartCoroutine(StopRecordingDeferred());
        }

        private IEnumerator StopRecordingDeferred()
        {
            yield return null;

            if (_controller == null || !_controller.IsRecording()) yield break;

            _controller.StopRecording();

            // エンコーダがファイルを書き終えるまで待ってから完了を通知する。
            yield return WaitForOutputFile();
        }

        private IEnumerator WaitForOutputFile()
        {
            var path = OutputFilePath;
            var deadline = Time.realtimeSinceStartup + 10f;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    // ファイルサイズが安定するまでもう1フレーム待つ。
                    yield return null;
                    NotifyRecordingReady();
                    yield break;
                }

                yield return null;
            }

            Debug.LogWarning($"録画ファイルが生成されませんでした: {path}");
        }
    }
}
