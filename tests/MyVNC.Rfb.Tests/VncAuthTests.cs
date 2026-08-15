using Xunit;

namespace MyVNC.Rfb.Tests;

public class VncAuthTests
{
    [Fact]
    public void EncryptChallenge_ProducesSixteenBytes()
    {
        var challenge = new byte[16];
        var result = VncAuth.EncryptChallenge("secret", challenge);
        Assert.Equal(16, result.Length);
    }

    [Fact]
    public void EncryptChallenge_IsDeterministic()
    {
        var challenge = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var a = VncAuth.EncryptChallenge("hunter2", challenge);
        var b = VncAuth.EncryptChallenge("hunter2", challenge);
        Assert.Equal(a, b);
    }

    [Fact]
    public void EncryptChallenge_DifferentPasswords_ProduceDifferentOutput()
    {
        // Only the first 8 characters matter (DES key length) — these two must differ within
        // that prefix, unlike e.g. "password1"/"password2" which share "password" and would
        // collide by design, not by bug.
        var challenge = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var a = VncAuth.EncryptChallenge("alpha123", challenge);
        var b = VncAuth.EncryptChallenge("beta4567", challenge);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EncryptChallenge_PasswordLongerThanEightBytes_OnlyFirstEightMatter()
    {
        // Real VNC clients truncate to 8 chars; verify our implementation does the same
        // (matches the DES-key-derivation loop capping at 8 bytes).
        var challenge = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var a = VncAuth.EncryptChallenge("12345678", challenge);
        var b = VncAuth.EncryptChallenge("12345678ignored-tail", challenge);
        Assert.Equal(a, b);
    }

    [Fact]
    public void EncryptChallenge_DifferentChallenges_ProduceDifferentOutput()
    {
        var challengeA = new byte[16];
        var challengeB = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        var a = VncAuth.EncryptChallenge("same-password", challengeA);
        var b = VncAuth.EncryptChallenge("same-password", challengeB);
        Assert.NotEqual(a, b);
    }
}
