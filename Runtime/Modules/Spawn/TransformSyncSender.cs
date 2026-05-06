using UnityEngine;

namespace Playserv.Spawn
{
    internal sealed class TransformSyncSender
    {
        private readonly NetworkObject _networkObject;
        private readonly Transform _transform;
        private readonly float _syncInterval;
        private float _nextSyncTime;
        private uint _sendSeq;
        private Vector3 _lastSentPosition;
        private Quaternion _lastSentRotation;
        private Vector3 _lastSentScale;
        private float _lastSendTime;

        public TransformSyncSender(NetworkObject networkObject, Transform transform, float syncInterval)
        {
            _networkObject = networkObject;
            _transform = transform;
            _syncInterval = syncInterval;
            ResetLocalState();
        }

        public void TrySend(NetworkTransformSyncSettings settings)
        {
            if (Time.time < _nextSyncTime)
                return;

            if (!HasTransformChanged(settings))
                return;

            SendSnapshot(settings, teleport: false, forceZeroVelocity: false);
            _nextSyncTime = Time.time + _syncInterval;
        }

        public void SendSnapshot(NetworkTransformSyncSettings settings, bool teleport, bool forceZeroVelocity)
        {
            var networkId = _networkObject.NetworkId;
            if (string.IsNullOrEmpty(networkId))
                return;

            var currentTime = Time.time;
            var stepTime = currentTime - _lastSendTime;
            if (stepTime <= 0f)
                stepTime = _syncInterval;

            var velocity = Vector3.zero;
            var angularVelocityYaw = 0f;
            var scaleVelocity = Vector3.zero;

            if (!forceZeroVelocity)
            {
                if (settings.SyncPosition)
                    velocity = (_transform.position - _lastSentPosition) / stepTime;

                if (settings.SyncRotation)
                    angularVelocityYaw = CalculateYawVelocity(_lastSentRotation, _transform.rotation, stepTime);

                if (settings.SyncScale)
                    scaleVelocity = (_transform.localScale - _lastSentScale) / stepTime;
            }

            _lastSentPosition = _transform.position;
            _lastSentRotation = _transform.rotation;
            _lastSentScale = _transform.localScale;
            _lastSendTime = currentTime;
            _sendSeq++;

            var syncEvent = new TransformSyncEvent(
                networkId,
                _sendSeq,
                settings.SyncPosition ? _lastSentPosition : Vector3.zero,
                settings.SyncRotation ? _lastSentRotation : Quaternion.identity,
                settings.SyncScale ? _lastSentScale : Vector3.one,
                velocity,
                angularVelocityYaw,
                scaleVelocity,
                stepTime,
                teleport,
                hasPosition: settings.SyncPosition,
                hasRotation: settings.SyncRotation,
                hasScale: settings.SyncScale)
            {
                OwnerId = _networkObject.OwnerId,
                ScopeGroupName = _networkObject.ScopeGroupName
            };

            PublishTransformSync(syncEvent);
        }

        public void Reset()
        {
            ResetLocalState();
            _nextSyncTime = 0f;
            _sendSeq = 0;
        }

        public void ResetLocalState()
        {
            _lastSentPosition = _transform.position;
            _lastSentRotation = _transform.rotation;
            _lastSentScale = _transform.localScale;
            _lastSendTime = Time.time;
        }

        private bool HasTransformChanged(NetworkTransformSyncSettings settings)
        {
            if (settings.SyncPosition &&
                Vector3.Distance(_transform.position, _lastSentPosition) > settings.PositionThreshold)
            {
                return true;
            }

            if (settings.SyncRotation &&
                Quaternion.Angle(_transform.rotation, _lastSentRotation) > settings.RotationThreshold)
            {
                return true;
            }

            if (settings.SyncScale &&
                Vector3.Distance(_transform.localScale, _lastSentScale) > settings.ScaleThreshold)
            {
                return true;
            }

            return false;
        }

        private void PublishTransformSync(TransformSyncEvent syncEvent)
        {
            var groupName = SpawnEventPublisher.NormalizeScopeGroupName(_networkObject.ScopeGroupName);
            if (!string.IsNullOrEmpty(groupName))
            {
                syncEvent.ScopeGroupName = groupName;
                PlayServSpawnRuntime.PublishForGroup(groupName, syncEvent);
                return;
            }

            syncEvent.ScopeGroupName = string.Empty;
            PlayServSpawnRuntime.Publish(syncEvent);
        }

        private static float CalculateYawVelocity(Quaternion from, Quaternion to, float deltaTime)
        {
            var fromYaw = from.eulerAngles.y;
            var toYaw = to.eulerAngles.y;

            var deltaYaw = Mathf.DeltaAngle(fromYaw, toYaw);
            return deltaYaw / deltaTime;
        }
    }
}
