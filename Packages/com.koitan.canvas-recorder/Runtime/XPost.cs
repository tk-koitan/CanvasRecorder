using System;
using System.Collections.Generic;
using UnityEngine;

namespace CanvasRecorder
{
    /// <summary>
    /// X の投稿画面を本文入りで開くヘルパー。
    ///
    /// 注意: X の Web Intent は仕様上**動画や画像を添付できない**。
    /// 添付まで行いたい場合は <see cref="ScreenRecorder.ShareRecording"/>（Web Share API）を使い、
    /// それが使えない環境でのフォールバックとしてこのクラスを併用する。
    /// その場合は先に <see cref="ScreenRecorder.SaveRecording"/> で動画を保存させ、
    /// ユーザーに手動で添付してもらう流れになる。
    /// </summary>
    public static class XPost
    {
        private const string IntentUrl = "https://x.com/intent/post";

        /// <summary>
        /// X の投稿画面を新しいタブで開く。
        /// ユーザー操作のハンドラから直接呼ぶこと。そうしないとポップアップブロックにかかる。
        /// </summary>
        /// <param name="text">本文。</param>
        /// <param name="url">本文に添える URL。ゲームのページなど。</param>
        /// <param name="hashtags">ハッシュタグ（先頭の # は不要）。</param>
        public static void OpenPostIntent(string text = null, string url = null, params string[] hashtags)
        {
            var parameters = new List<string>();

            if (!string.IsNullOrEmpty(text)) parameters.Add("text=" + Uri.EscapeDataString(text));
            if (!string.IsNullOrEmpty(url)) parameters.Add("url=" + Uri.EscapeDataString(url));

            if (hashtags != null && hashtags.Length > 0)
            {
                parameters.Add("hashtags=" + Uri.EscapeDataString(string.Join(",", hashtags)));
            }

            var query = parameters.Count > 0 ? "?" + string.Join("&", parameters) : string.Empty;
            Application.OpenURL(IntentUrl + query);
        }
    }
}
