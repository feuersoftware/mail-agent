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
        catch (Exception ex) when (ex is System.IO.FileNotFoundException || 
                                     ex is System.Reflection.ReflectionTypeLoadException ||
                                     ex is System.TypeLoadException)
        {
            System.Diagnostics.Debug.WriteLine($"Error reading FallbackClientId from assembly metadata: {ex.Message}");
            _fallbackClientId = null;
        }
    }

    internal static string FallbackClientId => _fallbackClientId ?? "00000000-0000-0000-0000-000000000000";
}

