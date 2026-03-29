using UnityEngine;

namespace VerbGame
{
    // 左右入力の押し始めでだけ反転要否を決めるための純粋ロジック。
    // 押しっぱなし中は倍率を保持し、入力を離した時だけ初期状態へ戻す。
    public static class PlayerInputOrientationLockUtility
    {
        public readonly struct State
        {
            public State(bool wasMoveInputActive, float orientationMultiplier)
            {
                WasMoveInputActive = wasMoveInputActive;
                OrientationMultiplier = orientationMultiplier;
            }

            public bool WasMoveInputActive { get; }
            public float OrientationMultiplier { get; }
        }

        public static State Update(State currentState, float rawMoveInput, Vector2Int surfaceNormal)
        {
            bool isMoveInputActive = Mathf.Abs(rawMoveInput) >= 0.5f;
            if (!isMoveInputActive)
            {
                return new State(false, 1f);
            }

            if (!currentState.WasMoveInputActive)
            {
                return new State(
                    wasMoveInputActive: true,
                    orientationMultiplier: PlayerInputOrientationUtility.GetHorizontalOrientationMultiplier(surfaceNormal));
            }

            return currentState;
        }
    }
}
