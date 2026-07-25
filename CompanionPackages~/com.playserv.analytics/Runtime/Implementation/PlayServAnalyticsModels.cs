using System;
using System.Globalization;

namespace Playserv.Analytics
{
    public enum PlayServAnalyticsParameterType
    {
        String,
        Integer,
        Number,
        Boolean
    }

    [Serializable]
    public sealed class PlayServAnalyticsParameter
    {
        public string Key;
        public PlayServAnalyticsParameterType Type;
        public string StringValue;
        public long IntegerValue;
        public double NumberValue;
        public bool BooleanValue;

        private PlayServAnalyticsParameter()
        {
        }

        public static PlayServAnalyticsParameter String(string key, string value)
        {
            return new PlayServAnalyticsParameter
            {
                Key = ValidateKey(key),
                Type = PlayServAnalyticsParameterType.String,
                StringValue = value ?? string.Empty
            };
        }

        public static PlayServAnalyticsParameter Integer(string key, long value)
        {
            return new PlayServAnalyticsParameter
            {
                Key = ValidateKey(key),
                Type = PlayServAnalyticsParameterType.Integer,
                IntegerValue = value
            };
        }

        public static PlayServAnalyticsParameter Number(string key, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Analytics numbers must be finite.");

            return new PlayServAnalyticsParameter
            {
                Key = ValidateKey(key),
                Type = PlayServAnalyticsParameterType.Number,
                NumberValue = value
            };
        }

        public static PlayServAnalyticsParameter Boolean(string key, bool value)
        {
            return new PlayServAnalyticsParameter
            {
                Key = ValidateKey(key),
                Type = PlayServAnalyticsParameterType.Boolean,
                BooleanValue = value
            };
        }

        internal static PlayServAnalyticsParameter FromObject(string key, object value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value), $"Analytics parameter '{key}' cannot be null.");

            switch (value)
            {
                case string stringValue:
                    return String(key, stringValue);
                case bool booleanValue:
                    return Boolean(key, booleanValue);
                case byte byteValue:
                    return Integer(key, byteValue);
                case sbyte signedByteValue:
                    return Integer(key, signedByteValue);
                case short shortValue:
                    return Integer(key, shortValue);
                case ushort unsignedShortValue:
                    return Integer(key, unsignedShortValue);
                case int intValue:
                    return Integer(key, intValue);
                case uint unsignedIntValue:
                    return Integer(key, unsignedIntValue);
                case long longValue:
                    return Integer(key, longValue);
                case ulong unsignedLongValue when unsignedLongValue <= long.MaxValue:
                    return Integer(key, (long)unsignedLongValue);
                case float floatValue:
                    return Number(key, floatValue);
                case double doubleValue:
                    return Number(key, doubleValue);
                case decimal decimalValue:
                    return Number(key, Convert.ToDouble(decimalValue, CultureInfo.InvariantCulture));
                default:
                    throw new ArgumentException(
                        $"Analytics parameter '{key}' has unsupported type {value.GetType().FullName}.",
                        nameof(value));
            }
        }

        private static string ValidateKey(string key)
        {
            var normalized = (key ?? string.Empty).Trim();
            if (normalized.Length == 0)
                throw new ArgumentException("Analytics parameter key is required.", nameof(key));
            if (normalized.Length > 64)
                throw new ArgumentException("Analytics parameter key cannot exceed 64 characters.", nameof(key));

            return normalized;
        }
    }

    [Serializable]
    public sealed class PlayServAnalyticsUserProperty
    {
        public string Key;
        public string Value;

        public PlayServAnalyticsUserProperty(string key, string value)
        {
            Key = key ?? string.Empty;
            Value = value ?? string.Empty;
        }
    }

    [Serializable]
    public sealed class PlayServAnalyticsEvent
    {
        public string EventId;
        public string Name;
        public long TimestampUnixMilliseconds;
        public long Sequence;
        public string SessionId;
        public string UserId;
        public string SdkVersion;
        public string ApplicationVersion;
        public string Platform;
        public PlayServAnalyticsParameter[] Parameters;
        public PlayServAnalyticsUserProperty[] UserProperties;
    }

    [Serializable]
    public sealed class PlayServAnalyticsBatch
    {
        public string BatchId;
        public long SentAtUnixMilliseconds;
        public PlayServAnalyticsEvent[] Events;
    }
}
