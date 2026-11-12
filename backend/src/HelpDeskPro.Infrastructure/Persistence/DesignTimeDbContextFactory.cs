using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HelpDeskPro.Infrastructure.Persistence;

// Usada apenas pelo `dotnet ef` para gerar migrations; não conecta ao banco.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=helpdeskpro_design")
            .Options;
        return new AppDbContext(options);
    }
}
