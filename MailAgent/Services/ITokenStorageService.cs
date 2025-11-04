namespace FeuerSoftware.MailAgent.Services
{
    public interface ITokenStorageService
    {
        Task SaveTokenAsync(string username, string token);
        
        Task<string?> GetTokenAsync(string username);
        
        Task DeleteTokenAsync(string username);
    }
}
