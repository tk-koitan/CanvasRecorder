using UnityEngine;

namespace CanvasRecorder.Samples
{
    /// <summary>
    /// CanvasRecorder の使い方を一通り示すサンプル。
    /// 録画開始 → 停止 → プレビュー → 保存 / 破棄 の流れを IMGUI で組んでいる。
    /// 実際のプロジェクトでは uGUI などに置き換えて使うことを想定している。
    ///
    /// 注意: ビルドしたブラウザ上でのみ動作する。エディタの Play では
    /// <see cref="ScreenRecorder"/> の各メソッドは何もせず false を返す。
    /// また組み込みフォントに日本語グリフが無いため、画面に出す文字列は ASCII に限定している。
    /// </summary>
    public class CanvasRecorderSample : MonoBehaviour
    {
        [SerializeField]
        private ScreenRecorder _screenRecorder;

        [SerializeField]
        private RecordingPreview _recordingPreview;

        private long _readyBytes = -1;
        private string _previewError;

        // 音声が録音できているか確認するための、実行時に生成する 440Hz のトーン。
        private AudioSource _toneSource;
        private bool _includeAudio = true;

        // 映像と音声のズレを実測するための同期マーカー。
        // 一定間隔で「画面全体の白フラッシュ」と「短いクリック音」を同じフレームで発生させる。
        // 保存したファイル上でフラッシュとクリック音の位置を比べれば、
        // ズレの大きさと、それが一定なのか時間とともに広がるのかが分かる。
        private AudioSource _clickSource;
        private bool _syncMarkers;
        private float _markerTimer;
        private int _flashFramesLeft;
        private int _markerCount;

        private const float MarkerIntervalSeconds = 2f;

        // X へ投稿するときの本文と URL。実際のプロジェクトでは差し替えて使う。
        private const string PostText = "Recorded with CanvasRecorder";
        private const string GameUrl = "";

        private string _shareStatus;

        private void Awake()
        {
            if (_screenRecorder == null) _screenRecorder = FindAnyObjectByType<ScreenRecorder>();
            if (_recordingPreview == null) _recordingPreview = FindAnyObjectByType<RecordingPreview>();

            CreateToneSource();

            if (_screenRecorder == null)
            {
                Debug.LogError($"{nameof(ScreenRecorder)} がシーンに存在しません。" +
                               $"\"{ScreenRecorder.GameObjectName}\" という名前の GameObject を作り、" +
                               $"{nameof(ScreenRecorder)} をアタッチしてください。");
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (_screenRecorder != null)
            {
                _screenRecorder.RecordingReady += OnRecordingReady;
                _screenRecorder.ShareCompleted += OnShareCompleted;
            }

            if (_recordingPreview != null) _recordingPreview.Failed += OnPreviewFailed;
        }

        private void OnDisable()
        {
            if (_screenRecorder != null)
            {
                _screenRecorder.RecordingReady -= OnRecordingReady;
                _screenRecorder.ShareCompleted -= OnShareCompleted;
            }

            if (_recordingPreview != null) _recordingPreview.Failed -= OnPreviewFailed;
        }

        private void OnShareCompleted(RecordingShareResult result)
        {
            _shareStatus = result.ToString();

            // 共有できない環境では、保存させたうえで X の投稿画面を開く。
            // 動画の添付はユーザーの手動操作になる。
            if (result == RecordingShareResult.Unsupported) FallBackToDownloadAndIntent();
        }

        private void FallBackToDownloadAndIntent()
        {
            _screenRecorder.SaveRecording();
            XPost.OpenPostIntent(PostText, GameUrl);
        }

        private void OnRecordingReady(long sizeBytes) => _readyBytes = sizeBytes;

        private void OnPreviewFailed(string message) => _previewError = message;

        /// <summary>
        /// 録音の確認用に、途切れずループする 440Hz のサイン波を用意する。
        /// 1秒ぴったりで 440 周期になるのでループの継ぎ目が鳴らない。
        /// </summary>
        private void CreateToneSource()
        {
            const int sampleRate = 44100;
            const float frequency = 440f;

            var samples = new float[sampleRate];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / sampleRate) * 0.2f;
            }

            var clip = AudioClip.Create("SampleTone", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);

            _toneSource = gameObject.AddComponent<AudioSource>();
            _toneSource.clip = clip;
            _toneSource.loop = true;
            _toneSource.playOnAwake = false;

            // 同期マーカー用の短いクリック音。立ち上がりを鋭くしたいので
            // 減衰の速いエンベロープをかけた 1kHz バーストにする。
            const int clickSamples = sampleRate / 20; // 50ms
            var click = new float[clickSamples];
            for (var i = 0; i < click.Length; i++)
            {
                var envelope = 1f - (float)i / click.Length;
                click[i] = Mathf.Sin(2f * Mathf.PI * 1000f * i / sampleRate) * envelope * 0.6f;
            }

            var clickClip = AudioClip.Create("SyncClick", click.Length, 1, sampleRate, false);
            clickClip.SetData(click, 0);

            _clickSource = gameObject.AddComponent<AudioSource>();
            _clickSource.clip = clickClip;
            _clickSource.loop = false;
            _clickSource.playOnAwake = false;
        }

        private void Update()
        {
            if (_flashFramesLeft > 0) _flashFramesLeft--;

            if (!_syncMarkers) return;

            _markerTimer += UnityEngine.Time.deltaTime;
            if (_markerTimer < MarkerIntervalSeconds) return;

            _markerTimer = 0f;
            _markerCount++;
            // フラッシュとクリック音を同じフレームで発生させる。
            _flashFramesLeft = 2;
            _clickSource.Play();
        }

        private void OnGUI()
        {
            var width = Screen.width;
            var height = Screen.height;

            GUI.skin.label.fontSize = Mathf.RoundToInt(height * 0.035f);
            GUI.skin.button.fontSize = Mathf.RoundToInt(height * 0.035f);

            if (_recordingPreview != null && _recordingPreview.IsOpen)
            {
                DrawPreview(width, height);
                return;
            }

            DrawRecorder(width, height);
            DrawSyncFlash(width, height);
        }

        private void DrawRecorder(int width, int height)
        {
            // 録画された映像が真っ黒でないことを判別できるよう、明るい背景を敷く。
            DrawSolid(new Rect(0, 0, width, height), new Color(0.16f, 0.42f, 0.70f));

            // 動きがあることを確認するための往復するバー。
            var t = Mathf.PingPong(Time.time * 0.35f, 1f);
            var boxSize = height * 0.18f;
            DrawSolid(new Rect(t * (width - boxSize), height * 0.45f, boxSize, boxSize), new Color(1f, 0.78f, 0.16f));

            var isRecording = _screenRecorder.IsRecording;
            var hasRecording = _screenRecorder.HasRecording;

            GUI.Label(new Rect(20, 20, width - 40, height * 0.06f),
                isRecording ? $"RECORDING  {Time.time:F1}s" : $"IDLE  {Time.time:F1}s");

            if (_readyBytes >= 0)
            {
                GUI.Label(new Rect(20, 20 + height * 0.07f, width - 40, height * 0.06f),
                    hasRecording ? $"Ready to save: {_readyBytes / 1024f:F1} KB" : "No recording held");
            }

            GUI.Label(new Rect(20, 20 + height * 0.14f, width - 40, height * 0.06f),
                $"Audio available: {_screenRecorder.IsAudioAvailable}   Include audio: {_includeAudio}" +
                (_syncMarkers ? $"   Markers: {_markerCount}" : string.Empty));

            GUI.Label(new Rect(20, 20 + height * 0.21f, width - 40, height * 0.06f),
                $"Can share: {_screenRecorder.CanShare}   Mobile: {_screenRecorder.IsLikelyMobile}" +
                (string.IsNullOrEmpty(_shareStatus) ? string.Empty : $"   Share: {_shareStatus}"));

            if (!string.IsNullOrEmpty(_previewError))
            {
                GUI.Label(new Rect(20, 20 + height * 0.28f, width - 40, height * 0.06f),
                    $"Preview error: {_previewError}");
            }

            var buttonWidth = width * 0.22f;
            var buttonHeight = height * 0.09f;
            var buttonY = height - height * 0.14f;

            // 音声まわりのトグル。録画中でも操作できるようにしておく。
            var toggleY = buttonY - buttonHeight - height * 0.02f;
            if (GUI.Button(new Rect(20, toggleY, buttonWidth, buttonHeight),
                    _toneSource.isPlaying ? "TONE: ON" : "TONE: OFF"))
            {
                if (_toneSource.isPlaying) _toneSource.Stop();
                else _toneSource.Play();
            }

            if (GUI.Button(new Rect(30 + buttonWidth, toggleY, buttonWidth, buttonHeight),
                    _includeAudio ? "REC AUDIO: ON" : "REC AUDIO: OFF"))
            {
                _includeAudio = !_includeAudio;
            }

            if (GUI.Button(new Rect(40 + buttonWidth * 2, toggleY, buttonWidth, buttonHeight),
                    _syncMarkers ? "SYNC MARK: ON" : "SYNC MARK: OFF"))
            {
                _syncMarkers = !_syncMarkers;
                _markerTimer = 0f;
                _markerCount = 0;
            }

            if (isRecording)
            {
                if (GUI.Button(new Rect(20, buttonY, buttonWidth, buttonHeight), "STOP"))
                {
                    _screenRecorder.StopRecording();
                }

                return;
            }

            if (GUI.Button(new Rect(20, buttonY, buttonWidth, buttonHeight), "START"))
            {
                _screenRecorder.StartRecording(includeAudio: _includeAudio);
                _readyBytes = -1;
                _previewError = null;
            }

            // 停止・プレビュー・保存はそれぞれ独立した操作。
            if (!hasRecording) return;

            if (GUI.Button(new Rect(30 + buttonWidth, buttonY, buttonWidth, buttonHeight), "PREVIEW"))
            {
                _previewError = null;
                _recordingPreview.Open();
            }

            if (GUI.Button(new Rect(40 + buttonWidth * 2, buttonY, buttonWidth, buttonHeight), "SAVE"))
            {
                _screenRecorder.SaveRecording();
            }

            // 共有と「保存 + 投稿画面」は別のボタンにしておく。
            // デスクトップの共有シートには X が並ばないため、片方だけだと行き止まりになる。
            // どちらもユーザーのクリックの中で完結させる必要がある。
            var recommended = _screenRecorder.CanShareToApps;
            if (GUI.Button(new Rect(50 + buttonWidth * 3, buttonY, buttonWidth, buttonHeight),
                    recommended ? "SHARE *" : "SHARE"))
            {
                _shareStatus = null;
                if (!_screenRecorder.ShareRecording(PostText)) FallBackToDownloadAndIntent();
            }

            var upperY = buttonY - buttonHeight * 2 - height * 0.04f;
            if (GUI.Button(new Rect(20, upperY, buttonWidth, buttonHeight), "DISCARD"))
            {
                _screenRecorder.DiscardRecording();
                _readyBytes = -1;
                _shareStatus = null;
            }

            if (GUI.Button(new Rect(30 + buttonWidth, upperY, buttonWidth * 2, buttonHeight),
                    recommended ? "SAVE & OPEN X" : "SAVE & OPEN X *"))
            {
                _shareStatus = null;
                FallBackToDownloadAndIntent();
            }
        }

        /// <summary>
        /// 同期マーカーの白フラッシュ。クリック音と同じフレームで出す必要があるため、
        /// 他の描画をすべて隠すように最後に全画面へ描く。
        /// </summary>
        private void DrawSyncFlash(int width, int height)
        {
            if (_flashFramesLeft <= 0) return;
            DrawSolid(new Rect(0, 0, width, height), Color.white);
        }

        private void DrawPreview(int width, int height)
        {
            DrawSolid(new Rect(0, 0, width, height), new Color(0.08f, 0.08f, 0.10f));

            var texture = _recordingPreview.Texture;
            if (texture == null)
            {
                GUI.Label(new Rect(20, 20, width - 40, height * 0.06f), "Preparing...");
                return;
            }

            // 動画のアスペクト比を保ったまま中央に収める。
            var area = new Rect(0, height * 0.06f, width, height * 0.68f);
            var scale = Mathf.Min(area.width / texture.width, area.height / texture.height);
            var drawWidth = texture.width * scale;
            var drawHeight = texture.height * scale;
            GUI.DrawTexture(new Rect(area.x + (area.width - drawWidth) * 0.5f,
                                     area.y + (area.height - drawHeight) * 0.5f,
                                     drawWidth, drawHeight), texture);

            var length = _recordingPreview.Length;
            var time = _recordingPreview.Time;
            GUI.Label(new Rect(20, 20, width - 40, height * 0.06f),
                $"PREVIEW  {time:F1}s / {length:F1}s  ({texture.width}x{texture.height})");

            // 音量。音声トラックが無い動画では操作しても効かないので、その旨を出す。
            var volumeRect = new Rect(20, height - height * 0.29f, width - 40, height * 0.05f);
            if (_recordingPreview.HasAudioTrack)
            {
                GUI.Label(new Rect(20, volumeRect.y - height * 0.055f, width - 40, height * 0.05f),
                    $"VOLUME  {_recordingPreview.Volume * 100f:F0}%");
                var volume = GUI.HorizontalSlider(volumeRect, _recordingPreview.Volume, 0f, 1f);
                if (!Mathf.Approximately(volume, _recordingPreview.Volume)) _recordingPreview.Volume = volume;
            }
            else
            {
                GUI.Label(volumeRect, "no audio track");
            }

            // シークバー。length が 0 の動画（duration 不明）ではシークできない。
            var sliderRect = new Rect(20, height - height * 0.22f, width - 40, height * 0.05f);
            if (length > 0d)
            {
                var seeked = GUI.HorizontalSlider(sliderRect, (float)time, 0f, (float)length);
                if (!Mathf.Approximately(seeked, (float)time)) _recordingPreview.Seek(seeked);
            }
            else
            {
                GUI.Label(sliderRect, "duration unknown - cannot seek");
            }

            var buttonWidth = width * 0.22f;
            var buttonHeight = height * 0.09f;
            var buttonY = height - height * 0.14f;

            if (GUI.Button(new Rect(20, buttonY, buttonWidth, buttonHeight),
                    _recordingPreview.IsPlaying ? "PAUSE" : "PLAY"))
            {
                _recordingPreview.TogglePlay();
            }

            if (GUI.Button(new Rect(30 + buttonWidth, buttonY, buttonWidth, buttonHeight), "SAVE"))
            {
                _screenRecorder.SaveRecording();
            }

            if (GUI.Button(new Rect(40 + buttonWidth * 2, buttonY, buttonWidth, buttonHeight), "CLOSE"))
            {
                _recordingPreview.Close();
            }
        }

        private static void DrawSolid(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
