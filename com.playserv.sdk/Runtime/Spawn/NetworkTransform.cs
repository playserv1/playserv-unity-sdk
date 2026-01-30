using System;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Spawn
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkTransform : MonoBehaviour
    {
        [field: SerializeField]
        public bool SyncPosition { get; set; } = true;

        [field: SerializeField]
        public bool SyncRotation { get; set; } = true;

        [field: SerializeField]
        public bool SyncScale { get; set; }

        [field: SerializeField]
        public bool UsePrediction { get; set; } = true;

        [SerializeField]
        private float positionThreshold = 0.001f;

        [SerializeField]
        private float rotationThreshold = 0.1f;

        [SerializeField]
        private float scaleThreshold = 0.001f;

        [SerializeField]
        private float interpolationSpeed = 10f;

        private NetworkObject _networkObject;
        private IDisposable _subscription;
        private float _nextSyncTime;
        private float _syncInterval;

        private Vector3 _lastSentPosition;
        private Quaternion _lastSentRotation;
        private Vector3 _lastSentScale;

        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        private Vector3 _previousScale;

        private Vector3 _velocity;
        private Vector3 _angularVelocity;
        private Vector3 _scaleVelocity;
        private float _lastDeltaTime;
        private bool _hadNonZeroVelocity;

        private Interpolator<Vector3> _positionInterpolator;
        private Interpolator<Quaternion> _rotationInterpolator;
        private Interpolator<Vector3> _scaleInterpolator;

        private TransformPredictor _predictor;

        private void Awake()
        {
            _networkObject = GetComponent<NetworkObject>();

            var config = Resources.Load<PlayServConfig>("PlayServConfig");
            _syncInterval = config != null ? config.NetworkTransformSyncIntervalMs / 1000f : 0.1f;

            _lastSentPosition = transform.position;
            _lastSentRotation = transform.rotation;
            _lastSentScale = transform.localScale;

            _previousPosition = transform.position;
            _previousRotation = transform.rotation;
            _previousScale = transform.localScale;

            _positionInterpolator = Interpolator<Vector3>.CreateVector3(transform.position);
            _rotationInterpolator = Interpolator<Quaternion>.CreateQuaternion(transform.rotation);
            _scaleInterpolator = Interpolator<Vector3>.CreateVector3(transform.localScale);

            _predictor = new TransformPredictor(interpolationSpeed);
        }

        private void OnEnable()
        {
            _subscription = PlayServ.Subscribe<TransformSyncEvent>(OnTransformSyncReceived);
        }

        private void OnDisable()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private void Update()
        {
            CalculateVelocity();
            TrySendTransformUpdate();
            InterpolateToTarget();
        }

        private void CalculateVelocity()
        {
            if (!_networkObject.IsLocallyOwned)
                return;

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
                return;

            _lastDeltaTime = deltaTime;
            _velocity = (transform.position - _previousPosition) / deltaTime;

            Quaternion deltaRotation = transform.rotation * Quaternion.Inverse(_previousRotation);
            deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            _angularVelocity = axis * (angle / deltaTime);

            _scaleVelocity = (transform.localScale - _previousScale) / deltaTime;

            _previousPosition = transform.position;
            _previousRotation = transform.rotation;
            _previousScale = transform.localScale;
        }

        private void TrySendTransformUpdate()
        {
            if (Time.time < _nextSyncTime)
                return;

            if (!ShouldSendUpdate())
                return;

            SendTransformUpdate();
            _nextSyncTime = Time.time + _syncInterval;
        }

        private bool ShouldSendUpdate()
        {
            bool hasChanged = HasTransformChanged();

            if (hasChanged)
            {
                _hadNonZeroVelocity = true;
                return true;
            }

            if (_hadNonZeroVelocity && HasNonZeroVelocity())
                return true;

            _hadNonZeroVelocity = false;
            return false;
        }

        private bool HasTransformChanged()
        {
            if (SyncPosition && Vector3.Distance(transform.position, _lastSentPosition) > positionThreshold)
                return true;

            if (SyncRotation && Quaternion.Angle(transform.rotation, _lastSentRotation) > rotationThreshold)
                return true;

            if (SyncScale && Vector3.Distance(transform.localScale, _lastSentScale) > scaleThreshold)
                return true;

            return false;
        }

        private bool HasNonZeroVelocity() =>
            (SyncPosition && _velocity.sqrMagnitude > 0.0001f) ||
            (SyncRotation && _angularVelocity.sqrMagnitude > 0.0001f) ||
            (SyncScale && _scaleVelocity.sqrMagnitude > 0.0001f);

        private void SendTransformUpdate()
        {
            var networkId = _networkObject.NetworkId;
            if (string.IsNullOrEmpty(networkId))
                return;

            _lastSentPosition = transform.position;
            _lastSentRotation = transform.rotation;
            _lastSentScale = transform.localScale;

            var syncEvent = new TransformSyncEvent(
                networkId,
                SyncPosition ? _lastSentPosition : Vector3.zero,
                SyncRotation ? _lastSentRotation : Quaternion.identity,
                SyncScale ? _lastSentScale : Vector3.one,
                SyncPosition ? _velocity : Vector3.zero,
                SyncRotation ? _angularVelocity : Vector3.zero,
                SyncScale ? _scaleVelocity : Vector3.zero,
                Time.time,
                _lastDeltaTime);

            PlayServ.Publish(syncEvent);
        }

        private void OnTransformSyncReceived(TransformSyncEvent syncEvent)
        {
            if (syncEvent.NetworkId != _networkObject.NetworkId)
                return;

            if (_networkObject.IsLocallyOwned)
                return;

            if (UsePrediction)
            {
                _predictor.ApplyServerState(syncEvent);
            }
            else
            {
                if (SyncPosition)
                    _positionInterpolator.SetTarget(syncEvent.Position);

                if (SyncRotation)
                    _rotationInterpolator.SetTarget(syncEvent.Rotation);

                if (SyncScale)
                    _scaleInterpolator.SetTarget(syncEvent.Scale);
            }
        }

        private void InterpolateToTarget()
        {
            if (_networkObject.IsLocallyOwned)
                return;

            if (UsePrediction)
            {
                _predictor.Update(Time.deltaTime, SyncPosition, SyncRotation, SyncScale);

                if (SyncPosition)
                    transform.position = _predictor.Position;

                if (SyncRotation)
                    transform.rotation = _predictor.Rotation;

                if (SyncScale)
                    transform.localScale = _predictor.Scale;
            }
            else
            {
                float speed = interpolationSpeed * Time.deltaTime;

                if (SyncPosition && _positionInterpolator.HasTarget)
                {
                    _positionInterpolator.Update(speed);
                    transform.position = _positionInterpolator.Current;
                }

                if (SyncRotation && _rotationInterpolator.HasTarget)
                {
                    _rotationInterpolator.Update(speed);
                    transform.rotation = _rotationInterpolator.Current;
                }

                if (SyncScale && _scaleInterpolator.HasTarget)
                {
                    _scaleInterpolator.Update(speed);
                    transform.localScale = _scaleInterpolator.Current;
                }
            }
        }
    }
}
