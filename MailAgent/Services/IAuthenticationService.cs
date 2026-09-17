namespace FeuerSoftware.MailAgent.Services
{
    public interface IAuthenticationService
    {
        Task<string> GetAccessTokenAsync(string username, CancellationToken cancellationToken = default);
        
        Task InitializeAuthenticationAsync(IEnumerable<string> usernames, CancellationToken cancellationToken);
    }
}
