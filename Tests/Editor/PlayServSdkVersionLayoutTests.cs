using System.Collections;
using NUnit.Framework;
using Playserv.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServSdkVersionLayoutTests
    {
        [UnityTest]
        public IEnumerator Sdk_actions_do_not_make_the_version_card_taller_than_deployment()
        {
            var window = ScriptableObject.CreateInstance<SdkVersionLayoutWindow>();
            try
            {
                window.ShowUtility();
                foreach (var width in new[] { 260f, 540f })
                {
                    foreach (var status in new[] { "", "Checking for updates…", "No newer stable compatible release found. A deliberately long message to exercise clipping.", "Update available", "Registry unavailable; try again later." })
                    {
                    window.Status = status;
                    window.AvailableVersion = status == "Update available" ? "0.6.3" : null;
                    window.position = new Rect(30, 30, width, 500);
                    window.Measured = false;
                    for (var i = 0; i < 30 && !window.Measured; i++)
                    {
                        window.Repaint();
                        yield return null;
                    }
                    Assert.That(window.Measured, Is.True, "The real IMGUI layout must be rendered.");
                    Assert.That(window.Version.height, Is.LessThanOrEqualTo(window.Deployment.height),
                        "SDK actions must fit beside the version without adding rows to the card.");
                    }
                }
            }
            finally { window.Close(); Object.DestroyImmediate(window); }
        }
    }

    internal sealed class SdkVersionLayoutWindow : EditorWindow
    {
        public bool Measured;
        public Rect Version, Deployment;
        public string Status, AvailableVersion;

        private void OnGUI()
        {
            PlayServWindowTheme.Ensure();
            PlayServWindowChrome.DrawOverviewCard("Deployment ID", "example-game", "Editor deployment only");
            var deployment = GUILayoutUtility.GetLastRect();
            PlayServOverviewPresenter.DrawSdkVersionCard("0.6.2", Status, AvailableVersion, Status != "Checking for updates…");
            if (Event.current.type == EventType.Repaint)
            {
                Deployment = deployment;
                Version = GUILayoutUtility.GetLastRect();
                Measured = true;
            }
        }
    }
}
