using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Infrastructure.Identity;
using HelpDeskPro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HelpDeskPro.Infrastructure.Seed;

/// <summary>Seed idempotente da empresa fictícia Nexora Tecnologia.</summary>
public class DatabaseSeeder(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    RoleManager<ApplicationRole> roles,
    IConfiguration config)
{
    private static readonly string[] CategoryNames =
    [
        "Acesso e Senhas", "Hardware", "Software", "Rede e Internet",
        "E-mail", "Impressoras", "Sistemas Internos", "Segurança",
    ];

    private static readonly (string Title, string Description, int Cat)[] Templates =
    [
        ("Não consigo acessar minha conta", "Meu login foi bloqueado após várias tentativas.", 0),
        ("Notebook não liga", "O equipamento não responde ao botão de energia desde ontem.", 1),
        ("Instalação do pacote Office", "Preciso do Excel e do Word instalados na estação.", 2),
        ("Wi-Fi instável no 2º andar", "A conexão cai a cada poucos minutos na sala de reunião.", 3),
        ("Caixa de e-mail cheia", "Não recebo novas mensagens por falta de espaço.", 4),
        ("Impressora com papel enroscado", "A impressora do financeiro trava ao imprimir.", 5),
        ("Erro ao emitir relatório no ERP", "O relatório mensal retorna erro 500.", 6),
        ("E-mail suspeito recebido", "Recebi um e-mail pedindo credenciais. Como proceder?", 7),
        ("Mouse com defeito", "O botão esquerdo falha com frequência.", 1),
        ("VPN não conecta", "Aparece timeout ao conectar de casa.", 3),
        ("Solicitação de novo usuário no ERP", "Novo colaborador precisa de acesso ao módulo de vendas.", 6),
        ("Monitor sem imagem", "O segundo monitor não exibe sinal.", 1),
        ("Atualização do antivírus", "Aviso de definições desatualizadas na estação.", 7),
        ("Redefinir senha do e-mail", "Esqueci a senha do e-mail corporativo.", 0),
        ("Lentidão ao abrir planilhas", "Arquivos grandes demoram minutos para abrir.", 2),
        ("Scanner não envia para a pasta", "O scanner do RH não grava na pasta de rede.", 5),
        ("Sem acesso à pasta compartilhada", "Preciso de acesso à pasta Financeiro.", 0),
        ("Teclado com teclas travando", "Algumas teclas não respondem.", 1),
        ("Erro de certificado no navegador", "Site interno exibe aviso de certificado inválido.", 3),
        ("Agenda não sincroniza no celular", "Compromissos não aparecem no smartphone.", 4),
        ("Cadastro de cliente não salva", "O sistema retorna validação sem detalhar o campo.", 6),
        ("Licença de software expirada", "O editor de PDF pede nova licença.", 2),
        ("Tentativa de acesso indevido", "Notei logins de um local estranho na minha conta.", 7),
        ("Impressora sem toner", "Toner preto acabou na impressora da recepção.", 5),
        ("Computador reiniciando sozinho", "A estação reinicia aleatoriamente durante o dia.", 1),
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var password = config["Seed:InitialPassword"];
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                "Seed:InitialPassword não configurada. Defina via User Secrets ou variável de ambiente Seed__InitialPassword.");

        await db.Database.MigrateAsync(ct);

        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new ApplicationRole(role));

        var priorities = await SeedPrioritiesAsync(ct);
        var categories = await SeedCategoriesAsync(ct);

        var admin = await EnsureUserAsync("Marina Albuquerque", "admin@nexora.example", Roles.Admin, password);
        var agents = new[]
        {
            await EnsureUserAsync("Rafael Mendes", "rafael.mendes@nexora.example", Roles.Agent, password),
            await EnsureUserAsync("Camila Torres", "camila.torres@nexora.example", Roles.Agent, password),
            await EnsureUserAsync("Bruno Figueira", "bruno.figueira@nexora.example", Roles.Agent, password),
        };
        var requesters = new[]
        {
            await EnsureUserAsync("Ana Beatriz Lima", "ana.lima@nexora.example", Roles.Requester, password),
            await EnsureUserAsync("Carlos Eduardo Souza", "carlos.souza@nexora.example", Roles.Requester, password),
            await EnsureUserAsync("Fernanda Rocha", "fernanda.rocha@nexora.example", Roles.Requester, password),
            await EnsureUserAsync("Gustavo Pereira", "gustavo.pereira@nexora.example", Roles.Requester, password),
            await EnsureUserAsync("Juliana Martins", "juliana.martins@nexora.example", Roles.Requester, password),
        };

        var teams = await SeedTeamsAsync(agents, ct);
        await BackfillCategoryTeamsAsync(categories, teams, ct);

        if (!await db.Tickets.AnyAsync(ct))
            await SeedTicketsAsync(admin, agents, requesters, teams, priorities, categories, ct);
    }

    private async Task<List<Priority>> SeedPrioritiesAsync(CancellationToken ct)
    {
        if (!await db.Priorities.AnyAsync(ct))
        {
            db.Priorities.AddRange(
                new Priority("Baixa", 1, 8 * 60, 72 * 60),
                new Priority("Média", 2, 4 * 60, 48 * 60),
                new Priority("Alta", 3, 60, 8 * 60),
                new Priority("Crítica", 4, 15, 4 * 60));
            await db.SaveChangesAsync(ct);
        }
        return await db.Priorities.OrderBy(p => p.Level).ToListAsync(ct);
    }

    private async Task<List<Category>> SeedCategoriesAsync(CancellationToken ct)
    {
        var existing = await db.Categories.Select(c => c.Name).ToListAsync(ct);
        foreach (var name in CategoryNames.Except(existing))
            db.Categories.Add(new Category(name, $"Chamados relacionados a {name.ToLowerInvariant()}."));
        await db.SaveChangesAsync(ct);
        return await db.Categories.OrderBy(c => c.Name).ToListAsync(ct);
    }

    private async Task<List<Team>> SeedTeamsAsync(ApplicationUser[] agents, CancellationToken ct)
    {
        if (!await db.Teams.AnyAsync(ct))
        {
            var now = DateTime.UtcNow;
            var n1 = new Team("Suporte N1", "Atendimento inicial a usuários.", now);
            var infra = new Team("Infraestrutura", "Redes, servidores e segurança.", now);
            db.Teams.AddRange(n1, infra);
            db.TeamMembers.AddRange(
                new TeamMember(n1.Id, agents[0].Id, now),
                new TeamMember(n1.Id, agents[1].Id, now),
                new TeamMember(infra.Id, agents[2].Id, now),
                new TeamMember(infra.Id, agents[0].Id, now));
            await db.SaveChangesAsync(ct);
        }
        return await db.Teams.OrderBy(t => t.Name).ToListAsync(ct);
    }

    private async Task BackfillCategoryTeamsAsync(List<Category> categories, List<Team> teams, CancellationToken ct)
    {
        var n1 = teams.First(t => t.Name == "Suporte N1");
        var infra = teams.First(t => t.Name == "Infraestrutura");
        var infraCategories = new[] { "Rede e Internet", "Segurança" };

        foreach (var category in categories.Where(c => c.DefaultTeamId is null))
            category.SetDefaultTeam(infraCategories.Contains(category.Name) ? infra.Id : n1.Id);
        await db.SaveChangesAsync(ct);
    }

    private async Task<ApplicationUser> EnsureUserAsync(string name, string email, string role, string password)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is not null) return user;

        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = name,
            CreatedAt = DateTime.UtcNow,
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Falha ao criar {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        await users.AddToRoleAsync(user, role);
        return user;
    }

    private async Task SeedTicketsAsync(ApplicationUser admin, ApplicationUser[] agents, ApplicationUser[] requesters,
        List<Team> teams, List<Priority> priorities, List<Category> categories, CancellationToken ct)
    {
        var rng = new Random(42);
        var n1 = teams.First(t => t.Name == "Suporte N1");
        var infra = teams.First(t => t.Name == "Infraestrutura");
        var year = DateTime.UtcNow.Year;
        var start = DateTime.UtcNow.AddDays(-30);

        for (var i = 0; i < Templates.Length; i++)
        {
            var (title, description, catIndex) = Templates[i];
            var category = categories.First(c => c.Name == CategoryNames[catIndex]);
            var priority = priorities[rng.Next(priorities.Count)];
            var requester = requesters[rng.Next(requesters.Length)];
            var team = catIndex is 3 or 7 ? infra : n1;
            var agent = team == infra ? agents[2] : agents[rng.Next(2)];
            var created = start.AddHours(i * 28 + rng.Next(0, 6));

            var ticket = Ticket.Create($"HDP-{year}-{i + 1:D6}", title, description, requester.Id,
                category.Id, priority, team.Id, created);

            // Estágio do ciclo de vida: 0 novo, 1 em andamento, 2 aguardando, 3 resolvido, 4 fechado, 5 cancelado
            var stage = i % 6;
            if (stage >= 1 && stage != 5)
            {
                var t = created.AddMinutes(rng.Next(5, priority.FirstResponseMinutes + 30));
                ticket.Assign(agent.Id, agent.Id, t);
                ticket.AddMessage(agent.Id, "Olá! Já estou analisando seu chamado.", false, true, t.AddMinutes(2));
                ticket.AddMessage(agent.Id, "Verificar histórico do usuário antes do contato.", true, true, t.AddMinutes(3));
                ticket.AddMessage(requester.Id, "Obrigado, fico no aguardo.", false, false, t.AddMinutes(30));

                if (stage == 2)
                    ticket.ChangeStatus(TicketStatus.WaitingForUser, agent.Id, t.AddHours(1));
                if (stage >= 3)
                {
                    var r = t.AddHours(rng.Next(2, 30));
                    ticket.ChangeStatus(TicketStatus.Resolved, agent.Id, r);
                    ticket.AddMessage(agent.Id, "Problema solucionado. Confirme se está tudo certo.", false, true, r.AddSeconds(30));
                    if (stage == 4) ticket.ChangeStatus(TicketStatus.Closed, admin.Id, r.AddHours(24));
                }
            }
            else if (stage == 5)
            {
                ticket.ChangeStatus(TicketStatus.Cancelled, requester.Id, created.AddHours(2));
            }

            db.Tickets.Add(ticket);
        }

        await db.SaveChangesAsync(ct);
    }
}
