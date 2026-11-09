namespace BancoCarrefour.ServiceDefaults.Authentication;

/// <summary>
/// Parâmetros não secretos de validação de Bearer token (ADR-0006). Seção <c>Authentication</c>.
/// Nenhum segredo é necessário: a validação usa as chaves públicas (JWKS) do issuer configurado.
/// </summary>
public sealed class BearerAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public const int MaxClockSkewSeconds = 60;

    /// <summary>URL do IdP OIDC (discovery em <c>/.well-known/openid-configuration</c>). Obrigatória, HTTPS.</summary>
    public Uri? Authority { get; set; }

    /// <summary>Audience exata desta API. Obrigatória.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Issuer exato esperado. Opcional: se ausente, usa o issuer publicado no discovery da Authority.</summary>
    public string? Issuer { get; set; }

    /// <summary>Tolerância de relógio para exp/nbf, entre 0 e 60 segundos.</summary>
    public int ClockSkewSeconds { get; set; } = 30;

    /// <summary>
    /// Permite metadata HTTP (sem TLS) somente quando o ambiente é Development. Ignorado em qualquer outro ambiente.
    /// </summary>
    public bool AllowInsecureMetadataInDevelopment { get; set; }
}
