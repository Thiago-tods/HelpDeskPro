using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Infrastructure.Identity;
using HelpDeskPro.Infrastructure.Persistence;
using HelpDeskPro.Infrastructure.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HelpDeskPro.IntegrationTests;

/// <summary>
/// Sobe a API contra um PostgreSQL local de teste (helpdeskpro_test), derivado do
/// ConnectionStrings:Default do User Secrets da API. Sem Docker.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestPassword = "Teste!Senha#2026";
    public const string TestDatabase = "helpdeskpro_test";
    public const int MaxAttachmentBytes = 1024 * 1024;

    public int AuthPermitPerMinute { get; init; } = 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddUserSecrets(typeof(Program).Assembly);
            var built = cfg.Build();
            var main = built.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default ausente no User Secrets da API.");
            var test = new NpgsqlConnectionStringBuilder(main) { Database = TestDatabase }.ConnectionString;

            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = test,
                ["Seed:InitialPassword"] = TestPassword,
                ["RateLimiting:AuthPermitPerMinute"] = AuthPermitPerMinute.ToString(),
                ["Attachments:StoragePath"] = Path.Combine(Path.GetTempPath(), "hdp-tests-attachments"),
                ["Attachments:MaxBytes"] = MaxAttachmentBytes.ToString(),
                ["Sla:CheckIntervalSeconds"] = "0", // testes chamam o monitor explicitamente
            });
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    public async Task<ApplicationUser> CreateUserAsync(string role, bool active = true)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{role.ToLowerInvariant()}.{Guid.NewGuid():N}@teste.example";
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Usuário de Teste", IsActive = active, EmailConfirmed = true };
        var created = await users.CreateAsync(user, TestPassword);
        if (!created.Succeeded) throw new InvalidOperationException(string.Join(";", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, role);
        return user;
    }

    public async Task SetActiveAsync(Guid userId, bool active)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(userId.ToString()))!;
        user.IsActive = active;
        await users.UpdateAsync(user);
    }

    public static string[] AllRoles => Roles.All;
}
