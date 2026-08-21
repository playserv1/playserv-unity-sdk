using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Playserv.Schema;
using Playserv.Serialization;
using Playserv.Http.Interfaces;
using Playserv.Wrapper;

namespace Playserv.Data
{
    public enum PlayServQueryOperator
    {
        IsNull,
        IsNotNull,
        IsEmpty,
        IsNotEmpty,
        Eq,
        Neq,
        Contains,
        StartsWith,
        EndsWith,
        In,
        Nin,
        Gt,
        Gte,
        Lt,
        Lte,
        Between,
        CountEq,
        CountGt,
        CountGte,
        CountLt,
        CountLte,

        Equal = Eq,
        NotEqual = Neq,
        GreaterThan = Gt,
        GreaterThanOrEqual = Gte,
        LessThan = Lt,
        LessThanOrEqual = Lte
    }

    /// <summary>Execution plane that accepted or rejected a typed query.</summary>
    public enum PlayServQueryTarget
    {
        Rest,
        Realtime,
        KeyedEntitySubscription
    }

    public enum PlayServRecordConflictKind
    {
        Unknown,
        Unique,
        StaleVersion
    }

    public sealed class PlayServLoadOptions
    {
        public IReadOnlyList<string> Fields { get; set; }

        public IReadOnlyList<string> Expand { get; set; }

        internal bool IsPartial => Fields != null && Fields.Count > 0;
    }

    public sealed class PlayServPagination
    {
        public PlayServPagination(string cursor = null, int limit = 50)
        {
            if (limit < 1 || limit > 200)
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 200.");

            Cursor = cursor;
            Limit = limit;
        }

        public string Cursor { get; }

        public int Limit { get; }
    }

    public sealed class PlayServRecordPage<T>
    {
        internal PlayServRecordPage(
            IReadOnlyList<PlayServRecord<T>> records,
            string nextCursor,
            string previousCursor,
            bool hasMore,
            long? totalEstimate)
        {
            Records = records ?? Array.Empty<PlayServRecord<T>>();
            NextCursor = nextCursor;
            PreviousCursor = previousCursor;
            HasMore = hasMore;
            TotalEstimate = totalEstimate;
        }

        public IReadOnlyList<PlayServRecord<T>> Records { get; }

        public string NextCursor { get; }

        public string PreviousCursor { get; }

        public bool HasMore { get; }

        public long? TotalEstimate { get; }
    }

    public sealed class PlayServLoadOrCreateResult<T>
    {
        internal PlayServLoadOrCreateResult(PlayServRecord<T> record, bool wasCreated)
        {
            Record = record;
            WasCreated = wasCreated;
        }

        public PlayServRecord<T> Record { get; }

        public bool WasCreated { get; }
    }

    /// <summary>Cursor-loop result returned by <c>LoadAllAsync</c>.</summary>
    public sealed class PlayServLoadAllResult<T>
    {
        internal PlayServLoadAllResult(
            IReadOnlyList<PlayServRecord<T>> records,
            bool isTruncated,
            string nextCursor)
        {
            Records = records ?? Array.Empty<PlayServRecord<T>>();
            IsTruncated = isTruncated;
            NextCursor = nextCursor ?? string.Empty;
        }

        public IReadOnlyList<PlayServRecord<T>> Records { get; }

        /// <summary>Whether <c>maxRecords</c> stopped the cursor loop before the last page.</summary>
        public bool IsTruncated { get; }

        /// <summary>Cursor that can resume a truncated load; empty when all matching rows were loaded.</summary>
        public string NextCursor { get; }
    }

    /// <summary>Outcome for one item in a non-atomic client-side bulk operation.</summary>
    public sealed class PlayServBulkItemResult<T>
    {
        internal PlayServBulkItemResult(
            string key,
            T value,
            PlayServError error,
            Exception exception)
        {
            Key = key ?? string.Empty;
            Value = value;
            Error = error ?? PlayServError.None;
            Exception = exception;
        }

        public string Key { get; }

        public T Value { get; }

        public bool IsSuccess => !Error.IsError;

        public PlayServError Error { get; }

        /// <summary>
        /// Original typed failure, for example <see cref="PlayServRecordConflictException"/>.
        /// It is never logged by the SDK.
        /// </summary>
        public Exception Exception { get; }
    }

    /// <summary>Ordered results from a bounded, non-atomic client-side fan-out.</summary>
    public sealed class PlayServBulkResult<T>
    {
        internal PlayServBulkResult(IReadOnlyList<PlayServBulkItemResult<T>> items)
        {
            Items = items ?? Array.Empty<PlayServBulkItemResult<T>>();
        }

        public IReadOnlyList<PlayServBulkItemResult<T>> Items { get; }

        public bool IsSuccess => Items.All(item => item.IsSuccess);

        public int SucceededCount => Items.Count(item => item.IsSuccess);

        public int FailedCount => Items.Count - SucceededCount;
    }

    /// <summary>Required destructive intent for <c>DeleteAllAsync</c>.</summary>
    public enum PlayServDeleteAllConfirmation
    {
        MatchingRecords = 0,
        AllRecords = 1
    }

    public sealed class PlayServNaturalKey<T>
    {
        private PlayServNaturalKey(string field, object value)
        {
            Field = field;
            Value = value;
        }

        public string Field { get; }

        public object Value { get; }

        public static PlayServNaturalKey<T> For<TField>(
            Expression<Func<T, TField>> selector,
            TField value) =>
            new PlayServNaturalKey<T>(PlayServRecordWireNames.FromSelector(selector), value);

        public static PlayServNaturalKey<T> For(string field, object value)
        {
            if (string.IsNullOrWhiteSpace(field))
                throw new ArgumentException("Natural-key field is required.", nameof(field));
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            return new PlayServNaturalKey<T>(field.Trim(), value);
        }
    }

    public sealed class PlayServRecordQuery<T>
    {
        private readonly List<PlayServQueryFilter> _filters = new List<PlayServQueryFilter>();
        private readonly List<PlayServQuerySort> _sort = new List<PlayServQuerySort>();
        private readonly List<string> _hiddenColumns = new List<string>();
        private readonly List<string> _selectedFields = new List<string>();
        private readonly List<PlayServQueryInclude> _includes = new List<PlayServQueryInclude>();
        private readonly List<IReadOnlyList<PlayServQueryFilter>> _orGroups =
            new List<IReadOnlyList<PlayServQueryFilter>>();

        /// <summary>
        /// Adds comparisons joined with <c>&amp;&amp;</c>. Captured collection
        /// <c>Contains</c> calls become an <c>In</c> filter.
        /// </summary>
        public PlayServRecordQuery<T> Where(Expression<Func<T, bool>> predicate)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            _filters.AddRange(PlayServRecordExpressionParser.Parse(predicate));
            return this;
        }

        public PlayServRecordQuery<T> Where<TField>(
            Expression<Func<T, TField>> selector,
            PlayServQueryOperator op,
            object value = null,
            object value2 = null) =>
            Where(PlayServRecordWireNames.FromSelector(selector), op, value, value2);

        public PlayServRecordQuery<T> Where(
            string field,
            PlayServQueryOperator op,
            object value = null,
            object value2 = null)
        {
            if (string.IsNullOrWhiteSpace(field))
                throw new ArgumentException("Query field is required.", nameof(field));

            _filters.Add(new PlayServQueryFilter(field.Trim(), op, value, value2));
            return this;
        }

        /// <summary>Adds another AND predicate.</summary>
        public PlayServRecordQuery<T> And(Expression<Func<T, bool>> predicate) =>
            Where(predicate);

        public PlayServRecordQuery<T> And<TField>(
            Expression<Func<T, TField>> selector,
            PlayServQueryOperator op,
            object value = null,
            object value2 = null) =>
            Where(selector, op, value, value2);

        public PlayServRecordQuery<T> And(
            string field,
            PlayServQueryOperator op,
            object value = null,
            object value2 = null) =>
            Where(field, op, value, value2);

        /// <summary>
        /// Adds realtime-only alternatives. Every expression is an AND group, while ordinary
        /// <see cref="Where(System.Linq.Expressions.Expression{System.Func{T, bool}})"/> clauses
        /// remain common to every group.
        /// </summary>
        public PlayServRecordQuery<T> Or(
            params Expression<Func<T, bool>>[] alternatives)
        {
            if (alternatives == null)
                throw new ArgumentNullException(nameof(alternatives));
            if (alternatives.Length == 0)
                throw new ArgumentException("At least one OR alternative is required.", nameof(alternatives));
            if (_orGroups.Count + alternatives.Length > 8)
                throw new ArgumentException("Realtime queries support at most 8 OR groups.", nameof(alternatives));

            var parsedGroups = new List<IReadOnlyList<PlayServQueryFilter>>(alternatives.Length);
            foreach (var alternative in alternatives)
            {
                if (alternative == null)
                    throw new ArgumentException("OR alternatives cannot be null.", nameof(alternatives));

                var filters = PlayServRecordExpressionParser.Parse(alternative);
                if (filters.Count > 16)
                {
                    throw new ArgumentException(
                        "A realtime OR group supports at most 16 predicates.",
                        nameof(alternatives));
                }
                parsedGroups.Add(filters.ToArray());
            }

            _orGroups.AddRange(parsedGroups);

            return this;
        }

        public PlayServRecordQuery<T> OrderBy<TField>(Expression<Func<T, TField>> selector) =>
            OrderBy(PlayServRecordWireNames.FromSelector(selector));

        public PlayServRecordQuery<T> OrderBy(string field)
        {
            AddSort(field, false);
            return this;
        }

        public PlayServRecordQuery<T> OrderByDescending<TField>(Expression<Func<T, TField>> selector) =>
            OrderByDescending(PlayServRecordWireNames.FromSelector(selector));

        public PlayServRecordQuery<T> OrderByDescending(string field)
        {
            AddSort(field, true);
            return this;
        }

        public PlayServRecordQuery<T> Search(string text)
        {
            SearchText = string.IsNullOrWhiteSpace(text) ? null : text;
            return this;
        }

        public PlayServRecordQuery<T> Hide<TField>(Expression<Func<T, TField>> selector) =>
            Hide(PlayServRecordWireNames.FromSelector(selector));

        public PlayServRecordQuery<T> Hide(params string[] fields)
        {
            if (fields == null)
                throw new ArgumentNullException(nameof(fields));
            if (_selectedFields.Count > 0)
                throw new InvalidOperationException("SelectFields and Hide cannot be used in the same query.");

            foreach (var field in fields)
            {
                if (string.IsNullOrWhiteSpace(field))
                    throw new ArgumentException("Hidden-column names cannot be empty.", nameof(fields));
                if (!_hiddenColumns.Contains(field.Trim(), StringComparer.Ordinal))
                    _hiddenColumns.Add(field.Trim());
            }
            return this;
        }

        /// <summary>Selects top-level fields and produces partial record handles.</summary>
        public PlayServRecordQuery<T> SelectFields(
            params Expression<Func<T, object>>[] selectors)
        {
            if (selectors == null)
                throw new ArgumentNullException(nameof(selectors));
            if (_hiddenColumns.Count > 0)
                throw new InvalidOperationException("SelectFields and Hide cannot be used in the same query.");

            foreach (var selector in selectors)
                AddUnique(_selectedFields, PlayServRecordWireNames.FromSelector(selector), nameof(selectors));
            return this;
        }

        public PlayServRecordQuery<T> SelectFields(params string[] fields)
        {
            if (fields == null)
                throw new ArgumentNullException(nameof(fields));
            if (_hiddenColumns.Count > 0)
                throw new InvalidOperationException("SelectFields and Hide cannot be used in the same query.");

            foreach (var field in fields)
                AddUnique(_selectedFields, field, nameof(fields));
            return this;
        }

        /// <summary>
        /// Expands a relation path. Nested paths are supported by realtime subscriptions;
        /// REST queries continue to support direct relations only.
        /// </summary>
        public PlayServRecordQuery<T> Include<TField>(Expression<Func<T, TField>> selector)
        {
            AddInclude(PlayServRecordWireNames.FromSelectorPath(selector), nameof(selector));
            return this;
        }

        public PlayServRecordQuery<T> Include(params string[] fields)
        {
            if (fields == null)
                throw new ArgumentNullException(nameof(fields));

            foreach (var field in fields)
                AddInclude(ParseIncludePath(field, nameof(fields)), nameof(fields));
            return this;
        }

        public PlayServRecordQuery<T> WithCursor(string cursor)
        {
            Cursor = cursor;
            return this;
        }

        public PlayServRecordQuery<T> WithLimit(int limit)
        {
            if (limit < 1 || limit > 200)
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 200.");
            Limit = limit;
            return this;
        }

        internal IReadOnlyList<PlayServQueryFilter> Filters => _filters;

        internal IReadOnlyList<PlayServQuerySort> Sort => _sort;

        internal IReadOnlyList<string> HiddenColumns => _hiddenColumns;

        internal IReadOnlyList<string> SelectedFields => _selectedFields;

        internal IReadOnlyList<PlayServQueryInclude> Includes => _includes;

        internal IReadOnlyList<IReadOnlyList<PlayServQueryFilter>> OrGroups => _orGroups;

        internal string SearchText { get; private set; }

        internal string Cursor { get; private set; }

        internal int Limit { get; private set; } = 50;

        internal bool ProducesPartialRecords => _hiddenColumns.Count > 0 || _selectedFields.Count > 0;

        internal PlayServQuerySnapshot Snapshot(string cursor = null, int? limit = null) =>
            new PlayServQuerySnapshot(
                _filters.Select(filter => new PlayServQueryFilter(
                    filter.Field,
                    filter.Operator,
                    FreezeValue(filter.Value),
                    FreezeValue(filter.Value2))).ToArray(),
                _sort.ToArray(),
                _hiddenColumns.ToArray(),
                _selectedFields.ToArray(),
                _includes.Select(include => new PlayServQueryInclude(include.Segments)).ToArray(),
                _orGroups.Select(group => (IReadOnlyList<PlayServQueryFilter>)group.Select(filter =>
                    new PlayServQueryFilter(
                        filter.Field,
                        filter.Operator,
                        FreezeValue(filter.Value),
                        FreezeValue(filter.Value2))).ToArray()).ToArray(),
                SearchText,
                cursor ?? Cursor,
                limit ?? Limit);

        private static object FreezeValue(object value)
        {
            if (value == null || value is string || value is byte[])
                return value;
            if (!(value is IEnumerable enumerable))
                return value;

            var copy = new List<object>();
            foreach (var item in enumerable)
                copy.Add(item);
            return copy;
        }

        private void AddSort(string field, bool descending)
        {
            if (string.IsNullOrWhiteSpace(field))
                throw new ArgumentException("Sort field is required.", nameof(field));
            _sort.Add(new PlayServQuerySort(field.Trim(), descending));
        }

        private static void AddUnique(List<string> target, string field, string argumentName)
        {
            if (string.IsNullOrWhiteSpace(field))
                throw new ArgumentException("Field names cannot be empty.", argumentName);

            var normalized = field.Trim();
            if (normalized.IndexOf('.') >= 0 || normalized.IndexOf(',') >= 0)
                throw new ArgumentException("Projection and include fields must reference one top-level field.", argumentName);
            if (!target.Contains(normalized, StringComparer.Ordinal))
                target.Add(normalized);
        }

        private void AddInclude(IReadOnlyList<string> segments, string argumentName)
        {
            if (segments == null || segments.Count == 0)
                throw new ArgumentException("An include relation path is required.", argumentName);
            if (segments.Count > 6)
                throw new ArgumentException("Realtime includes support at most 6 relation levels.", argumentName);

            var include = new PlayServQueryInclude(segments);
            if (!_includes.Any(value => string.Equals(value.WirePath, include.WirePath, StringComparison.Ordinal)))
                _includes.Add(include);
        }

        private static IReadOnlyList<string> ParseIncludePath(string path, string argumentName)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Include paths cannot be empty.", argumentName);

            var segments = path.Split('.').Select(value => value.Trim()).ToArray();
            if (segments.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Include paths cannot contain empty segments.", argumentName);
            return segments;
        }
    }

    /// <summary>Reports query features unsupported by a selected execution plane.</summary>
    public sealed class PlayServQueryCapabilityException : InvalidOperationException
    {
        internal PlayServQueryCapabilityException(
            PlayServQueryTarget target,
            IEnumerable<string> unsupportedFeatures)
            : base(BuildMessage(target, unsupportedFeatures))
        {
            Target = target;
            UnsupportedFeatures = (unsupportedFeatures ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        public PlayServQueryTarget Target { get; }

        public IReadOnlyList<string> UnsupportedFeatures { get; }

        private static string BuildMessage(
            PlayServQueryTarget target,
            IEnumerable<string> unsupportedFeatures)
        {
            var features = (unsupportedFeatures ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var suffix = features.Length == 0 ? "unknown feature" : string.Join(", ", features);
            return $"The {target} query target does not support: {suffix}.";
        }
    }

    public class PlayServDataException : Exception
    {
        public PlayServDataException(
            string message,
            int statusCode = 0,
            string backendCode = null,
            bool isNetworkError = false,
            IReadOnlyDictionary<string, object> extensions = null,
            Exception innerException = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            BackendCode = backendCode ?? string.Empty;
            IsNetworkError = isNetworkError;
            Extensions = extensions ?? new Dictionary<string, object>();
            var http = innerException as PlayServRuntimeHttpException;
            var isTimeout = message?.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0;
            var backendRetryable = TryGetRetryable(Extensions);
            UnifiedError = PlayServError.FromHttp(
                statusCode,
                BackendCode,
                message,
                isNetworkError,
                isTimeout,
                http?.ResponseBody,
                backendRetryable);
        }

        public int StatusCode { get; }

        public string BackendCode { get; }

        public bool IsNetworkError { get; }

        public IReadOnlyDictionary<string, object> Extensions { get; }

        /// <summary>Cross-module representation of this data failure.</summary>
        public PlayServError UnifiedError { get; }

        private static bool? TryGetRetryable(IReadOnlyDictionary<string, object> extensions)
        {
            if (extensions == null)
                return null;
            foreach (var pair in extensions)
            {
                if (!string.Equals(pair.Key, "retryable", StringComparison.OrdinalIgnoreCase) || pair.Value == null)
                    continue;
                if (pair.Value is bool value)
                    return value;
                if (bool.TryParse(Convert.ToString(pair.Value), out value))
                    return value;
            }
            return null;
        }
    }

    /// <summary>
    /// Describes whether a runtime data authority flag is known and allowed for a subject.
    /// </summary>
    public enum PlayServDataCapabilityState
    {
        Unknown = 0,
        Allowed = 1,
        Denied = 2
    }

    /// <summary>Read and write authority advertised for one runtime data ACL subject.</summary>
    public sealed class PlayServDataSubjectCapabilities
    {
        internal PlayServDataSubjectCapabilities(
            PlayServDataCapabilityState read,
            PlayServDataCapabilityState write)
        {
            Read = read;
            Write = write;
        }

        public PlayServDataCapabilityState Read { get; }

        public PlayServDataCapabilityState Write { get; }

        public bool IsKnown =>
            Read != PlayServDataCapabilityState.Unknown &&
            Write != PlayServDataCapabilityState.Unknown;
    }

    /// <summary>
    /// Advisory table-level authority returned by the runtime table catalogue. The backend
    /// remains authoritative and can still reject an allowed operation because of row scope.
    /// </summary>
    public sealed class PlayServDataCapabilities
    {
        private static readonly PlayServDataSubjectCapabilities UnknownSubject =
            new PlayServDataSubjectCapabilities(
                PlayServDataCapabilityState.Unknown,
                PlayServDataCapabilityState.Unknown);

        internal PlayServDataCapabilities(
            PlayServDataSubjectCapabilities client,
            PlayServDataSubjectCapabilities server,
            PlayServDataSubjectCapabilities backend,
            string readPolicy)
        {
            Client = client ?? UnknownSubject;
            Server = server ?? UnknownSubject;
            Backend = backend ?? UnknownSubject;
            ReadPolicy = readPolicy ?? string.Empty;
        }

        /// <summary>Capabilities used by this Unity player/client SDK.</summary>
        public PlayServDataSubjectCapabilities Client { get; }

        /// <summary>Capabilities advertised for server-key callers.</summary>
        public PlayServDataSubjectCapabilities Server { get; }

        /// <summary>Capabilities advertised for project cloud functions.</summary>
        public PlayServDataSubjectCapabilities Backend { get; }

        /// <summary>Optional row read policy, for example <c>owner</c> or <c>public</c>.</summary>
        public string ReadPolicy { get; }

        /// <summary>Whether both client capability flags were present in the catalogue.</summary>
        public bool IsKnown => Client.IsKnown;

        public bool CanRead => Client.Read == PlayServDataCapabilityState.Allowed;

        public bool CanWrite => Client.Write == PlayServDataCapabilityState.Allowed;

        internal static PlayServDataCapabilities Unknown { get; } =
            new PlayServDataCapabilities(UnknownSubject, UnknownSubject, UnknownSubject, string.Empty);
    }

    public enum PlayServDataAccessOperation
    {
        Read = 0,
        Write = 1
    }

    /// <summary>
    /// Raised when the table catalogue or backend denies the requested runtime data authority.
    /// </summary>
    public sealed class PlayServDataAccessDeniedException : PlayServDataException
    {
        internal PlayServDataAccessDeniedException(
            string message,
            string entityId,
            PlayServDataAccessOperation operation,
            bool wasRejectedLocally,
            IReadOnlyDictionary<string, object> extensions = null,
            Exception innerException = null)
            : base(
                message,
                403,
                operation == PlayServDataAccessOperation.Read
                    ? "table_read_forbidden"
                    : "table_write_forbidden",
                false,
                MergeExtensions(extensions, entityId, operation, wasRejectedLocally),
                innerException)
        {
            EntityId = entityId ?? string.Empty;
            Operation = operation;
            WasRejectedLocally = wasRejectedLocally;
        }

        public string EntityId { get; }

        public PlayServDataAccessOperation Operation { get; }

        public bool WasRejectedLocally { get; }

        private static IReadOnlyDictionary<string, object> MergeExtensions(
            IReadOnlyDictionary<string, object> extensions,
            string entityId,
            PlayServDataAccessOperation operation,
            bool wasRejectedLocally)
        {
            var merged = extensions == null
                ? new Dictionary<string, object>(StringComparer.Ordinal)
                : new Dictionary<string, object>(extensions, StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(entityId) && !merged.ContainsKey("entity_id"))
                merged["entity_id"] = entityId;
            merged["operation"] = operation == PlayServDataAccessOperation.Read ? "read" : "write";
            merged["local_precheck"] = wasRejectedLocally;
            return merged;
        }
    }

    public sealed class PlayServRecordNotFoundException : PlayServDataException
    {
        internal PlayServRecordNotFoundException(
            string message,
            string backendCode,
            IReadOnlyDictionary<string, object> extensions,
            Exception innerException)
            : base(message, 404, backendCode, false, extensions, innerException)
        {
        }
    }

    public sealed class PlayServRecordConflictException : PlayServDataException
    {
        internal PlayServRecordConflictException(
            string message,
            int statusCode,
            string backendCode,
            PlayServRecordConflictKind kind,
            string existingRecordId,
            string field,
            IReadOnlyDictionary<string, object> extensions,
            Exception innerException)
            : base(message, statusCode, backendCode, false, extensions, innerException)
        {
            Kind = kind;
            ExistingRecordId = existingRecordId ?? string.Empty;
            Field = field ?? string.Empty;
        }

        public PlayServRecordConflictKind Kind { get; }

        public string ExistingRecordId { get; }

        public string Field { get; }
    }

    internal sealed class PlayServQueryFilter
    {
        public PlayServQueryFilter(string field, PlayServQueryOperator op, object value, object value2)
        {
            Field = field;
            Operator = op;
            Value = value;
            Value2 = value2;
        }

        public string Field { get; }

        public PlayServQueryOperator Operator { get; }

        public object Value { get; }

        public object Value2 { get; }
    }

    internal sealed class PlayServQuerySort
    {
        public PlayServQuerySort(string field, bool descending)
        {
            Field = field;
            Descending = descending;
        }

        public string Field { get; }

        public bool Descending { get; }
    }

    internal sealed class PlayServQuerySnapshot
    {
        public PlayServQuerySnapshot(
            IReadOnlyList<PlayServQueryFilter> filters,
            IReadOnlyList<PlayServQuerySort> sort,
            IReadOnlyList<string> hiddenColumns,
            IReadOnlyList<string> selectedFields,
            IReadOnlyList<PlayServQueryInclude> includes,
            IReadOnlyList<IReadOnlyList<PlayServQueryFilter>> orGroups,
            string searchText,
            string cursor,
            int limit)
        {
            Filters = filters ?? Array.Empty<PlayServQueryFilter>();
            Sort = sort ?? Array.Empty<PlayServQuerySort>();
            HiddenColumns = hiddenColumns ?? Array.Empty<string>();
            SelectedFields = selectedFields ?? Array.Empty<string>();
            Includes = includes ?? Array.Empty<PlayServQueryInclude>();
            OrGroups = orGroups ?? Array.Empty<IReadOnlyList<PlayServQueryFilter>>();
            SearchText = searchText;
            Cursor = cursor;
            Limit = limit;
        }

        public IReadOnlyList<PlayServQueryFilter> Filters { get; }

        public IReadOnlyList<PlayServQuerySort> Sort { get; }

        public IReadOnlyList<string> HiddenColumns { get; }

        public IReadOnlyList<string> SelectedFields { get; }

        public IReadOnlyList<PlayServQueryInclude> Includes { get; }

        public IReadOnlyList<IReadOnlyList<PlayServQueryFilter>> OrGroups { get; }

        public string SearchText { get; }

        public string Cursor { get; }

        public int Limit { get; }

        public bool ProducesPartialRecords => HiddenColumns.Count > 0 || SelectedFields.Count > 0;

        public bool RequiresHydration => SelectedFields.Count > 0 || Includes.Count > 0;
    }

    internal sealed class PlayServQueryInclude
    {
        public PlayServQueryInclude(IEnumerable<string> segments)
        {
            Segments = (segments ?? throw new ArgumentNullException(nameof(segments))).ToArray();
            WirePath = string.Join(".", Segments);
        }

        public IReadOnlyList<string> Segments { get; }

        public string WirePath { get; }

        public bool IsNested => Segments.Count > 1;
    }

    internal static class PlayServRecordWireNames
    {
        public static string FromSelector<T, TField>(Expression<Func<T, TField>> selector)
        {
            var path = FromSelectorPath(selector);
            if (path.Count != 1)
                throw new ArgumentException(
                    "Selector must directly reference one field or property, for example x => x.Code.",
                    nameof(selector));
            return path[0];
        }

        internal static IReadOnlyList<string> FromSelectorPath<T, TField>(
            Expression<Func<T, TField>> selector)
        {
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            Expression current = selector.Body;
            while (current is UnaryExpression unary &&
                   (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked))
            {
                current = unary.Operand;
            }

            var members = new List<MemberInfo>();
            while (current is MemberExpression member)
            {
                members.Add(member.Member);
                current = member.Expression;
                while (current is UnaryExpression ownerUnary &&
                       (ownerUnary.NodeType == ExpressionType.Convert || ownerUnary.NodeType == ExpressionType.ConvertChecked))
                {
                    current = ownerUnary.Operand;
                }
            }

            if (current != selector.Parameters[0] || members.Count == 0)
            {
                throw new ArgumentException(
                    "Selector must reference a field or relation path, for example x => x.Guild.Owner.",
                    nameof(selector));
            }

            members.Reverse();
            return members.Select(member => FromMember(member, nameof(selector))).ToArray();
        }

        internal static string FromMember(MemberInfo member, string argumentName = "member")
        {
            if (member == null)
                throw new ArgumentNullException(nameof(member));
            if (member.GetCustomAttribute<PlayServIgnoreAttribute>(true) != null)
                throw new ArgumentException($"Member '{member.Name}' is marked with PlayServIgnore.", argumentName);

            var jsonName = member.GetCustomAttribute<PlayServJsonNameAttribute>(true)?.Name;
            if (!string.IsNullOrWhiteSpace(jsonName))
                return jsonName;

            var schemaName = member.GetCustomAttribute<PlayServFieldAttribute>(true)?.Name;
            return string.IsNullOrWhiteSpace(schemaName) ? member.Name : schemaName;
        }

        internal static Type TryGetMemberType(Type modelType, string wireName)
        {
            if (modelType == null || string.IsNullOrWhiteSpace(wireName))
                return null;

            foreach (var member in modelType
                         .GetMembers(BindingFlags.Instance | BindingFlags.Public)
                         .Where(value => value is PropertyInfo || value is FieldInfo))
            {
                string mapped;
                try
                {
                    mapped = FromMember(member);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (!string.Equals(mapped, wireName, StringComparison.Ordinal))
                    continue;

                return member is PropertyInfo property
                    ? property.PropertyType
                    : ((FieldInfo)member).FieldType;
            }

            return null;
        }

        public static string OperatorToWire(PlayServQueryOperator value)
        {
            switch (value)
            {
                case PlayServQueryOperator.IsNull: return "is_null";
                case PlayServQueryOperator.IsNotNull: return "is_not_null";
                case PlayServQueryOperator.IsEmpty: return "is_empty";
                case PlayServQueryOperator.IsNotEmpty: return "is_not_empty";
                case PlayServQueryOperator.Eq: return "eq";
                case PlayServQueryOperator.Neq: return "neq";
                case PlayServQueryOperator.Contains: return "contains";
                case PlayServQueryOperator.StartsWith: return "starts_with";
                case PlayServQueryOperator.EndsWith: return "ends_with";
                case PlayServQueryOperator.In: return "in";
                case PlayServQueryOperator.Nin: return "nin";
                case PlayServQueryOperator.Gt: return "gt";
                case PlayServQueryOperator.Gte: return "gte";
                case PlayServQueryOperator.Lt: return "lt";
                case PlayServQueryOperator.Lte: return "lte";
                case PlayServQueryOperator.Between: return "between";
                case PlayServQueryOperator.CountEq: return "count_eq";
                case PlayServQueryOperator.CountGt: return "count_gt";
                case PlayServQueryOperator.CountGte: return "count_gte";
                case PlayServQueryOperator.CountLt: return "count_lt";
                case PlayServQueryOperator.CountLte: return "count_lte";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown query operator.");
            }
        }
    }
}
