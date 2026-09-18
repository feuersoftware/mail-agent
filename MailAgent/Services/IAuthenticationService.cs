namespace FeuerSoftware.MailAgent.Services
{
    public interface IAuthenticationService
    {
        Task<string> GetAccessTokenAsync(string username, bool allowInteractive, CancellationToken cancellationToken = default);
        
        Task InitializeAuthenticationAsync(IEnumerable<string> usernames, CancellationToken cancellationToken);
    }
}
