using Cofre.Core.Security;

namespace Cofre.Core.Tests;

public class GeneratorAndStrengthTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(20)]
    [InlineData(128)]
    public void Generate_RespectsLength(int length)
    {
        Assert.Equal(length, PasswordGenerator.Generate(new GeneratorOptions(Length: length)).Length);
    }

    [Fact]
    public void Generate_AlwaysContainsEverySelectedClass()
    {
        for (var i = 0; i < 300; i++)
        {
            var password = PasswordGenerator.Generate(new GeneratorOptions(Length: 8));
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
            Assert.Contains(password, c => !char.IsLetterOrDigit(c));
        }
    }

    [Fact]
    public void Generate_HonoursDisabledClasses_AndAmbiguousFilter()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = PasswordGenerator.Generate(new GeneratorOptions(Length: 32, Symbols: false, Upper: false, AvoidAmbiguous: true));
            Assert.All(password, c => Assert.True(char.IsLower(c) || char.IsDigit(c)));
            Assert.DoesNotContain(password, c => "O0Il1|".Contains(c));
        }
    }

    [Fact]
    public void Generate_IsNotRepeatable()
    {
        var set = Enumerable.Range(0, 50).Select(_ => PasswordGenerator.Generate(new GeneratorOptions())).ToHashSet();
        Assert.Equal(50, set.Count);
    }

    [Fact]
    public void Generate_Validates()
    {
        Assert.Throws<ArgumentException>(() => PasswordGenerator.Generate(new GeneratorOptions(Upper: false, Lower: false, Digits: false, Symbols: false)));
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordGenerator.Generate(new GeneratorOptions(Length: 3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordGenerator.Generate(new GeneratorOptions(Length: 129)));
    }

    [Theory]
    [InlineData("", StrengthLevel.VeryWeak)]
    [InlineData("123456", StrengthLevel.VeryWeak)]
    [InlineData("aaaaaaaaaaaa", StrengthLevel.VeryWeak)]
    [InlineData("abcdefgh", StrengthLevel.VeryWeak)]
    [InlineData("Password1!", StrengthLevel.VeryWeak)]
    public void Strength_RatesObviousPasswordsAsWeak(string password, StrengthLevel expected)
    {
        Assert.Equal(expected, PasswordStrength.Evaluate(password).Level);
    }

    [Fact]
    public void Strength_RatesLongRandomPasswordsHigh()
    {
        var generated = PasswordGenerator.Generate(new GeneratorOptions(Length: 24));
        Assert.Equal(StrengthLevel.Excellent, PasswordStrength.Evaluate(generated).Level);
    }

    [Fact]
    public void Strength_GrowsWithLengthAndVariety()
    {
        var shortLower = PasswordStrength.Evaluate("qzxwvbnm").Bits;
        var longer = PasswordStrength.Evaluate("qzxwvbnmkjhgfd").Bits;
        var mixed = PasswordStrength.Evaluate("qZxw7bn!kjhgfd").Bits;

        Assert.True(longer > shortLower);
        Assert.True(mixed > longer);
    }
}
