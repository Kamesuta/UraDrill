using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

namespace VerbGame
{
    // クリップボード操作の実行環境差分を吸収する橋渡しクラス。
    // Editor / スタンドアロンでは Unity 標準 API、WebGL 実機では jslib を使う。
    public static class LevelEditModeClipboardBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // コピー要求中の完了コールバックを保持しておく。
        private static Action<bool, string> pendingCopyCallback;
        // 貼り付け要求中の完了コールバックを保持しておく。
        private static Action<bool, string, string> pendingPasteCallback;

        // jslib から呼び返されるコピー完了コールバックの型。
        private delegate void CopyCompletedCallback(int success, IntPtr errorPointer);
        // jslib から呼び返される貼り付け完了コールバックの型。
        private delegate void PasteCompletedCallback(int success, IntPtr textPointer, IntPtr errorPointer);
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL 実機では .jslib 側の関数へ橋渡しする。
        [DllImport("__Internal")]
        private static extern void CopyWebGL(string text, CopyCompletedCallback callback);

        // WebGL 実機では navigator.clipboard.readText() を jslib 側で実行する。
        [DllImport("__Internal")]
        private static extern void AsyncPasteWebGL(PasteCompletedCallback callback);
#endif

        // 実行環境が非同期クリップボード API を使うかどうかを呼び出し側へ知らせる。
        public static bool UsesAsyncClipboard
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        // テキストをクリップボードへコピーする共通窓口。
        public static bool TryCopyText(string text)
        {
            return TryCopyText(text, null);
        }

        // テキストをクリップボードへコピーし、必要なら完了通知も受ける共通窓口。
        public static bool TryCopyText(string text, Action<bool, string> onCompleted)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL 実機ではブラウザ API が非同期になりうるため、完了通知を保持して開始する。
            if (pendingCopyCallback != null)
            {
                return false;
            }

            pendingCopyCallback = onCompleted;
            CopyWebGL(text, HandleCopyCompletedFromWebGl);
            return true;
#else
            // Editor / ネイティブ実行では Unity 標準のクリップボード API を使う。
            GUIUtility.systemCopyBuffer = text;
            onCompleted?.Invoke(true, string.Empty);
            return true;
#endif
        }

        // テキストをクリップボードから読む共通窓口。
        public static bool TryPasteText(Action<bool, string, string> onCompleted)
        {
            if (onCompleted == null)
            {
                return false;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL 実機ではブラウザ API が非同期のため、完了コールバックを保持して開始する。
            if (pendingPasteCallback != null)
            {
                // 何らかの理由で前回要求が取り残されても、次の操作を不能にしないよう最新要求を優先する。
                pendingPasteCallback = null;
            }

            pendingPasteCallback = onCompleted;
            try
            {
                AsyncPasteWebGL(HandlePasteCompletedFromWebGl);
            }
            catch (Exception exception)
            {
                pendingPasteCallback = null;
                onCompleted(false, string.Empty, exception.Message);
            }

            return true;
#else
            // Editor / ネイティブ実行ではその場で読み取って即時完了させる。
            onCompleted(true, GUIUtility.systemCopyBuffer, string.Empty);
            return true;
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // jslib から直接呼び返される WebGL コピー完了通知。
        [MonoPInvokeCallback(typeof(CopyCompletedCallback))]
        private static void HandleCopyCompletedFromWebGl(int success, IntPtr errorPointer)
        {
            Action<bool, string> callback = pendingCopyCallback;
            pendingCopyCallback = null;

            if (callback == null)
            {
                return;
            }

            string error = PtrToStringUtf8(errorPointer);
            callback(success != 0, error);
        }

        // jslib から直接呼び返される WebGL 貼り付け完了通知。
        [MonoPInvokeCallback(typeof(PasteCompletedCallback))]
        private static void HandlePasteCompletedFromWebGl(int success, IntPtr textPointer, IntPtr errorPointer)
        {
            Action<bool, string, string> callback = pendingPasteCallback;
            pendingPasteCallback = null;

            if (callback == null)
            {
                return;
            }

            string text = PtrToStringUtf8(textPointer);
            string error = PtrToStringUtf8(errorPointer);
            callback(success != 0, text, error);
        }

        // jslib 側から渡された UTF-8 文字列を C# 文字列へ変換する。
        private static string PtrToStringUtf8(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero)
            {
                return string.Empty;
            }

            return Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
        }
#endif
    }
}
