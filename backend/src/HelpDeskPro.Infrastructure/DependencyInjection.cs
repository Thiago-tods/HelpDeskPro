using HelpDeskPro.Application.Attachments;
using HelpDeskPro.Application.Auth;
using HelpDeskPro.Application.Catalog;
using HelpDeskPro.Application.Dashboard;
using HelpDeskPro.Application.Messages;
using HelpDeskPro.Application.Notifications;
using HelpDeskPro.Application.Reports;
using HelpDeskPro.Application.Tickets;
using HelpDeskPro.Infrastructure.Attachments;
using HelpDeskPro.Infrastructure.Auth;
using HelpDeskPro.Infrastructure.Catalog;
using HelpDeskPro.Infrastructure.Dashboard;
using HelpDeskPro.Infrastructure.Identity;
using HelpDeskPro.Infrastructure.Messages;
using HelpDeskPro.Infrastructure.Notifications;
using HelpDeskPro.Infrastructure.Persistence;
using HelpDeskPro.Infrastructure.Reports;
using HelpDeskPro.Infrastructure.Seed;
using HelpDeskPro.Infrastructure.Sla;
using HelpDeskPro.Infrastructure.Tickets;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDeskPro.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // Lida de forma preguiçosa para respeitar overrides de configuração do host (ex.: testes).
        services.AddDbContext<AppDbContext>((sp, o) =>
        {
            var connection = sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Default não configurada. Use User Secrets (veja docs/DEVELOPMENT.md).");
            o.UseNpgsql(connection);
        });

        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 10;
                o.User.RequireUniqueEmail = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.AddOptions<JwtOptions>()
            .Bind(config.GetSection(JwtOptions.Section))
            .Validate(o => o.Key.Length >= 32, "Jwt:Key deve ter ao menos 32 caracteres (use User Secrets).")
            .ValidateOnStart();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddOptions<AttachmentOptions>().Bind(config.GetSection(AttachmentOptions.Section));
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<TicketScope>();
        services.AddScoped<ITicketEvents, TicketEventService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddOptions<SlaOptions>().Bind(config.GetSection(SlaOptions.Section));
        services.AddScoped<ISlaMonitor, SlaMonitor>();
        services.AddHostedService<SlaBackgroundService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<ITicketService, TicketService>();
        services.AddScoped<DatabaseSeeder>();
        return services;
    }
}
