using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace TriSend.Auth.Api;

public sealed class CertificateLoader(IConfiguration configuration)
{
    public async Task<IReadOnlyList<X509Certificate2>> LoadAsync(string settingName, CancellationToken ct)
    {
        var names = configuration.GetSection(settingName).Get<string[]>() ?? [];
        if (names.Length == 0) return [];

        var vaultUri = configuration["KeyVault:VaultUri"]
            ?? throw new InvalidOperationException("KeyVault:VaultUri is required.");
        var client = new SecretClient(new Uri(vaultUri), new DefaultAzureCredential());

        var result = new List<X509Certificate2>();
        foreach (var name in names)
        {
            var secret = await client.GetSecretAsync(name, cancellationToken: ct);
            result.Add(new X509Certificate2(
                Convert.FromBase64String(secret.Value.Value),
                (string?)null,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.MachineKeySet));
        }
        return result;
    }
}
