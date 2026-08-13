using Brainy.Web.Security;
using AwesomeAssertions;
using Xunit;

namespace Brainy.Web.Tests.Security;

public class MarkdownHtmlRendererTests
{
    [Theory]
    [InlineData("javascript:alert(document.domain)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("javascript%3Aalert(1)")]
    [InlineData("javascript%253Aalert(1)")]
    [InlineData("javascript&#58;alert(1)")]
    [InlineData("javascript&colon;alert(1)")]
    [InlineData("jav&#x61;script:alert(1)")]
    [InlineData("java&#x09;script:alert(1)")]
    [InlineData("jav&Tab;ascript:alert(1)")]
    [InlineData("java%00script:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("//evil.example/steal")]
    public void Render_BlocksUnsafeOrDisguisedDestinations(string destination)
    {
        var html = MarkdownHtmlRenderer.Render($"[open]({destination})");

        html.Should().Contain("href=\"#\"");
        html.Should().NotContain(destination);
    }

    [Theory]
    [InlineData("https://example.com/path")]
    [InlineData("http://example.com/path")]
    [InlineData("mailto:person@example.com")]
    [InlineData("/notes/123")]
    [InlineData("notes/123")]
    [InlineData("../notes/123")]
    [InlineData("#heading")]
    [InlineData("?page=2")]
    public void Render_AllowsApprovedAbsoluteAndRelativeDestinations(string destination)
    {
        var html = MarkdownHtmlRenderer.Render($"[open]({destination})");

        html.Should().Contain($"href=\"{destination}\"");
    }

    [Fact]
    public void Render_DoesNotPassRawHtmlThrough()
    {
        var html = MarkdownHtmlRenderer.Render("<script>alert(document.domain)</script>");

        html.Should().NotContain("<script>");
    }

    [Fact]
    public void Render_BlocksUnsafeReferenceLinkDestination()
    {
        var html = MarkdownHtmlRenderer.Render("[open][unsafe]\n\n[unsafe]: javascript:alert(1)");

        html.Should().Contain("href=\"#\"");
        html.Should().NotContain("javascript:");
    }
}
