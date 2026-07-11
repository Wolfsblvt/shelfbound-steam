using Shelfbound.Tray;
using Shouldly;

namespace Shelfbound.Tray.Tests;

public sealed class LoopbackRedirectUriTests
{
    [Theory]
    [InlineData("http://example.com:49152/")]
    [InlineData("https://127.0.0.1:49152/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/plain,hello")]
    [InlineData("http://user@127.0.0.1:49152/")]
    [InlineData("http://127.0.0.1:49152/#fragment")]
    [InlineData("http://%31%32%37.0.0.1:49152/")]
    [InlineData("http://127。0。0。1:49152/")]
    [InlineData("http://localhost:49152/")]
    [InlineData("http://127.1:49152/")]
    [InlineData("http://2130706433:49152/")]
    [InlineData("http://0x7f000001:49152/")]
    [InlineData("http://127.000.000.001:49152/")]
    [InlineData("http://[0:0:0:0:0:0:0:1]:49152/")]
    [InlineData("http://[::ffff:127.0.0.1]:49152/")]
    [InlineData("http://[::01]:49152/")]
    [InlineData("http://::1:49152/")]
    [InlineData("http://127.0.0.1:49152/callback")]
    [InlineData("http://127.0.0.1:49152")]
    [InlineData("http://127.0.0.1:49152/?code=value")]
    [InlineData("HTTP://127.0.0.1:49152/")]
    [InlineData("http:\\127.0.0.1:49152/")]
    [InlineData("http://127.0.0.1:049152/")]
    [InlineData("http://127.0.0.1:0/")]
    [InlineData("http://127.0.0.1:65536/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.0.0.1:49152/\r\nHost: example.com")]
    public void TryCreateRejectsNonCanonicalCallbackBeforeUriParsing(string value)
    {
        bool accepted = LoopbackRedirectUri.TryCreate(value, out LoopbackRedirectUri? redirectUri);

        accepted.ShouldBeFalse("only the frozen lexical numeric-loopback spellings are valid");
        redirectUri.ShouldBeNull();
    }

    [Theory]
    [InlineData("http://127.0.0.1:1/", 1)]
    [InlineData("http://127.0.0.1:65535/", 65535)]
    [InlineData("http://[::1]:49152/", 49152)]
    public void TryCreateAcceptsOnlyCanonicalIpv4OrIpv6Loopback(string value, int expectedPort)
    {
        bool accepted = LoopbackRedirectUri.TryCreate(value, out LoopbackRedirectUri? redirectUri);

        accepted.ShouldBeTrue();
        redirectUri.ShouldNotBeNull();
        redirectUri.Value.ShouldBe(value);
        redirectUri.Port.ShouldBe(expectedPort);
    }

    [Theory]
    [InlineData("POST", false, "127.0.0.1:49152", "/?code=sbc_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&state=expected")]
    [InlineData("GET", true, "127.0.0.1:49152", "/?code=sbc_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&state=expected")]
    [InlineData("GET", false, "localhost:49152", "/?code=sbc_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&state=expected")]
    [InlineData("GET", false, "127.0.0.1:49152", "/callback?code=sbc_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&state=expected")]
    [InlineData("GET", false, "127.0.0.1:49152", "/?code=sbc_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("GET", false, "127.0.0.1:49152", "/?code=sbc_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&state=wrong")]
    [InlineData("GET", false, "127.0.0.1:49152", "/?code=sbc_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&state=expected&extra=value")]
    [InlineData("GET", false, "127.0.0.1:49152", "/?code=sbc_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&code=sbc_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("GET", false, "127.0.0.1:49152", "/?code=not-a-connect-code&state=expected")]
    [InlineData("GET", false, "127.0.0.1:49152", "/?code=sbc_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&state=expected#fragment")]
    [InlineData("GET", false, "127.0.0.1:49152", "/?code=sbc_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&state=expected\\suffix")]
    public void TryReadRejectsUnexpectedCallbackRequests(
        string method,
        bool secure,
        string host,
        string rawTarget)
    {
        LoopbackRedirectUri.TryCreate(
            "http://127.0.0.1:49152/",
            out LoopbackRedirectUri? expectedRedirectUri).ShouldBeTrue();

        bool accepted = LoopbackCallback.TryRead(
            method,
            secure,
            host,
            rawTarget,
            expectedRedirectUri!,
            "expected",
            out string? code);

        accepted.ShouldBeFalse("method, origin, path, fields, code, and state must all match exactly");
        code.ShouldBeNull();
    }

    [Fact]
    public void TryReadAcceptsEncodedCodeAndStateOnTheExactCallback()
    {
        const string expectedCode = "sbc_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string expectedState = "state-value";
        LoopbackRedirectUri.TryCreate(
            "http://[::1]:49152/",
            out LoopbackRedirectUri? expectedRedirectUri).ShouldBeTrue();
        string rawTarget = $"/?state={Uri.EscapeDataString(expectedState)}&code={Uri.EscapeDataString(expectedCode)}";

        bool accepted = LoopbackCallback.TryRead(
            "GET",
            isSecureConnection: false,
            "[::1]:49152",
            rawTarget,
            expectedRedirectUri!,
            expectedState,
            out string? code);

        accepted.ShouldBeTrue();
        code.ShouldBe(expectedCode);
    }

    [Fact]
    public void CreateStateProducesIndependentCryptographicBindingValues()
    {
        string first = ConnectFlow.CreateState();
        string second = ConnectFlow.CreateState();

        first.Length.ShouldBe(64);
        second.Length.ShouldBe(64);
        first.ShouldNotBe(second);
        first.All(character => char.IsAsciiHexDigit(character)).ShouldBeTrue();
        second.All(character => char.IsAsciiHexDigit(character)).ShouldBeTrue();
    }
}
