using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using FluentValidation;
using HelpDeskPro.Api.Infrastructure;
using HelpDeskPro.Api.Realtime;
using HelpDeskPro.Application.Auth;
using HelpDeskPro.Application.Common;
using HelpDeskPro.Application.Notifications;
using HelpDeskPro.Infrastructure;
using HelpDeskPro.Infrastructure.Persistence;
using HelpDeskPro.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
builder.Services.AddApiSecurity(builder.Configuration);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IRealtimePublisher, SignalRRealtimePublisher>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddControllers(o => o.Filters.Add<ValidationFilter>())
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
        // Mantém acentos legíveis, mas escapa <, >, & e aspas (defesa em profundidade contra HTML injetado em JSON).
        o.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
    });
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((document, _, _) =>
{
    document.Info = new OpenApiInfo
    {
        Title = "HelpDesk Pro API",
        Version = "v1",
        Description = "API de gerenciamento de chamados. Autentique em POST /api/v1/auth/login e use o accessToken como Bearer.",
    };
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    };
    document.Security ??= [];
    document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
    return Task.CompletedTask;
}));

var app = builder.Build();

// Comandos utilitários locais: `dotnet run -- migrate` e `dotnet run -- seed`.
if (args.Contains("migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    Console.WriteLine("Migrations aplicadas.");
    return;
}

if (args.Contains("seed"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
    Console.WriteLine("Seed concluído.");
    return;
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "HelpDesk Pro API v1");
        o.RoutePrefix = "swagger";
        o.DocumentTitle = "HelpDesk Pro — API";
    });
}

app.UseCors(Policies.CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<TicketsHub>("/hubs/tickets", o => o.CloseOnAuthenticationExpiration = true);
app.Run();

public partial class Program;
