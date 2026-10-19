namespace FeuerSoftware.MailAgent.Services
{
    /// <summary>
    /// Time budgets for mail-server operations. The agent raises real-time alarms, so a dead connection
    /// must be noticed within seconds - but a slow, still-progressing download must never be aborted.
    /// Hence two layers: <see cref="IoInactivity"/> (MailKit's socket timeout, resets with every byte) is the
    /// PRIMARY detector for dead/half-open connections; the other budgets are wall-clock safety nets for
    /// hangs that I/O timeouts can't see (MSAL token cache, EWS).
    ///
    /// Worst case without a successful poll (poll interval 5 s not included):
    ///  (a) dead IMAP/O365 connection: IoInactivity 15 s + Disconnect ~0 s (quit:false closes the socket)
    ///      + Connect 1-3 s healthy (a slow but alive login may take up to ConnectIoInactivity per phase)
    ///      => ~16-20 s; hard cap IoInactivity + Disconnect + Connect = 80 s.
    ///  (b) safety net only (hang without I/O timeout, e.g. MSAL; EWS): FetchTick + Disconnect + Connect = 185 s.
    ///  (c) persistent outage: reconnect attempts are spaced by <see cref="Backoff"/>, at most every 30 s.
    /// </summary>
    internal sealed record MailOperationTimeouts
    {
        public static readonly MailOperationTimeouts Default = new();

        /// <summary>MailKit socket Read/Write timeout: silence this long = dead connection. Data flowing never trips it.</summary>
        public TimeSpan IoInactivity { get; init; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Socket timeout only while connecting/authenticating (TCP+TLS, then AUTH). Exchange Online sometimes
        /// answers AUTHENTICATE only after >15 s; such a slow but alive login must not be aborted. Restored to
        /// <see cref="IoInactivity"/> right after, so dead connections in normal operation are still seen at 15 s.
        /// </summary>
        public TimeSpan ConnectIoInactivity { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Connect incl. TCP, TLS, OAuth token and AUTH (healthy: 1-3 s). Safety net, mainly for MSAL (no I/O
        /// timeout); must cover two silent phases of <see cref="ConnectIoInactivity"/> (connect + AUTH).
        /// </summary>
        public TimeSpan Connect { get; init; } = TimeSpan.FromSeconds(60);

        /// <summary>Interval of the scheduled reconnect (see MailService); drives <see cref="TokenMinValidity"/>.</summary>
        public TimeSpan ScheduledReconnect { get; init; } = TimeSpan.FromMinutes(60);

        /// <summary>
        /// A token handed to Connect should outlive the next scheduled reconnect (plus margin), otherwise the
        /// session dies with "AccessTokenExpired" in between. Cached tokens with less remaining are force-refreshed.
        /// </summary>
        public TimeSpan TokenMinValidity => ScheduledReconnect + TimeSpan.FromMinutes(5);

        /// <summary>Disconnect before a reconnect. Only a safety net: quit:false just closes the socket.</summary>
        public TimeSpan Disconnect { get; init; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// One poll tick. Safety net only, deliberately generous: GetUnseenMails downloads ALL unseen mails every
        /// round (mails ignored by a filter stay unseen), so a tight cap could abort the same big download forever
        /// and block alarm mails behind it. Equals the old limit, so EWS gets no worse.
        /// </summary>
        public TimeSpan FetchTick { get; init; } = TimeSpan.FromSeconds(120);

        public TimeSpan ReconnectBackoffBase { get; init; } = TimeSpan.FromSeconds(5);

        public TimeSpan ReconnectBackoffMax { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>Upper bound of any lock holder's hold time (tick, or Disconnect + Connect).</summary>
        public TimeSpan LockWait => FetchTick + Disconnect + Connect;

        /// <summary>EWS has no I/O inactivity timeout, so its request timeout follows the tick budget (as before).</summary>
        public TimeSpan EwsRequest => FetchTick;

        public TimeSpan WorstCaseDeadConnection => IoInactivity + Disconnect + Connect;

        public TimeSpan WorstCaseSafetyNetOnly => FetchTick + Disconnect + Connect;

        /// <summary>Delay after the n-th consecutive failed (re)connect: 5, 10, 20, 30, 30 ... s. The first attempt is never delayed.</summary>
        public TimeSpan Backoff(int failures)
        {
            if (failures <= 0)
            {
                return TimeSpan.Zero;
            }

            var delay = ReconnectBackoffBase * Math.Pow(2, Math.Min(failures - 1, 10));
            return delay < ReconnectBackoffMax ? delay : ReconnectBackoffMax;
        }
    }
}
