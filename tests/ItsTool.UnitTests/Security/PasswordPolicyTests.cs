using ItsTool.Infrastructure.Security;

namespace ItsTool.UnitTests.Security;

/// <summary>FNRC's ISO password standard: 8 or more characters with upper case, lower case, a digit and a symbol. The username
/// (a digital ID at FNRC) may appear in it.</summary>
public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Fnrc-It1")]
    [InlineData("Passw0rd!")]
    [InlineData("My-Own-Passphrase-2026")]
    [InlineData("كلمة-Sr1A")]
    [InlineData("Alamkhan-2026")]
    public void Passwords_that_meet_the_standard_are_accepted(string password)
    {
        Assert.Null(PasswordPolicy.Check(password));
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("Fnrc-It", "at least 8")]
    [InlineData("fnrc-it1", "upper-case")]
    [InlineData("FNRC-IT1", "upper-case")]
    [InlineData("Fnrc-Its", "upper-case")]
    [InlineData("FnrcIts1", "upper-case")]
    public void Passwords_that_miss_a_rule_are_refused(string password, string reason)
    {
        var problem = PasswordPolicy.Check(password);
        Assert.NotNull(problem);
        Assert.Contains(reason, problem);
        Assert.Throws<ArgumentException>(() => PasswordPolicy.Validate(password));
    }

    [Fact]
    public void Passwords_longer_than_bcrypt_reads_are_refused()
    {
        Assert.Contains("at most 64", PasswordPolicy.Check("Aa1-" + new string('x', 61)));
        Assert.Contains("at most 64", PasswordPolicy.Check("Aa1-" + new string('ع', 40)));
        Assert.Null(PasswordPolicy.Check("Aa1-" + new string('x', 60)));
    }
}
