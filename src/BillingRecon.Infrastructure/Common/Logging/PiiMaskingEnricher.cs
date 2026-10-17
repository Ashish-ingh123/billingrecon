using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace BillingRecon.Infrastructure.Common.Logging;

/// <summary>
/// Applies <see cref="PiiMasker"/> to every property of every log event, at any depth, before any sink sees the event.
/// It is registered as the last enricher so it also covers what other enrichers add. It cannot reach text baked into
/// an exception message: exceptions must never carry patient data (docs/adr/0012).
/// </summary>
public sealed class PiiMaskingEnricher : ILogEventEnricher
{
    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        List<LogEventProperty>? replacements = null;
        foreach (var (name, value) in logEvent.Properties)
        {
            var masked = MaskValue(value, PiiMasker.IsSensitiveName(name));
            if (!ReferenceEquals(masked, value))
            {
                (replacements ??= []).Add(new LogEventProperty(name, masked));
            }
        }

        if (replacements is null)
        {
            return;
        }

        foreach (var replacement in replacements)
        {
            logEvent.AddOrUpdateProperty(replacement);
        }
    }

    // Returns the same instance when nothing inside it changed, so an event without PII costs no allocation.
    private static LogEventPropertyValue MaskValue(LogEventPropertyValue value, bool sensitive) => value switch
    {
        ScalarValue scalar => MaskScalar(scalar, sensitive),
        SequenceValue sequence => MaskSequence(sequence, sensitive),
        StructureValue structure => MaskStructure(structure, sensitive),
        DictionaryValue dictionary => MaskDictionary(dictionary, sensitive),
        _ => value,
    };

    private static ScalarValue MaskScalar(ScalarValue scalar, bool sensitive)
    {
        switch (scalar.Value)
        {
            case string text:
                return MaskText(scalar, text, sensitive);
            case Uri uri:
                return MaskText(scalar, uri.OriginalString, sensitive);
            case sbyte or byte or short or ushort or int or uint or long or ulong or decimal or double or float when sensitive:
                // A phone number logged as a number under a name that gives it away.
                return new ScalarValue(PiiMasker.FullMask);
            default:
                return scalar;
        }
    }

    private static ScalarValue MaskText(ScalarValue scalar, string text, bool sensitive)
    {
        var masked = sensitive ? PiiMasker.MaskSensitive(text) : PiiMasker.Mask(text);

        return ReferenceEquals(masked, text) ? scalar : new ScalarValue(masked);
    }

    private static SequenceValue MaskSequence(SequenceValue sequence, bool sensitive)
    {
        LogEventPropertyValue[]? masked = null;
        for (var index = 0; index < sequence.Elements.Count; index++)
        {
            var element = sequence.Elements[index];
            var maskedElement = MaskValue(element, sensitive);
            if (!ReferenceEquals(maskedElement, element))
            {
                masked ??= [.. sequence.Elements];
                masked[index] = maskedElement;
            }
        }

        return masked is null ? sequence : new SequenceValue(masked);
    }

    // An object logged under a sensitive name is sensitive throughout: {@Phone} with a Number inside is still a phone.
    private static StructureValue MaskStructure(StructureValue structure, bool sensitive)
    {
        LogEventProperty[]? masked = null;
        for (var index = 0; index < structure.Properties.Count; index++)
        {
            var property = structure.Properties[index];
            var maskedValue = MaskValue(property.Value, sensitive || PiiMasker.IsSensitiveName(property.Name));
            if (!ReferenceEquals(maskedValue, property.Value))
            {
                masked ??= [.. structure.Properties];
                masked[index] = new LogEventProperty(property.Name, maskedValue);
            }
        }

        return masked is null ? structure : new StructureValue(masked, structure.TypeTag);
    }

    // Keys are logged text too, so an address or number used as a key is masked like any other string.
    private static DictionaryValue MaskDictionary(DictionaryValue dictionary, bool sensitive)
    {
        var changed = false;
        var masked = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(dictionary.Elements.Count);
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, value) in dictionary.Elements)
        {
            var maskedKey = key;
            var keyIsSensitive = sensitive;
            if (key.Value is string name)
            {
                keyIsSensitive = sensitive || PiiMasker.IsSensitiveName(name);
                maskedKey = MaskKey(key, name, usedKeys);
            }

            var maskedValue = MaskValue(value, keyIsSensitive);
            changed |= !ReferenceEquals(maskedKey, key) || !ReferenceEquals(maskedValue, value);
            masked.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(maskedKey, maskedValue));
        }

        return changed ? new DictionaryValue(masked) : dictionary;
    }

    // Two different addresses can mask to the same text; a suffix keeps the keys distinct so no entry is lost.
    private static ScalarValue MaskKey(ScalarValue key, string name, HashSet<string> usedKeys)
    {
        var masked = PiiMasker.Mask(name);
        var unique = masked;
        for (var suffix = 2; !usedKeys.Add(unique); suffix++)
        {
            unique = string.Create(CultureInfo.InvariantCulture, $"{masked} ({suffix})");
        }

        return ReferenceEquals(unique, name) ? key : new ScalarValue(unique);
    }
}
