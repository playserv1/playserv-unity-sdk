using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Playserv.Wrapper;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServWebGlWebSocketPluginTests
    {
        [Test]
        public void CoreWebSocketPlugin_ExportsEveryNativeSymbolUsedByTransport()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PlayServ).Assembly);
            Assert.NotNull(package);

            var root = package.resolvedPath;
            var plugin = File.ReadAllText(Path.Combine(root, "Runtime/Plugins/WebGL/playserv_websocket.jslib"));
            var imports = typeof(Playserv.Proxy.Implementation.WebGLWebSocketTransportImplementation)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Select(method => method.GetCustomAttribute<DllImportAttribute>())
                .Where(import => import != null && import.Value == "__Internal")
                .Select(import => import.EntryPoint).ToArray();
            Assert.That(imports, Is.EquivalentTo(new[] { "Ws_Connect", "Ws_Send", "Ws_Close" }));

            foreach (var symbol in new[] { "Ws_Connect", "Ws_Send", "Ws_Close" })
            {
                StringAssert.Contains(symbol + ": function", plugin, symbol);
            }
        }

        [Test]
        public void WebSocketPlugin_IsEnabledOnlyForWebGl()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PlayServ).Assembly);
            var meta = File.ReadAllText(Path.Combine(
                package.resolvedPath,
                "Runtime/Plugins/WebGL/playserv_websocket.jslib.meta"));

            StringAssert.Contains("Any:", meta);
            StringAssert.Contains("Editor: Editor", meta);
            StringAssert.Contains("WebGL: WebGL", meta);
            StringAssert.Contains("enabled: 1", meta);
        }

        [Test]
        public void WebRtcPlugin_DoesNotDuplicateCoreWebSocketSymbols()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PlayServ).Assembly);
            var webRtcPluginPath = Path.Combine(
                package.resolvedPath,
                "CompanionPackages~/com.playserv.webrtc/Runtime/Plugins/WebGL/webjl_rtc.jslib");
            if (!File.Exists(webRtcPluginPath))
                Assert.Ignore("WebRTC companion source is not present in this package layout.");

            var plugin = File.ReadAllText(webRtcPluginPath);
            StringAssert.DoesNotContain("Ws_Connect: function", plugin);
            StringAssert.DoesNotContain("Ws_Send: function", plugin);
            StringAssert.DoesNotContain("Ws_Close: function", plugin);
            StringAssert.Contains("RtcSig_Connect: function", plugin);
            StringAssert.Contains("Rtc_Connect: function", plugin);
        }
    }
}
