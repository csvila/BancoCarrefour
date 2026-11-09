using BancoCarrefour.TestKit;

namespace BancoCarrefour.ArchitectureTests;

/// <summary>
/// TC-152 (SEC-REQ-028, NFR-088): varredura somente leitura de <c>src/**</c> e <c>tests/**</c> por JWT, chave
/// privada PEM, PAN (Luhn) e CPF (DV válido). Allowlists vazias até a decisão D-9 (Compliance).
/// </summary>
public sealed class RepositoryHygieneTests
{
    /// <summary>PANs de teste aprovados (D-9). Vazio: nenhum PAN pode estar versionado.</summary>
    private static readonly HashSet<string> ApprovedTestPans = [];

    /// <summary>CPFs sintéticos aprovados (D-9). Vazio: nenhum CPF com DV válido pode estar versionado.</summary>
    private static readonly HashSet<string> ApprovedSyntheticCpfs = [];

    private static readonly string[] ScannedRoots = ["src", "tests"];

    private static readonly string[] TextExtensions =
        [".cs", ".csproj", ".props", ".targets", ".json", ".js", ".md", ".config", ".yml", ".yaml", ".xml", ".txt", ".proto", ".http", ".env", ".slnx"];

    private static readonly string[] ExcludedDirectories = ["bin", "obj", "TestResults", ".vs", "node_modules", ".playwright"];

    [Fact]
    public void Repository_ContainsNoJwtOrPrivateKey()
    {
        var findings = Scan((file, line, text) =>
        {
            var hits = new List<string>();
            if (SensitiveDataDetectors.Jwt().IsMatch(text))
            {
                hits.Add($"{file}:{line}: JWT");
            }

            if (SensitiveDataDetectors.PrivateKeyPem().IsMatch(text))
            {
                hits.Add($"{file}:{line}: chave privada PEM");
            }

            return hits;
        });

        findings.ShouldBeEmpty();
    }

    [Fact]
    public void Repository_ContainsNoPanOutsideApprovedAllowlist()
    {
        var findings = Scan((file, line, text) =>
        {
            if (file.EndsWith("packages.lock.json", StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            return SensitiveDataDetectors.PanCandidate().Matches(text)
                .Select(m => SensitiveDataDetectors.Digits(m.Value))
                .Where(d => d.Length is >= 13 and <= 19 && d.Distinct().Count() > 1 && SensitiveDataDetectors.IsLuhnValid(d))
                .Where(d => !ApprovedTestPans.Contains(d))
                .Select(d => $"{file}:{line}: possível PAN terminado em {d[^4..]}")
                .ToList();
        });

        findings.ShouldBeEmpty();
    }

    [Fact]
    public void Repository_ContainsNoValidCpfOutsideApprovedSyntheticList()
    {
        var findings = Scan((file, line, text) =>
        {
            if (file.EndsWith("packages.lock.json", StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            return SensitiveDataDetectors.CpfCandidate().Matches(text)
                .Select(m => SensitiveDataDetectors.Digits(m.Value))
                .Where(d => SensitiveDataDetectors.IsCpfValid(d) && !ApprovedSyntheticCpfs.Contains(d))
                .Select(_ => $"{file}:{line}: possível CPF com DV válido")
                .ToList();
        });

        findings.ShouldBeEmpty();
    }

    [Fact]
    public void Detectors_RecognizeSyntheticCanaries()
    {
        // Autoteste do scanner: canários gerados em runtime (nunca versionados) precisam ser detectados.
        var pan = SensitiveDataDetectors.NewPanCanary();
        SensitiveDataDetectors.IsLuhnValid(pan).ShouldBeTrue();
        SensitiveDataDetectors.PanCandidate().IsMatch($"card {pan[..4]} {pan[4..8]} {pan[8..12]} {pan[12..]} end").ShouldBeTrue();

        var fakeJwt = string.Join('.', "eyJ" + new string('a', 10), "eyJ" + new string('b', 10), "sig");
        SensitiveDataDetectors.Jwt().IsMatch(fakeJwt).ShouldBeTrue();

        var pem = new string('-', 5) + "BEGIN PRIVATE KEY" + new string('-', 5);
        SensitiveDataDetectors.PrivateKeyPem().IsMatch(pem).ShouldBeTrue();

        SensitiveDataDetectors.IsCpfValid(new string('1', 11)).ShouldBeFalse();
    }

    private static List<string> Scan(Func<string, int, string, IEnumerable<string>> inspect)
    {
        var root = RepositoryRoot.Find();
        var findings = new List<string>();
        var scannedFiles = 0;
        foreach (var scanned in ScannedRoots)
        {
            foreach (var file in EnumerateFiles(Path.Combine(root, scanned)))
            {
                scannedFiles++;
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                var lineNumber = 0;
                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;
                    findings.AddRange(inspect(relative, lineNumber, line));
                }
            }
        }

        scannedFiles.ShouldBeGreaterThan(10, "A varredura precisa ter encontrado os arquivos de src/ e tests/.");
        return findings;
    }

    private static IEnumerable<string> EnumerateFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (TextExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }

        foreach (var sub in Directory.EnumerateDirectories(directory))
        {
            if (!ExcludedDirectories.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
            {
                foreach (var file in EnumerateFiles(sub))
                {
                    yield return file;
                }
            }
        }
    }
}
