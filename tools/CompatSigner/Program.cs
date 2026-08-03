using System.Security.Cryptography;
using ROROROblox.Core;

// CompatSigner: standalone CLI that detached-signs an arbitrary already-written file (today,
// roblox-compat.json; plugins-catalog.json is a natural follow-up, out of scope for now) with
// ECDSA P-256 / SHA-256, in the EXACT RobloxCompatSignature.Format the app verifies with -- so
// sign and verify cannot drift. Ported from 626-mod-launcher's ManifestMiner --sign-file mode
// (tools/ManifestMiner/Program.cs + ManifestSigner.cs), which does the identical generic
// detached-sign-an-arbitrary-file job for that repo's manifest feed.
//
// The private key (PKCS#8 PEM, or that PEM base64-encoded to survive CI secrets mangling
// multi-line newlines) comes ONLY from the ROBLOXCOMPAT_SIGNING_KEY env var (a GitHub Actions
// secret in THIS repo); it never touches source. Hard-fails on a missing file, a missing key, or
// a key that won't import -- never emits an unsigned-but-named .sig.
//
// Usage: dotnet run --project tools/CompatSigner -- <path-to-file-to-sign>
// Writes: <path-to-file-to-sign>.sig (overwritten if it already exists -- nothing in this repo
// commits a .sig, it only ever rides as a fresh release asset, so there's no git-churn reason to
// skip re-signing unchanged content the way the mod-launcher's --sign-file mode does for a
// committed sibling).

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: CompatSigner <path-to-file-to-sign>");
    Environment.Exit(1);
    return;
}

var filePath = args[0];
if (!File.Exists(filePath))
{
    Console.Error.WriteLine($"CompatSigner: file not found: {filePath}");
    Environment.Exit(1);
    return;
}

var keyEnv = Environment.GetEnvironmentVariable("ROBLOXCOMPAT_SIGNING_KEY");
if (string.IsNullOrWhiteSpace(keyEnv))
{
    Console.Error.WriteLine("CompatSigner requires ROBLOXCOMPAT_SIGNING_KEY (PKCS#8 PEM, or that PEM base64-encoded) in the environment.");
    Environment.Exit(1);
    return;
}

byte[] signature;
try
{
    using var ecdsa = ECDsa.Create();
    ecdsa.ImportFromPem(NormalizeKey(keyEnv));
    var fileBytes = File.ReadAllBytes(filePath);
    signature = ecdsa.SignData(fileBytes, HashAlgorithmName.SHA256, RobloxCompatSignature.Format);
}
catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
{
    // No recognized PEM label / multiple keys / encrypted key / garbage — a bad
    // ROBLOXCOMPAT_SIGNING_KEY. Fail hard so we never emit an unsigned-but-named artifact.
    Console.Error.WriteLine(
        $"CompatSigner: could not sign with ROBLOXCOMPAT_SIGNING_KEY (expected an unencrypted PKCS#8 PEM, or that PEM base64-encoded). {ex.GetType().Name}: {ex.Message}");
    Environment.Exit(1);
    return;
}

var sigPath = filePath + ".sig";
File.WriteAllBytes(sigPath, signature);
Console.WriteLine($"Signed {filePath} -> {sigPath} ({signature.Length} bytes)");

// CI secrets routinely mangle a multi-line PEM's newlines. Accept either a raw PEM or a
// base64-encoded PEM (a single-line secret is newline-proof). Anything else is returned
// unchanged so ImportFromPem reports the clear error. Ported from ManifestSigner.NormalizeKey.
static string NormalizeKey(string privateKeyPem)
{
    var trimmed = privateKeyPem.Trim();
    if (trimmed.Contains("-----BEGIN", StringComparison.Ordinal))
    {
        return trimmed; // already a PEM (multi-line)
    }

    try
    {
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(trimmed));
        if (decoded.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            return decoded; // it was a base64-encoded PEM
        }
    }
    catch (FormatException)
    {
        // not base64 — fall through
    }

    return trimmed;
}
