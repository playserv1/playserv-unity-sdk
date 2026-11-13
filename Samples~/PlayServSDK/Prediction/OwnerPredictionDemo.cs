using System.Collections.Generic;
using Playserv.Prediction;
using UnityEngine;

namespace Playserv.Samples.Prediction
{
    /// <summary>Add to an empty scene. The local server fixture delays both directions by half the RTT.</summary>
    public sealed class OwnerPredictionDemo : MonoBehaviour
    {
        [Range(0, 1000)] public int RoundTripMilliseconds = 200;
        private const float StepSeconds = 1f / 30f;
        private PlayServPrediction<Vector3, Vector3> _prediction;
        private readonly PlayServPredictionSmoothing _smoothing = new PlayServPredictionSmoothing();
        private readonly Queue<(float at, PredictionInput<Vector3> command)> _outbound = new Queue<(float, PredictionInput<Vector3>)>();
        private readonly Queue<(float at, long generation, long sequence, Vector3 state)> _inbound = new Queue<(float, long, long, Vector3)>();
        private Vector3 _server;
        private Transform _view;
        private float _accumulator;
        private float _nextCorrection;
        private float _lastNow;
        private bool _full;

        private void Start()
        {
            _prediction = new PlayServPrediction<Vector3, Vector3>(Simulate, s => s, i => i);
            _prediction.Reset(Vector3.zero, 0);
            _view = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            _view.name = "Predicted owner";
            if (Camera.main == null)
            {
                var camera = new GameObject("Prediction camera").AddComponent<Camera>();
                camera.tag = "MainCamera"; camera.orthographic = true; camera.orthographicSize = 6;
                camera.transform.position = new Vector3(0, 0, -10);
            }
            _lastNow = Time.unscaledTime;
        }

        private static Vector3 Simulate(Vector3 state, Vector3 input, PredictionSimulationContext context) =>
            state + input * (4f * context.DeltaTime);

        private void Update()
        {
            if (_prediction == null) return;
            var now = Time.unscaledTime;
            var dt = Mathf.Min(.1f, now - _lastNow); _lastNow = now;
            var halfRtt = RoundTripMilliseconds / 2000f;
            while (_outbound.Count > 0 && _outbound.Peek().at <= now)
            {
                var pending = _outbound.Dequeue(); var c = pending.command;
                _server += c.Input * (4f * c.DeltaTime);
                // An authoritative nudge demonstrates correction without trusting client positions.
                if (now >= _nextCorrection) { _server.y -= .05f; _nextCorrection = now + 2; }
                _inbound.Enqueue((now + halfRtt, c.Generation, c.Sequence, _server));
            }
            while (_inbound.Count > 0 && _inbound.Peek().at <= now)
            {
                var r = _inbound.Dequeue(); var before = _prediction.State;
                if (_prediction.Reconcile(r.generation, r.sequence, r.sequence, r.state) == PredictionReconcileResult.Applied)
                {
                    _smoothing.Apply(before, Quaternion.identity, 0, out var shown, out var rotation);
                    _smoothing.Correct(shown, rotation, _prediction.State, Quaternion.identity);
                }
            }
            _accumulator += dt;
            var input = new Vector3((Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0),
                (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0), 0).normalized;
            while (_accumulator >= StepSeconds)
            {
                _accumulator -= StepSeconds;
                _full = !_prediction.TryPredict(input, StepSeconds, out var command);
                if (!_full) _outbound.Enqueue((now + halfRtt, command)); // Send/effects once, outside Simulate.
            }
            _smoothing.Apply(_prediction.State, Quaternion.identity, dt, out var position, out var orientation);
            _view.SetPositionAndRotation(position, orientation);
        }

        private void OnGUI()
        {
            if (_prediction == null) return;
            GUILayout.Label("Owner prediction: arrow keys move immediately. Local fixture, no platform connection.");
            GUILayout.Label($"RTT {RoundTripMilliseconds} ms | pending {_prediction.PendingInputCount} | ack {_prediction.LastAcknowledgedSequence}");
            if (_full) GUILayout.Label("History full: waiting for acknowledgement.");
            if (GUILayout.Button("Respawn / new baseline"))
            {
                _outbound.Clear(); _inbound.Clear(); _server = Vector3.zero; _accumulator = 0;
                _prediction.Reset(_server, 0); _smoothing.Reset();
            }
        }

        private void OnDestroy() { if (_view != null) Destroy(_view.gameObject); }
    }
}
