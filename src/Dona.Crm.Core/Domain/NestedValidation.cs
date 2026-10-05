using System.ComponentModel.DataAnnotations;

namespace Dona.Crm.Web.Domain;

/// <summary>
/// DataAnnotationsValidator in Blazor checks only top-level properties of the form model.
/// Root models call this helper from IValidatableObject.Validate to also check annotated items of their collections.
/// </summary>
internal static class NestedValidation
{
    public static IEnumerable<ValidationResult> ValidateItems<T>(
        IEnumerable<T>? items,
        string memberName,
        string itemLabel,
        IServiceProvider? serviceProvider = null)
        where T : class
    {
        if (items is null)
        {
            yield break;
        }

        var index = 0;
        foreach (var item in items)
        {
            index++;
            var results = new List<ValidationResult>();
            var context = new ValidationContext(item, serviceProvider, null);
            Validator.TryValidateObject(item, context, results, validateAllProperties: true);
            foreach (var result in results)
            {
                var message = result.ErrorMessage ?? "Некорректное значение";
                yield return new ValidationResult($"{itemLabel} {index}: {message}", [memberName]);
            }
        }
    }
}