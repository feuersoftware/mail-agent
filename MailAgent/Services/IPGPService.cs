namespace FeuerSoftware.MailAgent.Services
{
    public interface IPGPService
    {
        Task<string> DecryptWithGnupg(Stream encryptedData);
    }
}