using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public const string TicketNumberSequence = "ticket_number_seq";

    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Priority> Priorities => Set<Priority>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();
    public DbSet<TicketHistory> TicketHistory => Set<TicketHistory>();
    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();
    public DbSet<TicketSla> TicketSlas => Set<TicketSla>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.HasSequence<long>(TicketNumberSequence).StartsAt(1000);

        // Ids Guid são gerados pelo domínio: sem isso o EF trata filhos novos de entidades rastreadas como existentes.
        b.Entity<Team>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Category>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Priority>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Ticket>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<TicketMessage>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<TicketHistory>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<TicketAttachment>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Notification>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<RefreshToken>().Property(x => x.Id).ValueGeneratedNever();

        b.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.HasIndex(x => x.IsActive);
        });

        b.Entity<Team>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<TeamMember>(e =>
        {
            e.HasKey(x => new { x.TeamId, x.UserId });
            e.HasOne<Team>().WithMany(t => t.Members).HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.DefaultTeamId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Priority>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.Level).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_priority_sla", "\"FirstResponseMinutes\" > 0 AND \"ResolutionMinutes\" >= \"FirstResponseMinutes\""));
        });

        b.Entity<Ticket>(e =>
        {
            e.Property(x => x.TicketNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(10000).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => x.TicketNumber).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.RequesterId);
            e.HasIndex(x => x.AssignedToId);
            e.HasIndex(x => x.TeamId);
            e.HasIndex(x => x.CreatedAt);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AssignedToId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Priority>().WithMany().HasForeignKey(x => x.PriorityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Sla).WithOne().HasForeignKey<TicketSla>(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.History).WithOne().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<TicketSla>(e =>
        {
            e.HasKey(x => x.TicketId);
            e.HasIndex(x => x.ResolutionDueAt);
            e.HasIndex(x => x.BreachedAt);
        });

        b.Entity<TicketMessage>(e =>
        {
            e.Property(x => x.Content).HasMaxLength(10000).IsRequired();
            e.HasIndex(x => new { x.TicketId, x.CreatedAt });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<TicketHistory>(e =>
        {
            e.Property(x => x.Sequence).ValueGeneratedOnAdd().UseIdentityAlwaysColumn();
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.OldValue).HasMaxLength(200);
            e.Property(x => x.NewValue).HasMaxLength(200);
            e.HasIndex(x => new { x.TicketId, x.CreatedAt });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<TicketAttachment>(e =>
        {
            e.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            e.Property(x => x.StoredFileName).HasMaxLength(100).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.StoredFileName).IsUnique();
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UploadedById).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Notification>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Message).HasMaxLength(500).IsRequired();
            e.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
