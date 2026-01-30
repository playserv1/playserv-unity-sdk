using UnityEngine;

namespace Playserv.Spawn
{
    public sealed class TransformPredictor
    {
        private Vector3 _velocity;
        private Vector3 _angularVelocity;
        private Vector3 _scaleVelocity;

        private Vector3 _serverPosition;
        private Quaternion _serverRotation;
        private Vector3 _serverScale;

        private float _lastServerTimestamp;
        private float _senderDeltaTime;
        private float _reconcileSpeed;
        private bool _hasServerData;

        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; }
        public Vector3 Scale { get; private set; }

        public TransformPredictor(float reconcileSpeed = 10f)
        {
            _reconcileSpeed = reconcileSpeed;
            Rotation = Quaternion.identity;
            _serverRotation = Quaternion.identity;
            Scale = Vector3.one;
            _serverScale = Vector3.one;
        }

        public void ApplyServerState(TransformSyncEvent syncEvent)
        {
            _serverPosition = syncEvent.Position;
            _serverRotation = syncEvent.Rotation;
            _serverScale = syncEvent.Scale;

            _velocity = syncEvent.Velocity;
            _angularVelocity = syncEvent.AngularVelocity;
            _scaleVelocity = syncEvent.ScaleVelocity;
            _lastServerTimestamp = syncEvent.Timestamp;
            _senderDeltaTime = syncEvent.DeltaTime;

            if (!_hasServerData)
            {
                Position = _serverPosition;
                Rotation = _serverRotation;
                Scale = _serverScale;
                _hasServerData = true;
            }
        }

        public void Update(float localDeltaTime, bool syncPosition, bool syncRotation, bool syncScale)
        {
            if (!_hasServerData || _senderDeltaTime <= 0f)
                return;

            if (syncPosition)
                UpdatePosition(localDeltaTime);

            if (syncRotation)
                UpdateRotation(localDeltaTime);

            if (syncScale)
                UpdateScale(localDeltaTime);
        }

        private void UpdatePosition(float localDeltaTime)
        {
            Vector3 movement = _velocity * _senderDeltaTime;
            Vector3 predictedPosition = Position + movement;
            Vector3 serverPredicted = _serverPosition + movement;
            Position = Vector3.Lerp(predictedPosition, serverPredicted, _reconcileSpeed * localDeltaTime);
        }

        private void UpdateRotation(float localDeltaTime)
        {
            Quaternion angularDelta = Quaternion.Euler(_angularVelocity * _senderDeltaTime);
            Quaternion predictedRotation = Rotation * angularDelta;
            Quaternion serverPredicted = _serverRotation * angularDelta;
            Rotation = Quaternion.Slerp(predictedRotation, serverPredicted, _reconcileSpeed * localDeltaTime);
        }

        private void UpdateScale(float localDeltaTime)
        {
            Vector3 scaleChange = _scaleVelocity * _senderDeltaTime;
            Vector3 predictedScale = Scale + scaleChange;
            Vector3 serverPredicted = _serverScale + scaleChange;
            Scale = Vector3.Lerp(predictedScale, serverPredicted, _reconcileSpeed * localDeltaTime);
        }

        public void Reset(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
            _serverPosition = position;
            _serverRotation = rotation;
            _serverScale = scale;
            _velocity = Vector3.zero;
            _angularVelocity = Vector3.zero;
            _scaleVelocity = Vector3.zero;
            _hasServerData = false;
        }
    }
}
