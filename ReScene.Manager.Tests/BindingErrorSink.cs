using System.Globalization;
using Avalonia.Logging;

namespace ReScene.Manager.Tests;

/// <summary>
/// Reusable test helper that captures Avalonia binding warnings/errors. Installs itself as
/// <see cref="Logger.Sink"/> for its lifetime (wrapping and restoring any previous sink) and records
/// every message logged to <see cref="LogArea.Binding"/> at or above a minimum level (Warning by
/// default). View/window tests render into a headless top-level and then assert
/// <see cref="Messages"/> is empty — i.e. the bindings resolved cleanly.
/// </summary>
/// <remarks>
/// Avalonia 11.3.18's <see cref="ILogSink"/> has three members — <c>IsEnabled</c> plus two
/// <c>Log</c> overloads (one with a <c>params object?[]</c> of structured property values). We report
/// <c>IsEnabled</c> as <see langword="true"/> for binding-area messages so Avalonia actually invokes
/// <c>Log</c>, and format the message template with its property values for readable failures.
/// </remarks>
public sealed class BindingErrorSink : ILogSink, IDisposable
{
    private readonly ILogSink? _previous;
    private readonly LogEventLevel _minLevel;
    private readonly List<string> _messages = [];

    public BindingErrorSink(LogEventLevel minLevel = LogEventLevel.Warning)
    {
        _minLevel = minLevel;
        _previous = Logger.Sink;
        Logger.Sink = this;
    }

    /// <summary>The binding-area warnings/errors recorded so far.</summary>
    public IReadOnlyList<string> Messages => _messages;

    public bool IsEnabled(LogEventLevel level, string area) =>
        (level >= _minLevel && area == LogArea.Binding) || (_previous?.IsEnabled(level, area) ?? false);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
    {
        Record(level, area, source, messageTemplate, []);
        _previous?.Log(level, area, source, messageTemplate);
    }

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        Record(level, area, source, messageTemplate, propertyValues);
        _previous?.Log(level, area, source, messageTemplate, propertyValues);
    }

    public void Dispose() => Logger.Sink = _previous;

    private void Record(LogEventLevel level, string area, object? source, string template, object?[] values)
    {
        if (level >= _minLevel && area == LogArea.Binding)
        {
            _messages.Add(Format(level, source, template, values));
        }
    }

    // Best-effort rendering: fill positional {…} placeholders left-to-right with the property values,
    // then append the source. Enough to make a failing zero-binding-errors assertion legible.
    private static string Format(LogEventLevel level, object? source, string template, object?[] values)
    {
        string message = template;
        foreach (object? value in values)
        {
            int open = message.IndexOf('{', StringComparison.Ordinal);
            int close = open >= 0 ? message.IndexOf('}', open) : -1;
            if (close < 0)
            {
                break;
            }

            string rendered = System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
            message = message[..open] + rendered + message[(close + 1)..];
        }

        return $"[{level}] {message}" + (source is null ? string.Empty : $" (source: {source.GetType().Name})");
    }
}
