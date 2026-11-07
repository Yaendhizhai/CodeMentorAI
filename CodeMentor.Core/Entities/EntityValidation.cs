namespace CodeMentor.Core.Entities;

internal static class EntityValidation
{
    public static string RequiredText(string? value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        if (value.Length > maxLength)
        {
            throw new ArgumentException($"The value cannot exceed {maxLength} characters.", parameterName);
        }

        return value;
    }

    public static string? OptionalText(string? value, string parameterName, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A provided value cannot be empty.", parameterName);
        }

        if (value.Length > maxLength)
        {
            throw new ArgumentException($"The value cannot exceed {maxLength} characters.", parameterName);
        }

        return value;
    }

    public static string RelativePath(string? value, string parameterName, int maxLength)
    {
        var path = RequiredText(value, parameterName, maxLength);
        var segments = path.Split('/', '\\');

        if (path.StartsWith('/') ||
            path.StartsWith('\\') ||
            (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':') ||
            segments.Contains("..", StringComparer.Ordinal))
        {
            throw new ArgumentException("The path must be repository-relative and cannot traverse parent directories.", parameterName);
        }

        return path;
    }

    public static void RequiredId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An identifier is required.", parameterName);
        }
    }
}
