namespace HelpDeskPro.Domain.Enums;

public static class Roles
{
    public const string Admin = "ADMIN";
    public const string Agent = "AGENT";
    public const string Requester = "REQUESTER";

    public static readonly string[] All = [Admin, Agent, Requester];
}
