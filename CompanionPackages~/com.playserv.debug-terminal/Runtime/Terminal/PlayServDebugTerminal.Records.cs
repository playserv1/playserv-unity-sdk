using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Data;
using Playserv.DataSubscription;
using Playserv.DataSubscription.Exceptions;
using Playserv.Wrapper;

namespace Playserv.DebugTerminal
{
    public sealed partial class PlayServDebugTerminal
    {
        private const string DebugPlayerEntityId = "Player";

        private readonly Queue<PlayServError> _subscriptionErrors = new Queue<PlayServError>();
        private PlayServRecord<DebugTerminalPlayerDto> _activeRecord;
        private PlayServSingleton<DebugTerminalPlayerDto> _activeSingleton;
        private PlayServRecordPage<DebugTerminalPlayerDto> _lastRecordPage;
        private DebugTerminalRecordQueryOptions _lastRecordQueryOptions;
        private ISharedCollection<DebugTerminalPlayerDto> _recordSubscription;
        private IPlayServRecordSubscription<DebugTerminalPlayerDto> _recordWatch;
        private readonly List<PlayServRecord<DebugTerminalPlayerDto>> _recordBatch =
            new List<PlayServRecord<DebugTerminalPlayerDto>>();
        private string _lastRecordBatchSummary = "none";
        private string _lastRecordChangedFields = "none";
        private string _lastRecordConflict = "none";
        private string _lastRecordSynchronizationError = "none";
        private Func<PlayServRecordSet<DebugTerminalPlayerDto>> _recordSetProvider;
        private Func<bool, CancellationToken, Task<IReadOnlyList<PlayServDataTableInfo>>> _tableListProvider;
        private Func<string, bool, CancellationToken, Task<PlayServDataTableInfo>> _tableGetProvider;
        private string _recordCaller = "client";

        private PlayServRecordSet<DebugTerminalPlayerDto> DebugPlayerRecords =>
            _recordSetProvider?.Invoke() ??
            PlayServData.Records<DebugTerminalPlayerDto>(DebugPlayerEntityId);

        internal async Task SwitchRecordCallerAsync(
            string caller,
            Func<PlayServRecordSet<DebugTerminalPlayerDto>> recordSetProvider,
            Func<bool, CancellationToken, Task<IReadOnlyList<PlayServDataTableInfo>>> tableListProvider,
            Func<string, bool, CancellationToken, Task<PlayServDataTableInfo>> tableGetProvider)
        {
            await CloseRecordSubscriptionAsync();
            await CloseRecordWatchAsync(logAlreadyClosed: false);
            _activeRecord = null;
            _activeSingleton = null;
            _lastRecordPage = null;
            _lastRecordQueryOptions = null;
            _recordBatch.Clear();
            _lastRecordBatchSummary = "none";
            _recordSetProvider = recordSetProvider;
            _tableListProvider = tableListProvider;
            _tableGetProvider = tableGetProvider;
            _recordCaller = string.IsNullOrWhiteSpace(caller) ? "client" : caller;
            _status = $"Records caller switched to {_recordCaller}";
            AddLog(_status + "; previous handles and subscriptions were cleared.");
        }

        internal string RecordCaller => _recordCaller;

        private Task<IReadOnlyList<PlayServDataTableInfo>> GetTerminalTablesAsync(
            bool refresh,
            CancellationToken cancellationToken = default) =>
            _tableListProvider != null
                ? _tableListProvider(refresh, cancellationToken)
                : refresh
                    ? PlayServData.RefreshTablesAsync(cancellationToken)
                    : PlayServData.GetTablesAsync(cancellationToken);

        private async Task<PlayServDataTableInfo> GetTerminalTableAsync(
            string idOrName,
            bool refresh,
            CancellationToken cancellationToken = default)
        {
            if (_tableGetProvider != null)
                return await _tableGetProvider(idOrName, refresh, cancellationToken);
            if (refresh)
                await PlayServData.RefreshTablesAsync(cancellationToken);
            return await PlayServData.GetTableAsync(idOrName, cancellationToken);
        }

        private async Task ExecuteRecordCommandAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 1 ? parts[1].ToLowerInvariant() : "status";
            try
            {
                switch (operation)
                {
                    case "create":
                        await CreateRecordAsync(parts);
                        return;
                    case "load":
                        await LoadRecordAsync(parts);
                        return;
                    case "loadorcreate":
                        await LoadOrCreateRecordAsync(parts);
                        return;
                    case "query":
                        await QueryRecordsAsync(parts, 2, null);
                        return;
                    case "next":
                        await QueryNextRecordsPageAsync();
                        return;
                    case "subscribe":
                        await SubscribeRecordsAsync(parts);
                        return;
                    case "loadall":
                        await LoadAllRecordsAsync(parts);
                        return;
                    case "loadmany":
                        await LoadManyRecordsAsync(parts, populate: false);
                        return;
                    case "populate":
                        await PopulateRecordAsync(parts);
                        return;
                    case "populatemany":
                        await LoadManyRecordsAsync(parts, populate: true);
                        return;
                    case "deletebyid":
                        await DeleteRecordByIdAsync(parts);
                        return;
                    case "batch":
                        await ExecuteRecordBatchCommandAsync(parts);
                        return;
                    case "deleteall":
                        await DeleteAllRecordsAsync(parts);
                        return;
                    case "capabilities":
                        await PrintRecordCapabilitiesAsync(parts.Count > 2 &&
                            string.Equals(parts[2], "refresh", StringComparison.OrdinalIgnoreCase));
                        return;
                    case "watch":
                        await WatchActiveRecordAsync();
                        return;
                    case "unwatch":
                        await CloseRecordWatchAsync(logAlreadyClosed: true);
                        return;
                    case "save":
                        await SaveActiveRecordAsync(parts);
                        return;
                    case "reload":
                        await ReloadActiveRecordAsync();
                        return;
                    case "delete":
                        await DeleteActiveRecordAsync();
                        return;
                    case "singleton":
                        await LoadSingletonAsync();
                        return;
                    case "status":
                        PrintRecordStatus();
                        return;
                    case "close":
                        await CloseRecordStateAsync(clearHandles: true);
                        return;
                    default:
                        AddLog("Usage: record <create|load|loadorcreate|query|next|subscribe|loadall|loadmany|populate|populatemany|deletebyid|batch|deleteall|capabilities|watch|unwatch|save|reload|delete|singleton|status|close>");
                        return;
                }
            }
            catch (PlayServDataException ex)
            {
                _status = $"Record operation failed: {ex.UnifiedError.Code}";
                AddLog(FormatError(ex.UnifiedError));
            }
            catch (PlayServQueryCapabilityException ex)
            {
                _status = "Record query is not supported by realtime subscriptions";
                AddLog($"{_status}: {ex.Message}");
            }
        }

        private async Task CreateRecordAsync(IReadOnlyList<string> parts)
        {
            if (!TryReadPlayerValue(parts, out var player))
                return;

            var record = await DebugPlayerRecords.CreateAsync(player);
            await CloseRecordWatchAsync(logAlreadyClosed: false);
            _activeRecord = record;
            _activeSingleton = null;
            _status = $"Created Player record '{_activeRecord.Id}'";
            AddLog(_status);
            PrintRecord(_activeRecord);
        }

        private async Task LoadRecordAsync(IReadOnlyList<string> parts)
        {
            if (parts.Count < 3 || string.IsNullOrWhiteSpace(parts[2]))
            {
                AddLog("Usage: record load <recordId>");
                return;
            }

            var record = await DebugPlayerRecords.LoadAsync(parts[2]);
            await CloseRecordWatchAsync(logAlreadyClosed: false);
            _activeRecord = record;
            _activeSingleton = null;
            _status = $"Loaded Player record '{_activeRecord.Id}'";
            AddLog(_status);
            PrintRecord(_activeRecord);
        }

        private async Task LoadOrCreateRecordAsync(IReadOnlyList<string> parts)
        {
            if (!TryReadPlayerValue(parts, out var player))
                return;

            var result = await DebugPlayerRecords.LoadOrCreateAsync(
                PlayServNaturalKey<DebugTerminalPlayerDto>.For(
                    value => value.Nickname,
                    player.Nickname),
                () => player);
            await CloseRecordWatchAsync(logAlreadyClosed: false);
            _activeRecord = result.Record;
            _activeSingleton = null;
            _status = result.WasCreated
                ? $"Created Player record '{_activeRecord.Id}' by natural key"
                : $"Loaded existing Player record '{_activeRecord.Id}' by natural key";
            AddLog(_status);
            PrintRecord(_activeRecord);
        }

        private async Task QueryRecordsAsync(
            IReadOnlyList<string> parts,
            int optionsStart,
            string cursorOverride)
        {
            if (!DebugTerminalRecordQueryOptions.TryParse(
                    parts,
                    optionsStart,
                    out var options,
                    out var error))
            {
                AddLog(error);
                AddLog("Usage: record query [--nickname value] [--min-level n] [--search text] [--sort nickname|level] [--desc] [--limit n] [--cursor value] [--fields nickname,level] [--include path] [--or json]");
                return;
            }

            await QueryRecordsAsync(options, cursorOverride);
        }

        private async Task QueryRecordsAsync(
            DebugTerminalRecordQueryOptions options,
            string cursorOverride)
        {
            var page = await DebugPlayerRecords.QueryAsync(options.Build(cursorOverride));
            await CloseRecordWatchAsync(logAlreadyClosed: false);
            _lastRecordPage = page;
            _lastRecordQueryOptions = options;
            _activeRecord = _lastRecordPage.Records.FirstOrDefault();
            _activeSingleton = null;

            AddLog(
                $"Record page: count={_lastRecordPage.Records.Count}; hasMore={_lastRecordPage.HasMore}; " +
                $"next={_lastRecordPage.NextCursor ?? "-"}; previous={_lastRecordPage.PreviousCursor ?? "-"}; " +
                $"totalEstimate={_lastRecordPage.TotalEstimate?.ToString() ?? "-"}");
            foreach (var record in _lastRecordPage.Records)
                PrintRecord(record);
        }

        private async Task QueryNextRecordsPageAsync()
        {
            if (_lastRecordQueryOptions == null || string.IsNullOrWhiteSpace(_lastRecordPage?.NextCursor))
            {
                AddLog("No next records cursor. Run record query first.");
                return;
            }

            await QueryRecordsAsync(_lastRecordQueryOptions, _lastRecordPage.NextCursor);
        }

        private async Task SubscribeRecordsAsync(IReadOnlyList<string> parts)
        {
            if (!DebugTerminalRecordQueryOptions.TryParse(parts, 2, out var options, out var error))
            {
                AddLog(error);
                AddLog("Usage: record subscribe [record query options]");
                return;
            }

            await CloseRecordSubscriptionAsync();
            _recordSubscription = await DebugPlayerRecords.SubscribeAsync(options.Build());
            _recordSubscription.Changed += OnRecordCollectionChanged;
            _recordSubscription.Error += OnRecordCollectionError;
            _recordSubscription.Terminated += OnRecordCollectionTerminated;
            _recordSubscription.Failure += OnSubscriptionFailure;
            _status = "Typed Player record subscription active";
            AddLog($"{_status}; state={_recordSubscription.State}; count={_recordSubscription.Items.Count}");
        }

        private async Task PrintRecordCapabilitiesAsync(bool refresh)
        {
            var capabilities = refresh
                ? await DebugPlayerRecords.RefreshCapabilitiesAsync()
                : await DebugPlayerRecords.GetCapabilitiesAsync();
            AddLog(FormatCapabilities(capabilities, refresh));
        }

        internal static string FormatCapabilities(PlayServDataCapabilities capabilities, bool refreshed)
        {
            if (capabilities == null)
                return "Record capabilities: unavailable";
            return
                $"Record capabilities{(refreshed ? " (refreshed)" : string.Empty)}: known={capabilities.IsKnown}; " +
                $"readPolicy={capabilities.ReadPolicy}; " +
                $"client={capabilities.Client.Read}/{capabilities.Client.Write}; " +
                $"server={capabilities.Server.Read}/{capabilities.Server.Write}; " +
                $"backend={capabilities.Backend.Read}/{capabilities.Backend.Write}";
        }

        private async Task WatchActiveRecordAsync()
        {
            if (_activeRecord == null)
            {
                AddLog("No active record. Run record load/create/query first.");
                return;
            }
            if (_recordWatch != null)
            {
                AddLog($"Active record is already watched; state={_recordWatch.State}.");
                return;
            }

            var watch = await _activeRecord.SubscribeAsync();
            _recordWatch = watch;
            watch.Changed += OnRecordWatchChanged;
            watch.Conflict += OnRecordWatchConflict;
            watch.Error += OnRecordWatchError;
            watch.SynchronizationError += OnRecordWatchSynchronizationError;
            watch.Failure += OnSubscriptionFailure;
            watch.Terminated += OnRecordWatchTerminated;
            _lastRecordChangedFields = "none";
            _lastRecordConflict = "none";
            _lastRecordSynchronizationError = "none";
            AddLog($"Record watch active; id={_activeRecord.Id}; state={watch.State}; etag={_activeRecord.ETag}");
        }

        private async Task SaveActiveRecordAsync(IReadOnlyList<string> parts)
        {
            if (parts.Count < 4)
            {
                AddLog("Usage: record save <nickname|level> <value>");
                return;
            }

            var field = parts[2].ToLowerInvariant();
            var value = string.Join(" ", parts.Skip(3));
            if (_activeRecord == null && _activeSingleton == null)
            {
                AddLog("No active record or singleton. Run record load/query/singleton first.");
                return;
            }

            if (field == "nickname")
            {
                if (_activeRecord != null)
                    _activeRecord.Value.Nickname = value;
                else
                    _activeSingleton.Value.Nickname = value;
            }
            else if (field == "level")
            {
                if (!int.TryParse(value, out var level))
                {
                    AddLog("Record level must be an integer.");
                    return;
                }
                if (_activeRecord != null)
                    _activeRecord.Value.Level = level;
                else
                    _activeSingleton.Value.Level = level;
            }
            else
            {
                AddLog("Record field must be nickname or level.");
                return;
            }

            if (_activeRecord != null)
                await _activeRecord.SaveAsync();
            else
                await _activeSingleton.SaveAsync();
            _status = $"Saved active record field '{field}'";
            AddLog(_status);
            PrintRecordStatus();
        }

        private async Task ReloadActiveRecordAsync()
        {
            if (_activeRecord != null)
                await _activeRecord.ReloadAsync();
            else if (_activeSingleton != null)
                await _activeSingleton.ReloadAsync();
            else
            {
                AddLog("No active record or singleton to reload.");
                return;
            }

            _status = "Active record reloaded";
            AddLog(_status);
            PrintRecordStatus();
        }

        private async Task DeleteActiveRecordAsync()
        {
            if (_activeRecord == null)
            {
                AddLog("No active record to delete. Singleton records cannot be deleted by this command.");
                return;
            }

            await _activeRecord.DeleteAsync();
            _status = $"Deleted Player record '{_activeRecord.Id}'";
            AddLog(_status);
            PrintRecord(_activeRecord);
        }

        private async Task LoadSingletonAsync()
        {
            var singleton = await DebugPlayerRecords.GetSingletonAsync();
            await CloseRecordWatchAsync(logAlreadyClosed: false);
            _activeSingleton = singleton;
            _activeRecord = null;
            _status = "Loaded Player singleton";
            AddLog(_status);
            PrintSingleton(_activeSingleton);
        }

        private void PrintRecordStatus()
        {
            if (_activeRecord != null)
                PrintRecord(_activeRecord);
            else if (_activeSingleton != null)
                PrintSingleton(_activeSingleton);
            else
                AddLog("Active record: none");

            if (_lastRecordPage != null)
            {
                AddLog(
                    $"Last page: count={_lastRecordPage.Records.Count}; hasMore={_lastRecordPage.HasMore}; " +
                    $"next={_lastRecordPage.NextCursor ?? "-"}");
            }

            AddLog($"Record batch: count={_recordBatch.Count}; last={_lastRecordBatchSummary}");

            AddLog(_recordWatch == null
                ? "Record watch: none"
                : $"Record watch: state={_recordWatch.State}; etag={_activeRecord?.ETag}; " +
                  $"changed={_lastRecordChangedFields}; conflict={_lastRecordConflict}; " +
                  $"syncError={_lastRecordSynchronizationError}; deleted={_activeRecord?.IsDeleted}; " +
                  $"terminal={FormatError(_recordWatch.TerminalError)}");

            PrintSubscriptionStatus();
        }

        private async Task ExecuteSubscriptionCommandAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 1 ? parts[1].ToLowerInvariant() : "status";
            switch (operation)
            {
                case "status":
                    PrintSubscriptionStatus();
                    return;
                case "close":
                    await CloseSubscriptionsAsync(parts.Count > 2 ? parts[2] : "all");
                    return;
                case "refresh":
                    await RefreshSubscriptionsAsync(parts.Count > 2 ? parts[2] : "all");
                    return;
                case "errors":
                    PrintSubscriptionErrors();
                    return;
                default:
                    AddLog("Usage: subscription <status|refresh|close|errors> [legacy|records|record|all]");
                    return;
            }
        }

        private async Task RefreshSubscriptionsAsync(string target)
        {
            target = target?.ToLowerInvariant() ?? "all";
            if (target != "legacy" && target != "records" && target != "record" && target != "all")
            {
                AddLog("Usage: subscription refresh [legacy|records|record|all]");
                return;
            }

            var refreshed = 0;
            if (target == "legacy" || target == "all")
                refreshed += await RefreshSubscriptionAsync("Legacy", _player);
            if (target == "records" || target == "all")
                refreshed += await RefreshSubscriptionAsync("Records", _recordSubscription);
            if (target == "record" || target == "all")
                refreshed += await RefreshSubscriptionAsync("Record watch", _recordWatch);
            AddLog($"Subscription refresh completed; refreshed={refreshed}; target={target}");
        }

        private async Task<int> RefreshSubscriptionAsync(
            string label,
            IPlayServRefreshableSubscription subscription)
        {
            if (subscription == null)
            {
                AddLog($"{label} subscription: none");
                return 0;
            }
            try
            {
                await subscription.RefreshAsync();
                AddLog($"{label} subscription refreshed.");
                return 1;
            }
            catch (DataSubscriptionException exception)
            {
                AddLog($"{label} refresh failed: {FormatError(exception.UnifiedError)}");
                return 0;
            }
        }

        private void PrintSubscriptionStatus()
        {
            AddLog(_player == null
                ? "Legacy subscription: none"
                : $"Legacy subscription: state={_player.State}; player={_boundPlayerId}; backend={_activeBackend}; terminal={FormatError(_player.TerminalError)}");
            AddLog(_recordSubscription == null
                ? "Records subscription: none"
                : $"Records subscription: state={_recordSubscription.State}; count={_recordSubscription.Items.Count}; terminal={FormatError(_recordSubscription.TerminalError)}");
            AddLog(_recordWatch == null
                ? "Record watch: none"
                : $"Record watch: state={_recordWatch.State}; record={_recordWatch.Record.Id}; terminal={FormatError(_recordWatch.TerminalError)}");
        }

        private async Task CloseSubscriptionsAsync(string target)
        {
            target = target?.ToLowerInvariant() ?? "all";
            if (target != "legacy" && target != "records" && target != "record" && target != "all")
            {
                AddLog("Usage: subscription close [legacy|records|record|all]");
                return;
            }

            if (target == "legacy" || target == "all")
                await CloseLegacySubscriptionAsync();
            if (target == "records" || target == "all")
                await CloseRecordSubscriptionAsync();
            if (target == "record" || target == "all")
                await CloseRecordWatchAsync(logAlreadyClosed: true);
            if (target == "all")
            {
                CancelActiveCode(silent: true);
                CancelActiveMatch(silent: true);
            }
        }

        private async Task CloseLegacySubscriptionAsync()
        {
            if (_player == null)
            {
                AddLog("Legacy player subscription is already closed.");
                return;
            }

            DetachLegacySubscription();
            var handle = _player;
            PlayServSubscriptionCloseResult result;
            try
            {
                result = await handle.CloseAsync();
            }
            finally
            {
                ClearLegacySubscriptionState();
            }
            AddLog(result.IsSuccess
                ? $"Legacy subscription closed; alreadyClosed={result.WasAlreadyClosed}"
                : $"Legacy subscription close failed: {FormatError(result.Error)}");
        }

        private async Task CloseRecordSubscriptionAsync()
        {
            if (_recordSubscription == null)
                return;

            DetachRecordSubscription();
            var handle = _recordSubscription;
            PlayServSubscriptionCloseResult result;
            try
            {
                result = await handle.CloseAsync();
            }
            finally
            {
                _recordSubscription = null;
            }
            AddLog(result.IsSuccess
                ? $"Records subscription closed; alreadyClosed={result.WasAlreadyClosed}"
                : $"Records subscription close failed: {FormatError(result.Error)}");
        }

        private async Task CloseRecordStateAsync(bool clearHandles)
        {
            CancelActiveCode(silent: true);
            CancelActiveMatch(silent: true);
            await CloseRecordSubscriptionAsync();
            await CloseRecordWatchAsync(logAlreadyClosed: false);
            if (clearHandles)
            {
                _activeRecord = null;
                _activeSingleton = null;
                _lastRecordPage = null;
                _lastRecordQueryOptions = null;
                _recordBatch.Clear();
                _lastRecordBatchSummary = "none";
                AddLog("Record handles cleared.");
            }
        }

        private void PrintSubscriptionErrors()
        {
            if (_subscriptionErrors.Count == 0)
            {
                AddLog("No subscription errors captured.");
                return;
            }

            AddLog("Subscription errors:");
            foreach (var error in _subscriptionErrors)
                AddLog(FormatError(error));
        }

        private void OnRecordCollectionChanged(IReadOnlyList<DebugTerminalPlayerDto> items)
        {
            _status = $"Records subscription changed: {items?.Count ?? 0} item(s)";
            AddLog(_status);
        }

        private void OnRecordWatchChanged(PlayServRecordChange<DebugTerminalPlayerDto> change)
        {
            _lastRecordChangedFields = change?.ChangedFields == null || change.ChangedFields.Count == 0
                ? "none"
                : string.Join(",", change.ChangedFields);
            _status = $"Record watch changed: {_lastRecordChangedFields}";
            AddLog(
                $"Record watch changed; id={change?.Record.Id}; fields={_lastRecordChangedFields}; " +
                $"overwrotePending={change?.OverwrotePendingChanges}; etag={change?.Record.ETag}; " +
                $"deleted={change?.Record.IsDeleted}");
        }

        private void OnRecordWatchConflict(PlayServRecordRealtimeConflict<DebugTerminalPlayerDto> conflict)
        {
            var local = conflict?.LocalChangedFields == null ? "none" : string.Join(",", conflict.LocalChangedFields);
            var remote = conflict?.RemoteChangedFields == null ? "none" : string.Join(",", conflict.RemoteChangedFields);
            _lastRecordConflict = $"backend-wins local={local} remote={remote}";
            AddLog($"Record watch conflict: {_lastRecordConflict}");
        }

        private void OnRecordWatchError(DataSubscriptionException exception)
        {
            OnSubscriptionFailure(exception?.UnifiedError);
        }

        private void OnRecordWatchSynchronizationError(PlayServDataException exception)
        {
            _lastRecordSynchronizationError = FormatError(exception?.UnifiedError);
            AddLog($"Record watch synchronization error: {_lastRecordSynchronizationError}");
        }

        private void OnRecordWatchTerminated()
        {
            _status = $"Record watch terminated; deleted={_activeRecord?.IsDeleted}";
            AddLog(_status);
        }

        private void OnRecordCollectionError(DataSubscriptionException ex)
        {
            OnSubscriptionFailure(ex?.UnifiedError);
        }

        private void OnRecordCollectionTerminated()
        {
            _status = "Records subscription terminated";
            AddLog(_status);
        }

        private void OnSubscriptionFailure(PlayServError error)
        {
            if (error == null)
                return;
            _subscriptionErrors.Enqueue(error);
            while (_subscriptionErrors.Count > 20)
                _subscriptionErrors.Dequeue();
            AddLog($"Subscription failure: {FormatError(error)}");
        }

        private void DetachLegacySubscription()
        {
            if (_player == null)
                return;
            _player.Changed -= OnPlayerChanged;
            _player.Error -= OnPlayerError;
            _player.Terminated -= OnPlayerTerminated;
            _player.Failure -= OnSubscriptionFailure;
        }

        private void ClearLegacySubscriptionState()
        {
            _playerDisposable = null;
            _player = null;
            _boundPlayerId = null;
            _activeBackend = "none";
        }

        private void DetachRecordSubscription()
        {
            if (_recordSubscription == null)
                return;
            _recordSubscription.Changed -= OnRecordCollectionChanged;
            _recordSubscription.Error -= OnRecordCollectionError;
            _recordSubscription.Terminated -= OnRecordCollectionTerminated;
            _recordSubscription.Failure -= OnSubscriptionFailure;
        }

        private void DetachRecordWatch()
        {
            if (_recordWatch == null)
                return;
            _recordWatch.Changed -= OnRecordWatchChanged;
            _recordWatch.Conflict -= OnRecordWatchConflict;
            _recordWatch.Error -= OnRecordWatchError;
            _recordWatch.SynchronizationError -= OnRecordWatchSynchronizationError;
            _recordWatch.Failure -= OnSubscriptionFailure;
            _recordWatch.Terminated -= OnRecordWatchTerminated;
        }

        private async Task CloseRecordWatchAsync(bool logAlreadyClosed)
        {
            if (_recordWatch == null)
            {
                if (logAlreadyClosed)
                    AddLog("Record watch is already closed.");
                return;
            }

            DetachRecordWatch();
            var handle = _recordWatch;
            _recordWatch = null;
            var result = await handle.CloseAsync();
            AddLog(result.IsSuccess
                ? $"Record watch closed; alreadyClosed={result.WasAlreadyClosed}"
                : $"Record watch close failed: {FormatError(result.Error)}");
        }

        private bool TryReadPlayerValue(
            IReadOnlyList<string> parts,
            out DebugTerminalPlayerDto player)
        {
            player = null;
            if (parts.Count < 3 || string.IsNullOrWhiteSpace(parts[2]))
            {
                AddLog($"Usage: record {parts[1]} <nickname> [level]");
                return false;
            }

            var level = 1;
            if (parts.Count > 3 && !int.TryParse(parts[3], out level))
            {
                AddLog("Player level must be an integer.");
                return false;
            }

            player = new DebugTerminalPlayerDto
            {
                Nickname = parts[2],
                Level = level
            };
            return true;
        }

        private void PrintRecord(PlayServRecord<DebugTerminalPlayerDto> record)
        {
            if (record == null)
                return;
            AddLog(
                $"Record id={record.Id}; nickname='{record.Value?.Nickname}'; level={record.Value?.Level}; " +
                $"etag={record.ETag}; partial={record.IsPartial}; deleted={record.IsDeleted}; pending={record.HasPendingChanges}");
        }

        private void PrintSingleton(PlayServSingleton<DebugTerminalPlayerDto> singleton)
        {
            if (singleton == null)
                return;
            AddLog(
                $"Singleton entity={singleton.EntityId}; nickname='{singleton.Value?.Nickname}'; level={singleton.Value?.Level}; " +
                $"etag={singleton.ETag}; partial={singleton.IsPartial}; pending={singleton.HasPendingChanges}");
        }

        private void DisposeExtendedState()
        {
            DetachRecordSubscription();
            _recordSubscription?.Dispose();
            _recordSubscription = null;
            DetachRecordWatch();
            _recordWatch?.Dispose();
            _recordWatch = null;
            DisposeAllEventSubscriptions();
            CancelActiveRpc(silent: true);
            CancelActiveCode(silent: true);
            CancelActiveMatch(silent: true);
            CancelActivePlatform(silent: true);
            CloseCredentialPrompt();
            _commandExtension?.Dispose();
            _commandExtension = null;
            _recordSetProvider = null;
            _tableListProvider = null;
            _tableGetProvider = null;
            _recordCaller = "client";
        }
    }
}
