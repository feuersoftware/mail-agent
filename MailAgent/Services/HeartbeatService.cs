using FeuerSoftware.MailAgent.Extensions;
using FeuerSoftware.MailAgent.Options;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FeuerSoftware.MailAgent.Services
{
    public class HeartbeatService : IHostedService, IDisposable
    {
        private readonly ILogger<HeartbeatService> _log;
        private readonly HttpClient _httpClient;
        private readonly MailAgentOptions _options;
        private IDisposable? _heartbeatSubscription;

        public HeartbeatService(
            ILogger<HeartbeatService> log, 
            HttpClient httpClient,
            IOptions<MailAgentOptions> options)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public void Dispose()
        {
            _heartbeatSubscription?.Dispose();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await Initialize();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _heartbeatSubscription?.Dispose();

            return Task.CompletedTask;
        }

        private async Task Initialize()
        {
            if (!Uri.IsWellFormedUriString(_options.HeartbeatUrl, UriKind.Absolute) || _options.HeartbeatInterval is null)
            {
                _log.LogWarning("Heartbeat is not configured. Not sending any heartbeats.");
                return;
            }

            _httpClient.BaseAddress = new Uri(_options.HeartbeatUrl);

            _heartbeatSubscription = Observable.Interval(_options.HeartbeatInterval.Value)
                .SubscribeAsyncSafe(async _ =>
                {
                    await SendHeartbeat();
                },
                e => _log.LogError(e, "Failed to send heartbeat request."),
                () => _log.LogDebug("HeartbeatSubscription completed."));

            await SendHeartbeat();

            return;
        }
        private async Task SendHeartbeat()
        {
            _log.LogDebug("Sending Heartbeat...");

            await _httpClient.GetAsync(string.Empty);
        }
    }
}
