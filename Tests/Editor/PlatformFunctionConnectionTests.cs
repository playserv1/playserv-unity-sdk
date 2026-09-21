using System;
using NUnit.Framework;

namespace Playserv.Editor.Tests
{
    public class PlatformFunctionConnectionTests
    {
        [TestCase("environment", "local", "environment")]
        [TestCase(" ", "local", "local")]
        [TestCase(null, " local ", "local")]
        public void EnvironmentCredentialTakesPrecedence(string environment, string local, string expected) =>
            Assert.That(PlatformFunctionConnection.ResolveKey(environment, local), Is.EqualTo(expected));

        [TestCase("https://other.example", "key")]
        [TestCase("https://platform.example", "new-key")]
        public void CredentialOrApiChangeDropsClient(string api, string key)
        {
            using (var c = new PlatformFunctionConnection())
            {
                c.Create("https://platform.example", "key");
                Assert.That(c.InvalidateIfChanged(api, key), Is.True);
                Assert.That(c.Client, Is.Null);
            }
        }
        [Test] public void UnchangedTargetKeepsClientAndDisposeDropsIt()
        {
            var c = new PlatformFunctionConnection(); c.Create("https://platform.example", "key");
            var client = c.Client;
            Assert.That(c.InvalidateIfChanged("https://platform.example", "key"), Is.False);
            Assert.That(c.Client, Is.SameAs(client)); c.Dispose(); Assert.That(c.Client, Is.Null);
        }
    }
}
