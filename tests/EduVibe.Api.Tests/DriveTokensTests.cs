using EduVibe.Modules.Materials.Drive;
using Microsoft.Extensions.Configuration;

namespace EduVibe.Api.Tests;

/// <summary>The two secrets kept by the Drive link: the teacher's stored Google token, and the state that carries who is connecting through Google's page and back.</summary>
public class DriveTokensTests
{
    private static DriveTokens With(string signingKey) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SigningKey"] = signingKey }).Build());

    private static readonly DateTimeOffset Now = new(2026, 10, 20, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_stored_token_reads_back_but_never_looks_like_the_token()
    {
        var tokens = With("signing-key-one-signing-key-one-0123456789");
        var stored = tokens.Protect("1//refresh-token-from-google");
        Assert.DoesNotContain("refresh-token", stored);
        Assert.Equal("1//refresh-token-from-google", tokens.TryUnprotect(stored));

        // Each save is different, so two teachers with the same token cannot be matched up.
        Assert.NotEqual(stored, tokens.Protect("1//refresh-token-from-google"));
    }

    [Fact]
    public void A_stored_token_is_unreadable_after_the_signing_key_changes_or_when_damaged()
    {
        var stored = With("signing-key-one-signing-key-one-0123456789").Protect("secret");
        Assert.Null(With("signing-key-two-signing-key-two-0123456789").TryUnprotect(stored));

        var tokens = With("signing-key-one-signing-key-one-0123456789");
        var bytes = Convert.FromBase64String(stored);
        bytes[^1] ^= 0xFF;
        Assert.Null(tokens.TryUnprotect(Convert.ToBase64String(bytes)));
        Assert.Null(tokens.TryUnprotect("not base64!"));
        Assert.Null(tokens.TryUnprotect("AAAA"));
        Assert.Null(tokens.TryUnprotect(""));
    }

    [Fact]
    public void The_state_names_the_teacher_for_ten_minutes_only()
    {
        var tokens = With("signing-key-one-signing-key-one-0123456789");
        var teacher = Guid.NewGuid();
        var state = tokens.SignState(teacher, Now);

        Assert.Equal(teacher, tokens.ReadState(state, Now));
        Assert.Equal(teacher, tokens.ReadState(state, Now.AddMinutes(9)));
        Assert.Null(tokens.ReadState(state, Now.AddMinutes(11)));
    }

    [Fact]
    public void A_state_made_up_changed_or_signed_elsewhere_names_nobody()
    {
        var tokens = With("signing-key-one-signing-key-one-0123456789");
        var state = tokens.SignState(Guid.NewGuid(), Now);
        var payload = state.Split('.')[0];

        Assert.Null(tokens.ReadState(null, Now));
        Assert.Null(tokens.ReadState("", Now));
        Assert.Null(tokens.ReadState("abc", Now));
        Assert.Null(tokens.ReadState("a.b.c", Now));
        Assert.Null(tokens.ReadState($"{payload}.AAAA", Now));
        Assert.Null(tokens.ReadState($"{payload}x.{state.Split('.')[1]}", Now));
        Assert.Null(With("signing-key-two-signing-key-two-0123456789").ReadState(state, Now));
    }

    [Fact]
    public void The_token_key_and_the_state_key_are_not_the_same()
    {
        // A state must not decrypt as a token, nor a token pass as a state, even though both come from one signing key.
        var tokens = With("signing-key-one-signing-key-one-0123456789");
        Assert.Null(tokens.TryUnprotect(tokens.SignState(Guid.NewGuid(), Now)));
        Assert.Null(tokens.ReadState(tokens.Protect("x"), Now));
    }
}
