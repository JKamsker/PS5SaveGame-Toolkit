using System.Globalization;

namespace SaveToolkit.SaveDiff;

/// <summary>Bad command line or unusable input; reported with exit code 2.</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>Tiny parser: <c>command positional... --option value --flag</c>. Options may repeat.</summary>
internal sealed class CliArguments
{
    private readonly Dictionary<string, List<string>> _options = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

    private CliArguments(string command) => Command = command;

    public string Command { get; }

    public List<string> Positionals { get; } = [];

    /// <param name="flags">Option names (without <c>--</c>) that take no value.</param>
    /// <param name="valued">Option names that take a value.</param>
    public static CliArguments Parse(IReadOnlyList<string> args, IReadOnlySet<string> flags, IReadOnlySet<string> valued)
    {
        if (args.Count == 0)
        {
            throw new UsageException("Missing command.");
        }

        var result = new CliArguments(args[0]);
        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                result.Positionals.Add(arg);
                continue;
            }

            var name = arg[2..];
            if (flags.Contains(name))
            {
                result._flags.Add(name);
            }
            else if (valued.Contains(name))
            {
                if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new UsageException($"Option --{name} needs a value.");
                }

                if (!result._options.TryGetValue(name, out var values))
                {
                    values = [];
                    result._options[name] = values;
                }

                values.Add(args[++i]);
            }
            else
            {
                throw new UsageException($"Unknown option --{name} for '{result.Command}'.");
            }
        }

        return result;
    }

    public bool Has(string flag) => _flags.Contains(flag);

    public string? Get(string name) => _options.TryGetValue(name, out var values) ? values[^1] : null;

    public IReadOnlyList<string> GetAll(string name) => _options.TryGetValue(name, out var values) ? values : [];

    public int GetInt(string name, int fallback, int min = 0)
    {
        if (Get(name) is not { } text)
        {
            return fallback;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min
            ? value
            : throw new UsageException($"--{name} must be an integer >= {min}.");
    }

    public void ExpectPositionals(int count, string usage)
    {
        if (Positionals.Count != count)
        {
            throw new UsageException($"Usage: {usage}");
        }
    }
}
