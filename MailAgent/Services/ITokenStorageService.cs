namespace FeuerSoftware.MailAgent.Services
{
    public interface ITokenStorageService
    {
        Task SaveTokenAsync(string username, string token);

        Task SaveTokenByteAsync(string username, byte[] tokenBytes);
        
        Task<string?> GetTokenAsync(string username);

        Task<byte[]?> GetTokenAsByteAsync(string username);
        
        Task DeleteTokenAsync(string username);
    }
}