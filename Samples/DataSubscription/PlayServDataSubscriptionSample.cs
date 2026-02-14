using System;
using System.Threading.Tasks;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Exceptions;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Samples
{
    /// <summary>
    /// Example of SelectEntity + mutation + refresh flow.
    /// </summary>
    public sealed class PlayServDataSubscriptionSample : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private string playerId = "player-001";

        [Header("Mutations")]
        [SerializeField] private string renameTo = "RenamedPlayer";
        [SerializeField] private int levelToSet = 10;

        [Header("Behavior")]
        [SerializeField] private bool showOverlay = true;

        private ISharedEntity<SamplePlayerDto> _player;
        private IDisposable _playerDisposable;
        private string _status = "Not subscribed";
        private SamplePlayerDto _snapshot;

        [ContextMenu("Bind")]
        public void Bind()
        {
            _ = BindAsync();
        }

        [ContextMenu("Unbind")]
        public void Unbind()
        {
            UnbindInternal();
        }

        [ContextMenu("Rename")]
        public void Rename()
        {
            if (_player == null)
                return;

            _player.Update(dto => dto.Name = renameTo);
            _status = $"Rename requested: {renameTo}";
        }

        [ContextMenu("Add Level")]
        public void AddLevel()
        {
            if (_player == null)
                return;

            _player.Update(dto => dto.Level++);
            _status = "Add level requested";
        }

        [ContextMenu("Set Level Async")]
        public void SetLevelAsync()
        {
            _ = SetLevelInternalAsync();
        }

        [ContextMenu("Refresh")]
        public void Refresh()
        {
            _ = RefreshInternalAsync();
        }

        private async Task BindAsync()
        {
            if (_player != null)
            {
                _status = "Already bound";
                return;
            }

            try
            {
                _player = await PlayServ.SelectEntity<SamplePlayerEntity, SamplePlayerDto>(
                    playerId,
                    entity => new SamplePlayerDto
                    {
                        Id = entity?.Id ?? string.Empty,
                        Name = entity?.Name ?? string.Empty,
                        Level = entity?.Level ?? 0
                    });

                _player.Changed += OnPlayerChanged;
                _player.Error += OnPlayerError;
                _player.Terminated += OnPlayerTerminated;

                if (_player is IDisposable disposable)
                    _playerDisposable = disposable;

                _snapshot = _player.Value;
                _status = $"Bound to player: {playerId}";
            }
            catch (Exception ex)
            {
                _status = $"Bind error: {ex.Message}";
            }
        }

        private async Task SetLevelInternalAsync()
        {
            if (_player == null)
                return;

            try
            {
                await _player.UpdateAsync(dto => dto.Level = levelToSet);
                _status = $"Set level requested: {levelToSet}";
            }
            catch (Exception ex)
            {
                _status = $"Set level error: {ex.Message}";
            }
        }

        private async Task RefreshInternalAsync()
        {
            if (_player == null)
                return;

            try
            {
                await _player.RefreshAsync();
                _status = "Refresh requested";
            }
            catch (Exception ex)
            {
                _status = $"Refresh error: {ex.Message}";
            }
        }

        private void OnPlayerChanged(SamplePlayerDto dto)
        {
            _snapshot = dto;
            _status = "Player changed";
        }

        private void OnPlayerError(DataSubscriptionException ex)
        {
            _status = $"Subscription error [{ex.ErrorCode}]: {ex.Message}";
        }

        private void OnPlayerTerminated()
        {
            _status = "Subscription terminated by server";
            UnbindInternal();
        }

        private void UnbindInternal()
        {
            if (_player != null)
            {
                _player.Changed -= OnPlayerChanged;
                _player.Error -= OnPlayerError;
                _player.Terminated -= OnPlayerTerminated;
            }

            _playerDisposable?.Dispose();
            _playerDisposable = null;
            _player = null;
            _snapshot = null;
            _status = "Unbound";
        }

        private void OnDestroy()
        {
            UnbindInternal();
        }

        private void OnGUI()
        {
            if (!showOverlay)
                return;

            GUILayout.BeginArea(new Rect(540f, 10f, 420f, 260f), GUI.skin.box);
            GUILayout.Label("PlayServ DataSubscription Sample");
            GUILayout.Label($"State: {PlayServ.State}");
            GUILayout.Label($"Status: {_status}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Bind"))
                _ = BindAsync();
            if (GUILayout.Button("Unbind"))
                UnbindInternal();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rename"))
                Rename();
            if (GUILayout.Button("Add Level"))
                AddLevel();
            if (GUILayout.Button("Set Level Async"))
                _ = SetLevelInternalAsync();
            if (GUILayout.Button("Refresh"))
                _ = RefreshInternalAsync();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Current value:");
            if (_snapshot == null)
            {
                GUILayout.Label("- <null>");
            }
            else
            {
                GUILayout.Label($"- Id: {_snapshot.Id}");
                GUILayout.Label($"- Name: {_snapshot.Name}");
                GUILayout.Label($"- Level: {_snapshot.Level}");
            }
            GUILayout.EndArea();
        }
    }
}
