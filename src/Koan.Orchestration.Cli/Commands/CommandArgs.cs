using System.Collections.Generic;
using System.Linq;

namespace Koan.Orchestration.Cli.Commands;

internal sealed class CommandArgs
{
    private readonly Dictionary<string, List<string>> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _positionals = new();

    public CommandArgs(IEnumerable<string> tokens)
    {
        var list = tokens.ToArray();
        for (var i = 0; i < list.Length; i++)
        {
            var token = list[i];
            if (string.IsNullOrWhiteSpace(token)) continue;
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var (name, valueFromToken, hasValue) = SplitOption(token[2..]);
                if (hasValue)
                {
                    AddOption(name, valueFromToken);
                    continue;
                }

                if (i + 1 < list.Length && !IsSwitch(list[i + 1]))
                {
                    AddOption(name, list[i + 1]);
                    i++;
                }
                else
                {
                    _flags.Add(name);
                }
            }
            else if (token.StartsWith("-", StringComparison.Ordinal) && token.Length > 1)
            {
                if (token.Length == 2)
                {
                    _flags.Add(token);
                }
                else
                {
                    for (var j = 1; j < token.Length; j++)
                    {
                        _flags.Add("-" + token[j]);
                    }
                }
            }
            else
            {
                _positionals.Add(token);
            }
        }
    }

    public IReadOnlyList<string> Positionals => _positionals;

    public string? GetValue(string name)
        => _options.TryGetValue(name, out var list) && list.Count > 0 ? list[^1] : null;

    public IReadOnlyList<string> GetValues(string name)
        => _options.TryGetValue(name, out var list) ? list : Array.Empty<string>();

    public bool HasFlag(string name)
        => _flags.Contains(name) || _flags.Contains("--" + name) || _flags.Contains("-" + name.TrimStart('-'));

    private void AddOption(string name, string value)
    {
        if (!_options.TryGetValue(name, out var list))
        {
            list = new List<string>();
            _options[name] = list;
        }
        list.Add(value);
    }

    private static (string Name, string Value, bool HasValue) SplitOption(string token)
    {
        var idx = token.IndexOf('=');
        if (idx < 0) return (token, string.Empty, false);
        var name = token[..idx];
        var value = idx + 1 < token.Length ? token[(idx + 1)..] : string.Empty;
        return (name, value, true);
    }

    private static bool IsSwitch(string token)
        => token.StartsWith("-", StringComparison.Ordinal);
}

internal interface ICliCommand
{
    Task<int> ExecuteAsync(CommandArgs args);
}
