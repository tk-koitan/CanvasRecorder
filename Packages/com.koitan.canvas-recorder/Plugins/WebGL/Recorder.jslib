// Unity の WebGL キャンバスを MediaRecorder で録画する。
// 録画の停止とファイルの保存は別の関数に分かれている。
// 停止すると結果は Blob として保持され、Save を呼ぶまでダウンロードは発生しない。
//
// 音声について:
// Unity 6 の WebGL 実装は、各サウンドチャンネルの gain ノードを個別に
// audioContext.destination へ直結しており、まとめて取得できるマスターノードが無い。
// そのため AudioNode.prototype.connect にフックを入れ、destination へ接続されるノードを
// 録音用の MediaStreamAudioDestinationNode にも分岐させている。
mergeInto(LibraryManager.library, {

  // 音声フックを仕掛ける。録画開始より前に呼んでおくほど取りこぼしが減るため、
  // ScreenRecorder.Awake から呼んでいる。何度呼んでも安全。
  // 戻り値: 音声を取得できる状態なら 1、そうでなければ 0
  CanvasRecorder_InstallAudioTap: function () {
    var state = Module.__canvasRecorder ||
      (Module.__canvasRecorder = { recorder: null, chunks: [], mimeType: "", blob: null, previewUrl: null, audioTap: null });

    if (state.audioTap) return 1;

    if (typeof WEBAudio === "undefined" || !WEBAudio.audioContext) {
      // まだ音声が初期化されていない。次に呼ばれたときに再試行する。
      return 0;
    }

    var context = WEBAudio.audioContext;
    if (typeof context.createMediaStreamDestination !== "function") {
      console.warn("[CanvasRecorder] createMediaStreamDestination is not available");
      return 0;
    }

    var tap = context.createMediaStreamDestination();

    // destination へ繋がれたノードを録音用ノードにも繋ぐ。
    // Unity は再生のたびに disconnect / connect をやり直すので、
    // このフック以降に鳴る音は自動的に拾われる。
    var originalConnect = AudioNode.prototype.connect;
    AudioNode.prototype.connect = function (destination) {
      var result = originalConnect.apply(this, arguments);
      if (destination === context.destination) {
        try {
          originalConnect.call(this, tap);
        } catch (e) {
          // 分岐に失敗しても本来の再生は壊さない。
        }
      }
      return result;
    };

    state.audioTap = tap;

    // 最初のユーザー操作で AudioContext を running にしておく。
    // 録画開始と resume が近接すると、Unity 側の再開オフセット補正と重なって
    // 初回だけ音がずれることがあるため、できるだけ早い段階で起こしておく。
    ["pointerdown", "touchstart", "keydown"].forEach(function (type) {
      document.addEventListener(type, function () {
        if (context.state === "suspended") context.resume();
      }, { once: true, capture: true });
    });

    console.log("[CanvasRecorder] audio tap installed");
    return 1;
  },

  // モバイル環境らしいかどうか。
  // Web Share API は動くがデスクトップの共有シートには X が並ばないため、
  // 共有とダウンロードのどちらを既定にするかの判断に使う。
  CanvasRecorder_IsLikelyMobile: function () {
    try {
      if (navigator.userAgentData && typeof navigator.userAgentData.mobile === "boolean") {
        return navigator.userAgentData.mobile ? 1 : 0;
      }
      return /Android|iPhone|iPad|iPod|Mobile/i.test(navigator.userAgent) ? 1 : 0;
    } catch (e) {
      return 0;
    }
  },

  // 録画結果をファイルとして共有できるか（Web Share API の対応状況）。
  CanvasRecorder_CanShare: function () {
    try {
      if (!navigator.share || !navigator.canShare) return 0;
      var probe = new File([new Blob(["0"], { type: "video/mp4" })], "probe.mp4", { type: "video/mp4" });
      return navigator.canShare({ files: [probe] }) ? 1 : 0;
    } catch (e) {
      return 0;
    }
  },

  // 保持している録画結果を Web Share API で共有する。
  // 結果は SendMessage で OnShareResult に返る。
  // ユーザー操作のハンドラから同期的に呼ばれる必要がある。
  CanvasRecorder_Share: function (textPtr, fileNamePtr) {
    var state = Module.__canvasRecorder;
    var send = (typeof SendMessage !== "undefined") ? SendMessage : Module.SendMessage;
    function report(result) {
      console.log("[CanvasRecorder] share result: " + result);
      if (send) send("ScreenRecorder", "OnShareResult", result);
    }

    if (!state || !state.blob) {
      report("unsupported");
      return 0;
    }
    if (!navigator.share) {
      report("unsupported");
      return 0;
    }

    var mime = state.mimeType.split(";")[0];
    var extension = mime.indexOf("mp4") >= 0 ? "mp4" : "webm";
    var fileName = fileNamePtr ? UTF8ToString(fileNamePtr) : "";
    if (!fileName) fileName = "capture-" + Date.now() + "." + extension;
    var text = textPtr ? UTF8ToString(textPtr) : "";

    var file;
    try {
      file = new File([state.blob], fileName, { type: mime });
    } catch (e) {
      console.error("[CanvasRecorder] failed to create File:", e);
      report("failed");
      return 0;
    }

    var data = { files: [file] };
    if (text) data.text = text;

    if (navigator.canShare && !navigator.canShare(data)) {
      report("unsupported");
      return 0;
    }

    navigator.share(data).then(
      function () { report("shared"); },
      function (e) {
        if (e && e.name === "AbortError") {
          report("cancelled");
        } else {
          console.error("[CanvasRecorder] share failed:", e);
          report("failed");
        }
      });

    return 1;
  },

  // 音声を取得できる状態かどうか。
  CanvasRecorder_HasAudio: function () {
    var state = Module.__canvasRecorder;
    return (state && state.audioTap) ? 1 : 0;
  },

  // fps: キャプチャするフレームレート / bitsPerSecond: 映像ビットレート
  // includeAudio: 0 以外なら音声も録音する
  // 戻り値: 録画を開始できたら 1、失敗したら 0
  CanvasRecorder_Start: function (fps, bitsPerSecond, includeAudio) {
    var state = Module.__canvasRecorder ||
      (Module.__canvasRecorder = { recorder: null, chunks: [], mimeType: "", blob: null, previewUrl: null, audioTap: null });

    if (state.recorder && state.recorder.state !== "inactive") {
      console.warn("[CanvasRecorder] already recording");
      return 0;
    }

    if (typeof MediaRecorder === "undefined") {
      console.error("[CanvasRecorder] MediaRecorder is not supported by this browser");
      return 0;
    }

    var canvas = Module.canvas || document.querySelector("#unity-canvas") || document.querySelector("canvas");
    if (!canvas || typeof canvas.captureStream !== "function") {
      console.error("[CanvasRecorder] canvas.captureStream is not available");
      return 0;
    }

    var stream = canvas.captureStream(fps);

    // 音声トラックを合流させる。取得できなくても映像だけで続行する。
    var withAudio = false;
    if (includeAudio && state.audioTap) {
      // 一度でも suspend されていると無音になるので念のため resume しておく。
      if (WEBAudio.audioContext.state === "suspended") WEBAudio.audioContext.resume();
      state.audioTap.stream.getAudioTracks().forEach(function (track) {
        stream.addTrack(track);
        withAudio = true;
      });
    }
    if (includeAudio && !withAudio) {
      console.warn("[CanvasRecorder] audio was requested but no audio track is available; recording video only");
    }

    var candidates = withAudio
      ? ["video/mp4;codecs=avc1,mp4a.40.2", "video/webm;codecs=vp9,opus", "video/webm;codecs=vp8,opus", "video/webm"]
      : ["video/mp4;codecs=avc1", "video/webm;codecs=vp9", "video/webm;codecs=vp8", "video/webm"];
    var mimeType = candidates.filter(function (m) { return MediaRecorder.isTypeSupported(m); })[0];
    if (!mimeType) {
      console.error("[CanvasRecorder] no supported mimeType found");
      return 0;
    }

    var options = { mimeType: mimeType, videoBitsPerSecond: bitsPerSecond };
    if (withAudio) options.audioBitsPerSecond = 128000;

    var recorder;
    try {
      recorder = new MediaRecorder(stream, options);
    } catch (e) {
      console.error("[CanvasRecorder] failed to create MediaRecorder:", e);
      return 0;
    }

    // 前回の録画結果は開始時点で破棄する。
    if (state.previewUrl) {
      URL.revokeObjectURL(state.previewUrl);
      state.previewUrl = null;
    }
    state.blob = null;
    state.chunks = [];
    state.mimeType = mimeType;
    state.recorder = recorder;

    recorder.ondataavailable = function (e) {
      if (e.data && e.data.size > 0) state.chunks.push(e.data);
    };

    recorder.onerror = function (e) {
      console.error("[CanvasRecorder] recorder error:", e);
    };

    recorder.onstop = function () {
      var mime = state.mimeType;
      state.blob = new Blob(state.chunks, { type: mime });
      state.chunks = [];
      state.recorder = null;

      console.log("[CanvasRecorder] stopped. size=" + state.blob.size + " type=" + mime);

      // 保存可能になったことを Unity 側に通知する。ダウンロードはここでは行わない。
      var send = (typeof SendMessage !== "undefined") ? SendMessage : Module.SendMessage;
      if (send) send("ScreenRecorder", "OnRecordingReady", state.blob.size);
    };

    // timeslice は指定しないこと。
    // 指定すると録画中に MP4 のヘッダが送出済みになり、停止時に mvhd の duration を
    // 書き戻せず mfra（シーク索引）も付かないため、再生時間が不明でシークできない
    // ファイルになる。引数なしで start すると停止時に単一の完成したファイルが得られる。
    recorder.start();
    console.log("[CanvasRecorder] started. mimeType=" + mimeType + " fps=" + fps + " audio=" + withAudio);
    return 1;
  },

  // 録画を停止する。結果は保持されるだけでダウンロードは発生しない。
  CanvasRecorder_Stop: function () {
    var state = Module.__canvasRecorder;
    if (state && state.recorder && state.recorder.state !== "inactive") {
      state.recorder.stop();
    }
  },

  // 保持している録画結果をファイルとしてダウンロードさせる。
  // fileNamePtr が空なら日時ベースの名前を自動生成する。
  // 戻り値: ダウンロードを開始できたら 1、保持している録画が無ければ 0
  CanvasRecorder_Save: function (fileNamePtr) {
    var state = Module.__canvasRecorder;
    if (!state || !state.blob) {
      console.warn("[CanvasRecorder] nothing to save");
      return 0;
    }

    var extension = state.mimeType.indexOf("mp4") >= 0 ? "mp4" : "webm";
    var fileName = fileNamePtr ? UTF8ToString(fileNamePtr) : "";
    if (!fileName) fileName = "capture-" + Date.now() + "." + extension;

    var url = URL.createObjectURL(state.blob);
    var anchor = document.createElement("a");
    anchor.style.display = "none";
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    setTimeout(function () { URL.revokeObjectURL(url); }, 30000);

    console.log("[CanvasRecorder] saved as " + fileName + " (" + state.blob.size + " bytes)");
    return 1;
  },

  // 保持している録画結果の再生用 URL を作り、Unity 側へ文字列で通知する。
  // 戻り値: 作成できたら 1、保持している録画が無ければ 0
  CanvasRecorder_RequestPreviewUrl: function () {
    var state = Module.__canvasRecorder;
    if (!state || !state.blob) {
      console.warn("[CanvasRecorder] nothing to preview");
      return 0;
    }

    if (state.previewUrl) URL.revokeObjectURL(state.previewUrl);
    state.previewUrl = URL.createObjectURL(state.blob);

    // 文字列を C# へ返すのに _malloc を使わずに済むよう SendMessage で渡す。
    var send = (typeof SendMessage !== "undefined") ? SendMessage : Module.SendMessage;
    if (send) send("ScreenRecorder", "OnPreviewUrlReady", state.previewUrl);
    return 1;
  },

  // 再生用 URL を解放する。プレビューを閉じたら必ず呼ぶこと。
  CanvasRecorder_ReleasePreviewUrl: function () {
    var state = Module.__canvasRecorder;
    if (state && state.previewUrl) {
      URL.revokeObjectURL(state.previewUrl);
      state.previewUrl = null;
    }
  },

  // 保持している録画結果を保存せずに破棄する。
  CanvasRecorder_Discard: function () {
    var state = Module.__canvasRecorder;
    if (!state) return;
    if (state.previewUrl) {
      URL.revokeObjectURL(state.previewUrl);
      state.previewUrl = null;
    }
    state.blob = null;
  },

  CanvasRecorder_IsRecording: function () {
    var state = Module.__canvasRecorder;
    return (state && state.recorder && state.recorder.state === "recording") ? 1 : 0;
  },

  // 保存できる録画結果を保持しているか。
  CanvasRecorder_HasRecording: function () {
    var state = Module.__canvasRecorder;
    return (state && state.blob) ? 1 : 0;
  },
});
