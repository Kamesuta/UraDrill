using NUnit.Framework;
using UnityEngine;

namespace VerbGame.Tests
{
    // 上下の面向きによって左右入力の意味が変わらないように、
    // 補正ロジックだけを EditMode で固定する。
    public sealed class PlayerInputOrientationUtilityTests
    {
        [Test]
        public void ApplyHorizontalInversion_OnGround_KeepsInput()
        {
            float result = PlayerInputOrientationUtility.ApplyHorizontalInversion(-1f, Vector2Int.up);

            Assert.That(result, Is.EqualTo(-1f));
        }

        [Test]
        public void ApplyHorizontalInversion_OnCeiling_InvertsLeftToRight()
        {
            float result = PlayerInputOrientationUtility.ApplyHorizontalInversion(-1f, Vector2Int.down);

            Assert.That(result, Is.EqualTo(1f));
        }

        [Test]
        public void ApplyHorizontalInversion_OnCeiling_InvertsRightToLeft()
        {
            float result = PlayerInputOrientationUtility.ApplyHorizontalInversion(1f, Vector2Int.down);

            Assert.That(result, Is.EqualTo(-1f));
        }

        [Test]
        public void ApplyHorizontalInversion_OnWall_KeepsInput()
        {
            float result = PlayerInputOrientationUtility.ApplyHorizontalInversion(1f, Vector2Int.left);

            Assert.That(result, Is.EqualTo(1f));
        }
    }
}
