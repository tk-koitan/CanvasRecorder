using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CanvasRecorder.Editor
{
    /// <summary>
    /// エディタ用バックエンドの共通部分。
    /// 出力先の管理、保存、破棄、プレビュー用パスの受け渡しをまとめている。
    /// 実際の録画方法は派生クラスが決める。
    /// </summary>
    public abstract class EditorScreenRecorderBackendBase : IScreenRecorderBackend
    {
        private const string OutputDirectoryName = "CanvasRecorderEditor";
        private const string SaveDirectoryName = "Recordings";

        /// <summary>非同期の完了通知を返す先。</summary>
        protected ScreenRecorder Owner { get; private set; }

        /// <summary>録画中の出力先。拡張子まで含む絶対パス。</summary>
        protected string OutputFilePath { get; set; }

        public abstract bool IsRecording { get; }

        public abstract bool IsAudioAvailable { get; }

        /// <summary>
        /// 保持している録画結果があるかどうか。
        /// OnGUI から毎フレーム何度も参照されるため、ファイルの有無はキャッシュしておく。
        /// </summary>
        public bool HasRecording { get; private set; }

        /// <summary>エディタには Web Share API が無い。</summary>
        public bool CanShare => false;

        public bool IsLikelyMobile => false;

        public virtual void Initialize(ScreenRecorder owner)
        {
            Owner = owner;
        }

        public abstract bool StartRecording(int fps, int bitsPerSecond, bool includeAudio);

        public abstract void StopRecording();

        /// <summary>
        /// 新しい出力先を決める。拡張子を除いたパスを返す。
        /// Unity Recorder は拡張子なしのパスを要求するため、両方を扱えるようにしている。
        /// </summary>
        protected string PrepareOutputPath()
        {
            var directory = Path.Combine(Application.temporaryCachePath, OutputDirectoryName);
            Directory.CreateDirectory(directory);

            var pathWithoutExtension = Path.Combine(directory, "capture-" + DateTime.Now.Ticks);
            OutputFilePath = pathWithoutExtension + ".mp4";
            return pathWithoutExtension;
        }

        /// <summary>
        /// 出力ファイルができたことを通知する。
        /// </summary>
        protected void NotifyRecordingReady()
        {
            HasRecording = true;
            Owner.OnRecordingReady(new FileInfo(OutputFilePath).Length);
        }

        public bool SaveRecording(string fileName)
        {
            if (!HasRecording) return false;

            if (string.IsNullOrEmpty(fileName)) fileName = Path.GetFileName(OutputFilePath);

            // エディタにはブラウザのダウンロードが無いので、プロジェクト直下にコピーして場所を開く。
            var destinationDirectory = Path.Combine(Directory.GetCurrentDirectory(), SaveDirectoryName);
            Directory.CreateDirectory(destinationDirectory);
            var destination = Path.Combine(destinationDirectory, fileName);

            try
            {
                File.Copy(OutputFilePath, destination, true);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"録画を保存できませんでした: {e.Message}");
                return false;
            }

            Debug.Log($"録画を保存しました: {destination}");
            EditorUtility.RevealInFinder(destination);
            return true;
        }

        public virtual void DiscardRecording()
        {
            HasRecording = false;

            if (string.IsNullOrEmpty(OutputFilePath)) return;

            try
            {
                if (File.Exists(OutputFilePath)) File.Delete(OutputFilePath);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"録画ファイルを削除できませんでした: {e.Message}");
            }

            OutputFilePath = null;
        }

        public bool RequestPreviewUrl()
        {
            if (!HasRecording) return false;

            // VideoPlayer はローカルの絶対パスをそのまま再生できる。
            Owner.OnPreviewUrlReady(OutputFilePath);
            return true;
        }

        public void ReleasePreviewUrl()
        {
            // ローカルファイルなので解放するものは無い。
        }

        public bool ShareRecording(string text, string fileName)
        {
            Owner.OnShareResult("unsupported");
            return false;
        }
    }
}
