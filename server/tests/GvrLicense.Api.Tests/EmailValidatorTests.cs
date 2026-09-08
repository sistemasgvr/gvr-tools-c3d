using GvrLicense.Domain.Validation;
using Xunit;

namespace GvrLicense.Api.Tests;

public sealed class EmailValidatorTests
{
    [Theory]
    [InlineData("juan@empresa.com")]
    [InlineData("Juan.Perez+tag@empresa.co")]
    public void Accepts_valid_emails(string email)
    {
        Assert.True(EmailValidator.TryNormalize(email, out var normalized, out var error), error);
        Assert.Equal(email.Trim().ToLowerInvariant(), normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("missing-at.com")]
    public void Rejects_invalid_emails(string email)
    {
        Assert.False(EmailValidator.TryNormalize(email, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
