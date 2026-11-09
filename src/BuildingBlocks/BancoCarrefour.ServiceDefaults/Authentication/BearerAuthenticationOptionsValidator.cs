using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BancoCarrefour.ServiceDefaults.Authentication;

/// <summary>Validação fail-closed no startup: sem Authority/Audience válidas o serviço não sobe.</summary>
internal sealed class BearerAuthenticationOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<BearerAuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, BearerAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.Authority is null || !options.Authority.IsAbsoluteUri)
        {
            failures.Add($"{BearerAuthenticationOptions.SectionName}:Authority deve ser uma URI absoluta.");
        }
        else if (options.Authority.Scheme != Uri.UriSchemeHttps && !AllowsInsecureMetadata(options))
        {
            failures.Add($"{BearerAuthenticationOptions.SectionName}:Authority deve usar HTTPS.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add($"{BearerAuthenticationOptions.SectionName}:Audience é obrigatória.");
        }

        if (options.ClockSkewSeconds is < 0 or > BearerAuthenticationOptions.MaxClockSkewSeconds)
        {
            failures.Add(
                $"{BearerAuthenticationOptions.SectionName}:ClockSkewSeconds deve estar entre 0 e {BearerAuthenticationOptions.MaxClockSkewSeconds}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    internal bool AllowsInsecureMetadata(BearerAuthenticationOptions options) =>
        environment.IsDevelopment() && options.AllowInsecureMetadataInDevelopment;
}
