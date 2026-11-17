using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace ProjektLabor.Security;

/// <summary>
/// A Minimal API (.NET 8) nem futtatja le magától a DTO-k ellenőrző attribútumait
/// ([Required], [MaxLength], [EmailAddress], [Range], [RegularExpression] ...).
/// Ez a szűrő minden olyan végpont-paramétert ellenőriz, amelynek típusán ilyen attribútum van,
/// és hiba esetén 400-at ad a hibaüzenetekkel (az adatbázis 500-as csonkolási hibája helyett).
/// </summary>
public static class DataAnnotationsValidationFilter
{
    private static readonly ConcurrentDictionary<Type, bool> ValidatableTypes = new();

    public static EndpointFilterDelegate Create(EndpointFilterFactoryContext context, EndpointFilterDelegate next)
    {
        var validatedArgumentIndexes = context.MethodInfo
            .GetParameters()
            .Select((parameter, index) => (parameter.ParameterType, index))
            .Where(item => IsValidatable(item.ParameterType))
            .Select(item => item.index)
            .ToArray();
        if (validatedArgumentIndexes.Length == 0)
        {
            return next;
        }

        return async invocationContext =>
        {
            foreach (var index in validatedArgumentIndexes)
            {
                var argument = invocationContext.Arguments[index];
                if (argument is null)
                {
                    continue;
                }

                var results = new List<ValidationResult>();
                if (!Validator.TryValidateObject(argument, new ValidationContext(argument), results, validateAllProperties: true))
                {
                    var errors = results
                        .SelectMany(result => (result.MemberNames.Any() ? result.MemberNames : [string.Empty])
                            .Select(member => (Member: member, Message: result.ErrorMessage ?? "Érvénytelen érték.")))
                        .GroupBy(item => item.Member)
                        .ToDictionary(group => group.Key, group => group.Select(item => item.Message).ToArray());
                    return Results.BadRequest(new
                    {
                        message = results[0].ErrorMessage ?? "Érvénytelen kérés.",
                        errors
                    });
                }
            }

            return await next(invocationContext);
        };
    }

    private static bool IsValidatable(Type type) =>
        ValidatableTypes.GetOrAdd(type, static candidate =>
            candidate.IsClass &&
            candidate != typeof(string) &&
            (typeof(IValidatableObject).IsAssignableFrom(candidate) ||
             candidate.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                 .Any(property => property.GetCustomAttributes<ValidationAttribute>(inherit: true).Any())));
}
