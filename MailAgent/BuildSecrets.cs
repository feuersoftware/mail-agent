namespace FeuerSoftware.MailAgent;

internal static class BuildSecrets
{
    private static readonly string? _fallbackClientId;

    static BuildSecrets()
    {
        try
        {
            var asm = typeof(BuildSecrets).Assembly;
            var metadata = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                .OfType<System.Reflection.AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "FallbackClientId");
            _fallbackClientId = metadata?.Value;
        }
        catch
        {
            _fallbackClientId = null;
        }
    }

    internal static string FallbackClientId => _fallbackClientId ?? "00000000-0000-0000-0000-000000000000";
}

