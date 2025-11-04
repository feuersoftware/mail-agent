using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FeuerSoftware.MailAgent.Services
{
    public class TokenStorageService : ITokenStorageService
    {
        private readonly ILogger<TokenStorageService> _log;
        private readonly string _tokenDirectory;
        private readonly byte[] _entropy;

        public TokenStorageService(ILogger<TokenStorageService> log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _tokenDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FeuerSoftware",
                "MailAgent",
                "Tokens");
            
            if (!Directory.Exists(_tokenDirectory))
            {
                Directory.CreateDirectory(_tokenDirectory);
            }

            // Use a static entropy for this application
            _entropy = Encoding.UTF8.GetBytes("FeuerSoftware.MailAgent.O365Auth.v1");
        }

        public async Task SaveTokenAsync(string username, string token)
        {
            try
            {
                var filePath = GetTokenFilePath(username);
                var tokenBytes = Encoding.UTF8.GetBytes(token);
                
                byte[] encryptedToken;
                if (OperatingSystem.IsWindows())
                {
                    encryptedToken = ProtectedData.Protect(tokenBytes, _entropy, DataProtectionScope.CurrentUser);
                }
                else
                {
                    // For non-Windows, we'll use a simple encoding (not truly encrypted)
                    // In production, consider using a proper cross-platform encryption library
                    _log.LogWarning("Running on non-Windows platform. Token storage is not fully encrypted.");
                    encryptedToken = tokenBytes;
                }
                
                await File.WriteAllBytesAsync(filePath, encryptedToken);
                _log.LogInformation($"Token saved for user {username}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to save token for user {username}");
                throw;
            }
        }

        public async Task<string?> GetTokenAsync(string username)
        {
            try
            {
                var filePath = GetTokenFilePath(username);
                
                if (!File.Exists(filePath))
                {
                    _log.LogDebug($"No token found for user {username}");
                    return null;
                }

                var encryptedToken = await File.ReadAllBytesAsync(filePath);
                
                byte[] tokenBytes;
                if (OperatingSystem.IsWindows())
                {
                    tokenBytes = ProtectedData.Unprotect(encryptedToken, _entropy, DataProtectionScope.CurrentUser);
                }
                else
                {
                    tokenBytes = encryptedToken;
                }
                
                return Encoding.UTF8.GetString(tokenBytes);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to retrieve token for user {username}");
                return null;
            }
        }

        public async Task DeleteTokenAsync(string username)
        {
            try
            {
                var filePath = GetTokenFilePath(username);
                
                if (File.Exists(filePath))
                {
                    await Task.Run(() => File.Delete(filePath));
                    _log.LogInformation($"Token deleted for user {username}");
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to delete token for user {username}");
            }
        }

        private string GetTokenFilePath(string username)
        {
            // Create a safe filename from the username
            var safeUsername = Convert.ToBase64String(Encoding.UTF8.GetBytes(username))
                .Replace("/", "_")
                .Replace("+", "-");
            return Path.Combine(_tokenDirectory, $"{safeUsername}.token");
        }
    }
}
