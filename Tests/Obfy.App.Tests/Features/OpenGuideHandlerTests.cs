using Mediator;
using Obfy.App;
using Obfy.App.Features.App;
using Obfy.App.Services;
using Obfy.App.Tests.Common;
using Shouldly;

namespace Obfy.App.Tests.Features;

public sealed class OpenGuideHandlerTests
{
    private readonly FakeShell _shell = new();

    private async Task<Unit> Open(string? topic) =>
        await new OpenGuideRequestHandler(_shell).Handle(new OpenGuideRequest(topic), default);

    [Theory]
    [InlineData("getting-started")]
    [InlineData("files-and-output")]
    [InlineData("levels-and-settings")]
    [InlineData("techniques")]
    [InlineData("what-can-break")]
    [InlineData("results")]
    [InlineData("stack-traces")]
    [InlineData("shortcuts")]
    [InlineData("command-line")]
    [InlineData("ide-and-build")]
    [InlineData("troubleshooting")]
    public async Task Handle_KnownTopic_OpensThatSectionOfTheGuide(string topic)
    {
        await Open(topic);

        _shell.OpenedUrls.Select(u => u.AbsoluteUri).ShouldBe([$"https://brudvik.com/obfy/guide#{topic}"]);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("start")] // the in-app quick-start's ids are not the online guide's
    [InlineData("tech")]
    [InlineData("keys")]
    [InlineData("TECHNIQUES")]
    [InlineData("techniques ")]
    [InlineData("techniques#x")]
    [InlineData("https://evil.example/")]
    [InlineData("http://brudvik.com/obfy/guide")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("../techniques")]
    public async Task Handle_UnknownTopic_OpensTheBaseGuideUrl(string topic)
    {
        await Open(topic);

        _shell.OpenedUrls.Select(u => u.AbsoluteUri).ShouldBe(["https://brudvik.com/obfy/guide"]);
    }

    [Fact]
    public async Task Handle_NullTopic_OpensTheBaseGuideUrl()
    {
        await Open(null);

        _shell.OpenedUrls.Select(u => u.AbsoluteUri).ShouldBe(["https://brudvik.com/obfy/guide"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("getting-started")]
    [InlineData("unknown")]
    [InlineData("http://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:")]
    public async Task Handle_WhateverThePageSends_OnlyAnHttpsBrudvikAddressReachesTheShell(string? topic)
    {
        await Open(topic);

        var url = _shell.OpenedUrls.ShouldHaveSingleItem();
        url.IsAbsoluteUri.ShouldBeTrue();
        url.Scheme.ShouldBe(Uri.UriSchemeHttps);
        url.Host.ShouldBe("brudvik.com");
        url.AbsolutePath.ShouldBe("/obfy/guide");
        url.Query.ShouldBeEmpty();
        url.UserInfo.ShouldBeEmpty();
    }

    [Fact]
    public void Topics_AreTheSeededClosedSet_AndEachMapsToAnHttpsUrlUnderTheBase()
    {
        // The section ids of https://brudvik.com/obfy/guide: a contract with the web guide's anchors.
        GuideLinks.Topics.Keys.ShouldBe(
            [
                "getting-started", "files-and-output", "levels-and-settings", "techniques", "what-can-break", "results",
                "stack-traces", "shortcuts", "command-line", "ide-and-build", "troubleshooting",
            ],
            ignoreOrder: true);
        GuideLinks.BaseUrl.ShouldStartWith("https://");

        foreach (var (id, fragment) in GuideLinks.Topics)
        {
            var url = GuideLinks.Resolve(id);

            url.Scheme.ShouldBe(Uri.UriSchemeHttps, id);
            url.AbsoluteUri.ShouldBe($"{GuideLinks.BaseUrl}#{fragment}", id);
        }
    }

    [Fact]
    public async Task Handle_LaunchFailure_IsAUserFacingError_WithoutTheCauseOrTheTopic()
    {
        _shell.OpenUrlFailure = new IOException("secret internal detail: Win32 error 1155");

        var ex = await Should.ThrowAsync<ObfyApiException>(async () => await Open("stack-traces"));

        ex.Code.ShouldBe("app.openGuideFailed");
        ex.Message.ShouldNotBeNullOrWhiteSpace();
        ex.Message.ShouldNotContain("secret");
        ex.Message.ShouldNotContain("Win32");
        ex.Message.ShouldNotContain("stack-traces");
        ex.InnerException.ShouldBeNull();
        _shell.OpenedUrls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_Success_AnswersNothingTheTopicCouldBeReadFrom()
    {
        var reply = await Open("stack-traces");

        reply.ShouldBe(Unit.Value);
    }

    // ---- ShellLauncher itself: the https rule holds before anything is started ----

    [Theory]
    [InlineData("http://brudvik.com/obfy/guide")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:")]
    [InlineData("ftp://brudvik.com/")]
    [InlineData("javascript:alert(1)")]
    public void ShellLauncher_OpenUrl_RejectsAnythingButHttps(string url) =>
        Should.Throw<ArgumentException>(() => new ShellLauncher().OpenUrl(new Uri(url)));

    [Fact]
    public void ShellLauncher_OpenUrl_RejectsARelativeOrNullAddress()
    {
        Should.Throw<ArgumentException>(() => new ShellLauncher().OpenUrl(new Uri("guide", UriKind.Relative)));
        Should.Throw<ArgumentException>(() => new ShellLauncher().OpenUrl(null!));
    }
}
