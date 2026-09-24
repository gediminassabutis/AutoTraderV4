namespace AutoTraderV4;

public static class ApiValidation
{
    public static Dictionary<string, string[]> Build(Action<Dictionary<string, List<string>>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        configure(errors);

        return errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    public static void AddError(this Dictionary<string, List<string>> errors, string key, string message)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (!errors.TryGetValue(key, out var entries))
        {
            entries = [];
            errors[key] = entries;
        }

        entries.Add(message);
    }
}
