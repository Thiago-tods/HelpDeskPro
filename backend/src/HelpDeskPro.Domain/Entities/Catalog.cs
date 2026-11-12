namespace HelpDeskPro.Domain.Entities;

public class Team
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private set; }
    public List<TeamMember> Members { get; private set; } = [];

    private Team() { }

    public Team(string name, string? description, DateTime now)
    {
        Name = name;
        Description = description;
        CreatedAt = now;
    }

    public void Update(string name, string? description)
    {
        Name = name;
        Description = description;
    }

    public void SetActive(bool active) => IsActive = active;
}

public class TeamMember
{
    public Guid TeamId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime JoinedAt { get; private set; }

    private TeamMember() { }

    public TeamMember(Guid teamId, Guid userId, DateTime now)
    {
        TeamId = teamId;
        UserId = userId;
        JoinedAt = now;
    }
}

public class Category
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Category() { }

    public Category(string name, string? description, Guid? defaultTeamId = null)
    {
        Name = name;
        Description = description;
        DefaultTeamId = defaultTeamId;
    }

    /// <summary>Equipe que recebe os chamados desta categoria.</summary>
    public Guid? DefaultTeamId { get; private set; }

    public void Update(string name, string? description, Guid? defaultTeamId)
    {
        Name = name;
        Description = description;
        DefaultTeamId = defaultTeamId;
    }

    public void SetDefaultTeam(Guid? teamId) => DefaultTeamId = teamId;

    public void SetActive(bool active) => IsActive = active;
}

public class Priority
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;
    public int Level { get; private set; }
    public int FirstResponseMinutes { get; private set; }
    public int ResolutionMinutes { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Priority() { }

    public Priority(string name, int level, int firstResponseMinutes, int resolutionMinutes)
    {
        Name = name;
        Level = level;
        SetSla(firstResponseMinutes, resolutionMinutes);
    }

    public void Update(string name, int level, int firstResponseMinutes, int resolutionMinutes)
    {
        Name = name;
        Level = level;
        SetSla(firstResponseMinutes, resolutionMinutes);
    }

    public void SetSla(int firstResponseMinutes, int resolutionMinutes)
    {
        if (firstResponseMinutes <= 0 || resolutionMinutes < firstResponseMinutes)
            throw new Exceptions.DomainException("Prazos de SLA inválidos.");
        FirstResponseMinutes = firstResponseMinutes;
        ResolutionMinutes = resolutionMinutes;
    }

    public void SetActive(bool active) => IsActive = active;
}
