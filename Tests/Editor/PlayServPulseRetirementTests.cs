using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Playserv.Editor;
using Playserv.Modules;
using UnityEngine;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServPulseRetirementTests
    {
        [Test]
        public void Package_and_editor_do_not_offer_retired_Pulse()
        {
            PlayServModuleManifestJsonRegistry.Reload();
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.playserv.sdk/package.json");
            Assert.That(package, Is.Not.Null);
            Assert.That(Directory.Exists(Path.Combine(package.resolvedPath, "CompanionPackages~/com.playserv.pulse")), Is.False);
            Assert.That(PlayServCompanionPackageCatalog.TryGetByPackageId("com.playserv.pulse", out _), Is.False);
            Assert.That(PlayServCompanionPackageCatalog.IsCompanionModule("pulse"), Is.False);
            Assert.That(PlayServModuleManifest.TryGet("pulse", out _), Is.False);
            foreach (var profile in PlayServSdkProfiles.All)
                Assert.That(profile.EnablesModule("pulse"), Is.False, profile.Id);
            Assert.That(typeof(PlayServModuleIds).GetField("Pulse"), Is.Null);
            Assert.That(typeof(PlayServModuleManifest).GetField("PulseId"), Is.Null);
            Assert.That(typeof(PlayServModuleManifest).GetField("DefineDisablePulse"), Is.Null);
        }

        [Test]
        public void Retired_Pulse_symbols_are_removed_without_touching_other_defines()
        {
            var defines = new HashSet<string>
            {
                "PLAYSERV_DISABLE_PULSE", "PLAYSERV_MODULE_DISABLED_PULSE",
                "PLAYSERV_MODULE_DISABLED_ANALYTICS", "GAME_CUSTOM_DEFINE"
            };
            Assert.That(PlayServRuntimeModuleDefines.RemoveLegacyRuntimeModuleDefines(defines), Is.True);
            Assert.That(defines, Is.EquivalentTo(new[] { "PLAYSERV_MODULE_DISABLED_ANALYTICS", "GAME_CUSTOM_DEFINE" }));
            Assert.That(PlayServRuntimeModuleDefines.RemoveLegacyRuntimeModuleDefines(defines), Is.False);
        }

        [Test]
        public void Loading_old_module_settings_removes_Pulse_from_every_platform_and_preserves_other_choices()
        {
            PlayServModuleManifestJsonRegistry.Reload();
            var path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, PlayServProjectModuleSettings.ProjectRelativePath);
            var previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                var known = PlayServModuleManifest.RuntimeModules.Select(module => module.Id).Concat(new[] { "pulse", "studio-module" }).Distinct();
                var data = new JObject
                {
                    ["schemaVersion"] = 1, ["activeProfileId"] = "custom",
                    ["knownModuleIds"] = new JArray(known),
                    ["enabledModuleIds"] = new JArray("transport-websocket", "pulse", "studio-module"),
                    ["enabledEditorToolIds"] = new JArray("deployment"),
                    ["platformOverrides"] = new JArray(
                        new JObject { ["buildTargetGroup"] = "Android", ["profileId"] = "custom", ["enabledModuleIds"] = new JArray("transport-websocket", "pulse", "studio-module") },
                        new JObject { ["buildTargetGroup"] = "WebGL", ["profileId"] = "custom", ["enabledModuleIds"] = new JArray("pulse", "studio-module") })
                };
                File.WriteAllText(path, data.ToString());
                PlayServProjectModuleSettings.LoadEffectiveState();
                var migrated = JObject.Parse(File.ReadAllText(path));
                Assert.That(migrated["knownModuleIds"].Values<string>(), Does.Not.Contain("pulse"));
                Assert.That(migrated["knownModuleIds"].Values<string>(), Does.Contain("studio-module"));
                Assert.That(migrated["enabledModuleIds"].Values<string>(), Is.EquivalentTo(new[] { "transport-websocket", "studio-module" }));
                Assert.That(migrated["enabledEditorToolIds"].Values<string>(), Is.EqualTo(new[] { "deployment" }));
                Assert.That(migrated["activeProfileId"].Value<string>(), Is.EqualTo("custom"));
                Assert.That(migrated["platformOverrides"][0]["enabledModuleIds"].Values<string>(), Is.EquivalentTo(new[] { "transport-websocket", "studio-module" }));
                Assert.That(migrated["platformOverrides"][1]["enabledModuleIds"].Values<string>(), Is.EqualTo(new[] { "studio-module" }));
                var contents = File.ReadAllText(path);
                PlayServProjectModuleSettings.LoadEffectiveState();
                Assert.That(File.ReadAllText(path), Is.EqualTo(contents), "Migration must be idempotent.");
            }
            finally
            {
                if (previous != null) File.WriteAllBytes(path, previous);
                else File.Delete(path);
            }
        }
    }
}
