using System.Collections.Generic;
using UnityEngine;

namespace VerbGame
{
    // モバイル向けの 3 分割タッチ判定だけを切り出した純粋ロジック。
    // MonoBehaviour に依存させず、EditMode テストで挙動を固定する。
    public static class PlayerTouchInputResolver
    {
        public readonly struct Result
        {
            public Result(float moveInput, bool shouldTriggerDrill, bool isDrillTouchActive)
            {
                MoveInput = moveInput;
                ShouldTriggerDrill = shouldTriggerDrill;
                IsDrillTouchActive = isDrillTouchActive;
            }

            public float MoveInput { get; }
            public bool ShouldTriggerDrill { get; }
            public bool IsDrillTouchActive { get; }
        }

        public static Result Resolve(IReadOnlyList<float> activeTouchPositions, float screenWidth, bool wasDrillTouchActive)
        {
            bool isLeftPressed = false;
            bool isMiddlePressed = false;
            bool isRightPressed = false;
            float safeScreenWidth = Mathf.Max(1f, screenWidth);
            float leftBoundary = safeScreenWidth / 3f;
            float rightBoundary = leftBoundary * 2f;

            // 画面を 3 等分し、
            // 触れている指がどの操作ゾーンに入っているかだけを見る。
            for (int i = 0; i < activeTouchPositions.Count; i++)
            {
                float positionX = activeTouchPositions[i];
                if (positionX < leftBoundary)
                {
                    isLeftPressed = true;
                }
                else if (positionX < rightBoundary)
                {
                    isMiddlePressed = true;
                }
                else
                {
                    isRightPressed = true;
                }
            }

            if (isMiddlePressed)
            {
                // 中央ゾーンは移動せず、
                // 状態が立ち上がった瞬間だけドリル要求を返す。
                return new Result(0f, !wasDrillTouchActive, true);
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
