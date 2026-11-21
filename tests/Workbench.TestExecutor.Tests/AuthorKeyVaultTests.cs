using System.Text;
using Xunit;

namespace Workbench.TestExecutor.Tests;

public sealed class AuthorKeyVaultTests
{
    [Fact]
    public void A_saved_key_is_ciphertext_and_round_trips()
    {
        var preview = Fixtures.Temp();
        const string key = "sample-key-0001";
        Assert.True(TestAuthorKeys.Vault.Save(preview, key));
        Assert.False(TestAuthorKeys.Vault.Save(preview, "short"));
        var path = Path.Combine(preview, AuthorKeyVault.FileName);
        var stored = File.ReadAllBytes(path);
        Assert.DoesNotContain(key, Encoding.UTF8.GetString(stored), StringComparison.Ordinal);
        Assert.Equal(key, TestAuthorKeys.Vault.Read(preview));
        Assert.Equal(key, TestAuthorKeys.Secrets().Read(preview));
        Assert.Null(ExecutablePath.NormalizeLaunch(Path.Combine(preview, "missing.exe")));
        Directory.Delete(preview, true);
    }
}

internal static class TestAuthorKeys
{
    public static AuthorKeyVault Vault { get; } = new(new ReversibleAuthorKeyProtector());

    public static AuthorSecrets Secrets(Func<string, string?>? environment = null)
        => new(environment ?? (_ => null), Vault);
}

internal sealed class ReversibleAuthorKeyProtector : IAuthorKeyProtector
{
    public byte[] Protect(byte[] plaintext)
    {
        var cipher = new byte[plaintext.Length];
        for (var index = 0; index < plaintext.Length; index++) cipher[index] = (byte)(plaintext[index] ^ (0xA5 + index));
        return cipher;
    }

    public byte[] Unprotect(byte[] ciphertext) => Protect(ciphertext);
}
