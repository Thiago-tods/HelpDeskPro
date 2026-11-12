using HelpDeskPro.Application.Attachments;
using HelpDeskPro.Application.Tickets;
using HelpDeskPro.Domain.Enums;

namespace HelpDeskPro.UnitTests;

public class FileInspectorTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 1];

    [Fact]
    public void Png_WithMatchingSignature_IsAccepted() =>
        Assert.Equal("image/png", FileInspector.Inspect("a.PNG", "image/png", Png).ContentType);

    [Fact]
    public void Extension_MustMatchContent() =>
        Assert.False(FileInspector.Inspect("a.png", "image/png", Jpeg).IsValid);

    [Theory]
    [InlineData("a.exe")]
    [InlineData("a.html")]
    [InlineData("a.svg")]
    [InlineData("a.png.exe")]
    [InlineData("a")]
    [InlineData(".png.")]
    public void UnknownOrDangerousExtensions_AreRejected(string name) =>
        Assert.False(FileInspector.Inspect(name, null, Png).IsValid);

    [Theory]
    [InlineData("application/x-msdownload")]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    public void DisallowedDeclaredType_IsRejected(string declared) =>
        Assert.False(FileInspector.Inspect("a.png", declared, Png).IsValid);

    [Fact]
    public void MissingDeclaredType_IsToleratedBecauseContentIsVerified() =>
        Assert.True(FileInspector.Inspect("a.png", null, Png).IsValid);

    [Fact]
    public void EmptyFile_IsRejected() => Assert.False(FileInspector.Inspect("a.png", null, []).IsValid);

    [Fact]
    public void Pdf_NeedsMagicHeader()
    {
        Assert.True(FileInspector.Inspect("a.pdf", "application/pdf", "%PDF-1.7\n"u8.ToArray()).IsValid);
        Assert.False(FileInspector.Inspect("a.pdf", "application/pdf", "<html></html>"u8.ToArray()).IsValid);
    }

    [Fact]
    public void Text_MustBeValidUtf8WithoutBinaryControlChars()
    {
        Assert.True(FileInspector.Inspect("a.txt", "text/plain", "olá\nmundo\t!"u8.ToArray()).IsValid);
        Assert.False(FileInspector.Inspect("a.txt", "text/plain", [0x68, 0x00, 0x69]).IsValid);
        Assert.False(FileInspector.Inspect("a.csv", "text/csv", [0xC3, 0x28]).IsValid);
    }

    [Theory]
    [InlineData("../../segredo.png", "segredo.png")]
    [InlineData("..\\..\\windows\\evil.png", "evil.png")]
    [InlineData("C:\\temp\\a.png", "a.png")]
    [InlineData("...", "arquivo")]
    [InlineData("", "arquivo")]
    public void SanitizeFileName_StripsPaths(string input, string expected) =>
        Assert.Equal(expected, FileInspector.SanitizeFileName(input));

    [Fact]
    public void SanitizeFileName_LimitsLength() =>
        Assert.True(FileInspector.SanitizeFileName(new string('a', 500) + ".png").Length <= 200);
}

public class MessageAccessTests
{
    [Theory]
    [InlineData(Roles.Admin, false, false, true)]
    [InlineData(Roles.Admin, false, true, true)]
    [InlineData(Roles.Agent, true, false, true)]
    [InlineData(Roles.Agent, false, true, true)]
    [InlineData(Roles.Agent, false, false, false)]
    [InlineData(Roles.Requester, false, false, true)]
    [InlineData(Roles.Requester, false, true, false)]
    public void CheckMessage_FollowsRoleRules(string role, bool isAssignee, bool isInternal, bool allowed) =>
        Assert.Equal(allowed, TicketAccess.CheckMessage(role, isAssignee, isInternal) is null);
}
