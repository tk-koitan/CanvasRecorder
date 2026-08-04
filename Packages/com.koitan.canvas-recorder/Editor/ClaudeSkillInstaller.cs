using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace CanvasRecorder.Editor
{
    /// <summary>
    /// 本パッケージに同梱した Claude Code 用スキルを、利用者プロジェクトの
    /// <c>.claude/skills/</c> へ配置する。
    ///
    /// UPM 経由で導入されたパッケージは <c>Library/PackageCache/</c> に展開され、
    /// Unity 公式の .gitignore で除外される。そのため利用者プロジェクトの Claude Code は
    /// このパッケージのソースを発見できず、API を推測してしまう。
    /// スキルを配置してソースの在処を伝えることでそれを防ぐ。
    ///
    /// 書き込み先は <c>Assets/</c> の外なので、初回に必ず同意を求める。
    /// 同意状態はプロジェクトごとに分けて保存する。
    /// </summary>
    [InitializeOnLoad]
    public static class ClaudeSkillInstaller
    {
        private const string SkillName = "canvas-recorder";
        private const string SkillSourceRelativePath = "Documentation~/skills/" + SkillName;
        private const string SkillDestinationRelativePath = ".claude/skills/" + SkillName;

        /// <summary>配置済みバージョンを記録するファイル。このパッケージが作ったものである印も兼ねる。</summary>
        private const string VersionStampFileName = ".version";

        private const string ConsentKeyPrefix = "CanvasRecorder.ClaudeSkill.Consent.";
        private const string ConsentGranted = "granted";
        private const string ConsentDenied = "denied";

        private const string MenuSetup = "Tools/CanvasRecorder/Setup Claude Code Skill";
        private const string MenuRemove = "Tools/CanvasRecorder/Remove Claude Code Skill";

        static ClaudeSkillInstaller()
        {
            // InitializeOnLoad はドメインリロードのたびに走る。
            // 起動処理を止めないよう、判定は次のエディタ更新まで遅らせる。
            EditorApplication.delayCall += () => TrySynchronize(askForConsent: true);
        }

        /// <summary>
        /// 手動でスキルを配置する。メニューを選ぶ操作自体が同意とみなせるため、
        /// 過去に拒否していても再度配置する。
        /// </summary>
        [MenuItem(MenuSetup)]
        private static void SetupFromMenu()
        {
            SetConsent(ConsentGranted);
            TrySynchronize(askForConsent: false, forceCopy: true);
        }

        /// <summary>
        /// 配置したスキルを削除し、同意を取り消す。
        /// このパッケージが配置したものだけを対象にする。
        /// </summary>
        [MenuItem(MenuRemove)]
        private static void RemoveFromMenu()
        {
            try
            {
                var destination = GetDestinationDirectory();

                if (!Directory.Exists(destination))
                {
                    Debug.Log("配置済みの Claude Code スキルはありません。");
                }
                else if (!File.Exists(Path.Combine(destination, VersionStampFileName)))
                {
                    // 印が無いものは他者が作った可能性があるため触らない。
                    Debug.LogWarning($"このパッケージが配置したものではないため削除しません: {destination}");
                    return;
                }
                else
                {
                    Directory.Delete(destination, true);
                    Debug.Log($"Claude Code スキルを削除しました: {destination}");
                }

                EditorPrefs.DeleteKey(GetConsentKey());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Claude Code スキルの削除に失敗しました: {e.Message}");
            }
        }

        /// <summary>
        /// 配置状態をパッケージのバージョンに合わせる。
        /// 失敗しても利用者のエディタを壊さないよう、例外は握りつぶして警告にとどめる。
        /// </summary>
        /// <param name="askForConsent">未回答のときにダイアログを出すかどうか。</param>
        /// <param name="forceCopy">バージョンが一致していてもコピーし直すかどうか。</param>
        private static void TrySynchronize(bool askForConsent, bool forceCopy = false)
        {
            // CI などの非対話環境ではダイアログを出せないので何もしない。
            if (Application.isBatchMode) return;

            try
            {
                Synchronize(askForConsent, forceCopy);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Claude Code スキルの配置に失敗しました: {e.Message}");
            }
        }

        private static void Synchronize(bool askForConsent, bool forceCopy)
        {
            var package = PackageInfo.FindForAssembly(typeof(ClaudeSkillInstaller).Assembly);
            if (package == null) return;

            // resolvedPath は PackageCache に展開された実体でも、Packages/ に埋め込まれた
            // 状態でも、どちらも実際のファイルシステム上の絶対パスを返す。
            var source = Path.Combine(package.resolvedPath, SkillSourceRelativePath);
            if (!Directory.Exists(source)) return;

            var destination = GetDestinationDirectory();
            var stamp = Path.Combine(destination, VersionStampFileName);

            if (!forceCopy && File.Exists(stamp) &&
                string.Equals(File.ReadAllText(stamp).Trim(), package.version, StringComparison.Ordinal))
            {
                // 配置済みでバージョンも一致しているので何もしない。
                return;
            }

            if (Directory.Exists(destination) && !File.Exists(stamp))
            {
                // 印が無いものは他者が作った可能性があるため上書きしない。
                Debug.LogWarning($"このパッケージが配置したものではないため上書きしません: {destination}");
                return;
            }

            if (!EnsureConsent(askForConsent)) return;

            CopySkill(source, destination);
            File.WriteAllText(stamp, package.version);

            Debug.Log($"Claude Code スキルを配置しました（{package.version}）: {destination}");
        }

        /// <summary>
        /// 同意を得ているか確認する。未回答なら必要に応じてダイアログを出す。
        /// </summary>
        /// <returns>配置してよければ true。</returns>
        private static bool EnsureConsent(bool askForConsent)
        {
            var stored = EditorPrefs.GetString(GetConsentKey(), string.Empty);

            if (stored == ConsentGranted) return true;
            if (stored == ConsentDenied) return false;
            if (!askForConsent) return false;

            var granted = EditorUtility.DisplayDialog(
                "Canvas Recorder",
                "Claude Code 用のスキルをプロジェクトに配置しますか？\n\n" +
                $"配置先: {SkillDestinationRelativePath}\n\n" +
                "このパッケージのソースは Library/PackageCache に展開され gitignore されるため、" +
                "Claude Code から発見できません。スキルを置くとソースの在処を伝えられます。\n\n" +
                "Assets の外に書き込みます。Tools/CanvasRecorder メニューから後で追加・削除できます。",
                "配置する",
                "配置しない");

            SetConsent(granted ? ConsentGranted : ConsentDenied);
            return granted;
        }

        private static void SetConsent(string value) => EditorPrefs.SetString(GetConsentKey(), value);

        /// <summary>
        /// 同意状態の保存キー。EditorPrefs はマシン共通なので、
        /// プロジェクトのパスから作った識別子を付けて他プロジェクトと分離する。
        /// </summary>
        private static string GetConsentKey() => ConsentKeyPrefix + GetProjectIdentifier();

        private static string GetProjectIdentifier()
        {
            var root = GetProjectRoot();

            // string.GetHashCode は実行ごとに変わりうるため使わない。
            using (var md5 = MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(root));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }

        /// <summary>プロジェクトのルート（Assets の親）。</summary>
        private static string GetProjectRoot() => Directory.GetParent(Application.dataPath).FullName;

        private static string GetDestinationDirectory() =>
            Path.Combine(GetProjectRoot(), SkillDestinationRelativePath);

        /// <summary>
        /// スキルをコピーする。配置先は毎回作り直し、パッケージ側で削除された
        /// ファイルが残らないようにする。
        /// </summary>
        private static void CopySkill(string source, string destination)
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            Directory.CreateDirectory(destination);

            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(directory.Replace(source, destination));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                // Documentation~ は Unity にインポートされないので通常 .meta は無いが、
                // 紛れ込んでいても配置先には持ち込まない。
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

                File.Copy(file, file.Replace(source, destination), true);
            }
        }
    }
}
