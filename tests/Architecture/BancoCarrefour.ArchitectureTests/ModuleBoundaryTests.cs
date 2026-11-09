using System.Reflection;
using System.Xml.Linq;
using BancoCarrefour.CoreBanking.Accounts;
using BancoCarrefour.CoreBanking.Ledger;
using BancoCarrefour.ServiceDefaults;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace BancoCarrefour.ArchitectureTests;

/// <summary>
/// Fronteiras de módulo e regras estruturais (ADR-0009; TC-131, TC-200..203). Enquanto os módulos são marcadores,
/// as regras passam trivialmente, mas já funcionam como catraca contra regressões.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly Ledger = typeof(LedgerModule).Assembly;
    private static readonly Assembly Accounts = typeof(AccountsModule).Assembly;
    private static readonly Assembly ServiceDefaults = typeof(ServiceDefaultsExtensions).Assembly;
    private static readonly Assembly PaymentsApi = Assembly.Load("BancoCarrefour.Payments.Api");
    private static readonly Assembly CoreBankingHost = Assembly.Load("BancoCarrefour.CoreBanking.Host");

    public static TheoryData<string> DomainModules() => new() { "Ledger", "Accounts" };

    public static TheoryData<string> AllProductionAssemblies() => new() { "Ledger", "Accounts", "ServiceDefaults", "PaymentsApi", "CoreBankingHost" };

    [Fact]
    public void Ledger_DoesNotDependOnAccounts()
    {
        // TC-200: módulos do Core não acessam internals uns dos outros (no skeleton, sem dependência direta).
        AssertNoDependency(Ledger, "BancoCarrefour.CoreBanking.Accounts");
    }

    [Fact]
    public void Accounts_DoesNotDependOnLedger() => AssertNoDependency(Accounts, "BancoCarrefour.CoreBanking.Ledger");

    [Fact]
    public void PaymentsApi_DoesNotDependOnCoreBankingImplementation()
    {
        // TC-200: Payments depende só de contratos gRPC do Core (ainda inexistentes); nunca de módulos/host.
        AssertNoDependency(
            PaymentsApi,
            "BancoCarrefour.CoreBanking.Ledger",
            "BancoCarrefour.CoreBanking.Accounts",
            "BancoCarrefour.CoreBanking.Host");
    }

    [Fact]
    public void CoreBankingHost_DoesNotDependOnPayments() => AssertNoDependency(CoreBankingHost, "BancoCarrefour.Payments");

    [Fact]
    public void ServiceDefaults_DoesNotDependOnAnyService() =>
        AssertNoDependency(ServiceDefaults, "BancoCarrefour.Payments", "BancoCarrefour.CoreBanking");

    [Theory]
    [MemberData(nameof(DomainModules))]
    public void DomainModule_DoesNotDependOnWebOrPersistenceFrameworks(string module)
    {
        // TC-202 (desenho confirmado pelo README dos módulos: sem ASP.NET Core/EF no domínio).
        AssertNoDependency(
            Resolve(module),
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Grpc",
            "BancoCarrefour.ServiceDefaults",
            "BancoCarrefour.Payments");
    }

    [Theory]
    [MemberData(nameof(DomainModules))]
    public void DebitPathModule_DoesNotDependOnCachesOrReadModels(string module)
    {
        // TC-131 / SEC-REQ-014: cache e read model nunca autorizam débito.
        AssertNoDependency(
            Resolve(module),
            "Microsoft.Extensions.Caching",
            "StackExchange.Redis",
            "BancoCarrefour.Statement",
            "BancoCarrefour.CoreBanking.Statement");
    }

    [Theory]
    [MemberData(nameof(AllProductionAssemblies))]
    public void ProductionAssembly_DoesNotUseDistributedTransactions(string module)
    {
        // TC-201 (ADR-0009 "nenhum 2PC"): TransactionScope/MSDTC vivem em System.Transactions.
        AssertNoDependency(Resolve(module), "System.Transactions");
    }

    [Theory]
    [MemberData(nameof(DomainModules))]
    public void DomainModule_DoesNotReadSystemClockDirectly(string module)
    {
        // TC-203 (H-01): varredura de IL por chamadas a DateTime/DateTimeOffset.Now/UtcNow/Today.
        string[] forbidden =
        [
            "System.DateTime System.DateTime::get_Now()",
            "System.DateTime System.DateTime::get_UtcNow()",
            "System.DateTime System.DateTime::get_Today()",
            "System.DateTimeOffset System.DateTimeOffset::get_Now()",
            "System.DateTimeOffset System.DateTimeOffset::get_UtcNow()",
        ];

        using var assembly = AssemblyDefinition.ReadAssembly(Resolve(module).Location);
        var offenders = assembly.MainModule.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions
                .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is MethodReference)
                .Where(i => forbidden.Contains(((MethodReference)i.Operand).FullName))
                .Select(_ => m.FullName))
            .Distinct()
            .ToList();

        offenders.ShouldBeEmpty("Use TimeProvider injetado (H-01).");
    }

    [Fact]
    public void ProjectReferences_ServicesDoNotReferenceOtherServicesDirectly()
    {
        // TC-200 no nível de projeto: um serviço não referencia projeto de outro serviço (só contratos, quando existirem).
        var srcRoot = Path.Combine(RepositoryRoot.Find(), "src", "Services");
        var violations = new List<string>();
        foreach (var csproj in Directory.EnumerateFiles(srcRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var service = Path.GetRelativePath(srcRoot, csproj).Split(Path.DirectorySeparatorChar)[0];
            var references = XDocument.Load(csproj).Descendants("ProjectReference")
                .Select(r => (string?)r.Attribute("Include") ?? string.Empty);
            foreach (var reference in references)
            {
                var full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(csproj)!, reference));
                if (full.StartsWith(srcRoot, StringComparison.OrdinalIgnoreCase))
                {
                    var target = Path.GetRelativePath(srcRoot, full).Split(Path.DirectorySeparatorChar)[0];
                    if (!string.Equals(target, service, StringComparison.OrdinalIgnoreCase)
                        && !full.Contains(".Contracts", StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add($"{Path.GetFileName(csproj)} -> {Path.GetFileName(full)}");
                    }
                }
            }
        }

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void Harness_DetectsKnownDependency()
    {
        // Controle positivo: ServiceDefaults depende de ASP.NET Core; se a regra não detectar, os testes acima são vácuos.
        var result = Types.InAssembly(ServiceDefaults).ShouldNot().HaveDependencyOnAny("Microsoft.AspNetCore").GetResult();

        result.IsSuccessful.ShouldBeFalse();
    }

    private static Assembly Resolve(string module) => module switch
    {
        "Ledger" => Ledger,
        "Accounts" => Accounts,
        "ServiceDefaults" => ServiceDefaults,
        "PaymentsApi" => PaymentsApi,
        "CoreBankingHost" => CoreBankingHost,
        _ => throw new ArgumentOutOfRangeException(nameof(module)),
    };

    private static void AssertNoDependency(Assembly assembly, params string[] forbiddenNamespaces)
    {
        Types.InAssembly(assembly).GetTypes().ShouldNotBeEmpty($"Nenhum tipo encontrado em {assembly.GetName().Name}.");

        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenNamespaces)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"{assembly.GetName().Name} depende de [{string.Join(", ", forbiddenNamespaces)}]: "
            + string.Join(", ", (result.FailingTypes ?? []).Select(t => t.FullName)));
    }
}

internal static class RepositoryRoot
{
    public static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BancoCarrefour.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Raiz do repositório (BancoCarrefour.slnx) não encontrada.");
    }
}
