using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace BancoCarrefour.TestKit;

/// <summary>
/// Detectores do PiiScanner (§5.9 do plano). A asserção primária dos testes de vazamento é sobre canários
/// injetados; as heurísticas abaixo servem à varredura do repositório (TC-152).
/// Os padrões são escritos de forma que o próprio código-fonte não case com eles.
/// </summary>
public static partial class SensitiveDataDetectors
{
    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*", RegexOptions.CultureInvariant)]
    public static partial Regex Jwt();

    [GeneratedRegex(@"-{5}BEGIN (?:RSA |EC |DSA |OPENSSH |ENCRYPTED )?PRIVATE KEY-{5}", RegexOptions.CultureInvariant)]
    public static partial Regex PrivateKeyPem();

    /// <summary>13 a 19 dígitos, opcionalmente separados por espaço ou hífen, não colados a outros dígitos/letras.</summary>
    [GeneratedRegex(@"(?<![0-9A-Za-z])(?:\d[ -]?){12,18}\d(?![0-9A-Za-z])", RegexOptions.CultureInvariant)]
    public static partial Regex PanCandidate();

    [GeneratedRegex(@"(?<![0-9])(?:\d{3}\.\d{3}\.\d{3}-\d{2}|\d{11})(?![0-9])", RegexOptions.CultureInvariant)]
    public static partial Regex CpfCandidate();

    public static bool IsLuhnValid(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        var sum = 0;
        var doubleIt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
            doubleIt = !doubleIt;
        }

        return sum % 10 == 0;
    }

    public static bool IsCpfValid(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        if (digits.Length != 11 || digits.Distinct().Count() == 1)
        {
            return false;
        }

        static int Dv(string s, int length)
        {
            var sum = 0;
            for (var i = 0; i < length; i++)
            {
                sum += (s[i] - '0') * (length + 1 - i);
            }

            var rest = sum % 11;
            return rest < 2 ? 0 : 11 - rest;
        }

        return Dv(digits, 9) == digits[9] - '0' && Dv(digits, 10) == digits[10] - '0';
    }

    /// <summary>
    /// Gera em runtime um número de 16 dígitos válido em Luhn com prefixo fora das faixas de bandeira conhecidas
    /// (canário de PAN; nunca versionado).
    /// </summary>
    public static string NewPanCanary()
    {
        var body = "990" + string.Concat(Enumerable.Range(0, 12).Select(_ => RandomNumberGenerator.GetInt32(10)));
        for (var check = 0; check <= 9; check++)
        {
            var candidate = body + check;
            if (IsLuhnValid(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unreachable.");
    }

    public static string Digits(string value) => string.Concat((value ?? string.Empty).Where(char.IsAsciiDigit));
}
