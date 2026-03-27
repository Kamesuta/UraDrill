using System.Collections.Generic;
using UnityEngine;

namespace VerbGame
{
    // モバイル向けの左右タッチ判定だけを切り出した純粋ロジック。
    // MonoBehaviour に依存させず、EditMode テストで挙動を固定する。
    public static class PlayerTouchInputResolver
    {
        public readonly struct Result
        {
            public Result(float moveInput, bool shouldTriggerDrill, bool isDualTouchActive)
            {
                MoveInput = moveInput;
                ShouldTriggerDrill = shouldTriggerDrill;
                IsDualTouchActive = isDualTouchActive;
            }

            public float MoveInput { get; }
            public bool ShouldTriggerDrill { get; }
            public bool IsDualTouchActive { get; }
        }

        public static Result Resolve(IReadOnlyList<float> activeTouchPositions, float screenWidth, bool wasDualTouchActive)
        {
            bool isLeftPressed = false;
            bool isRightPressed = false;
            float halfWidth = Mathf.Max(1f, screenWidth) * 0.5f;

            // 画面左半分に1本でも触れていれば左押下、
            // 右半分も同様に独立して判定する。
            for (int i = 0; i < activeTouchPositions.Count; i++)
            {
                if (activeTouchPositions[i] < halfWidth)
                {
                    isLeftPressed = true;
                }
                else
                {
                    isRightPressed = true;
                }
            }

            bool isDualTouchActive = isLeftPressed && isRightPressed;
            if (isDualTouchActive)
            {
                // 両側同時タッチは移動せず、
                // 状態が立ち上がった瞬間だけドリル要求を返す。
                return new Result(0f, !wasDualTouchActive, true);
            }

            if (isLeftPressed)
            {
                return new Result(-1f, false, false);
            }

            if (isRightPressed)
            {
                return new Result(1f, false, false);
            }

            return new Result(0f, false, false);
        }
    }
}
