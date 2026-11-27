using System.Text;
using Lumerist.Core;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>The bridge token the tests' clients send (protocol 4).</summary>
internal static class TestToken
{
    public const string Value = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
}

/// <summary>The bridge token file (protocol 4), as the Mac's BridgeTokenTests.swift.</summary>
public sealed class BridgeTokenTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"lumerist-token-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void GeneratedTokensAreLongRandomAndLowercaseHex()
    {
        var first = BridgeToken.Generate();
        var second = BridgeToken.Generate();
        Assert.True(BridgeToken.IsValid(first));
        Assert.True(BridgeToken.IsValid(second));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void OnlySixtyFourLowercaseHexCharactersAreValid()
    {
        Assert.True(BridgeToken.IsValid(TestToken.Value));
        Assert.False(BridgeToken.IsValid(TestToken.Value[1..]));
        Assert.False(BridgeToken.IsValid(TestToken.Value + "0"));
        Assert.False(BridgeToken.IsValid(TestToken.Value.ToUpperInvariant()));
        Assert.False(BridgeToken.IsValid(TestToken.Value[1..] + "g"));
        Assert.False(BridgeToken.IsValid(""));
        Assert.False(BridgeToken.IsValid(null));
    }

    [Fact]
    public void TheFileIsCreatedOnceAndReused()
    {
        var data = Path.Combine(folder, "Lumerist");
        var token = BridgeToken.LoadOrCreate(data);
        Assert.True(BridgeToken.IsValid(token));
        Assert.Equal(token, File.ReadAllText(BridgeToken.PathIn(data), Encoding.UTF8));
        Assert.Equal(Path.Combine(data, "bridge-token"), BridgeToken.PathIn(data));
        Assert.Equal(token, BridgeToken.LoadOrCreate(data));
        Assert.Empty(Directory.GetFiles(data, "*.tmp"));
    }

    [Fact]
    public void ABrokenFileIsReplacedAndAWellFormedOneKept()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(BridgeToken.PathIn(folder), "not a token");
        Assert.True(BridgeToken.IsValid(BridgeToken.LoadOrCreate(folder)));

        File.WriteAllText(BridgeToken.PathIn(folder), TestToken.Value.ToUpperInvariant() + "\r\n");
        Assert.Equal(TestToken.Value, BridgeToken.LoadOrCreate(folder));
    }
}
