// Unity Room でクリップボードを操作するための .jslib ファイル
// https://qiita.com/ttttpzm/items/1015b148c98a2d4da182
mergeInto(LibraryManager.library, {
  CopyWebGL: function (textPointer, callback) {
    // Unity から受け取った UTF-8 文字列を JavaScript 文字列へ変換する。
    var text = UTF8ToString(textPointer);

    // JavaScript から C# コールバックを直接呼び返すためのヘルパー。
    var invokeCallback = function (success, error) {
      var errorBuffer = error ? stringToNewUTF8(error) : 0;
      try {
        {{{ makeDynCall('vii', 'callback') }}}(success ? 1 : 0, errorBuffer);
      } finally {
        if (errorBuffer) {
          _free(errorBuffer);
        }
      }
    };

    // 非同期 API が使える環境ではこちらを優先する。
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text)
        .then(function () {
          invokeCallback(true, "");
        })
        .catch(function () {
          fallbackCopy();
        });
      return;
    }

    // writeText が失敗する環境向けに、hidden textarea + execCommand へフォールバックする。
    var fallbackCopy = function () {
      var textarea = document.createElement("textarea");
      textarea.value = text;
      textarea.setAttribute("readonly", "");
      textarea.style.position = "fixed";
      textarea.style.top = "-1000px";
      textarea.style.left = "-1000px";
      document.body.appendChild(textarea);
      textarea.focus();
      textarea.select();

      var copied = false;
      try {
        copied = document.execCommand("copy");
      } catch (error) {
        copied = false;
      } finally {
        document.body.removeChild(textarea);
      }

      if (copied) {
        invokeCallback(true, "");
        return;
      }

      var listener = function (e) {
        e.clipboardData.setData("text/plain", text);
        e.preventDefault();
        document.removeEventListener("copy", listener);
        invokeCallback(true, "");
      };

      try {
        document.addEventListener("copy", listener);
        if (!document.execCommand("copy")) {
          document.removeEventListener("copy", listener);
          invokeCallback(false, "ブラウザでクリップボードへの書き込みが拒否されました");
        }
      } catch (error) {
        document.removeEventListener("copy", listener);
        invokeCallback(false, error ? String(error) : "クリップボードへの書き込みに失敗しました");
      }
    };

    fallbackCopy();
  },
  CheckClipboardAvailabilityWebGL: function (callback) {
    // JavaScript から C# コールバックを直接呼び返すためのヘルパー。
    var invokeCallback = function (available, reason) {
      var reasonBuffer = reason ? stringToNewUTF8(reason) : 0;
      try {
        {{{ makeDynCall('vii', 'callback') }}}(available ? 1 : 0, reasonBuffer);
      } finally {
        if (reasonBuffer) {
          _free(reasonBuffer);
        }
      }
    };

    // iframe / Permissions Policy 側で塞がれている場合はここで弾ける。
    var permissionsPolicy = document.permissionsPolicy || document.featurePolicy;
    if (permissionsPolicy && permissionsPolicy.allowsFeature && !permissionsPolicy.allowsFeature("clipboard-read")) {
      invokeCallback(false, "ClipboardBlockedByPermissionsPolicy");
      return;
    }

    if (!navigator.clipboard || !navigator.clipboard.readText) {
      invokeCallback(false, "ClipboardUnsupported");
      return;
    }

    // 権限 API が使えれば denied を事前に検知する。
    if (navigator.permissions && navigator.permissions.query) {
      navigator.permissions.query({ name: "clipboard-read" })
        .then(function (result) {
          if (result.state === "denied") {
            invokeCallback(false, "ClipboardPermissionDenied");
            return;
          }

          invokeCallback(true, "");
        })
        .catch(function (error) {
          invokeCallback(false, error ? String(error) : "ClipboardAvailabilityCheckFailed");
        });
      return;
    }

    invokeCallback(true, "");
  },
  AsyncPasteWebGL: function (callback) {
    // JavaScript から C# コールバックを直接呼び返すためのヘルパー。
    var invokeCallback = function (success, text, error) {
      var textBuffer = text ? stringToNewUTF8(text) : 0;
      var errorBuffer = error ? stringToNewUTF8(error) : 0;
      try {
        {{{ makeDynCall('viii', 'callback') }}}(success ? 1 : 0, textBuffer, errorBuffer);
      } finally {
        if (textBuffer) {
          _free(textBuffer);
        }
        if (errorBuffer) {
          _free(errorBuffer);
        }
      }
    };

    if (!navigator.clipboard || !navigator.clipboard.readText) {
      invokeCallback(false, "", "ブラウザが clipboard.readText() をサポートしていません");
      return;
    }

    navigator.clipboard.readText()
      .then(function (text) {
        invokeCallback(true, text, "");
      })
      .catch(function (error) {
        invokeCallback(false, "", error ? String(error) : "クリップボードの読み取りに失敗しました");
      });
  }
});
