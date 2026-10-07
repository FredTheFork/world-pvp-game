using System;
using NUnit.Framework;
using WorldPvp.Phase1.Configuration;

namespace WorldPvp.Phase1.Tests
{
    public sealed class GooglePhotorealisticTilesUrlTests
    {
        [Test]
        public void BuildsGooglePhotorealisticRootUrl()
        {
            const string testKey = "AIzaSy_A-TestKey.123";
            string url = GooglePhotorealisticTilesUrl.BuildRootTilesetUrl(testKey);
            Assert.That(
                url,
                Is.EqualTo("https://tile.googleapis.com/v1/3dtiles/root.json?key=AIzaSy_A-TestKey.123"));
        }

        [Test]
        public void EncodesQueryCharactersInKey()
        {
            string url = GooglePhotorealisticTilesUrl.BuildRootTilesetUrl("key value&other=value");
            Assert.That(url, Does.Contain("key=key%20value%26other%3Dvalue"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void RejectsMissingKey(string apiKey)
        {
            Assert.Throws<ArgumentException>(
                delegate { GooglePhotorealisticTilesUrl.BuildRootTilesetUrl(apiKey); });
        }
    }
}
