using NUnit.Framework;
using WorldPvp.Phase1.Battles;

namespace WorldPvp.Phase1.Tests
{
    public sealed class MatchJoinLinkTests
    {
        [TestCase("7h29f", "7H29F")]
        [TestCase("  Abc123  ", "ABC123")]
        public void JoinCodeIsNormalizedWithoutChangingItsOpaqueValue(string input, string expected)
        {
            Assert.That(MatchJoinLink.TryNormalizeJoinCode(input, out string normalized), Is.True);
            Assert.That(normalized, Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("abc")]
        [TestCase("A BCD")]
        [TestCase("AB-CD")]
        [TestCase("ABCD?lat=51.5")]
        [TestCase("ABCDEFGHIJKLMNOQRSTUVWXYZ1234567890EXTRA")]
        public void InvalidJoinCodesAreRejected(string input)
        {
            Assert.That(MatchJoinLink.TryNormalizeJoinCode(input, out _), Is.False);
        }

        [Test]
        public void ShareUrlContainsOnlyTheOpaqueCodeAndPreservesNonDefaultPort()
        {
            const string currentUrl = "https://game.example:8443/join/OLD?lat=51.5&radius=500#details";
            Assert.That(MatchJoinLink.TryBuildShareUrl(currentUrl, "7H29F", out string url), Is.True);
            Assert.That(url, Is.EqualTo("https://game.example:8443/join/7H29F"));
            Assert.That(url, Does.Not.Contain("51.5"));
            Assert.That(url, Does.Not.Contain("radius"));
            Assert.That(url, Does.Not.Contain("lat="));
        }

        [Test]
        public void SharedInviteRouteExtractsOnlyTheOpaqueCode()
        {
            Assert.That(
                MatchJoinLink.TryExtractJoinCode("https://game.example/join/7h29f", out string code),
                Is.True);
            Assert.That(code, Is.EqualTo("7H29F"));
            Assert.That(MatchJoinLink.TryExtractJoinCode("https://game.example/join/7H29F/51.5", out _), Is.False);
        }
    }
}
