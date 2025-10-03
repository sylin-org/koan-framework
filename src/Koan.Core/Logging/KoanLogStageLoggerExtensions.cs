using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Koan.Core.Logging;

public static class KoanLogStageLoggerExtensions
{
    private static readonly EventId _stageEventId = new(4100, "KoanStage");

    public static void LogKoanStage(this ILogger logger, KoanLogStage stage, LogLevel level, string action, string? outcome = null, params (string Key, object? Value)[] context)
    {
        if (logger is null) throw new ArgumentNullException(nameof(logger));
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Action cannot be null or whitespace", nameof(action));

        var pairs = context?.Select(kvp => new KeyValuePair<string, object?>(kvp.Key, kvp.Value)).ToArray()
                    ?? Array.Empty<KeyValuePair<string, object?>>();

        var state = new KoanStageLogState(stage.GetCode(), stage.GetToken(), action, outcome, pairs);
        logger.Log(level, _stageEventId, state, null, static (s, _) => KoanStageLogState.Format(s));
    }

    public static void LogKoanStageInfo(this ILogger logger, KoanLogStage stage, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => logger.LogKoanStage(stage, LogLevel.Information, action, outcome, context);

    public static void LogKoanStageWarning(this ILogger logger, KoanLogStage stage, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => logger.LogKoanStage(stage, LogLevel.Warning, action, outcome, context);

    public static void LogKoanStageDebug(this ILogger logger, KoanLogStage stage, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => logger.LogKoanStage(stage, LogLevel.Debug, action, outcome, context);

    public static void LogKoanStageError(this ILogger logger, KoanLogStage stage, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => logger.LogKoanStage(stage, LogLevel.Error, action, outcome, context);

    private readonly struct KoanStageLogState : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly KeyValuePair<string, object?>[] _state;

        public KoanStageLogState(string stageCode, string stageToken, string action, string? outcome, IReadOnlyList<KeyValuePair<string, object?>> data)
        {
            StageCode = stageCode;
            StageToken = stageToken;
            Action = action;
            Outcome = outcome;
            Data = data ?? Array.Empty<KeyValuePair<string, object?>>();

            var list = new List<KeyValuePair<string, object?>>(Data.Count + 4)
            {
                new("KoanStage", StageCode),
                new("KoanStageToken", StageToken),
                new("KoanStageAction", Action)
            };

            if (!string.IsNullOrWhiteSpace(outcome))
            {
                list.Add(new("KoanStageOutcome", outcome));
            }

            foreach (var item in Data)
            {
                list.Add(item);
            }

            list.Add(new("{OriginalFormat}", "{StageCode}|{StageAction}: {Payload}"));
            _state = list.ToArray();
        }

        public string StageCode { get; }
        public string StageToken { get; }
        public string Action { get; }
        public string? Outcome { get; }
        public IReadOnlyList<KeyValuePair<string, object?>> Data { get; }

        public int Count => _state.Length;

        public KeyValuePair<string, object?> this[int index] => _state[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)_state).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _state.GetEnumerator();

        public static string Format(KoanStageLogState state)
        {
            var builder = new StringBuilder();
            builder.Append(state.StageCode);
            builder.Append('|');
            builder.Append(state.Action);
            builder.Append(':');

            if (!string.IsNullOrWhiteSpace(state.Outcome))
            {
                builder.Append(' ');
                builder.Append(state.Outcome);
            }

            foreach (var kvp in state.Data)
            {
                if (string.Equals(kvp.Key, "{OriginalFormat}", StringComparison.Ordinal))
                {
                    continue;
                }

                builder.Append(' ');
                builder.Append(kvp.Key);
                builder.Append('=');
                builder.Append(FormatValue(kvp.Value));
            }

            return builder.ToString();
        }

        private static string FormatValue(object? value) => value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            DateTime dt => dt.ToString("o"),
            DateTimeOffset dto => dto.ToString("o"),
            TimeSpan ts => ts.ToString(),
            _ => value?.ToString() ?? string.Empty
        };
    }
}
