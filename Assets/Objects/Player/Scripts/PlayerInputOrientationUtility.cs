using UnityEngine;

namespace VerbGame
{
    // 面の向きによって、プレイヤーが感じる左右入力と
    // 論理上の進行方向がズレるケースだけを補正する純粋ロジック。
    public static class PlayerInputOrientationUtility
    {
        public static float ApplyHorizontalInversion(float moveInput, Vector2Int surfaceNormal)
        {
            return moveInput * GetHorizontalOrientationMultiplier(surfaceNormal);
        }

        public static float GetHorizontalOrientationMultiplier(Vector2Int surfaceNormal)
        {
            // 天井に張り付いている時は、法線が下向きになる。
            // この時だけ接線ベクトルの向きが見た目基準の左右と逆になるので、
            // 入力符号を反転して通常どおりの操作感に戻す。
            if (surfaceNormal == Vector2Int.down)
            {
                return -1f;
            }

            return 1f;
        }
    }
}
