using System.Reflection;

namespace BillingRecon.ArchitectureTests;

/// <summary>
/// Builds the documentation comment id of a member (<c>P:System.DateTime.Now</c>,
/// <c>M:Namespace.Type.Method``1(System.Linq.IQueryable{``0})</c>), the format <c>BannedSymbols.txt</c> uses.
/// It covers what the ban list needs: properties and methods, generic methods, and parameters that are constructed
/// generic types, arrays or by-reference.
/// </summary>
internal static class DocumentationId
{
    public static string Of(PropertyInfo property) => $"P:{TypeName(property.DeclaringType!)}.{property.Name}";

    public static string Of(MethodInfo method)
    {
        var name = method.IsGenericMethodDefinition ? $"{method.Name}``{method.GetGenericArguments().Length}" : method.Name;
        var parameters = method.GetParameters();
        var list = parameters.Length == 0 ? string.Empty : $"({string.Join(",", parameters.Select(parameter => ParameterTypeName(parameter.ParameterType)))})";

        return $"M:{TypeName(method.DeclaringType!)}.{name}{list}";
    }

    private static string TypeName(Type type) => type.FullName!.Replace('+', '.');

    private static string ParameterTypeName(Type type)
    {
        if (type.IsByRef)
        {
            return ParameterTypeName(type.GetElementType()!) + "@";
        }

        if (type.IsArray)
        {
            return ParameterTypeName(type.GetElementType()!) + "[]";
        }

        if (type.IsGenericMethodParameter)
        {
            return $"``{type.GenericParameterPosition}";
        }

        if (type.IsGenericTypeParameter)
        {
            return $"`{type.GenericParameterPosition}";
        }

        if (type.IsGenericType)
        {
            var definition = TypeName(type.GetGenericTypeDefinition());
            var arguments = string.Join(",", type.GetGenericArguments().Select(ParameterTypeName));

            return $"{definition[..definition.IndexOf('`', StringComparison.Ordinal)]}{{{arguments}}}";
        }

        return TypeName(type);
    }
}
