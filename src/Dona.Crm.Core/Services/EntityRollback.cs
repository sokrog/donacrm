using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace Dona.Crm.Web.Services;

/// <summary>
/// Откатывает объект, переданный вызывающим кодом, если операция над ним завершилась ошибкой.
/// Перед операцией делается глубокая копия; при исключении значения копируются обратно в исходный экземпляр.
/// </summary>
public static class EntityRollback
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<TResult> RunAsync<TEntity, TResult>(TEntity entity, Func<Task<TResult>> operation) where TEntity : class
    {
        var snapshot = Clone(entity);
        try { return await operation(); }
        catch
        {
            Restore(entity, snapshot);
            throw;
        }
    }

    public static async Task RunAsync<TEntity>(TEntity entity, Func<Task> operation) where TEntity : class
    {
        await RunAsync<TEntity, bool>(entity, async () => { await operation(); return true; });
    }

    private static TEntity Clone<TEntity>(TEntity entity) =>
        JsonSerializer.Deserialize<TEntity>(JsonSerializer.Serialize(entity, JsonOptions), JsonOptions)
        ?? throw new InvalidOperationException("Не удалось создать копию объекта для отката.");

    private static void Restore(object target, object source)
    {
        foreach (var property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0) continue;
            var sourceValue = property.GetValue(source);
            var targetValue = property.GetValue(target);
            if (sourceValue is IList sourceList && targetValue is IList targetList && property.PropertyType.IsGenericType)
            {
                RestoreList(targetList, sourceList);
                continue;
            }
            if (sourceValue is not null && targetValue is not null && IsComplex(property.PropertyType))
            {
                Restore(targetValue, sourceValue);
                continue;
            }
            property.SetValue(target, sourceValue);
        }
    }

    // Сохраняем идентичность элементов (UI держит ссылки на строки), сопоставляя их по Id.
    private static void RestoreList(IList target, IList source)
    {
        var existing = new Dictionary<object, object>();
        foreach (var item in target)
            if (item is not null && IdOf(item) is { } id) existing.TryAdd(id, item);

        var restored = new List<object?>();
        foreach (var item in source)
        {
            if (item is not null && IsComplex(item.GetType()) && IdOf(item) is { } id && existing.TryGetValue(id, out var original))
            {
                Restore(original, item);
                restored.Add(original);
            }
            else restored.Add(item);
        }

        target.Clear();
        foreach (var item in restored) target.Add(item);
    }

    private static object? IdOf(object item)
    {
        var value = item.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(item);
        return value is Guid guid && guid != Guid.Empty ? guid : null;
    }

    private static bool IsComplex(Type type) => type.IsClass && type != typeof(string);
}
