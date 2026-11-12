namespace HelpDeskPro.Domain.Exceptions;

public class DomainException(string message) : Exception(message);

public sealed class InvalidStatusTransitionException(Enums.TicketStatus from, Enums.TicketStatus to)
    : DomainException($"Transição de status inválida: {from} -> {to}.");
