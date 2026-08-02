using System;
using UnityEngine;

namespace CanvasRecorder.Editor
{
    /// <summary>
    /// エディタの Play モードで使うバックエンドを決める。
    ///
    /// Unity Recorder を使う実録画は別アセンブリ（CanvasRecorder.Editor.Recorder）にあり、
    /// そちらは com.unity.recorder が入っているときだけコンパイルされる。
    /// 存在する場合は起動時に <see cref="RecorderBackendFactory"/> を差し込んでくるので、
    /// ここではその有無を見て切り替える。Recorder が無ければダミー映像だけが使われる。
    /// </summary>
    public static class EditorBackendRegistry
    {
        /// <summary>
        /// Unity Recorder を使うバックエンドの生成方法。
        /// com.unity.recorder が存在する場合のみ設定される。
        /// </summary>
        public static Func<IScreenRecorderBackend> RecorderBackendFactory { get; set; }

        /// <summary>Unity Recorder を使う実録画が利用できるかどうか。</summary>
        public static bool IsRecorderAvailable => RecorderBackendFactory != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            ScreenRecorderBackendProvider.Factory = CreateBackend;
        }

        private static IScreenRecorderBackend CreateBackend()
        {
            if (CanvasRecorderEditorSettings.UseDummyVideo) return new DummyScreenRecorderBackend();

#if UNITY_EDITOR_WIN
            // Windows エディタでは Media Foundation のプラグインを優先する。
            // ビルドと同じ経路なので、Unity Recorder と違って録画中も音が聞こえる。
            if (!CanvasRecorderEditorSettings.ForceUnityRecorder &&
                StandaloneScreenRecorderBackend.IsPluginAvailable)
            {
                return new StandaloneScreenRecorderBackend();
            }
#endif

            if (RecorderBackendFactory == null)
            {
                Debug.LogWarning(
                    "実映像の録画には Media Foundation のプラグインか com.unity.recorder が必要です。" +
                    "どちらも利用できないためダミー映像で録画します。");
                return new DummyScreenRecorderBackend();
            }

            return RecorderBackendFactory();
        }
    }
}
