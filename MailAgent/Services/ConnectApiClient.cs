using FeuerSoftware.MailAgent.Models;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FeuerSoftware.MailAgent.Services
{
    public class ConnectApiClient : IDisposable, IConnectApiClient
    {
        private readonly ILogger<ConnectApiClient> _log;
        private readonly HttpClient _httpClient;
        private readonly string _connectBaseUrl = "https://connectapi.feuersoftware.com";

        public ConnectApiClient(
            [NotNull] ILogger<ConnectApiClient> log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(_connectBaseUrl),
                Timeout = TimeSpan.FromSeconds(30),
            };
        }

        public async Task PublishOperation(OperationModel operation, SiteModel site)
        {
            if (operation is null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (site is null)
            {
                throw new ArgumentNullException(nameof(site));
            }

            if (string.IsNullOrWhiteSpace(site.ApiKey))
            {
                _log.LogWarning($"Operation not published for site {site.Name} without ApiKey.");
                return;
            }

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", site.ApiKey);

            var response = await _httpClient.PostAsJsonAsync($"/interfaces/public/operation?updateStrategy=byNumber", operation);

            if (!response.IsSuccessStatusCode)
            {
                string? responseContent = null;
                if (response.Content is StringContent stringContent)
                {
                    responseContent = await stringContent.ReadAsStringAsync();
                }

                _log.LogError($"Failed to publish operation. Statuscode '{response.StatusCode}' and content '{responseContent}'");

                return;
            }

            _log.LogDebug($"Successfully published Operation. Statuscode: '{response.StatusCode}'");
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
