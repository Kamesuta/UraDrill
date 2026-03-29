using NUnit.Framework;
using UnityEngine;

namespace VerbGame.Tests
{
    // 入力の押し始めでだけ反転要否を決める仕様を、
    // 面移動中の回帰バグから守るためのテスト。
    public sealed class PlayerInputOrientationLockUtilityTests
    {
        [Test]
        public void Update_WhenInputStartsOnCeiling_LocksInvertedMultiplier()
        {
            var state = new PlayerInputOrientationLockUtility.State(false, 1f);

            PlayerInputOrientationLockUtility.State result = PlayerInputOrientationLockUtility.Update(state, -1f, Vector2Int.down);

            Assert.That(result.WasMoveInputActive, Is.True);
            Assert.That(result.OrientationMultiplier, Is.EqualTo(-1f));
        }

        [Test]
        public void Update_WhenHoldingInputAcrossSurfaceChange_KeepsOriginalMultiplier()
        {
            var state = new PlayerInputOrientationLockUtility.State(true, -1f);

            PlayerInputOrientationLockUtility.State result = PlayerInputOrientationLockUtility.Update(state, -1f, Vector2Int.up);

            Assert.That(result.WasMoveInputActive, Is.True);
            Assert.That(result.OrientationMultiplier, Is.EqualTo(-1f));
        }

        [Test]
        public void Update_WhenInputReleased_ResetsMultiplier()
        {
            var state = new PlayerInputOrientationLockUtility.State(true, -1f);

            PlayerInputOrientationLockUtility.State result = PlayerInputOrientationLockUtility.Update(state, 0f, Vector2Int.down);

            Assert.That(result.WasMoveInputActive, Is.False);
            Assert.That(result.OrientationMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void Update_WhenInputStartsAgainAfterRelease_UsesCurrentSurface()
        {
            var releasedState = new PlayerInputOrientationLockUtility.State(false, 1f);

            PlayerInputOrientationLockUtility.State result = PlayerInputOrientationLockUtility.Update(releasedState, 1f, Vector2Int.up);

            Assert.That(result.WasMoveInputActive, Is.True);
            Assert.That(result.OrientationMultiplier, Is.EqualTo(1f));
        }
    }
}
