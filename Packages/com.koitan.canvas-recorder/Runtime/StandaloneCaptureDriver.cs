using System;
using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace CanvasRecorder
{
    /// <summary>
    /// スタンドアロンビルドで画面と音声を取得し、エンコーダへ渡す。
    ///
    /// 映像は <c>AsyncGPUReadback</c> で非同期に読み出すため、メインスレッドを止めない。
    /// 音声は <see cref="StandaloneAudioCapture"/> がオーディオスレッドで溜めたものを、
    /// メインスレッドから取り出して渡す。エンコーダをオーディオスレッドから直接叩かないための構成。
    /// </summary>
    public class StandaloneCaptureDriver : MonoBehaviour
    {
        private Action<byte[], int, long> _writeVideo;
        private Action<float[], int> _writeAudio;
        private Action _abort;

        private RenderTexture _captureTexture;
        private byte[] _frameBuffer;
        private StandaloneAudioCapture _audioCapture;

        private int _width;
        private int _height;
        private float _frameInterval;
        private float _startTime;
        private float _nextCaptureTime;
        private int _pendingReadbacks;
        private bool _stopRequested;
        private Action _onFinished;

        /// <summary>キャプチャ中かどうか。</summary>
        public bool IsCapturing { get; private set; }

        internal void Initialize(Action<byte[], int, long> writeVideo, Action<float[], int> writeAudio, Action abort)
        {
            _writeVideo = writeVideo;
            _writeAudio = writeAudio;
            _abort = abort;
        }

        internal void StartCapture(int width, int height, int fps, bool includeAudio)
        {
            _width = width;
            _height = height;
            _frameInterval = 1f / Mathf.Max(1, fps);
            _startTime = Time.realtimeSinceStartup;
            _nextCaptureTime = 0f;
            _pendingReadbacks = 0;
            _stopRequested = false;
            _onFinished = null;

            _captureTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "CanvasRecorderCapture",
            };
            _captureTexture.Create();

            _frameBuffer = new byte[width * height * 4];

            if (includeAudio) _audioCapture = StandaloneAudioCapture.Attach();

            IsCapturing = true;
            StartCoroutine(CaptureLoop());
        }

        internal void StopCapture(Action onFinished)
        {
            if (!IsCapturing) return;

            _onFinished = onFinished;
            _stopRequested = true;
        }

        private IEnumerator CaptureLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();

            while (!_stopRequested)
            {
                yield return endOfFrame;

                DrainAudio();

                var elapsed = Time.realtimeSinceStartup - _startTime;
                if (elapsed < _nextCaptureTime) continue;

                _nextCaptureTime += _frameInterval;
                // 大きく遅れた場合に一気に取り返そうとしないよう、基準を現在時刻へ寄せる。
                if (_nextCaptureTime < elapsed) _nextCaptureTime = elapsed + _frameInterval;

                CaptureFrame(elapsed);
            }

            // 停止要求後、読み出し中のフレームが片付くまで待つ。
            while (_pendingReadbacks > 0) yield return null;

            DrainAudio();
            Cleanup();

            IsCapturing = false;
            _onFinished?.Invoke();
        }

        private void CaptureFrame(float elapsed)
        {
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_captureTexture);

            // Media Foundation の RGB32 は BGRA 並びなので、読み出し時点で合わせておく。
            // ここで揃えておけば C# 側でのチャンネル入れ替えが不要になる。
            var timeHns = (long)(elapsed * 10_000_000.0);
            _pendingReadbacks++;

            AsyncGPUReadback.Request(_captureTexture, 0, GraphicsFormat.B8G8R8A8_UNorm,
                request => OnReadbackComplete(request, timeHns));
        }

        private void OnReadbackComplete(AsyncGPUReadbackRequest request, long timeHns)
        {
            _pendingReadbacks--;

            if (request.hasError)
            {
                Debug.LogWarning("フレームの読み出しに失敗しました。");
                return;
            }

            if (_frameBuffer == null) return;

            var data = request.GetData<byte>();
            if (data.Length < _frameBuffer.Length) return;

            NativeArray<byte>.Copy(data, 0, _frameBuffer, 0, _frameBuffer.Length);
            _writeVideo?.Invoke(_frameBuffer, _frameBuffer.Length, timeHns);
        }

        private void DrainAudio()
        {
            if (_audioCapture == null) return;

            var count = _audioCapture.Drain(out var samples);
            if (count > 0) _writeAudio?.Invoke(samples, count);
        }

        private void Cleanup()
        {
            if (_audioCapture != null)
            {
                StandaloneAudioCapture.Detach(_audioCapture);
                _audioCapture = null;
            }

            if (_captureTexture != null)
            {
                _captureTexture.Release();
                Destroy(_captureTexture);
                _captureTexture = null;
            }

            _frameBuffer = null;
        }

        private void OnDestroy()
        {
            // Play モードを抜けたときなど、録画中に破棄されることがある。
            // エンコーダを開いたままにしないよう中断処理を呼ぶ。
            if (IsCapturing)
            {
                IsCapturing = false;
                _abort?.Invoke();
            }

            Cleanup();
        }
    }
}
