namespace Cofre.Core.Security;

public enum StrengthLevel { VeryWeak = 0, Weak = 1, Fair = 2, Strong = 3, Excellent = 4 }

public readonly record struct StrengthResult(double Bits, StrengthLevel Level)
{
    /// <summary>0..1, handy for progress bars.</summary>
    public double Fraction => ((int)Level + 1) / 5.0;
}

/// <summary>
/// A quick entropy estimate from length and character variety, with penalties for repeats, runs and
/// well-known base words. It is a guide for the UI, not a guarantee.
/// </summary>
public static class PasswordStrength
{
    private static readonly string[] CommonBases =
    [
        "password", "senha", "qwerty", "admin", "welcome", "letmein", "abc", "iloveyou", "brasil",
        "123456", "12345678", "123456789", "mudar", "master", "login", "teste", "test",
    ];

    public static StrengthResult Evaluate(string? password)
    {
        if (string.IsNullOrEmpty(password)) return new StrengthResult(0, StrengthLevel.VeryWeak);

        var poolSize = 0;
        if (password.Any(char.IsLower)) poolSize += 26;
        if (password.Any(char.IsUpper)) poolSize += 26;
        if (password.Any(char.IsDigit)) poolSize += 10;
        if (password.Any(c => !char.IsLetterOrDigit(c) && c < 128)) poolSize += 33;
        if (password.Any(c => c >= 128)) poolSize += 100;

        var effectiveLength = EffectiveLength(password);
        var bits = effectiveLength * Math.Log2(Math.Max(poolSize, 2));

        if (IsCommon(password)) bits = Math.Min(bits, 20);

        return new StrengthResult(bits, bits switch
        {
            < 28 => StrengthLevel.VeryWeak,
            < 45 => StrengthLevel.Weak,
            < 60 => StrengthLevel.Fair,
            < 80 => StrengthLevel.Strong,
            _ => StrengthLevel.Excellent,
        });
    }

    /// <summary>Length discounted for repeated characters and ascending/descending runs ("aaaa", "abcd", "4321").</summary>
    private static double EffectiveLength(string password)
    {
        double length = 0;
        for (var i = 0; i < password.Length; i++)
        {
            var weight = 1.0;
            if (i > 0 && password[i] == password[i - 1]) weight = 0.2;
            else if (i > 1
                     && password[i] - password[i - 1] == password[i - 1] - password[i - 2]
                     && Math.Abs(password[i] - password[i - 1]) == 1) weight = 0.3;
            else if (password.IndexOf(password[i]) < i) weight = 0.6;
            length += weight;
        }

        return length;
    }

    private static bool IsCommon(string password)
    {
        var stripped = password.TrimEnd(c => !char.IsLetter(c)).ToLowerInvariant();
        var lowered = password.ToLowerInvariant();
        foreach (var word in CommonBases)
        {
            if (stripped == word || lowered == word) return true;
        }

        return false;
    }

    private static string TrimEnd(this string value, Func<char, bool> predicate)
    {
        var end = value.Length;
        while (end > 0 && predicate(value[end - 1])) end--;
        return value[..end];
    }
}
