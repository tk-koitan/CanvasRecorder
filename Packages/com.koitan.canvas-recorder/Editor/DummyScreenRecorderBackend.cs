using System.Collections;
using System.IO;
using UnityEngine;

namespace CanvasRecorder.Editor
{
    /// <summary>
    /// Game View を録らず、テストパターンの動画を生成するバックエンド。
    ///
    /// Unity Recorder に依存しないため、Recorder が入っていないプロジェクトでも動作する。
    /// 録画〜プレビュー〜保存の流れと UI の配線を軽量に確認するためのもの。
    /// 音声は含まれない。
    /// </summary>
    public class DummyScreenRecorderBackend : EditorScreenRecorderBackendBase
    {
        private bool _recording;
        private float _startTime;
        private int _fps;

        public override bool IsRecording => _recording;

        /// <summary>ダミー映像は音声トラックを持たない。</summary>
        public override bool IsAudioAvailable => false;

        public override void Initialize(ScreenRecorder owner)
        {
            base.Initialize(owner);
            Debug.Log($"{nameof(DummyScreenRecorderBackend)} を使用します。" +
                      "Game View の内容は記録されません。");
        }

        public override bool StartRecording(int fps, int bitsPerSecond, bool includeAudio)
        {
            if (IsRecording)
            {
                Debug.LogWarning("すでに録画中です。");
                return false;
            }

            DiscardRecording();
            PrepareOutputPath();

            _fps = fps;
            _startTime = Time.realtimeSinceStartup;
            _recording = true;
            return true;
        }

        public override void StopRecording()
        {
            if (!_recording) return;

            Owner.StartCoroutine(StopAndWrite());
        }

        private IEnumerator StopAndWrite()
        {
            // OnGUI などの描画コールバックから抜けてから書き出す。
            yield return null;

            var duration = Time.realtimeSinceStartup - _startTime;
            _recording = false;

            var aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 0f;
            yield return DummyVideoWriter.Write(OutputFilePath, _fps, duration, aspect);

            if (!File.Exists(OutputFilePath))
            {
                Debug.LogWarning($"ダミー動画を生成できませんでした: {OutputFilePath}");
                yield break;
            }

            NotifyRecordingReady();
        }

        public override void DiscardRecording()
        {
            _recording = false;
            base.DiscardRecording();
        }
    }
}
