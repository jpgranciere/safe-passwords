using System.Security.Cryptography;

namespace Cofre.Core.Security;

public sealed record GeneratorOptions(
    int Length = 20,
    bool Upper = true,
    bool Lower = true,
    bool Digits = true,
    bool Symbols = true,
    bool AvoidAmbiguous = false);

public static class PasswordGenerator
{
    public const int MinLength = 4;
    public const int MaxLength = 128;

    private const string UpperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowerChars = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitChars = "0123456789";
    private const string SymbolChars = "!@#$%^&*()-_=+[]{};:,.?/";
    private const string Ambiguous = "O0Il1|";

    public static string Generate(GeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var sets = new List<string>();
        if (options.Upper) sets.Add(Filter(UpperChars, options.AvoidAmbiguous));
        if (options.Lower) sets.Add(Filter(LowerChars, options.AvoidAmbiguous));
        if (options.Digits) sets.Add(Filter(DigitChars, options.AvoidAmbiguous));
        if (options.Symbols) sets.Add(Filter(SymbolChars, options.AvoidAmbiguous));

        if (sets.Count == 0)
            throw new ArgumentException("Selecione pelo menos um tipo de caractere.", nameof(options));
        if (options.Length < Math.Max(MinLength, sets.Count) || options.Length > MaxLength)
            throw new ArgumentOutOfRangeException(nameof(options), $"O tamanho deve ficar entre {MinLength} e {MaxLength}.");

        var pool = string.Concat(sets);
        var chars = new char[options.Length];

        // One guaranteed character from every selected set, the rest from the whole pool, then shuffle.
        for (var i = 0; i < sets.Count; i++) chars[i] = Pick(sets[i]);
        for (var i = sets.Count; i < chars.Length; i++) chars[i] = Pick(pool);

        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    private static char Pick(string source) => source[RandomNumberGenerator.GetInt32(source.Length)];

    private static string Filter(string source, bool avoidAmbiguous) =>
        avoidAmbiguous ? new string(source.Where(c => !Ambiguous.Contains(c)).ToArray()) : source;
}
