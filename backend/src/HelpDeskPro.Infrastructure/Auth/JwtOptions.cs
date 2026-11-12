namespace HelpDeskPro.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "HelpDeskPro";
    public string Audience { get; set; } = "HelpDeskPro.Web";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}
