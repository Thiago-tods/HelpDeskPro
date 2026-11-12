using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace HelpDeskPro.IntegrationTests;

/// <summary>Cria um chamado de Hardware (equipe N1) assumido por Camila.</summary>
public abstract class CommunicationTestBase(ApiFactory factory) : ApiTestBase(factory)
{
    protected async Task<JsonElement> AssignedTicket(HttpClient requester, string category = "Hardware")
    {
        var ticket = await OpenTicket(requester, category);
        var camila = await As(Seeded.Camila);
        var res = await camila.PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Camila) });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return ticket;
    }

    protected static Task<HttpResponseMessage> Say(HttpClient c, JsonElement ticket, string content, bool internalNote = false) =>
        c.PostAsJsonAsync(Url(ticket, "/messages"), new { content, isInternal = internalNote });

    protected static MultipartFormDataContent FileContent(string fileName, byte[] bytes, string contentType)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { part, "file", fileName } };
    }

    protected static byte[] Png(int extra = 32) =>
        [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, .. Enumerable.Repeat((byte)7, extra)];
}

[Collection(ApiCollection.Name)]
public sealed class MessageTests(ApiFactory factory) : CommunicationTestBase(factory)
{
    [Fact]
    public async Task Requester_PostsPublicMessage_AndSeesItInTheThread()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);

        var res = await Say(ana, ticket, "Ainda sem resposta, podem ajudar?");
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = await res.Json();
        Assert.Equal("REQUESTER", created.Str("authorRole"));
        Assert.False(created.GetProperty("isInternal").GetBoolean());

        var list = await (await ana.GetAsync(Url(ticket, "/messages"))).Json();
        Assert.Equal(1, list.GetArrayLength());
    }

    [Fact]
    public async Task InternalNotes_AreInvisibleToRequester_AndRequesterCannotCreateThem()
    {
        var ana = await As(Seeded.Ana);
        var camila = await As(Seeded.Camila);
        var ticket = await AssignedTicket(ana);

        Assert.Equal(HttpStatusCode.Created, (await Say(camila, ticket, "Nota privada: validar com o fornecedor", internalNote: true)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Say(camila, ticket, "Olá, estamos analisando.")).StatusCode);

        var requesterView = await (await ana.GetAsync(Url(ticket, "/messages"))).Json();
        Assert.Equal(1, requesterView.GetArrayLength());
        Assert.DoesNotContain("fornecedor", requesterView.GetRawText());

        var staffView = await (await camila.GetAsync(Url(ticket, "/messages"))).Json();
        Assert.Equal(2, staffView.GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await Say(ana, ticket, "tentando nota", internalNote: true)).StatusCode);
    }

    [Fact]
    public async Task OnlyPublicAgentReply_CountsAsFirstResponse()
    {
        var ana = await As(Seeded.Ana);
        var camila = await As(Seeded.Camila);
        var ticket = await AssignedTicket(ana);

        await Say(camila, ticket, "nota interna", internalNote: true);
        await Say(ana, ticket, "alguma novidade?");
        var afterNote = await (await ana.GetAsync(Url(ticket))).Json();
        Assert.Equal(JsonValueKind.Null, afterNote.GetProperty("firstRespondedAt").ValueKind);

        await Say(camila, ticket, "Resposta pública");
        var afterReply = await (await ana.GetAsync(Url(ticket))).Json();
        Assert.NotEqual(JsonValueKind.Null, afterReply.GetProperty("firstRespondedAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, afterReply.GetProperty("sla").GetProperty("firstRespondedAt").ValueKind);
    }

    [Fact]
    public async Task Messages_IdorAndScope_Return404()
    {
        var ticket = await AssignedTicket(await As(Seeded.Ana));

        var carlos = await As(Seeded.Carlos);
        Assert.Equal(HttpStatusCode.NotFound, (await carlos.GetAsync(Url(ticket, "/messages"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Say(carlos, ticket, "invasão")).StatusCode);

        var bruno = await As(Seeded.Bruno); // equipe Infraestrutura
        Assert.Equal(HttpStatusCode.NotFound, (await bruno.GetAsync(Url(ticket, "/messages"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Say(bruno, ticket, "x", internalNote: true)).StatusCode);
    }

    [Fact]
    public async Task Agent_PublicReplyRequiresAssignment_InternalNoteDoesNot()
    {
        var ticket = await OpenTicket(await As(Seeded.Ana), "Hardware");
        var rafael = await As(Seeded.Rafael);

        Assert.Equal(HttpStatusCode.Forbidden, (await Say(rafael, ticket, "posso ajudar")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Say(rafael, ticket, "nota de triagem", internalNote: true)).StatusCode);
    }

    [Fact]
    public async Task Admin_CanReplyAnywhere()
    {
        var ticket = await OpenTicket(await As(Seeded.Ana), "Rede e Internet");
        Assert.Equal(HttpStatusCode.Created, (await Say(await As(Seeded.Admin), ticket, "Admin respondendo")).StatusCode);
    }

    [Fact]
    public async Task RequesterReply_ReturnsWaitingTicketToInProgress()
    {
        var ana = await As(Seeded.Ana);
        var camila = await As(Seeded.Camila);
        var ticket = await AssignedTicket(ana);
        await camila.PatchJsonAsync(Url(ticket, "/status"), new { status = "WAITING_FOR_USER" });

        await Say(ana, ticket, "Aqui estão as informações pedidas.");

        var detail = await (await ana.GetAsync(Url(ticket))).Json();
        Assert.Equal("IN_PROGRESS", detail.Str("status"));
    }

    [Fact]
    public async Task Messages_RejectedOnFinalTicket_AndEmptyContentIs400()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);
        Assert.Equal(HttpStatusCode.BadRequest, (await Say(ana, ticket, "")).StatusCode);

        await ana.PatchJsonAsync(Url(ticket, "/status"), new { status = "CANCELLED" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Say(ana, ticket, "ainda aí?")).StatusCode);
    }

    [Fact]
    public async Task Messages_AreChronologicalWithAuthorData()
    {
        var ana = await As(Seeded.Ana);
        var camila = await As(Seeded.Camila);
        var ticket = await AssignedTicket(ana);
        await Say(camila, ticket, "primeira");
        await Say(ana, ticket, "segunda");
        await Say(camila, ticket, "terceira");

        var list = (await (await ana.GetAsync(Url(ticket, "/messages"))).Json()).EnumerateArray().ToList();
        Assert.Equal(["primeira", "segunda", "terceira"], list.Select(m => m.Str("content")).ToArray());
        Assert.Equal("Camila Torres", list[0].Str("authorName"));
        Assert.Equal("AGENT", list[0].Str("authorRole"));
    }
}

[Collection(ApiCollection.Name)]
public sealed class AttachmentTests(ApiFactory factory) : CommunicationTestBase(factory)
{
    private static string Files(JsonElement ticket, string suffix = "") => Url(ticket, "/attachments" + suffix);

    [Fact]
    public async Task Upload_Png_StoresRandomName_AndDownloadReturnsSameBytes()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);
        var bytes = Png();

        var res = await ana.PostAsync(Files(ticket), FileContent("captura de tela.png", bytes, "image/png"));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var meta = await res.Json();
        Assert.Equal("captura de tela.png", meta.Str("fileName"));
        Assert.Equal("image/png", meta.Str("contentType"));
        Assert.DoesNotContain("storedFileName", meta.GetRawText());

        var download = await ana.GetAsync(Files(ticket, $"/{meta.Guid("id")}"));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());

        var storage = Path.Combine(Path.GetTempPath(), "hdp-tests-attachments");
        Assert.All(Directory.GetFiles(storage), f => Assert.Matches("^[0-9a-f]{48}$", Path.GetFileName(f)));
    }

    [Fact]
    public async Task Upload_ContentMustMatchExtension_ExecutableDisguisedAsImageIsRejected()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);
        var exe = "MZ"u8.ToArray().Concat(new byte[64]).ToArray();

        var res = await ana.PostAsync(Files(ticket), FileContent("foto.png", exe, "image/png"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("pagina.html")]
    [InlineData("imagem.svg")]
    [InlineData("script.ps1")]
    [InlineData("foto.png.exe")]
    [InlineData("semextensao")]
    public async Task Upload_DisallowedExtensions_AreRejected(string name)
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);
        var res = await ana.PostAsync(Files(ticket), FileContent(name, Png(), "image/png"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task Upload_DisallowedDeclaredContentType_IsRejected()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);
        var res = await ana.PostAsync(Files(ticket), FileContent("foto.png", Png(), "application/x-msdownload"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task Upload_AboveLimit_IsRejected()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);
        var res = await ana.PostAsync(Files(ticket), FileContent("grande.png", Png(ApiFactory.MaxAttachmentBytes), "image/png"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task Upload_PathTraversalInFileName_IsNeutralized()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);
        var res = await ana.PostAsync(Files(ticket), FileContent("..\\..\\windows\\system32\\evil.png", Png(), "image/png"));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var name = (await res.Json()).Str("fileName");
        Assert.DoesNotContain("..", name);
        Assert.DoesNotContain("\\", name);
        Assert.DoesNotContain("/", name);
    }

    [Fact]
    public async Task Upload_Docx_RequiresRealOpenXmlStructure()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in new[] { "[Content_Types].xml", "word/document.xml" })
                await using (var s = zip.CreateEntry(entry).Open()) await s.WriteAsync("<x/>"u8.ToArray());
        }
        var docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        Assert.Equal(HttpStatusCode.Created, (await ana.PostAsync(Files(ticket), FileContent("relatorio.docx", ms.ToArray(), docx))).StatusCode);

        var fakeZip = new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5 };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ana.PostAsync(Files(ticket), FileContent("falso.docx", fakeZip, docx))).StatusCode);
    }

    [Fact]
    public async Task Download_IdorAndScope_Return404()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await AssignedTicket(ana);
        var meta = await (await ana.PostAsync(Files(ticket), FileContent("a.png", Png(), "image/png"))).Json();
        var url = Files(ticket, $"/{meta.Guid("id")}");

        Assert.Equal(HttpStatusCode.NotFound, (await (await As(Seeded.Carlos)).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await As(Seeded.Bruno)).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await As(Seeded.Camila)).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await As(Seeded.Admin)).GetAsync(url)).StatusCode);

        // O id do anexo não vale em outro chamado.
        var other = await OpenTicket(ana);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync(Files(other, $"/{meta.Guid("id")}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await As(Seeded.Carlos)).GetAsync(Files(ticket))).StatusCode);
    }

    [Fact]
    public async Task Upload_NotAllowedOnFinalTicket_OrForAgentWithoutAssignment_OrAnonymous()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana, "Hardware");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await (await As(Seeded.Rafael)).PostAsync(Files(ticket), FileContent("a.png", Png(), "image/png"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Factory.CreateClient().PostAsync(Files(ticket), FileContent("a.png", Png(), "image/png"))).StatusCode);

        await ana.PatchJsonAsync(Url(ticket, "/status"), new { status = "CANCELLED" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ana.PostAsync(Files(ticket), FileContent("a.png", Png(), "image/png"))).StatusCode);
    }

    [Fact]
    public async Task Upload_WithoutFile_Returns400()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);
        var res = await ana.PostAsync(Files(ticket), new MultipartFormDataContent());
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task List_ShowsUploaderAndMetadata()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);
        await ana.PostAsync(Files(ticket), FileContent("nota.txt", "olá mundo"u8.ToArray(), "text/plain"));

        var list = await (await ana.GetAsync(Files(ticket))).Json();
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal("Ana Beatriz Lima", list[0].Str("uploadedByName"));
        Assert.Equal("text/plain", list[0].Str("contentType"));
    }
}

[Collection(ApiCollection.Name)]
public sealed class NotificationTests(ApiFactory factory) : CommunicationTestBase(factory)
{
    private static async Task<JsonElement> Mine(HttpClient c, string query = "") =>
        await (await c.GetAsync($"/api/v1/notifications?pageSize=100{query}")).Json();

    private static IEnumerable<JsonElement> ForTicket(JsonElement page, JsonElement ticket) =>
        page.GetProperty("items").EnumerateArray().Where(n => n.TryGetProperty("ticketId", out var id) && id.ValueKind == JsonValueKind.String && id.GetGuid() == ticket.Guid("id"));

    [Fact]
    public async Task NewTicket_NotifiesTeamAgents_NotOtherTeamNorRequester()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana, "Hardware"); // N1: Camila e Rafael

        Assert.Contains(ForTicket(await Mine(await As(Seeded.Camila)), ticket), n => n.Str("type") == "NEW_TICKET");
        Assert.Contains(ForTicket(await Mine(await As(Seeded.Rafael)), ticket), n => n.Str("type") == "NEW_TICKET");
        Assert.Empty(ForTicket(await Mine(await As(Seeded.Bruno)), ticket));
        Assert.Empty(ForTicket(await Mine(ana), ticket));
    }

    [Fact]
    public async Task Assignment_NotifiesAssigneeAndRequester()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);
        var admin = await As(Seeded.Admin);
        await admin.PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Camila) });

        Assert.Contains(ForTicket(await Mine(await As(Seeded.Camila)), ticket), n => n.Str("type") == "TICKET_ASSIGNED");
        var forRequester = ForTicket(await Mine(ana), ticket).Single(n => n.Str("type") == "TICKET_ASSIGNED");
        Assert.Contains("Camila Torres", forRequester.Str("message"));
    }

    [Fact]
    public async Task PublicReply_NotifiesRequester_InternalNoteDoesNot_AndRequesterReplyNotifiesAssignee()
    {
        var ana = await As(Seeded.Ana);
        var camila = await As(Seeded.Camila);
        var ticket = await AssignedTicket(ana);

        await Say(camila, ticket, "nota interna", internalNote: true);
        Assert.DoesNotContain(ForTicket(await Mine(ana), ticket), n => n.Str("type") == "NEW_MESSAGE");

        await Say(camila, ticket, "resposta pública");
        Assert.Single(ForTicket(await Mine(ana), ticket), n => n.Str("type") == "NEW_MESSAGE");

        await Say(ana, ticket, "obrigada");
        Assert.Contains(ForTicket(await Mine(camila), ticket), n => n.Str("type") == "NEW_MESSAGE");
    }

    [Fact]
    public async Task StatusChange_NotifiesRequester_ButNotTheActor()
    {
        var ana = await As(Seeded.Ana);
        var camila = await As(Seeded.Camila);
        var ticket = await AssignedTicket(ana);
        await camila.PatchJsonAsync(Url(ticket, "/status"), new { status = "RESOLVED" });

        var forAna = ForTicket(await Mine(ana), ticket).Where(n => n.Str("type") == "STATUS_CHANGED").ToList();
        Assert.Single(forAna);
        Assert.Contains("Resolvido", forAna[0].Str("message"));
        Assert.DoesNotContain(ForTicket(await Mine(camila), ticket), n => n.Str("type") == "STATUS_CHANGED");
    }

    [Fact]
    public async Task ReadStateAndCounters_AreScopedToTheOwner()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await AssignedTicket(ana);
        await Say(await As(Seeded.Camila), ticket, "oi");

        var before = (await (await ana.GetAsync("/api/v1/notifications/unread-count")).Json()).GetProperty("count").GetInt32();
        Assert.True(before >= 1);

        var first = (await Mine(ana, "&unreadOnly=true")).GetProperty("items")[0];
        // Outro usuário não pode marcar a notificação alheia (IDOR).
        Assert.Equal(HttpStatusCode.NotFound,
            (await (await As(Seeded.Carlos)).PatchAsync($"/api/v1/notifications/{first.Guid("id")}/read", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await ana.PatchAsync($"/api/v1/notifications/{first.Guid("id")}/read", null)).StatusCode);
        var after = (await (await ana.GetAsync("/api/v1/notifications/unread-count")).Json()).GetProperty("count").GetInt32();
        Assert.Equal(before - 1, after);

        Assert.Equal(HttpStatusCode.NoContent, (await ana.PostAsync("/api/v1/notifications/read-all", null)).StatusCode);
        Assert.Equal(0, (await (await ana.GetAsync("/api/v1/notifications/unread-count")).Json()).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Notifications_RequireAuthentication_AndPaginationIsValidated()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Factory.CreateClient().GetAsync("/api/v1/notifications")).StatusCode);
    }
}

[Collection(ApiCollection.Name)]
public sealed class RealtimeTests(ApiFactory factory) : CommunicationTestBase(factory)
{
    private sealed class Listener : IAsyncDisposable
    {
        private readonly HubConnection _connection;
        public Channel<JsonElement> Events { get; } = Channel.CreateUnbounded<JsonElement>();
        public Channel<JsonElement> Notifications { get; } = Channel.CreateUnbounded<JsonElement>();

        public Listener(ApiFactory factory, string? token)
        {
            _connection = new HubConnectionBuilder()
                .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/tickets"), o =>
                {
                    o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    o.Transports = HttpTransportType.LongPolling;
                    o.AccessTokenProvider = () => Task.FromResult(token);
                })
                .Build();
            _connection.On<JsonElement>("ticketEvent", e => Events.Writer.TryWrite(e));
            _connection.On<JsonElement>("notification", n => Notifications.Writer.TryWrite(n));
        }

        public Task StartAsync() => _connection.StartAsync();

        public async Task<JsonElement?> NextAsync(Channel<JsonElement> channel, int seconds = 5)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            try { return await channel.Reader.ReadAsync(cts.Token); }
            catch (OperationCanceledException) { return null; }
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    private async Task<string> TokenFor(string email)
    {
        var res = await Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = ApiFactory.TestPassword });
        return (await res.Json()).Str("accessToken");
    }

    private async Task<Listener> Connect(string email)
    {
        var listener = new Listener(Factory, await TokenFor(email));
        await listener.StartAsync();
        return listener;
    }

    [Fact]
    public async Task Hub_RejectsAnonymousAndGarbageTokens()
    {
        await using var anonymous = new Listener(Factory, null);
        await Assert.ThrowsAnyAsync<Exception>(() => anonymous.StartAsync());

        await using var garbage = new Listener(Factory, "abc.def.ghi");
        await Assert.ThrowsAnyAsync<Exception>(() => garbage.StartAsync());
    }

    [Fact]
    public async Task Hub_AcceptsQueryStringToken_OnlyForHubPaths()
    {
        var token = await TokenFor(Seeded.Ana);
        var client = Factory.CreateClient();

        // Navegadores enviam o token na query ao abrir o WebSocket.
        var negotiate = await client.PostAsync($"/hubs/tickets/negotiate?negotiateVersion=1&access_token={token}", null);
        Assert.Equal(HttpStatusCode.OK, negotiate.StatusCode);

        // Fora do hub, o token na query NÃO autentica (evita vazamento via URL em endpoints REST).
        var rest = await client.GetAsync($"/api/v1/auth/me?access_token={token}");
        Assert.Equal(HttpStatusCode.Unauthorized, rest.StatusCode);
    }

    [Fact]
    public async Task NewTicket_IsPushedToTeamAgentsAndAdmins_NotToOtherTeams()
    {
        await using var camila = await Connect(Seeded.Camila);
        await using var admin = await Connect(Seeded.Admin);
        await using var bruno = await Connect(Seeded.Bruno);
        await using var carlos = await Connect(Seeded.Carlos);

        var hardware = await OpenTicket(await As(Seeded.Ana), "Hardware"); // N1
        var network = await OpenTicket(await As(Seeded.Ana), "Rede e Internet"); // Infra

        var forCamila = await camila.NextAsync(camila.Events);
        Assert.Equal("ticket.created", forCamila!.Value.Str("type"));
        Assert.Equal(hardware.Guid("id"), forCamila.Value.Guid("ticketId"));

        var forAdminFirst = await admin.NextAsync(admin.Events);
        Assert.Equal(hardware.Guid("id"), forAdminFirst!.Value.Guid("ticketId"));

        // Bruno (Infra) só recebe o chamado de rede; nunca o de hardware.
        var forBruno = await bruno.NextAsync(bruno.Events);
        Assert.Equal(network.Guid("id"), forBruno!.Value.Guid("ticketId"));

        // Outro solicitante não recebe nenhum dos dois.
        Assert.Null(await carlos.NextAsync(carlos.Events, seconds: 1));
    }

    [Fact]
    public async Task Message_IsPushedToRequester_ButInternalNoteIsNot()
    {
        var ana = await As(Seeded.Ana);
        var camila = await As(Seeded.Camila);
        var ticket = await AssignedTicket(ana);

        await using var requester = await Connect(Seeded.Ana);
        // Descarta eventos anteriores à conexão, se houver.
        while (await requester.NextAsync(requester.Events, 1) is not null) { }

        var note = await (await Say(camila, ticket, "nota interna sigilosa", internalNote: true)).Json();
        var reply = await (await Say(camila, ticket, "resposta pública")).Json();

        var first = await requester.NextAsync(requester.Events);
        Assert.Equal("ticket.message", first!.Value.Str("type"));
        Assert.Equal(reply.Guid("id"), first.Value.Guid("messageId")); // a nota (enviada antes) não chegou
        Assert.NotEqual(note.Guid("id"), first.Value.Guid("messageId"));
        Assert.DoesNotContain("sigilosa", first.Value.GetRawText());
        Assert.False(first.Value.GetProperty("isInternal").GetBoolean());
    }

    [Fact]
    public async Task InternalNote_ReachesStaffOfTheTeam()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await AssignedTicket(ana);
        await using var rafael = await Connect(Seeded.Rafael); // membro da equipe N1
        while (await rafael.NextAsync(rafael.Events, 1) is not null) { }

        var note = await (await Say(await As(Seeded.Camila), ticket, "triagem", internalNote: true)).Json();

        var evt = await rafael.NextAsync(rafael.Events);
        Assert.Equal(note.Guid("id"), evt!.Value.Guid("messageId"));
        Assert.True(evt.Value.GetProperty("isInternal").GetBoolean());
    }

    [Fact]
    public async Task Notification_IsPushedToTheTargetUser()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await AssignedTicket(ana);
        await using var requester = await Connect(Seeded.Ana);
        while (await requester.NextAsync(requester.Notifications, 1) is not null) { }

        await Say(await As(Seeded.Camila), ticket, "resposta");

        var n = await requester.NextAsync(requester.Notifications);
        Assert.Equal("NEW_MESSAGE", n!.Value.Str("type"));
        Assert.Equal(ticket.Guid("id"), n.Value.Guid("ticketId"));
    }

    [Fact]
    public async Task Http_KeepsWorking_WhenNobodyIsConnected()
    {
        var ticket = await AssignedTicket(await As(Seeded.Ana));
        Assert.Equal(HttpStatusCode.Created, (await Say(await As(Seeded.Camila), ticket, "sem hub conectado")).StatusCode);
    }
}
