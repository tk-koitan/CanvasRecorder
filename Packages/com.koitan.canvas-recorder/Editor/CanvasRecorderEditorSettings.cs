using UnityEditor;

namespace CanvasRecorder.Editor
{
    /// <summary>
    /// エディタ用バックエンドの設定。
    /// </summary>
    [InitializeOnLoad]
    public static class CanvasRecorderEditorSettings
    {
        private const string DummyVideoMenuPath = "Tools/CanvasRecorder/Use Dummy Video";
        private const string DummyVideoKey = "CanvasRecorder.Editor.UseDummyVideo";

        private const string VerboseMenuPath = "Tools/CanvasRecorder/Verbose Recorder Logging";
        private const string VerboseKey = "CanvasRecorder.Editor.VerboseLogging";

        private const string ForceRecorderMenuPath = "Tools/CanvasRecorder/Force Unity Recorder";
        private const string ForceRecorderKey = "CanvasRecorder.Editor.ForceUnityRecorder";

        // EditorPrefs は Windows ではレジストリが実体で読み出しが安くない。
        // これらの値は OnGUI から毎フレーム参照されうるので、メモリ上にキャッシュして
        // EditorPrefs へのアクセスは起動時と変更時だけにする。
        private static bool _useDummyVideo;
        private static bool _verboseLogging;
        private static bool _forceUnityRecorder;

        static CanvasRecorderEditorSettings()
        {
            // Windows エディタでは Media Foundation のプラグインが使えるようになり、
            // 録画中も音が聞こえる実録画ができるようになったため、既定を実録画に戻した。
            // ダミー映像は必要なときだけ有効にする。
            _useDummyVideo = EditorPrefs.GetBool(DummyVideoKey, false);
            _verboseLogging = EditorPrefs.GetBool(VerboseKey, false);
            _forceUnityRecorder = EditorPrefs.GetBool(ForceRecorderKey, false);
            EditorApplication.delayCall += () =>
            {
                Menu.SetChecked(DummyVideoMenuPath, _useDummyVideo);
                Menu.SetChecked(VerboseMenuPath, _verboseLogging);
                Menu.SetChecked(ForceRecorderMenuPath, _forceUnityRecorder);
            };
        }

        /// <summary>
        /// Windows エディタでも Media Foundation ではなく Unity Recorder を使うかどうか。
        /// Media Foundation 経路で問題が出た場合の逃げ道。
        /// </summary>
        public static bool ForceUnityRecorder
        {
            get => _forceUnityRecorder;
            set
            {
                if (_forceUnityRecorder == value) return;

                _forceUnityRecorder = value;
                EditorPrefs.SetBool(ForceRecorderKey, value);
                Menu.SetChecked(ForceRecorderMenuPath, value);
            }
        }

        [MenuItem(ForceRecorderMenuPath)]
        private static void ToggleForceUnityRecorder() => ForceUnityRecorder = !ForceUnityRecorder;

        /// <summary>
        /// Game View の代わりにダミー映像を録画するかどうか。**既定で有効。**
        ///
        /// 有効な間は Unity Recorder を一切使わず、<c>UnityEditor.Media.MediaEncoder</c> で
        /// テストパターンの MP4 を生成する。録画〜プレビュー〜保存の流れだけを
        /// 軽量かつ確実に確認できる。
        ///
        /// Game View の実際の映像を録りたい場合のみ無効にする。
        /// </summary>
        public static bool UseDummyVideo
        {
            get => _useDummyVideo;
            set
            {
                if (_useDummyVideo == value) return;

                _useDummyVideo = value;
                EditorPrefs.SetBool(DummyVideoKey, value);
                Menu.SetChecked(DummyVideoMenuPath, value);
            }
        }

        /// <summary>
        /// Unity Recorder の詳細ログを有効にするかどうか。
        /// フレームごとの実測 fps や待ち時間が Console に出る。切り分け用。
        /// </summary>
        public static bool VerboseLogging
        {
            get => _verboseLogging;
            set
            {
                if (_verboseLogging == value) return;

                _verboseLogging = value;
                EditorPrefs.SetBool(VerboseKey, value);
                Menu.SetChecked(VerboseMenuPath, value);
            }
        }

        // 検証関数の中で Menu.SetChecked を呼ぶとメニューの再構築と検証が循環しうるため、
        // チェック状態の更新は起動時と切り替え時にだけ行う。
        [MenuItem(DummyVideoMenuPath)]
        private static void ToggleUseDummyVideo() => UseDummyVideo = !UseDummyVideo;

        [MenuItem(VerboseMenuPath)]
        private static void ToggleVerboseLogging() => VerboseLogging = !VerboseLogging;
    }
}
