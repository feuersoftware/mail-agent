// MailMonitor - unabhängige, nur lesende IMAP-Sitzung neben dem MailAgent im E2E-Test.
// Schreibt für jede Testmail (Einsatznummer im Betreff) nach events.csv:
//   arrived = liegt im INBOX, read = \Seen gesetzt (vom Agent verarbeitet)
// sowie connect / login / ready / error. Run-E2ETest.ps1 liest die Datei live mit.
//
// Nie SELECT, nur EXAMINE: Exchange Online liefert einer Sitzung nach einem einzigen SELECT über EXAMINE + SEARCH
// keine neuen Mails mehr, bis neu verbunden wird (Ursache des "Hängenbleibens", Befund 08.10.2026). Zusätzlich
// verbindet sich der Monitor alle 10 min neu, damit er selbst nicht veraltet.
//
// Aufruf: MailMonitor <host> <port> <user> <o365ClientId> <ausgabeOrdner> <pollSekunden> <eltern-PID>
// Beendet sich selbst, sobald der Elternprozess (Run-E2ETest.ps1) nicht mehr läuft.
using System.Diagnostics;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Identity.Client;
using ImapClient = MailKit.Net.Imap.ImapClient;

if (args.Length != 7)
{
    Console.Error.WriteLine("Aufruf: MailMonitor <host> <port> <user> <o365ClientId> <ausgabeOrdner> <pollSekunden> <eltern-PID>");
    return 2;
}

var (host, port, user, clientId, outDir, poll) =
    (args[0], int.Parse(args[1]), args[2], args[3], args[4], TimeSpan.FromSeconds(int.Parse(args[5])));
Directory.CreateDirectory(outDir);
var eventsFile = Path.Combine(outDir, "events.csv");
File.WriteAllText(eventsFile, "Time,Kind,Nr,Uid,Detail" + Environment.NewLine);

void Event(string kind, string nr = "", uint uid = 0, string detail = "") =>
    File.AppendAllText(eventsFile, string.Join(",", DateTimeOffset.Now.ToString("o"), kind, nr, uid == 0 ? "" : uid.ToString(),
        "\"" + detail.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ") + "\"") + Environment.NewLine);

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
try { _ = Process.GetProcessById(int.Parse(args[6])).WaitForExitAsync().ContinueWith(_ => cts.Cancel()); }
catch (ArgumentException) { return 3; }   // Elternprozess schon beendet

// Eigene Anmeldung (einmal im Browser), unabhängig vom Token-Cache des Agents; Token nur im Speicher.
string[] scopes = ["https://outlook.office365.com/IMAP.AccessAsUser.All", "offline_access"];
var app = PublicClientApplicationBuilder.Create(clientId)
    .WithAuthority(AzureCloudInstance.AzurePublic, "common")
    .WithRedirectUri("http://localhost")
    .Build();

async Task<string> GetTokenAsync(CancellationToken ct)
{
    var account = (await app.GetAccountsAsync()).FirstOrDefault();
    if (account != null) return (await app.AcquireTokenSilent(scopes, account).ExecuteAsync(ct)).AccessToken;
    Event("login", detail: "Browser-Anmeldung");
    return (await app.AcquireTokenInteractive(scopes).WithLoginHint(user).WithPrompt(Prompt.SelectAccount).ExecuteAsync(ct)).AccessToken;
}

var nrPattern = new Regex(@"MA-E2E-\d{8}-\d{6}");
UniqueId? from = null;                     // über Neuverbindungen hinweg: nichts zwischendurch verpassen
var nrByUid = new Dictionary<uint, string>();
var done = new HashSet<uint>();            // gelesen oder keine Testmail: nicht weiter beobachten
var ready = false;

while (!cts.IsCancellationRequested)
{
    using var client = new ImapClient { Timeout = 30_000 };
    try
    {
        await client.ConnectAsync(host, port, SecureSocketOptions.SslOnConnect, cts.Token);
        await client.AuthenticateAsync(new SaslMechanismOAuth2(user, await GetTokenAsync(cts.Token)), cts.Token);
        Event("connect");
        if (!ready) { ready = true; Event("ready"); }

        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, cts.Token);
        from ??= client.Inbox.UidNext ?? UniqueId.MinValue;
        var reconnectAt = DateTime.Now.AddMinutes(10);
        while (DateTime.Now < reconnectAt)
        {
            await client.Inbox.OpenAsync(FolderAccess.ReadOnly, cts.Token);
            var uids = await client.Inbox.SearchAsync(SearchQuery.Uids(new UniqueIdRange(from.Value, UniqueId.MaxValue)), cts.Token);
            var pending = uids.Where(u => !done.Contains(u.Id)).ToList();
            if (pending.Count > 0)
            {
                var items = MessageSummaryItems.UniqueId | MessageSummaryItems.Flags | MessageSummaryItems.Envelope;
                foreach (var mail in await client.Inbox.FetchAsync(pending, items, cts.Token))
                {
                    var id = mail.UniqueId.Id;
                    if (!nrByUid.TryGetValue(id, out var nr))
                    {
                        nr = nrPattern.Match(mail.Envelope?.Subject ?? "").Value;
                        if (nr.Length == 0) { done.Add(id); continue; }
                        nrByUid[id] = nr;
                        Event("arrived", nr, id);
                    }
                    if (mail.Flags?.HasFlag(MessageFlags.Seen) == true) { done.Add(id); Event("read", nr, id); }
                }
            }
            await Task.Delay(poll, cts.Token);
        }
        await client.DisconnectAsync(true, cts.Token);
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested) { break; }
    catch (Exception ex)
    {
        Event("error", detail: $"{ex.GetType().Name}: {ex.Message}");
        try { await Task.Delay(TimeSpan.FromSeconds(5), cts.Token); } catch (OperationCanceledException) { break; }
    }
}
return 0;
