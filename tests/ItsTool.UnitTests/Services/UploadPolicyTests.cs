using System.Text;
using ItsTool.Infrastructure.Services;
using Xunit;

namespace ItsTool.UnitTests.Services;

public class UploadPolicyTests
{
    [Theory]
    [InlineData("drawing.svg")]
    [InlineData("page.html")]
    [InlineData("tool.exe")]
    [InlineData("noextension")]
    public void Types_a_browser_could_run_or_unknown_types_are_not_allowed(string fileName)
    {
        Assert.Null(UploadPolicy.ContentTypeFor(fileName));
    }

    [Theory]
    [InlineData("data.xml")]
    [InlineData("data.json")]
    [InlineData("README.MD")]
    [InlineData("query.sql")]
    public void Text_formats_are_served_as_plain_text(string fileName)
    {
        Assert.Equal("text/plain", UploadPolicy.ContentTypeFor(fileName));
    }

    [Fact]
    public void Tar_gz_archives_are_allowed()
    {
        Assert.Equal("application/gzip", UploadPolicy.ContentTypeFor("logs.tar.gz"));
        Assert.True(UploadPolicy.HasExpectedSignature("logs.tar.gz", new byte[] { 0x1F, 0x8B, 0x08 }));
    }

    [Theory]
    [InlineData("photo.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    [InlineData("photo.jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
    [InlineData("report.docx", new byte[] { 0x50, 0x4B, 0x03, 0x04 })]
    [InlineData("sheet.xls", new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 })]
    public void Files_whose_content_matches_their_type_pass(string fileName, byte[] header)
    {
        Assert.True(UploadPolicy.HasExpectedSignature(fileName, header));
    }

    [Theory]
    [InlineData("photo.png", "<html><script>alert(1)</script>")]
    [InlineData("report.pdf", "plain text")]
    [InlineData("archive.zip", "")]
    public void Files_whose_content_does_not_match_their_type_fail(string fileName, string content)
    {
        Assert.False(UploadPolicy.HasExpectedSignature(fileName, Encoding.UTF8.GetBytes(content)));
    }

    [Fact]
    public void Webp_needs_both_parts_of_its_signature()
    {
        var webp = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ");
        var wav = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt ");

        Assert.True(UploadPolicy.HasExpectedSignature("a.webp", webp));
        Assert.False(UploadPolicy.HasExpectedSignature("a.webp", wav));
    }

    [Theory]
    [InlineData(@"x\..\..\wwwroot\a.txt", "a.txt")]
    [InlineData("x/../../a.txt", "a.txt")]
    [InlineData(" notes.txt ", "notes.txt")]
    public void The_display_name_drops_any_folder_part(string fileName, string expected)
    {
        Assert.Equal(expected, UploadPolicy.DisplayName(fileName));
    }
}
