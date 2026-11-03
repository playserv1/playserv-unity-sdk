using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Playserv.Data;

namespace Playserv.DebugTerminal
{
    internal sealed class DebugTerminalRecordQueryOptions
    {
        internal string Nickname { get; private set; }
        internal int? MinimumLevel { get; private set; }
        internal string Search { get; private set; }
        internal string Sort { get; private set; }
        internal bool Descending { get; private set; }
        internal int Limit { get; private set; } = 50;
        internal string Cursor { get; private set; }
        internal string[] Fields { get; private set; } = Array.Empty<string>();
        internal List<DebugTerminalRecordOrGroup> OrGroups { get; } =
            new List<DebugTerminalRecordOrGroup>();
        internal List<string> Includes { get; } = new List<string>();

        internal static bool TryParse(
            IReadOnlyList<string> parts,
            int startIndex,
            out DebugTerminalRecordQueryOptions options,
            out string error)
        {
            options = new DebugTerminalRecordQueryOptions();
            error = string.Empty;

            for (var index = startIndex; index < parts.Count; index++)
            {
                var option = parts[index].ToLowerInvariant();
                switch (option)
                {
                    case "--desc":
                        options.Descending = true;
                        break;
                    case "--nickname":
                        if (!TryReadValue(parts, ref index, option, out var nickname, out error))
                            return false;
                        options.Nickname = nickname;
                        break;
                    case "--min-level":
                        if (!TryReadValue(parts, ref index, option, out var levelText, out error))
                            return false;
                        if (!int.TryParse(levelText, out var level))
                        {
                            error = "--min-level must be an integer.";
                            return false;
                        }
                        options.MinimumLevel = level;
                        break;
                    case "--search":
                        if (!TryReadValue(parts, ref index, option, out var search, out error))
                            return false;
                        options.Search = search;
                        break;
                    case "--sort":
                        if (!TryReadValue(parts, ref index, option, out var sort, out error))
                            return false;
                        sort = sort.ToLowerInvariant();
                        if (sort != "nickname" && sort != "level")
                        {
                            error = "--sort must be nickname or level.";
                            return false;
                        }
                        options.Sort = sort;
                        break;
                    case "--limit":
                        if (!TryReadValue(parts, ref index, option, out var limitText, out error))
                            return false;
                        if (!int.TryParse(limitText, out var limit) || limit < 1 || limit > 200)
                        {
                            error = "--limit must be between 1 and 200.";
                            return false;
                        }
                        options.Limit = limit;
                        break;
                    case "--cursor":
                        if (!TryReadValue(parts, ref index, option, out var cursor, out error))
                            return false;
                        options.Cursor = cursor;
                        break;
                    case "--fields":
                        if (!TryReadValue(parts, ref index, option, out var fieldsText, out error))
                            return false;
                        var fields = fieldsText
                            .Split(',')
                            .Select(field => field.Trim().ToLowerInvariant())
                            .Where(field => field.Length > 0)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray();
                        if (fields.Length == 0 || fields.Any(field => field != "nickname" && field != "level"))
                        {
                            error = "--fields accepts nickname, level, or nickname,level.";
                            return false;
                        }
                        options.Fields = fields;
                        break;
                    case "--or":
                        if (!TryReadValue(parts, ref index, option, out var groupJson, out error))
                            return false;
                        if (!TryParseOrGroup(groupJson, out var group, out error))
                            return false;
                        options.OrGroups.Add(group);
                        if (options.OrGroups.Count > 8)
                        {
                            error = "Realtime queries support at most 8 --or groups.";
                            return false;
                        }
                        break;
                    case "--include":
                        if (!TryReadValue(parts, ref index, option, out var include, out error))
                            return false;
                        include = include.Trim();
                        if (include.Length == 0)
                        {
                            error = "--include requires a non-empty relation path.";
                            return false;
                        }
                        options.Includes.Add(include);
                        break;
                    default:
                        error = $"Unknown record query option '{parts[index]}'.";
                        return false;
                }
            }

            return true;
        }

        internal PlayServRecordQuery<DebugTerminalPlayerDto> Build(string cursorOverride = null)
        {
            var query = new PlayServRecordQuery<DebugTerminalPlayerDto>();
            if (!string.IsNullOrWhiteSpace(Nickname))
                query.Where(player => player.Nickname, PlayServQueryOperator.Eq, Nickname);
            if (MinimumLevel.HasValue)
                query.Where(player => player.Level, PlayServQueryOperator.Gte, MinimumLevel.Value);
            if (!string.IsNullOrWhiteSpace(Search))
                query.Search(Search);
            if (!string.IsNullOrWhiteSpace(Sort))
            {
                var field = Sort == "nickname" ? nameof(DebugTerminalPlayerDto.Nickname) : nameof(DebugTerminalPlayerDto.Level);
                if (Descending)
                    query.OrderByDescending(field);
                else
                    query.OrderBy(field);
            }
            if (Fields.Length > 0)
            {
                query.SelectFields(Fields
                    .Select(field => field == "nickname"
                        ? nameof(DebugTerminalPlayerDto.Nickname)
                        : nameof(DebugTerminalPlayerDto.Level))
                    .ToArray());
            }
            if (OrGroups.Count > 0)
            {
                var alternatives = new List<Expression<Func<DebugTerminalPlayerDto, bool>>>(OrGroups.Count);
                foreach (var group in OrGroups)
                {
                    var nickname = group.Nickname;
                    var minimumLevel = group.MinimumLevel;
                    if (nickname != null && minimumLevel.HasValue)
                    {
                        var level = minimumLevel.Value;
                        alternatives.Add(player => player.Nickname == nickname && player.Level >= level);
                    }
                    else if (nickname != null)
                        alternatives.Add(player => player.Nickname == nickname);
                    else
                    {
                        var level = minimumLevel.GetValueOrDefault();
                        alternatives.Add(player => player.Level >= level);
                    }
                }
                query.Or(alternatives.ToArray());
            }
            if (Includes.Count > 0)
                query.Include(Includes.ToArray());

            var cursor = cursorOverride ?? Cursor;
            if (!string.IsNullOrWhiteSpace(cursor))
                query.WithCursor(cursor);
            return query.WithLimit(Limit);
        }

        private static bool TryParseOrGroup(
            string json,
            out DebugTerminalRecordOrGroup group,
            out string error)
        {
            group = null;
            if (!DebugTerminalArguments.TryParseJson(json, true, out var value, out error))
                return false;
            var source = (IDictionary<string, object>)value;
            string nickname = null;
            int? minimumLevel = null;
            foreach (var pair in source)
            {
                if (string.Equals(pair.Key, "nickname", StringComparison.OrdinalIgnoreCase))
                {
                    nickname = pair.Value?.ToString();
                    continue;
                }
                if (string.Equals(pair.Key, "minLevel", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.Key, "min_level", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryConvertInt(pair.Value, out var parsed))
                    {
                        error = "Realtime --or minLevel must be an integer.";
                        return false;
                    }
                    minimumLevel = parsed;
                    continue;
                }

                error = $"Unknown realtime --or field '{pair.Key}'. Use nickname or minLevel.";
                return false;
            }
            if (nickname == null && !minimumLevel.HasValue)
            {
                error = "Realtime --or group must contain nickname or minLevel.";
                return false;
            }

            group = new DebugTerminalRecordOrGroup(nickname, minimumLevel);
            return true;
        }

        private static bool TryConvertInt(object value, out int result)
        {
            switch (value)
            {
                case int integer:
                    result = integer;
                    return true;
                case long integer when integer >= int.MinValue && integer <= int.MaxValue:
                    result = (int)integer;
                    return true;
                case double number when Math.Abs(number % 1d) < double.Epsilon &&
                                        number >= int.MinValue && number <= int.MaxValue:
                    result = (int)number;
                    return true;
                default:
                    return int.TryParse(value?.ToString(), out result);
            }
        }

        private static bool TryReadValue(
            IReadOnlyList<string> parts,
            ref int index,
            string option,
            out string value,
            out string error)
        {
            if (index + 1 >= parts.Count || parts[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = string.Empty;
                error = $"{option} requires a value.";
                return false;
            }

            value = parts[++index];
            error = string.Empty;
            return true;
        }
    }

    internal sealed class DebugTerminalRecordOrGroup
    {
        internal DebugTerminalRecordOrGroup(string nickname, int? minimumLevel)
        {
            Nickname = nickname;
            MinimumLevel = minimumLevel;
        }

        internal string Nickname { get; }
        internal int? MinimumLevel { get; }
    }
}
