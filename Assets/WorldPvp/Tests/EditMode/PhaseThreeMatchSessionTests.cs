using NUnit.Framework;
using WorldPvp.Phase1.Battles;

namespace WorldPvp.Phase1.Tests
{
    public sealed class PhaseThreeMatchSessionTests
    {
        [TestCase(100.0)]
        [TestCase(250.0)]
        [TestCase(500.0)]
        [TestCase(1000.0)]
        [TestCase(2000.0)]
        public void SupportedRadiusPresetsDoNotExceedTheStrictMaximum(double radiusMeters)
        {
            Assert.That(MatchSession.IsSupportedRadius(radiusMeters), Is.True);
            Assert.That(radiusMeters, Is.LessThanOrEqualTo(MatchSession.MaximumArenaRadiusMeters));
        }

        [TestCase(0.0)]
        [TestCase(-1.0)]
        [TestCase(2000.000001)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void UnsupportedRadiusIsRejected(double radiusMeters)
        {
            Assert.That(MatchSession.IsSupportedRadius(radiusMeters), Is.False);
        }

        [TestCase(2)]
        [TestCase(8)]
        [TestCase(32)]
        public void SupportedCapacityIncludesHostAndAtLeastOneGuest(int playerCount)
        {
            Assert.That(MatchSession.IsSupportedPlayerCount(playerCount), Is.True);
        }

        [TestCase(0)]
        [TestCase(33)]
        public void InvalidCapacityIsRejected(int playerCount)
        {
            Assert.That(MatchSession.IsSupportedPlayerCount(playerCount), Is.False);
        }

        [TestCase("7h29f", "7H29F")]
        [TestCase("https://example.test/join/7H29F", "7H29F")]
        public void JoinInputAcceptsBareCodesAndOpaqueInviteUrls(string input, string expectedCode)
        {
            string joinCode;
            Assert.That(MatchJoinLink.TryParseJoinInput(input, out joinCode), Is.True);
            Assert.That(joinCode, Is.EqualTo(expectedCode));
        }

        [Test]
        public void JoinInputRejectsUrlsThatDoNotContainAnOpaqueJoinRoute()
        {
            string joinCode;
            Assert.That(MatchJoinLink.TryParseJoinInput("https://example.test/?latitude=51.5", out joinCode), Is.False);
        }
    }
}
