using NUnit.Framework;

namespace VerbGame.Tests
{
    // スマホ向けの左右タッチ判定が崩れないように、
    // 左右押しっぱなしと両側同時タップの規則を固定する。
    public sealed class PlayerTouchInputResolverTests
    {
        [Test]
        public void Resolve_LeftHalfHold_MovesLeft()
        {
            PlayerTouchInputResolver.Result result = PlayerTouchInputResolver.Resolve(
                new[] { 100f },
                1000f,
                wasDualTouchActive: false);

            Assert.That(result.MoveInput, Is.EqualTo(-1f));
            Assert.That(result.ShouldTriggerDrill, Is.False);
            Assert.That(result.IsDualTouchActive, Is.False);
        }

        [Test]
        public void Resolve_RightHalfHold_MovesRight()
        {
            PlayerTouchInputResolver.Result result = PlayerTouchInputResolver.Resolve(
                new[] { 900f },
                1000f,
                wasDualTouchActive: false);

            Assert.That(result.MoveInput, Is.EqualTo(1f));
            Assert.That(result.ShouldTriggerDrill, Is.False);
            Assert.That(result.IsDualTouchActive, Is.False);
        }

        [Test]
        public void Resolve_DualTouchRise_TriggersDrillOnce()
        {
            PlayerTouchInputResolver.Result result = PlayerTouchInputResolver.Resolve(
                new[] { 100f, 900f },
                1000f,
                wasDualTouchActive: false);

            Assert.That(result.MoveInput, Is.EqualTo(0f));
            Assert.That(result.ShouldTriggerDrill, Is.True);
            Assert.That(result.IsDualTouchActive, Is.True);
        }

        [Test]
        public void Resolve_DualTouchHold_DoesNotRepeatDrill()
        {
            PlayerTouchInputResolver.Result result = PlayerTouchInputResolver.Resolve(
                new[] { 100f, 900f },
                1000f,
                wasDualTouchActive: true);

            Assert.That(result.MoveInput, Is.EqualTo(0f));
            Assert.That(result.ShouldTriggerDrill, Is.False);
            Assert.That(result.IsDualTouchActive, Is.True);
        }

        [Test]
        public void Resolve_AfterDualTouchReleaseToLeft_ResumesLeftMove()
        {
            PlayerTouchInputResolver.Result result = PlayerTouchInputResolver.Resolve(
                new[] { 100f },
                1000f,
                wasDualTouchActive: true);

            Assert.That(result.MoveInput, Is.EqualTo(-1f));
            Assert.That(result.ShouldTriggerDrill, Is.False);
            Assert.That(result.IsDualTouchActive, Is.False);
        }
    }
}
