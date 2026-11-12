using FluentValidation;

namespace HelpDeskPro.Application.Messages;

public sealed record MessageDto(
    Guid Id, Guid TicketId, Guid AuthorId, string AuthorName, string AuthorRole,
    string Content, bool IsInternal, DateTime CreatedAt);

public sealed record CreateMessageRequest(string Content, bool IsInternal = false);

public interface IMessageService
{
    /// <summary>Solicitantes nunca recebem notas internas.</summary>
    Task<IReadOnlyList<MessageDto>> ListAsync(Guid ticketId, CancellationToken ct);
    Task<MessageDto> CreateAsync(Guid ticketId, CreateMessageRequest request, CancellationToken ct);
}

public sealed class CreateMessageRequestValidator : AbstractValidator<CreateMessageRequest>
{
    public CreateMessageRequestValidator() =>
        RuleFor(x => x.Content).NotEmpty().MaximumLength(10000);
}
