using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// バッチモードから Web(WebGL) ビルドを実行するためのエディタスクリプト。
/// </summary>
public static class WebBuilder
{
    private const string OutputPath = "Build/Web";

    /// <summary>
    /// サンプルシーンの場所を探す。
    /// パッケージのサンプルは Package Manager 経由で Assets/Samples/ 以下へ取り込まれるため、
    /// 固定パスではなく名前で解決する。
    /// </summary>
    private static string ResolveSampleScene()
    {
        var guids = AssetDatabase.FindAssets("CanvasRecorderSample t:Scene");
        return guids.Length > 0 ? AssetDatabase.GUIDToAssetPath(guids[0]) : null;
    }

    public static void Build()
    {
        // ローカルの静的サーバでそのまま配信できるよう、圧縮を無効にする。
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;
        // 録画のために preserveDrawingBuffer を有効にしたカスタムテンプレートを使う。
        PlayerSettings.WebGL.template = "PROJECT:Recorder";

        var scene = ResolveSampleScene();
        if (scene == null)
        {
            Debug.LogError("サンプルシーンが見つかりません。Package Manager から Canvas Recorder の " +
                           "Basic Sample をインポートしてください。");
            EditorApplication.Exit(1);
            return;
        }

        var options = new BuildPlayerOptions
        {
            scenes = new[] { scene },
            locationPathName = OutputPath,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;

        Debug.Log($"[WebBuilder] result={summary.result} " +
                  $"totalErrors={summary.totalErrors} totalTime={summary.totalTime} output={summary.outputPath}");

        if (summary.result != BuildResult.Succeeded)
        {
            foreach (var message in report.steps.SelectMany(step => step.messages)
                         .Where(message => message.type == LogType.Error || message.type == LogType.Exception))
            {
                Debug.LogError($"[WebBuilder] {message.content}");
            }
        }

        EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
