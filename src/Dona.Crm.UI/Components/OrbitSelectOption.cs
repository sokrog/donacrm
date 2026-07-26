namespace Dona.Crm.UI.Components;

public sealed record OrbitSelectOption<TValue>(TValue Value, string Label, string? Description = null);

public static class OrbitSelectOptions
{
    public static IReadOnlyList<OrbitSelectOption<TValue>> From<TValue>(
        IEnumerable<TValue> values,
        Func<TValue, string> label) =>
        values.Select(value => new OrbitSelectOption<TValue>(value, label(value))).ToList();

    public static IReadOnlyList<OrbitSelectOption<TValue?>> Optional<TValue>(
        IEnumerable<TValue> values,
        string emptyLabel,
        Func<TValue, string> label) where TValue : struct =>
        [new(default, emptyLabel), .. values.Select(value => new OrbitSelectOption<TValue?>(value, label(value)))];

    public static IReadOnlyList<OrbitSelectOption<string>> OptionalStrings(
        IEnumerable<string> values,
        string emptyLabel) =>
        [new(string.Empty, emptyLabel), .. values.Select(value => new OrbitSelectOption<string>(value, value))];
}
