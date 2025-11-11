namespace FeuerSoftware.MailAgent.Services
{
    public interface IAuthenticationService
    {
        Task<string> GetAccessTokenAsync(string username);
        
        Task InitializeAuthenticationAsync(IEnumerable<string> usernames, CancellationToken cancellationToken);
    }
}
