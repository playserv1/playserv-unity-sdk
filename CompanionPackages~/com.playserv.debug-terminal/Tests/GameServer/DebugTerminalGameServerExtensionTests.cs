using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.DebugTerminal.GameServer;
using UnityEngine;

namespace Playserv.DebugTerminal.Tests
{
    public sealed class DebugTerminalGameServerExtensionTests
    {
        [Test]
        public void MemoryServerKeyProvider_IsMemoryOnlyAndClearsOnDispose()
        {
            const string key = "sk_test-only-secret";
            var provider = new DebugTerminalMemoryServerKeyProvider(key);

            Assert.AreEqual(key, provider.GetServerKeyAsync().GetAwaiter().GetResult());
            Assert.IsNull(typeof(DebugTerminalMemoryServerKeyProvider)
                .GetField("_serverKey", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetCustomAttribute<SerializeField>());

            provider.Dispose();
            Assert.Throws<ObjectDisposedException>(
                () => provider.GetServerKeyAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void PromptConfiguration_OpensSecureModalWithoutPuttingAKeyInHistoryOrLogs()
        {
            var gameObject = new GameObject("DebugTerminalGameServerTests");
            try
            {
                var terminal = gameObject.AddComponent<PlayServDebugTerminal>();
                using var extension = new PlayServDebugTerminalGameServerExtension(terminal);

                extension.ExecuteServerCommandAsync(
                        new[] { "server", "configure", "--backend", "https://api.example.test", "--prompt" })
                    .GetAwaiter().GetResult();

                var operation = GetField<object>(terminal, "_credentialOperation");
                Assert.AreEqual("External", operation.ToString());
                Assert.AreEqual(string.Empty, GetField<string>(terminal, "_credentialValue"));
                Assert.IsEmpty(GetField<Queue<string>>(terminal, "_history"));
                Assert.IsFalse(GetField<List<string>>(terminal, "_logs")
                    .Exists(line => line.Contains("sk_", StringComparison.Ordinal)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void InvalidServerRangesAreRejectedBeforeConfiguration()
        {
            var gameObject = new GameObject("DebugTerminalGameServerValidationTests");
            try
            {
                var terminal = gameObject.AddComponent<PlayServDebugTerminal>();
                using var extension = new PlayServDebugTerminalGameServerExtension(terminal);

                extension.ExecuteServerCommandAsync(
                        new[] { "server", "configure", "--heartbeat-sec", "0" })
                    .GetAwaiter().GetResult();

                StringAssert.Contains(
                    "between 1 and 10",
                    string.Join("\n", GetField<List<string>>(terminal, "_logs")));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private static T GetField<T>(PlayServDebugTerminal terminal, string name)
        {
            var field = typeof(PlayServDebugTerminal).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, name);
            return (T)field.GetValue(terminal);
        }
    }
}
