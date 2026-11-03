using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System;
using System.Threading.Tasks;
using Playserv.Code;
using Playserv.Matchmaking;
using Playserv.Data;
using Playserv.DataSubscription;
using NUnit.Framework;
using Playserv.Wrapper;
using UnityEngine;
using UnityEngine.TestTools;

namespace Playserv.DebugTerminal.Tests
{
    public sealed class DebugTerminalSecurityAndDiagnosticsTests
    {
        private GameObject _gameObject;
        private PlayServDebugTerminal _terminal;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("DebugTerminalTests");
            _terminal = _gameObject.AddComponent<PlayServDebugTerminal>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void CredentialValue_IsPrivateAndNeverSerialized()
        {
            var field = typeof(PlayServDebugTerminal).GetField(
                "_credentialValue",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(field);
            Assert.IsTrue(field.IsPrivate);
            Assert.IsNull(field.GetCustomAttribute<SerializeField>());
        }

        [Test]
        public void CredentialErrorSanitizer_RedactsCredentialFromExceptionText()
        {
            const string credential = "provider-secret";
            var sanitized = PlayServDebugTerminal.SanitizeCredentialError(
                $"Provider rejected {credential} and repeated {credential}",
                credential);

            Assert.AreEqual("Provider rejected [REDACTED] and repeated [REDACTED]", sanitized);
            StringAssert.DoesNotContain(credential, sanitized);
        }

        [UnityTest]
        public IEnumerator ClosingCredentialPopup_ClearsCredentialWithoutAddingItToHistoryOrLogs()
        {
            const string secret = "provider-secret-that-must-not-survive";
            SetField("_credentialValue", secret);
            SetEnumField("_credentialOperation", 2);

            Invoke("CloseCredentialPrompt");
            yield return null;

            Assert.AreEqual(string.Empty, GetField<string>("_credentialValue"));
            CollectionAssert.DoesNotContain(GetField<Queue<string>>("_history"), secret);
            Assert.IsFalse(GetField<List<string>>("_logs").Exists(line => line.Contains(secret)));
        }

        [Test]
        public void ErrorFormatter_IncludesStructuredMetadataButNeverRawDetails()
        {
            const string raw = "authorization=Bearer should-not-appear";
            var error = new PlayServError(
                PlayServErrorCode.Transport,
                "ws_closed",
                "Connection closed",
                httpStatus: 503,
                transportCode: 1006,
                retryable: true,
                rawDetails: raw);

            var formatted = PlayServDebugTerminal.FormatError(error);

            StringAssert.Contains("Transport", formatted);
            StringAssert.Contains("source=ws_closed", formatted);
            StringAssert.Contains("http=503", formatted);
            StringAssert.Contains("transport=1006", formatted);
            StringAssert.Contains("retryable=True", formatted);
            StringAssert.DoesNotContain(raw, formatted);
            StringAssert.DoesNotContain(error.RawDetails, formatted);
        }

        [Test]
        public void ErrorBuffer_IsBoundedToFiftyEntries()
        {
            for (var index = 0; index < 60; index++)
            {
                Invoke(
                    "OnPlayServError",
                    new PlayServError(PlayServErrorCode.Transport, $"code-{index}", "failure"));
            }

            Assert.AreEqual(50, GetField<Queue<PlayServError>>("_capturedErrors").Count);
        }

        [Test]
        public void RpcTimeout_ValidatesRangeAndCancelSignalsActiveInvocation()
        {
            Invoke("SetRpcTimeout", DebugTerminalCommandParser.Tokenize("rpc timeout 4500"));
            Assert.AreEqual(4500, GetField<int>("_rpcTimeoutMs"));

            Invoke("SetRpcTimeout", DebugTerminalCommandParser.Tokenize("rpc timeout 99"));
            Assert.AreEqual(4500, GetField<int>("_rpcTimeoutMs"));

            var cancellation = new CancellationTokenSource();
            SetField("_rpcCancellation", cancellation);
            SetField("_activeRpcRequestId", "request-1");
            Invoke("CancelActiveRpc", false);

            Assert.IsTrue(cancellation.IsCancellationRequested);
            cancellation.Dispose();
            SetField("_rpcCancellation", null);
        }

        [Test]
        public void CodeAndMatchmakingCancellation_SignalActiveOperations()
        {
            var codeCancellation = new CancellationTokenSource();
            SetField("_codeCancellation", codeCancellation);
            SetField("_activeCodeSummary", "POST echo");
            Invoke("CancelActiveCode", false);
            Assert.IsTrue(codeCancellation.IsCancellationRequested);

            var matchCancellation = new CancellationTokenSource();
            SetField("_matchCancellation", matchCancellation);
            SetField("_activeMatchSummary", "join ranked");
            Invoke("CancelActiveMatch", false);
            Assert.IsTrue(matchCancellation.IsCancellationRequested);

            codeCancellation.Dispose();
            matchCancellation.Dispose();
            SetField("_codeCancellation", null);
            SetField("_matchCancellation", null);
        }

        [Test]
        public void RecordClose_CancelsCodeAndMatchmakingOperations()
        {
            var codeCancellation = new CancellationTokenSource();
            var matchCancellation = new CancellationTokenSource();
            SetField("_codeCancellation", codeCancellation);
            SetField("_matchCancellation", matchCancellation);

            ((Task)InvokeWithResult("CloseRecordStateAsync", true)).GetAwaiter().GetResult();

            Assert.IsTrue(codeCancellation.IsCancellationRequested);
            Assert.IsTrue(matchCancellation.IsCancellationRequested);
            codeCancellation.Dispose();
            matchCancellation.Dispose();
            SetField("_codeCancellation", null);
            SetField("_matchCancellation", null);
        }

        [Test]
        public void MatchReservationFormatter_NeverPrintsReservationToken()
        {
            const string token = "reservation-secret-that-must-not-be-logged";
            var reservation = (PlayServMatchReservation)Activator.CreateInstance(
                typeof(PlayServMatchReservation),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: new object[] { "room-1", token, DateTimeOffset.UtcNow },
                culture: null);

            var formatted = PlayServDebugTerminal.FormatMatchReservation(reservation);

            StringAssert.Contains("room-1", formatted);
            StringAssert.DoesNotContain(token, formatted);
            StringAssert.DoesNotContain("ReservationToken", formatted);
        }

        [Test]
        public void FunctionResponseFormatter_BoundsBodyAndOmitsHeaders()
        {
            const string secretHeader = "header-secret-that-must-not-be-logged";
            var response = (PlayServFunctionResponse)Activator.CreateInstance(
                typeof(PlayServFunctionResponse),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: new object[]
                {
                    200,
                    new string('x', 3000),
                    "application/json",
                    new Dictionary<string, string> { ["X-Secret"] = secretHeader },
                    null
                },
                culture: null);

            var formatted = PlayServDebugTerminal.FormatFunctionResponse("POST echo", response);

            Assert.Less(formatted.Length, 2200);
            StringAssert.Contains("http=200", formatted);
            StringAssert.DoesNotContain(secretHeader, formatted);
            StringAssert.DoesNotContain("X-Secret", formatted);
        }

        [Test]
        public void BinaryFunctionFormatter_UsesOnlyLengthAndHash()
        {
            var bytes = new byte[] { 0, 1, 2, 3, 255 };
            var response = (PlayServFunctionResponse)Activator.CreateInstance(
                typeof(PlayServFunctionResponse),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: new object[]
                {
                    200,
                    "secret-text-body",
                    "application/octet-stream",
                    new Dictionary<string, string> { ["X-Secret"] = "secret-header" },
                    bytes
                },
                culture: null);

            var formatted = PlayServDebugTerminal.FormatBinaryFunctionResponse("GET binary", response);

            StringAssert.Contains("bytes=5", formatted);
            StringAssert.Contains("sha256=", formatted);
            StringAssert.DoesNotContain("secret-text-body", formatted);
            StringAssert.DoesNotContain("secret-header", formatted);
            StringAssert.DoesNotContain("X-Secret", formatted);
        }

        [Test]
        public void SubscriptionRefresh_WithNoHandlesDoesNotCreateSubscriptions()
        {
            ((Task)InvokeWithResult("RefreshSubscriptionsAsync", "all")).GetAwaiter().GetResult();

            Assert.IsNull(GetField<ISharedCollection<DebugTerminalPlayerDto>>("_recordSubscription"));
            Assert.IsNull(GetField<IPlayServRecordSubscription<DebugTerminalPlayerDto>>("_recordWatch"));
        }

        private void Invoke(string methodName, params object[] parameters)
        {
            var method = typeof(PlayServDebugTerminal).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, methodName);
            method.Invoke(_terminal, parameters);
        }

        private object InvokeWithResult(string methodName, params object[] parameters)
        {
            var method = typeof(PlayServDebugTerminal).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, methodName);
            return method.Invoke(_terminal, parameters);
        }

        private T GetField<T>(string fieldName)
        {
            var field = typeof(PlayServDebugTerminal).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, fieldName);
            return (T)field.GetValue(_terminal);
        }

        private void SetField(string fieldName, object value)
        {
            var field = typeof(PlayServDebugTerminal).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, fieldName);
            field.SetValue(_terminal, value);
        }

        private void SetEnumField(string fieldName, int value)
        {
            var field = typeof(PlayServDebugTerminal).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, fieldName);
            field.SetValue(_terminal, System.Enum.ToObject(field.FieldType, value));
        }
    }
}
