using NUnit.Framework;

namespace VerbGame.Tests
{
    // スマホ向けの 3 分割タッチ判定が崩れないように、
    // 左移動・中央で掘る・右移動の規則を固定する。
    public sealed class PlayerTouchInputResolverTests
    {
        [Test]
        public void Resolve_LeftThirdHold_MovesLeft()
        {
            PlayerTouchInputResolver.Result result = PlayerTouchInputResolver.Resolve(
                new[] { 100f },
                1000f,
                wasDrillTouchActive: false);

            Assert.That(result.MoveInput, Is.EqualTo(-1f));
            Assert.That(result.ShouldTriggerDrill, Is.False);
            Assert.That(result.IsDrillTouchActive, Is.False);
        }

        [Test]
        public void Resolve_RightThirdHold_MovesRight()
        {
            PlayerTouchInputResolver.Result result = PlayerTouchInputResolver.Resolve(
                new[] { 900f },
                1000f,
                wasDrillTouchActive: false);

            Assert.That(result.MoveInput, Is.EqualTo(1f));
            Assert.That(result.ShouldTriggerDrill, Is.False);
            Assert.That(result.IsDrillTouchActive, Is.False);
        }

        [Test]
        public void Resolve_MiddleThirdTap_TriggersDrillOnce()
        {
            PlayerTouchInputResolver.Result result = PlayerTouchInputResolver.Resolve(
                new[] { 500f },
                1000f,
                wasDrillTouchActive: false);

            Assert.That(result.MoveInput, Is.EqualTo(0f));
            Assert.That(result.ShouldTriggerDrill, Is.True);
            Assert.That(result.IsDrillTouchActive, Is.True);
        }

        [Test]
        public void Resolve_MiddleThirdHold_DoesNotRepeatDrill()
        {
            PlayerTouchInputResolver.Result result = PlayerTouchInputResolver.Resolve(
                new[] { 500f },
                1000f,
                wasDrillTouchActive: true);

            Assert.That(result.MoveInput, Is.EqualTo(0f));
            Assert.That(result.ShouldTriggerDrill, Is.False);
            Assert.That(result.IsDrillTouchActive, Is.True);
        }

        [Test]
        public void Resolve_AfterMiddleTouchReleaseToLeft_ResumesLeftMove()
        {
            PlayerTouchInputResolver.Result result = PlayerTouchInputResolver.Resolve(
                new[] { 100f },
                1000f,
                wasDrillTouchActive: true);

            Assert.That(result.MoveInput, Is.EqualTo(-1f));
            Assert.That(result.ShouldTriggerDrill, Is.False);
            Assert.That(result.IsDrillTouchActive, Is.False);
        }

        [Test]
        public void Resolve_LeftAndMiddleTouch_PrioritizesDrill()
        {
            PlayerTouchInputResolver.Result result = PlayerTouchInputResolver.Resolve(
                new[] { 100f, 500f },
                1000f,
                wasDrillTouchActive: false);

            Assert.That(result.MoveInput, Is.EqualTo(0f));
            Assert.That(result.ShouldTriggerDrill, Is.True);
            Assert.That(result.IsDrillTouchActive, Is.True);
        }
    }
}
