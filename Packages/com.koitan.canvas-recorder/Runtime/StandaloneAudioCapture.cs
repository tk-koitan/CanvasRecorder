using UnityEngine;

namespace CanvasRecorder
{
    /// <summary>
    /// <see cref="AudioListener"/> に取り付けて、最終ミックス後の音声を取得する。
    ///
    /// <c>OnAudioFilterRead</c> は渡されたバッファを書き換えなければ再生に影響しないため、
    /// コピーするだけなら録画中も音はスピーカーから聞こえる。
    /// エディタの Unity Recorder が使う <c>AudioRenderer</c> と違い、出力を奪わない。
    ///
    /// このコールバックはオーディオスレッドで呼ばれる。エンコーダを直接叩かず、
    /// いったんバッファへ溜めてメインスレッドから取り出す。
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public class StandaloneAudioCapture : MonoBehaviour
    {
        // 数フレーム分の余裕があれば足りる。溢れた分は捨てる。
        private const int BufferCapacity = 48000 * 8;

        private readonly object _lock = new object();
        private float[] _buffer = new float[BufferCapacity];
        private float[] _drainBuffer = new float[BufferCapacity];
        private int _count;
        private bool _overflowReported;

        /// <summary>
        /// 現在のオーディオ設定からチャンネル数を求める。
        /// </summary>
        public static int ResolveChannelCount()
        {
            switch (AudioSettings.driverCapabilities)
            {
                case AudioSpeakerMode.Mono: return 1;
                case AudioSpeakerMode.Quad: return 4;
                case AudioSpeakerMode.Surround: return 5;
                case AudioSpeakerMode.Mode5point1: return 6;
                case AudioSpeakerMode.Mode7point1: return 8;
                default: return 2;
            }
        }

        /// <summary>
        /// シーン内の <see cref="AudioListener"/> にキャプチャを取り付ける。
        /// </summary>
        public static StandaloneAudioCapture Attach()
        {
            var listener = FindAnyObjectByType<AudioListener>();
            if (listener == null)
            {
                Debug.LogWarning("AudioListener が見つからないため、音声を録音できません。");
                return null;
            }

            var existing = listener.GetComponent<StandaloneAudioCapture>();
            if (existing != null) return existing;

            return listener.gameObject.AddComponent<StandaloneAudioCapture>();
        }

        /// <summary>
        /// 取り付けたキャプチャを外す。
        /// </summary>
        public static void Detach(StandaloneAudioCapture capture)
        {
            if (capture != null) Destroy(capture);
        }

        /// <summary>
        /// 溜まっている音声を取り出す。メインスレッドから呼ぶこと。
        /// </summary>
        /// <param name="samples">取り出したサンプルが入るバッファ。内部で使い回される。</param>
        /// <returns>有効なサンプル数。</returns>
        public int Drain(out float[] samples)
        {
            lock (_lock)
            {
                var count = _count;
                if (count > 0)
                {
                    System.Array.Copy(_buffer, _drainBuffer, count);
                    _count = 0;
                }

                samples = _drainBuffer;
                return count;
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            lock (_lock)
            {
                if (_count + data.Length > _buffer.Length)
                {
                    // メインスレッドの取り出しが追いつかない場合。音が途切れるより落とす方を選ぶ。
                    if (!_overflowReported)
                    {
                        _overflowReported = true;
                        Debug.LogWarning("音声バッファが溢れました。一部のサンプルを破棄します。");
                    }

                    return;
                }

                System.Array.Copy(data, 0, _buffer, _count, data.Length);
                _count += data.Length;
            }

            // data は書き換えないので、再生音はそのまま出力される。
        }
    }
}
