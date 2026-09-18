namespace FeuerSoftware.MailAgent.Services
{
    /// <summary>
    /// Shared timeout budgets for bounding mail-server operations that have no cancellation support of
    /// their own, so a single hung call can no longer block a per-mailbox lock forever. Kept in one place
    /// so the different layers that each need to know "how long is a hung mail-server call tolerated"
    /// (the outer wait in MailService, EWS's own transport timeout in ExchangeClient) can't silently drift
    /// out of sync with each other.
    /// </summary>
    internal static class MailOperationTimeouts
    {
        /// <summary>
        /// How long a single connect/fetch/disconnect operation against a mail server is allowed to run
        /// before it's considered hung and abandoned/retried.
        /// </summary>
        public static readonly TimeSpan HungCallTimeout = TimeSpan.FromMinutes(2);
    }
}
