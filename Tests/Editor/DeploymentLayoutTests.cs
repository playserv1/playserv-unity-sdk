using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Playserv.Editor.Tests
{
    public class DeploymentLayoutTests
    {
        [UnityTest] public IEnumerator RegistrationFormAndRefreshedListStayStableBetweenLayoutAndRepaint()
        {
            using (var panel = new ServerImagePanel())
            {
                yield return DrawTransition(panel.Draw, () =>
                {
                    Set(panel, "_showCreateServer", true);
                    Set(panel, "_session", new PlatformFunctionSession { Project = "tanks", Environment = "dev" });
                });
                yield return DrawTransition(panel.Draw, () =>
                    Get<ServerImageDraft>(panel, "_draft").CompleteConnection(new[] { "tank-room" }));
                yield return DrawTransition(panel.Draw, () => Set(panel, "_showCreateServer", false));
            }
        }
        [UnityTest] public IEnumerator FirstDockerLogBetweenLayoutAndRepaintDoesNotBreakDrawing()
        {
            using (var panel = new ServerImagePanel())
            {
                Call(panel, "Update");
                var pending = new TaskCompletionSource<Action>();
                var operation = Get<ServerImageOperation>(panel, "_operation");
                var run = operation.TryRunAsync(_ => pending.Task, e => Assert.Fail(e.ToString()));
                try
                {
                    yield return DrawTransition(panel.Draw, () =>
                        Task.Run(() => Call(panel, "AddLog", "Docker build output")).GetAwaiter().GetResult());
                }
                finally { operation.Cancel(); pending.TrySetResult(null); }
            }
        }

        [UnityTest] public IEnumerator GrowingAndClearingDockerLogDoesNotChangeCurrentLayout()
        {
            using (var panel = new ServerImagePanel())
            {
                var log = Get<Queue<string>>(panel, "_log");
                log.Enqueue("first");
                yield return DrawTransition(panel.Draw, () => log.Enqueue("second"));
                yield return DrawTransition(panel.Draw, () => log.Clear());
            }
        }

        [UnityTest] public IEnumerator ImageResultsBetweenLayoutAndRepaintDoNotBreakDrawing()
        {
            using (var panel = new ServerImagePanel())
            {
                yield return DrawTransition(panel.Draw, () => Set(panel, "_session", new PlatformFunctionSession { Project = "test", Environment = "dev" }));
                yield return DrawTransition(panel.Draw, () => Set(panel, "_built", new BuiltServerImage("sha256:test", "context", "Dockerfile")));
                yield return DrawTransition(panel.Draw, () => Set(panel, "_last", new ServerImagePublication { Server = "tank-room", Tag = "test" }));
                yield return DrawTransition(panel.Draw, () => Call(panel, "InvalidateBuild"));
            }
        }

        [UnityTest] public IEnumerator FunctionPreviewAndSessionBetweenLayoutAndRepaintDoNotBreakDrawing()
        {
            var temp = Path.GetTempPath();
            if (temp.StartsWith("/var/", StringComparison.Ordinal)) temp = "/private" + temp;
            var folder = Path.Combine(temp, "playserv-layout-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Function.cs"), "class Function {}");
            try
            {
                using (var panel = new PlatformFunctionPanel())
                {
                    yield return DrawTransition(panel.Draw, () => Set(panel, "_session", new PlatformFunctionSession { Project = "test", Environment = "dev" }));
                    yield return DrawTransition(panel.Draw, () => Set(panel, "_preview", PlatformFunctionPackage.Preview(folder)));
                    yield return DrawTransition(panel.Draw, () => Set(panel, "_last", new PlatformDeploymentReference { Project = "test", Environment = "dev", Id = "deployment" }));
                    yield return DrawTransition(panel.Draw, () => Set(panel, "_preview", null));
                }
            }
            finally { Directory.Delete(folder, true); }
        }

        [UnityTest] public IEnumerator CancelAndLateCompletionKeepTheCurrentLayoutAndDiscardResults()
        {
            using (var images = new ServerImagePanel())
            using (var functions = new PlatformFunctionPanel())
            {
                foreach (var panel in new object[] { images, functions })
                {
                    Call(panel, panel is ServerImagePanel ? "Update" : "CheckCredentials");
                    var operation = Get<ServerImageOperation>(panel, "_operation");
                    var pending = new TaskCompletionSource<Action>();
                    var applied = false;
                    var run = operation.TryRunAsync(_ => pending.Task, e => Assert.Fail(e.ToString()));
                    Action<Action> draw = panel is ServerImagePanel ? images.Draw : functions.Draw;
                    try
                    {
                        yield return DrawTransition(draw, () => { operation.Cancel(); pending.SetResult(() => applied = true); });
                        Assert.That(run.IsCompleted, Is.True);
                        Assert.That(applied, Is.False);
                    }
                    finally { operation.Cancel(); pending.TrySetResult(null); }
                }
            }
        }

        [UnityTest] public IEnumerator CancelClickAfterCompletionDoesNotReplaceTheResult()
        {
            using (var images = new ServerImagePanel())
            using (var functions = new PlatformFunctionPanel())
            {
                foreach (var panel in new object[] { images, functions })
                {
                    Call(panel, panel is ServerImagePanel ? "Update" : "CheckCredentials");
                    Set(panel, "_status", "Waiting");
                    var operation = Get<ServerImageOperation>(panel, "_operation");
                    var pending = new TaskCompletionSource<Action>();
                    var run = operation.TryRunAsync(_ => pending.Task, e => Assert.Fail(e.ToString()));
                    var window = ScriptableObject.CreateInstance<DeploymentLayoutTestWindow>();
                    try
                    {
                        window.DrawPanel = panel is ServerImagePanel ? images.Draw : functions.Draw;
                        window.ShowUtility(); window.position = new Rect(20, 20, 600, 900);
                        for (var i = 0; i < 30 && window.Repaints == 0; i++) { window.Repaint(); yield return null; }
                        Assert.That(window.Repaints, Is.GreaterThan(0));
                        // All editable controls are disabled while waiting. The cancellation
                        // button is the only enabled control that can capture this mouse down.
                        var point = Vector2.zero;
                        for (var y = 5; y < 900 && !window.MouseCaptured; y += 5)
                        {
                            point = new Vector2(300, y);
                            window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = point });
                            if (!window.MouseCaptured) window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = point });
                        }
                        Assert.That(window.MouseCaptured, Is.True, "The real cancellation button must receive the click.");
                        window.BeforeMouseUp = () => pending.SetResult(() => Set(panel, "_status", "Completed successfully."));
                        window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = point });
                        Assert.That(run.IsCompleted, Is.True);
                        Assert.That(Get<string>(panel, "_status"), Is.EqualTo("Completed successfully."));
                        LogAssert.NoUnexpectedReceived();
                    }
                    finally
                    {
                        operation.Cancel(); pending.TrySetResult(null);
                        window.DrawPanel = null; window.Close(); UnityEngine.Object.DestroyImmediate(window);
                    }
                }
            }
        }

        [UnityTest] public IEnumerator TargetChangeRejectsActionsFromTheVisibleOldLayout()
        {
            var config = ScriptableObject.CreateInstance<PlayServConfig>();
            try
            {
                using (var images = new ServerImagePanel { Config = () => config })
                using (var functions = new PlatformFunctionPanel { Config = () => config })
                {
                    foreach (var panel in new object[] { images, functions })
                    {
                        Action<Action> draw = panel is ServerImagePanel ? images.Draw : functions.Draw;
                        yield return DrawTransition(draw, () =>
                        {
                            var view = Get<object>(panel, "_layout");
                            var serialized = new SerializedObject(config);
                            serialized.FindProperty("dashboardAddress").stringValue = "https://" + Guid.NewGuid().ToString("N") + ".example";
                            serialized.ApplyModifiedPropertiesWithoutUndo();
                            Assert.That(Invoke<bool>(panel, "CanAct", view, false), Is.False);
                        });
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        }

        [UnityTest] public IEnumerator ClosingPanelDropsPendingCompletion()
        {
            foreach (var panel in new IDisposable[] { new ServerImagePanel(), new PlatformFunctionPanel() })
            {
                Call(panel, panel is ServerImagePanel ? "Update" : "CheckCredentials");
                var operation = Get<ServerImageOperation>(panel, "_operation");
                var pending = new TaskCompletionSource<Action>();
                var applied = false;
                var run = operation.TryRunAsync(_ => pending.Task, e => applied = true);
                try
                {
                    Action<Action> draw = panel is ServerImagePanel image ? image.Draw : ((PlatformFunctionPanel)panel).Draw;
                    yield return DrawTransition(draw, () => { panel.Dispose(); pending.SetResult(() => applied = true); });
                    Assert.That(run.IsCompleted, Is.True);
                    Assert.That(applied, Is.False);
                }
                finally { panel.Dispose(); pending.TrySetResult(null); }
            }
        }

        [UnityTest, Explicit("Requires a local Linux Docker daemon; builds locally and never pushes.")]
        public IEnumerator LocalDockerBuildRendersItsLiveLog()
        {
            var folder = Path.Combine(Application.temporaryCachePath, "layout-docker-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Dockerfile"), "FROM scratch\nCOPY payload /payload\n");
            File.WriteAllText(Path.Combine(folder, "payload"), Guid.NewGuid().ToString());
            var window = ScriptableObject.CreateInstance<DeploymentLayoutTestWindow>();
            using (var panel = new ServerImagePanel())
            using (var api = new PlatformFunctionClient("http://127.0.0.1", "sk_fixture"))
            {
                BuiltServerImage built = null;
                try
                {
                    Call(panel, "Update");
                    Set(panel, "_folder", folder); Set(panel, "_dockerfile", "Dockerfile");
                    Set(panel, "_session", new PlatformFunctionSession { Project = "local", Environment = "dev" });
                    Set(panel, "_publisher", new ServerImagePublisher(api, new ServerImageProcess(api.Redact, line => Call(panel, "AddLog", line))));
                    window.DrawPanel = panel.Draw; window.ShowUtility(); window.position = new Rect(20, 20, 420, 850);
                    Call(panel, "Start", "Building smoke image…", new Func<CancellationToken, Task<Action>>(ct => Invoke<Task<Action>>(panel, "BuildAsync", ct)));
                    var deadline = EditorApplication.timeSinceStartup + 90;
                    while (panel.Running && EditorApplication.timeSinceStartup < deadline)
                    {
                        window.Repaint(); yield return null;
                    }
                    Assert.That(panel.Running, Is.False, "Local Docker build timed out.");
                    built = Get<BuiltServerImage>(panel, "_built");
                    Assert.That(built, Is.Not.Null, Get<string>(panel, "_status"));
                    Assert.That(built.Id, Does.StartWith("sha256:"));
                    Assert.That(Get<Queue<string>>(panel, "_log").Count, Is.GreaterThan(0));
                    window.position = new Rect(20, 20, 1000, 850);
                    window.Repaint(); yield return null;
                    Assert.That(window.Repaints, Is.GreaterThan(0));
                    LogAssert.NoUnexpectedReceived();
                }
                finally
                {
                    window.DrawPanel = null; window.Close(); UnityEngine.Object.DestroyImmediate(window);
                    if (built != null) new ServerImageProcess(s => s).RunAsync(new[] { "image", "rm", built.Id }, null, null, null, TimeSpan.FromSeconds(30), default).GetAwaiter().GetResult();
                    Directory.Delete(folder, true);
                }
            }
        }

        private static IEnumerator DrawTransition(Action<Action> draw, Action transition)
        {
            var window = ScriptableObject.CreateInstance<DeploymentLayoutTestWindow>();
            try
            {
                window.DrawPanel = draw;
                window.ShowUtility();
                window.position = new Rect(20, 20, 420, 850);
                for (var i = 0; i < 30 && window.Repaints == 0; i++) { window.Repaint(); yield return null; }
                Assert.That(window.Repaints, Is.GreaterThan(0), "The real EditorWindow must render first.");
                window.AfterLayout = transition;
                var before = window.Repaints;
                for (var i = 0; i < 30 && (window.AfterLayout != null || window.Repaints == before); i++) { window.Repaint(); yield return null; }
                Assert.That(window.AfterLayout, Is.Null, "Transition must happen after real Layout.");
                Assert.That(window.Repaints, Is.GreaterThan(before));
                window.position = new Rect(20, 20, 1000, 850);
                window.Repaint(); yield return null;
                LogAssert.NoUnexpectedReceived();
            }
            finally { window.DrawPanel = null; window.Close(); UnityEngine.Object.DestroyImmediate(window); }
        }

        internal static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        internal static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        internal static void Call(object target, string name, params object[] args) => Invoke<object>(target, name, args);
        internal static T Invoke<T>(object target, string name, params object[] args) => (T)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }

    internal sealed class DeploymentLayoutTestWindow : EditorWindow
    {
        internal Action<Action> DrawPanel;
        internal Action AfterLayout;
        internal Action BeforeMouseUp;
        internal bool MouseCaptured;
        internal int Repaints;
        private void OnGUI()
        {
            var type = Event.current.type;
            if (type == EventType.MouseUp && BeforeMouseUp != null)
            { var action = BeforeMouseUp; BeforeMouseUp = null; action(); }
            DrawPanel?.Invoke(Repaint);
            if (type == EventType.MouseDown) MouseCaptured = GUIUtility.hotControl != 0;
            if (Event.current.type == EventType.Layout && AfterLayout != null)
            {
                var action = AfterLayout; AfterLayout = null; action();
            }
            if (Event.current.type == EventType.Repaint) Repaints++;
        }
    }
}
