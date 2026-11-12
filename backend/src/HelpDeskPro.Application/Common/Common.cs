using HelpDeskPro.Domain.Enums;

namespace HelpDeskPro.Application.Common;

public class NotFoundException(string message = "Recurso não encontrado.") : Exception(message);

public class ForbiddenException(string message = "Você não tem permissão para esta operação.") : Exception(message);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public interface ICurrentUser
{
    Guid Id { get; }
    string Role { get; }
    bool IsAdmin => Role == Roles.Admin;
    bool IsAgent => Role == Roles.Agent;
    bool IsRequester => Role == Roles.Requester;
    bool IsStaff => IsAdmin || IsAgent;
}

public static class EnumCodes
{
    /// <summary>"InProgress" -> "IN_PROGRESS".</summary>
    public static string ToCode(string name)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append('_');
            sb.Append(char.ToUpperInvariant(name[i]));
        }
        return sb.ToString();
    }

    public static string ToCode<T>(T value) where T : struct, Enum => ToCode(value.ToString());

    /// <summary>Aceita "IN_PROGRESS", "InProgress" ou "in_progress".</summary>
    public static bool TryParse<T>(string? text, out T value) where T : struct, Enum
    {
        value = default;
        return !string.IsNullOrWhiteSpace(text)
            && Enum.TryParse(text.Replace("_", string.Empty), ignoreCase: true, out value)
            && Enum.IsDefined(value);
    }
}
