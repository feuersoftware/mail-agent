using System.Security.Cryptography;
using System.Text;

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
                    // For non-Windows, we'll store tokens with file system permissions only
                    // NOTE: This is not as secure as Windows Data Protection API
                    // For production on Linux, consider:
                    // 1. Using system keyring (e.g., gnome-keyring, kwallet)
                    // 2. Implementing encryption with a key derivation function
                    // 3. Using container secrets or vault solutions
                    _log.LogWarning("Running on non-Windows platform. Tokens are stored with file system permissions only. Ensure proper file permissions are set.");
                    encryptedToken = tokenBytes;
                }
                
                await File.WriteAllBytesAsync(filePath, encryptedToken);
                
                // On Unix-like systems, set restrictive file permissions (user read/write only)
                if (!OperatingSystem.IsWindows())
                {
                    try
                    {
                        // chmod 600 (user read/write only)
                        File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    }
                    catch (UnauthorizedAccessException permEx)
                    {
                        _log.LogWarning(permEx, "Failed to set restrictive file permissions on token file (unauthorized access)");
                    }
                    catch (IOException permEx)
                    {
                        _log.LogWarning(permEx, "Failed to set restrictive file permissions on token file (I/O error)");
                    }
                    catch (PlatformNotSupportedException permEx)
                    {
                        _log.LogWarning(permEx, "Failed to set restrictive file permissions on token file (platform not supported)");
                    }
                }
                
                _log.LogInformation($"Token saved for user {MaskUsername(username)}");
            }
            catch (CryptographicException ex)
            {
                _log.LogError(ex, $"Failed to encrypt token for user {MaskUsername(username)}");
                throw;
            }
            catch (IOException ex)
            {
                _log.LogError(ex, $"Failed to save token file for user {MaskUsername(username)}");
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.LogError(ex, $"Access denied when saving token for user {MaskUsername(username)}");
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
                    _log.LogDebug($"No token found for user {MaskUsername(username)}");
                    return null;
                }

                var encryptedToken = await File.ReadAllBytesAsync(filePath);
                
                byte[] tokenBytes;
                tokenBytes = OperatingSystem.IsWindows()
                    ? ProtectedData.Unprotect(encryptedToken, _entropy, DataProtectionScope.CurrentUser)
                    : encryptedToken;
                
                return Encoding.UTF8.GetString(tokenBytes);
            }
            catch (CryptographicException ex)
            {
                _log.LogError(ex, $"Failed to decrypt token for user {MaskUsername(username)}");
                return null;
            }
            catch (IOException ex)
            {
                _log.LogError(ex, $"Failed to read token file for user {MaskUsername(username)}");
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.LogError(ex, $"Access denied when reading token for user {MaskUsername(username)}");
                return null;
            }
        }

        public Task DeleteTokenAsync(string username)
        {
            try
            {
                var filePath = GetTokenFilePath(username);
                
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _log.LogInformation($"Token deleted for user {MaskUsername(username)}");
                }
            }
            catch (IOException ex)
            {
                _log.LogError(ex, $"Failed to delete token file for user {MaskUsername(username)}");
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.LogError(ex, $"Access denied when deleting token for user {MaskUsername(username)}");
            }
            
            return Task.CompletedTask;
        }

        private string GetTokenFilePath(string username)
        {
            // Create a safe filename from the username
            var safeUsername = Convert.ToBase64String(Encoding.UTF8.GetBytes(username))
                .Replace("/", "_")
                .Replace("+", "-")
                .Replace("=", "");
            return Path.Combine(_tokenDirectory, $"{safeUsername}.token");
        }

        private static string MaskUsername(string username)
        {
            // Mask the username for logging to avoid exposing full email addresses
            if (string.IsNullOrEmpty(username) || username.Length < 5)
            {
                return "***";
            }

            var atIndex = username.IndexOf('@');
            if (atIndex <= 0)
            {
                return username.Substring(0, 3) + "***";
            }

            return username.Substring(0, Math.Min(3, atIndex)) + "***@" + username.Substring(atIndex + 1);
        }
    }
}
