#Requires -Version 5.1
<#
.SYNOPSIS
    End-to-End-Test des MailAgents: Testalarm-Mail (Outlook, optional PGP via GpgOL) -> Postfach -> MailAgent -> Connect.

.DESCRIPTION
    Ein Aufruf erledigt alles:
      1. Konfiguration lesen und prüfen (e2e.config.json, Vorlage: e2e.config.template.json)
      2. Vorabprüfungen: RegEx gegen das Testmailformat, PGP-Roundtrip, Connect-API, Outlook/GpgOL
      3. MailAgent bauen (self-contained, Architektur passend zur installierten libgpgme von GnuPG)
      4. appsettings.json für den Agent erzeugen und den Agent in eigenem Fenster starten
      5. Postfach-Monitor starten (MailMonitor: eigene, nur lesende IMAP-Sitzung), danach den Agent
      6. Testalarme in zufälligen Abständen senden und jeden 1:1 über seine Einsatznummer verfolgen:
         gesendet -> im Postfach (Monitor) -> vom Agent verarbeitet -> als gelesen markiert (Monitor) -> Einsatz in Connect
      7. Nebenbei das Agent-Log auf Hänger, Fehler und Abbrüche überwachen
      8. Bei Hänger/Subscription-Abbruch: Speicherabbild + Thread-Stacks des Agents sichern; bei Absturz Crash-Dump
      9. Zusammenfassung mit Kennzahlen, Agent und Monitor beenden

    Alle Ergebnisse liegen unter .\out\ (bin\ = Kompilat, runs\<Zeitstempel>\ = Report, Agent-Log, Monitor-Ereignisse).
    Exit-Code 0 = alle Alarme korrekt in Connect, sonst 1.
#>
$ErrorActionPreference = 'Stop'

$Root = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $Root '..\..')).Path
$ConfigFile = Join-Path $Root 'e2e.config.json'
$TemplateFile = Join-Path $Root 'e2e.config.template.json'
$Out = Join-Path $Root 'out'
$Bin = Join-Path $Out 'bin'
$SubjectPrefix = 'MailAgent-E2E'

# Schwellen, abgeleitet aus MailOperationTimeouts: erfolgreicher Poll ("Found N unread mails") alle
# EMailPollingIntervalSeconds, tote Verbindung nach 15 s erkannt. Auch bei Dauerstörung loggt der Agent
# spätestens alle ~215 s (FetchTick 120 s + Disconnect 5 s + Connect 60 s + Backoff 30 s) - Stille darüber = Hänger.
$StallWarnSeconds = 30
$StallCriticalSeconds = 240
$UnreadWarnSeconds = 60            # Mail liegt im Postfach, wird aber so lange nicht gelesen -> Agent sieht sie nicht
$MonitorReadyTimeoutMinutes = 10   # Build + Browser-Anmeldung des Monitors
$FirstPollTimeoutMinutes = 10      # Zeit für Build-Start + interaktiven O365-Login
$AgentTimeoutSeconds = 180         # Senden -> Agent hat die Mail verarbeitet
$ConnectTimeoutSeconds = 300       # Senden -> Einsatz in Connect
$MaxSendFailures = 10              # Versandversuche in Folge (je 1 min Abstand), bevor der Test abbricht

# GpgOL entscheidet beim Senden anhand dieser MAPI-Eigenschaft der Mail (E/e = verschlüsseln, S/s = signieren).
$GpgOLDraftInfo = 'http://schemas.microsoft.com/mapi/string/{31805AB8-3E92-11DC-879C-00061B031004}/GpgOL Draft Info/0x0000001E'

# GnuPG-Installation wie gpgme-sharp sie findet (Registry "Install Directory", sonst Standardpfade). Der Agent muss
# dieselbe Architektur haben wie libgpgme-11.dll: Gpg4win 4.x ist 32-bit, Gpg4win 5.x 64-bit.
function Get-GnuPG {
    $dirs = @('HKLM:\SOFTWARE\GNU\GnuPG', 'HKLM:\SOFTWARE\WOW6432Node\GNU\GnuPG') |
        ForEach-Object { (Get-ItemProperty $_ -ErrorAction SilentlyContinue).'Install Directory' }
    $dirs += 'C:\Program Files\GnuPG', 'C:\Program Files (x86)\GnuPG'
    foreach ($dir in $dirs | Where-Object { $_ }) {
        $dll = Join-Path $dir 'bin\libgpgme-11.dll'
        if (-not (Test-Path $dll)) { continue }
        $b = [IO.File]::ReadAllBytes($dll)
        $machine = [BitConverter]::ToUInt16($b, [BitConverter]::ToInt32($b, 0x3C) + 4)
        $rid = @{ 0x14C = 'win-x86'; 0x8664 = 'win-x64'; 0xAA64 = 'win-arm64' }[[int]$machine]
        if ($rid) { return [pscustomobject]@{ Gpg = Join-Path $dir 'bin\gpg.exe'; Rid = $rid; Dll = $dll } }
    }
}
$GnuPG = Get-GnuPG
$Gpg = if ($GnuPG) { $GnuPG.Gpg }
# Ohne GnuPG (nur ConnectPlain sinnvoll) passend zum Betriebssystem bauen.
$Rid = if ($GnuPG) { $GnuPG.Rid } elseif ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }

$Colors = @{ OK = 'Green'; INFO = 'Gray'; WARN = 'Yellow'; ERR = 'Red'; CRIT = 'Magenta' }
$script:Report = $null

function Say($level, $msg) {
    $line = "{0} [{1,-4}] {2}" -f (Get-Date -Format 'HH:mm:ss'), $level, $msg
    Write-Host $line -ForegroundColor $Colors[$level]
    if ($script:Report) { Add-Content $script:Report $line }
}

function Stop-Test($msg) { Say CRIT $msg; exit 1 }

# --- Testalarm: eine Tabelle ist die Quelle für Mailtext, RegEx, Selbsttest und Soll-Werte in Connect -------
# Nur ASCII ohne '=': Der ConnectEncryptedProcessor dekodiert den entschlüsselten Teil immer als quoted-printable.
function New-Alarm($i, $count) {
    $now = Get-Date
    $nr = 'MA-E2E-' + $now.ToString('yyyyMMdd-HHmmss')
    $rows = @(
        # Label           Wert                                                ConnectPatternOptions   Pfad im Connect-OperationModel
        @('EINSATZNUMMER', $nr,                                              'NumberPattern',        'Number'),
        @('ALARMZEIT',     $now.ToString('dd.MM.yyyy HH:mm:ss'),             'StartPattern',         ''),
        @('STICHWORT',     'THL 1 - Testeinsatz',                            'KeywordPattern',       'Keyword'),
        @('SACHVERHALT',   "MailAgent E2E-Test $i/$count - bitte ignorieren", 'FactsPattern',        'Facts'),
        @('STRASSE',       'Karlsbader Strasse',                             'StreetPattern',        'Address.Street'),
        @('HAUSNUMMER',    '16',                                             'HouseNumberPattern',   'Address.HouseNumber'),
        @('PLZ',           '65760',                                          'ZipCodePattern',       'Address.ZipCode'),
        @('ORT',           'Eschborn',                                       'CityPattern',          'Address.City'),
        @('ORTSTEIL',      'Niederhoechstadt',                               'DistrictPattern',      'Address.District'),
        @('MELDER',        'Erika Musterfrau',                               'ReporterNamePattern',  'Reporter.Name'),
        @('RUFNUMMER',     '06196 1234567',                                  'ReporterPhonePattern', 'Reporter.PhoneNumber'),
        @('RIC',           '1234567 A',                                      'RicPattern',           ''),
        @('RIC',           '7654321 B',                                      'RicPattern',           ''),
        @('BREITE',        '50.14310',                                       'LatitudePattern',      ''),
        @('LAENGE',        '8.57010',                                        'LongitudePattern',     ''),
        @('BEMERKUNG',     "Testlauf $i/$count",                             'Bemerkung',            'Properties.Bemerkung')
    ) | ForEach-Object { [pscustomobject]@{ Label = $_[0]; Value = $_[1]; Pattern = $_[2]; Path = $_[3] } }

    $expected = [ordered]@{}
    foreach ($r in $rows | Where-Object Path) { $expected[$r.Path] = $r.Value }
    $expected['Ric'] = @($rows | Where-Object Label -eq 'RIC' | ForEach-Object Value) -join '; '

    [pscustomobject]@{
        Nr = $nr; Rows = $rows; Expected = $expected
        Body = (@('ALARMIERUNG MAILAGENT-E2E-TEST') + @($rows | ForEach-Object { "$($_.Label): $($_.Value)" })) -join "`r`n"
        Sent = $null; Arrived = $null; Agent = $null; Read = $null; Connect = $null; Result = 'offen'; Warned = @{}
    }
}

function Get-Regex($label) {
    switch ($label) {
        'PLZ' { '(?m)^PLZ:[ \t]*(\d{5})' }
        { $_ -in 'BREITE', 'LAENGE' } { "(?m)^$($label):[ \t]*(-?\d+[.,]\d+)" }
        default { "(?m)^$($label):[ \t]*([^\r\n]+)" }
    }
}

function Get-PatternOptions {
    $p = [ordered]@{}
    foreach ($r in (New-Alarm 1 1).Rows | Where-Object Pattern -ne 'Bemerkung') { $p[$r.Pattern] = Get-Regex $r.Label }
    $p.AdditionalProperties = @([ordered]@{ Name = 'Bemerkung'; Pattern = Get-Regex 'BEMERKUNG' })
    $p
}

function Test-Patterns {
    $a = New-Alarm 1 1
    $p = Get-PatternOptions
    foreach ($g in $a.Rows | Group-Object Pattern) {
        $pattern = if ($g.Name -eq 'Bemerkung') { $p.AdditionalProperties[0].Pattern } else { $p[$g.Name] }
        $is = @([regex]::Matches($a.Body, $pattern) | ForEach-Object { $_.Groups[1].Value.Trim() }) -join '; '
        $expected = @($g.Group | ForEach-Object Value) -join '; '
        if ($is -cne $expected) { throw "RegEx-Selbsttest: $($g.Name) liefert '$is' statt '$expected'." }
    }
}

function Get-Field($op, $path) {
    if ($path -like 'Properties.*') { return ($op.Properties | Where-Object Key -eq $path.Substring(11) | Select-Object -First 1).Value }
    $v = $op
    foreach ($part in $path.Split('.')) { if ($null -eq $v) { return $null }; $v = $v.$part }
    $v
}

# --- Konfiguration -------------------------------------------------------------------------------------
function Read-Config {
    if (-not (Test-Path $ConfigFile)) {
        Copy-Item $TemplateFile $ConfigFile
        Stop-Test "e2e.config.json wurde aus der Vorlage angelegt. Bitte ausfüllen und den Test erneut starten."
    }
    $raw = Get-Content $ConfigFile -Raw
    if ($raw -match '<<[A-Z_]+>>') { Stop-Test "e2e.config.json enthält noch den Platzhalter $($Matches[0])." }
    $c = $raw | ConvertFrom-Json
    $m = $c.MailAgentOptions

    $errors = @()
    if (-not $c.Sender) { $errors += 'Sender fehlt.' }
    if ($c.MailCount -lt 1) { $errors += 'MailCount muss >= 1 sein.' }
    if ($c.MinIntervalMinutes -lt 1 -or $c.MaxIntervalMinutes -lt $c.MinIntervalMinutes) { $errors += 'Es muss gelten: 1 <= MinIntervalMinutes <= MaxIntervalMinutes.' }
    if (-not [guid]::TryParse([string]$c.O365ClientId, [ref][guid]::Empty)) { $errors += 'O365ClientId ist keine GUID.' }
    if (@($m.EmailSettings).Count -ne 1) { $errors += 'MailAgentOptions.EmailSettings muss genau einen Eintrag haben.' }
    elseif (-not $m.EmailSettings[0].EMailUsername) { $errors += 'EMailUsername fehlt.' }
    elseif (-not $m.EmailSettings[0].ApiKey) { $errors += 'ApiKey (Standort-API-Key) fehlt.' }
    if ($m.ProcessMode -notin 'ConnectEncrypted', 'ConnectPlain') { $errors += "ProcessMode '$($m.ProcessMode)' wird nicht unterstützt (ConnectEncrypted oder ConnectPlain)." }
    if (-not [Uri]::IsWellFormedUriString([string]$m.ConnectApiUrl, 'Absolute')) { $errors += 'ConnectApiUrl ist keine absolute URL.' }
    if ($errors) { Stop-Test ("Konfiguration ungültig:`n  " + ($errors -join "`n  ")) }

    $c
}

# --- Vorabprüfungen ------------------------------------------------------------------------------------
function Test-Pgp($recipient, $passphrase) {
    if (-not $Gpg) { Stop-Test 'GnuPG (Gpg4win) nicht gefunden.' }
    $dir = Join-Path $Out 'tmp'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $plain = Join-Path $dir 'pgp-check.txt'; $enc = "$plain.asc"
    Set-Content $plain 'MailAgent E2E PGP-Check'
    # gpg schreibt Statusmeldungen nach stderr - hier zählt nur der Exit-Code.
    $ErrorActionPreference = 'Continue'
    & $Gpg --batch --yes --quiet --trust-model always --armor --encrypt -r $recipient -o $enc $plain 2>$null
    $encOk = $LASTEXITCODE -eq 0
    $dec = if ($encOk) { & $Gpg --batch --quiet --pinentry-mode loopback --passphrase "$passphrase" --decrypt $enc 2>$null }
    $ErrorActionPreference = 'Stop'
    Remove-Item -Recurse -Force $dir
    if (-not $encOk) { Stop-Test "PGP: Kein öffentlicher Schlüssel für $recipient in Kleopatra." }
    if (($dec -join '') -notmatch 'PGP-Check') { Stop-Test "PGP: Entschlüsseln für $recipient fehlgeschlagen (privater Schlüssel fehlt oder SecretKeyPassphrase falsch)." }
}

function Connect-Outlook($sender, $encrypted) {
    $script:Outlook = New-Object -ComObject Outlook.Application
    # Konto per Item(i): Pipeline-Objekte (Where-Object) sind PowerShell-verpackt und werden von InvokeMember abgelehnt.
    $accounts = $script:Outlook.Session.Accounts
    $script:FromAccount = $null
    for ($k = 1; $k -le $accounts.Count; $k++) { if ($accounts.Item($k).SmtpAddress -eq $sender) { $script:FromAccount = $accounts.Item($k) } }
    if (-not $script:FromAccount) { Stop-Test "Kein Outlook-Konto für Sender '$sender'." }

    # SendUsingAccount lässt sich per Late Binding nicht setzen (wird still ignoriert) - nur über den Interop-Typ.
    $pia = Get-ChildItem "$env:WINDIR\assembly\GAC_MSIL\Microsoft.Office.Interop.Outlook" -Recurse -Filter 'Microsoft.Office.Interop.Outlook.dll' -ErrorAction SilentlyContinue |
        Sort-Object FullName | Select-Object -Last 1
    if (-not $pia) { Stop-Test 'Outlook-Interop-Assembly (PIA) nicht gefunden - wird zum Setzen des Absenders gebraucht.' }
    $script:MailItemType = [Reflection.Assembly]::LoadFrom($pia.FullName).GetType('Microsoft.Office.Interop.Outlook._MailItem')

    $addins = $script:Outlook.COMAddIns
    $script:HasGpgOL = $false
    for ($k = 1; $k -le $addins.Count; $k++) { if ($addins.Item($k).ProgId -eq 'GNU.GpgOL' -and $addins.Item($k).Connect) { $script:HasGpgOL = $true } }
    if ($encrypted -and -not $script:HasGpgOL) { Stop-Test 'ProcessMode ConnectEncrypted braucht das aktive Outlook-Add-In GpgOL (Gpg4win).' }
}

function Invoke-Preflight($c) {
    $m = $c.MailAgentOptions
    $encrypted = $m.ProcessMode -eq 'ConnectEncrypted'
    Test-Patterns
    Say OK 'RegEx passen zum Testmailformat.'
    if ($encrypted) { Test-Pgp $m.EmailSettings[0].EMailUsername $m.SecretKeyPassphrase; Say OK 'PGP-Roundtrip mit dem Empfängerschlüssel erfolgreich.' }
    try { [void](Invoke-RestMethod (Get-OpsUrl $m) -Headers (Get-ConnectHeaders $m)) }
    catch { Stop-Test "Connect-API nicht nutzbar ($($m.ConnectApiUrl)): $($_.Exception.Message)" }
    Say OK 'Connect-API erreichbar, Standort-API-Key gültig.'
    Connect-Outlook $c.Sender $encrypted
    Say OK "Outlook bereit, Absender $($c.Sender)$(if ($script:HasGpgOL) { ', GpgOL aktiv' })."
    $script:DotnetDump = Get-DiagTool 'dotnet-dump'
    $script:DotnetStack = Get-DiagTool 'dotnet-stack'
    if ($script:DotnetDump -and $script:DotnetStack) { Say OK 'Diagnose-Werkzeuge bereit (dotnet-dump, dotnet-stack).' }
    else { Say WARN 'dotnet-dump/dotnet-stack nicht verfügbar - bei einem Hänger werden keine Abbilder/Stacks gesichert.' }
}

# Diagnose-Werkzeuge: dotnet-dump (Speicherabbild) und dotnet-stack (lesbare Thread-Stacks). Sie sprechen über die
# Diagnose-Schnittstelle der Runtime mit dem Agent, funktionieren also auch mit einem x86-Agent. Fehlen sie im PATH,
# werden sie lokal nach out\tools installiert.
function Get-DiagTool($name) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $tools = Join-Path $Out 'tools'
    $exe = Join-Path $tools "$name.exe"
    if (-not (Test-Path $exe)) { dotnet tool install $name --tool-path $tools *> $null }
    if (Test-Path $exe) { $exe }
}

function Save-Diagnostics($agent, $runDir, $reason) {
    if ($agent.HasExited) { return }
    $stamp = Get-Date -Format 'HHmmss'
    $ErrorActionPreference = 'Continue'
    if ($script:DotnetStack) {
        $stacks = Join-Path $runDir "stacks-$stamp-$reason.txt"
        & $script:DotnetStack report -p $agent.Id *> $stacks
        Say CRIT "Thread-Stacks gesichert: $stacks"
    }
    if ($script:DotnetDump) {
        $dump = Join-Path $runDir "dump-$stamp-$reason.dmp"
        & $script:DotnetDump collect -p $agent.Id --type Heap -o $dump *> $null
        if (Test-Path $dump) { Say CRIT "Speicherabbild gesichert: $dump" } else { Say WARN 'Speicherabbild konnte nicht erstellt werden.' }
    }
}

function Get-OpsUrl($m) { "$(([string]$m.ConnectApiUrl).TrimEnd('/'))/interfaces/public/operation?`$orderby=CreatedAt desc&`$top=50" }
function Get-ConnectHeaders($m) { @{ Authorization = "Bearer $($m.EmailSettings[0].ApiKey)" } }

# --- Build und Agent -----------------------------------------------------------------------------------
function Build-Agent($c, $runDir) {
    Say INFO "Baue MailAgent ($Rid self-contained$(if ($GnuPG) { ", passend zu $($GnuPG.Dll)" }))..."
    $clientIdProp = "/p:O365ClientId=$($c.O365ClientId)"
    # Aus diesem Ordner bauen: die lokale global.json erlaubt neuere SDKs als die Repo-global.json.
    Push-Location $Root
    try {
        # Ausgabe (inkl. Compiler-Warnungen aus dem Agent-Code) nur ins Build-Log, angezeigt wird sie nur bei Fehlern.
        $buildLog = Join-Path $runDir 'build.log'
        dotnet publish (Join-Path $RepoRoot 'MailAgent\MailAgent.csproj') -c Release -r $Rid --self-contained -o $Bin -v q -nologo $clientIdProp *> $buildLog
        if ($LASTEXITCODE) {
            Get-Content $buildLog | Select-String ' error ' | Select-Object -First 10 | ForEach-Object { Say ERR $_.Line.Trim() }
            Stop-Test "Build fehlgeschlagen (Exit $LASTEXITCODE), Details: $buildLog"
        }
    }
    finally { Pop-Location }
    Say OK 'Build erfolgreich.'
}

function Write-AgentSettings($c, $runDir) {
    $m = $c.MailAgentOptions
    # Nur Testmails verarbeiten - alle anderen Mails im Postfach bleiben unberührt (außer: siehe README, 15-min-Regel).
    $m.EmailSettings[0] | Add-Member -Force NoteProperty EMailSubjectFilter $SubjectPrefix
    $settings = [ordered]@{
        Serilog = [ordered]@{
            Using = @('Serilog.Sinks.Console', 'Serilog.Sinks.File')
            MinimumLevel = [ordered]@{ Default = 'Debug'; Override = [ordered]@{ Microsoft = 'Information'; System = 'Warning' } }
            WriteTo = @(
                [ordered]@{ Name = 'Console' },
                [ordered]@{ Name = 'File'; Args = [ordered]@{
                        path = (Join-Path $runDir 'agent-.log'); rollingInterval = 'Day'
                        outputTemplate = '{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}' } }
            )
        }
        ConnectPatternOptions = Get-PatternOptions
        MailAgentOptions = $m
    }
    $settings | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $Bin 'appsettings.json') -Encoding UTF8
}

# --- Versand -------------------------------------------------------------------------------------------
function Send-Alarm($c, $i) {
    $m = $c.MailAgentOptions
    $a = New-Alarm $i $c.MailCount
    $mail = $script:Outlook.CreateItem(0)
    [void]$script:MailItemType.InvokeMember('SendUsingAccount', [Reflection.BindingFlags]::SetProperty, $null, $mail, @($script:FromAccount))
    $mail.To = $m.EmailSettings[0].EMailUsername
    $mail.Subject = "$SubjectPrefix $($a.Nr)"
    $mail.BodyFormat = 1   # Klartext, kein TNEF/winmail.dat
    $mail.Body = $a.Body
    try {
        # GpgOL hängt sich nur an geöffnete Mails und belegt dabei seine Vorgabe - danach pro Mail festlegen.
        $mail.Display()
        Start-Sleep -Seconds 3
        if ($script:HasGpgOL) {
            $flags = if ($m.ProcessMode -eq 'ConnectEncrypted') { 'EsA' } else { 'esA' }
            try { $mail.PropertyAccessor.SetProperty($GpgOLDraftInfo, $flags) }
            catch { $mail.Close(1); Stop-Test "GpgOL-Verschlüsselung ließ sich nicht automatisch steuern ($($_.Exception.Message)). In GpgOL 'Neue Nachrichten standardmäßig verschlüsseln' passend zum ProcessMode einstellen und neu starten." }
        }
        $actual = $mail.SendUsingAccount.SmtpAddress
        if ($actual -ne $c.Sender) { $mail.Close(1); Stop-Test "Absender wäre '$actual' statt '$($c.Sender)' - nicht gesendet." }
        $mail.Send()
    }
    catch {
        # z. B. "Outlook kann die gewünschte Aktion nicht ausführen, da ein Dialogfeld geöffnet ist": Entwurf verwerfen,
        # der Aufrufer versucht es später erneut.
        try { $mail.Close(1) } catch { }
        throw
    }
    $a.Sent = Get-Date
    Say INFO "Gesendet $i/$($c.MailCount): $($a.Nr)"
    $a
}

function Format-Seconds($from, $to) { if ($from -and $to) { '{0,7:N0}' -f ($to - $from).TotalSeconds } else { '      -' } }

function Get-Stats($values) {
    $v = @($values | Sort-Object)
    if (-not $v.Count) { return '-' }
    'Median {0:N0} s, Max {1:N0} s' -f $v[[int][math]::Floor(($v.Count - 1) / 2)], $v[-1]
}

function Show-Summary($alarms, $n) {
    Say INFO '===== Zusammenfassung (Sekunden nach Versand) ====='
    Say INFO ('{0,-24} {1,-8} {2,7} {3,7} {4,7} {5,7}  {6}' -f 'Alarm', 'gesendet', 'Postf.', 'Agent', 'gelesen', 'Connect', 'Ergebnis')
    foreach ($a in $alarms) {
        $lvl = if ($a.Result -eq 'OK') { 'OK' } elseif ($a.Result -eq 'offen') { 'WARN' } else { 'ERR' }
        Say $lvl ('{0,-24} {1:HH:mm:ss} {2} {3} {4} {5}  {6}' -f $a.Nr, $a.Sent, (Format-Seconds $a.Sent $a.Arrived),
            (Format-Seconds $a.Sent $a.Agent), (Format-Seconds $a.Sent $a.Read), (Format-Seconds $a.Sent $a.Connect), $a.Result)
    }
    # Zustellung (Outlook/Exchange) und Agent getrennt: Ausreißer beim Versand gehen nicht zu Lasten des Agents.
    $delivery = $alarms | Where-Object { $_.Arrived } | ForEach-Object { ($_.Arrived - $_.Sent).TotalSeconds }
    $agentTime = $alarms | Where-Object { $_.Arrived -and $_.Agent } | ForEach-Object { [math]::Max(0, ($_.Agent - $_.Arrived).TotalSeconds) }
    Say INFO "Zustellung (Versand -> Postfach): $(Get-Stats $delivery)"
    Say INFO "Agent (Postfach -> verarbeitet):  $(Get-Stats $agentTime)"
    Say INFO ("Gesendet {0} | korrekt in Connect {1} | fehlerhaft/fehlend {2} | Polls {3} | Reconnects {4} | Agent-Fehler {5} | Timeouts {6}" -f `
        @($alarms).Count, @($alarms | Where-Object Result -eq 'OK').Count, @($alarms | Where-Object { $_.Result -notin 'OK', 'offen' }).Count,
        $n.polls, $n.reconnects, $n.errors, $n.timeouts)
}

# --- Postfach-Monitor ----------------------------------------------------------------------------------
# Unabhängige, nur lesende IMAP-Sitzung (MailMonitor): zeigt, wann eine Testmail wirklich im Postfach liegt und wann
# der Agent sie als gelesen markiert. Liegt eine Mail länger als $UnreadWarnSeconds ungelesen da, sieht der Agent sie
# nicht - genau das ursprüngliche Fehlerbild.
function Start-Monitor($c, $runDir) {
    $s = $c.MailAgentOptions.EmailSettings[0]
    $monitorBin = Join-Path $Out 'monitor-bin'
    $buildLog = Join-Path $runDir 'monitor-build.log'
    Push-Location $Root   # lokale global.json
    try { dotnet build (Join-Path $Root 'MailMonitor\MailMonitor.csproj') -c Release -o $monitorBin -v q -nologo *> $buildLog }
    finally { Pop-Location }
    if ($LASTEXITCODE) { Stop-Test "Build des Postfach-Monitors fehlgeschlagen, Details: $buildLog" }

    $monitorDir = Join-Path $runDir 'monitor'
    $script:MonitorCsv = Join-Path $monitorDir 'events.csv'
    $monitorArgs = @($s.EMailHost, $s.EMailPort, $s.EMailUsername, $c.O365ClientId, "`"$monitorDir`"",
        $c.MailAgentOptions.EMailPollingIntervalSeconds, $PID)
    $script:Monitor = Start-Process (Join-Path $monitorBin 'MailMonitor.exe') -ArgumentList $monitorArgs -PassThru -WindowStyle Minimized
    Say INFO "Postfach-Monitor gestartet (PID $($script:Monitor.Id)), Anmeldung..."

    $deadline = (Get-Date).AddMinutes($MonitorReadyTimeoutMinutes); $reported = 0
    while ((Get-Date) -lt $deadline -and -not $script:Monitor.HasExited) {
        $events = if (Test-Path $script:MonitorCsv) { @(Import-Csv $script:MonitorCsv) } else { @() }
        foreach ($e in $events | Select-Object -Skip $reported) {
            if ($e.Kind -eq 'login') { Say WARN 'Postfach-Monitor: bitte im Browser anmelden.' }
            elseif ($e.Kind -eq 'error') { Say WARN "Postfach-Monitor: $($e.Detail)" }
        }
        $reported = $events.Count
        if ($events | Where-Object Kind -eq 'ready') {
            $script:MonitorPos = (Get-Item $script:MonitorCsv).Length   # Update-Monitor setzt nach der Anmeldung ein
            Say OK 'Postfach-Monitor angemeldet.'
            return
        }
        Start-Sleep -Seconds 1
    }
    Stop-Test "Postfach-Monitor hat sich nicht innerhalb von $MonitorReadyTimeoutMinutes min angemeldet (Browser-Anmeldung?)."
}

function Update-Monitor($alarms) {
    if (-not $script:MonitorCsv -or -not (Test-Path $script:MonitorCsv)) { return }
    $fs = [IO.File]::Open($script:MonitorCsv, 'Open', 'Read', 'ReadWrite')
    [void]$fs.Seek($script:MonitorPos, 'Begin')
    $sr = New-Object IO.StreamReader($fs)
    $lines = @(); while ($null -ne ($l = $sr.ReadLine())) { $lines += $l }
    $script:MonitorPos = $fs.Position; $sr.Close()

    foreach ($e in $lines | Where-Object { $_ -and $_ -notlike 'Time,*' } | ConvertFrom-Csv -Header 'Time', 'Kind', 'Nr', 'Uid', 'Detail') {
        $a = $alarms | Where-Object Nr -eq $e.Nr | Select-Object -First 1
        switch ($e.Kind) {
            'arrived' { if ($a) { $a.Arrived = [datetime]$e.Time; Say INFO ('Monitor: {0} im Posteingang angekommen nach {1:N0} s.' -f $a.Nr, ($a.Arrived - $a.Sent).TotalSeconds) } }
            'read' { if ($a) { $a.Read = [datetime]$e.Time; Say OK ('Monitor: {0} als gelesen markiert nach {1:N0} s.' -f $a.Nr, ($a.Read - $a.Sent).TotalSeconds) } }
            'error' { Say WARN "Postfach-Monitor: $($e.Detail)" }
        }
    }
    foreach ($a in $alarms | Where-Object { $_.Arrived -and -not $_.Read -and -not $_.Warned.unread }) {
        $waiting = ((Get-Date) - $a.Arrived).TotalSeconds
        if ($waiting -gt $UnreadWarnSeconds) {
            $a.Warned.unread = $true
            Say ERR ('Monitor: {0} liegt seit {1:N0} s ungelesen im Posteingang - der Agent sieht sie nicht!' -f $a.Nr, $waiting)
        }
    }
}

function Stop-Monitor {
    if ($script:Monitor -and -not $script:Monitor.HasExited) { Stop-Process -Id $script:Monitor.Id -Force }
}

# --- Ablauf --------------------------------------------------------------------------------------------
function Invoke-E2ETest {
    $c = Read-Config
    $m = $c.MailAgentOptions
    $encrypted = $m.ProcessMode -eq 'ConnectEncrypted'
    $runDir = Join-Path $Out ('runs\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Force $runDir | Out-Null
    $script:Report = Join-Path $runDir 'e2e-report.log'
    Say INFO "E2E-Test: $($c.MailCount) Alarme an $($m.EmailSettings[0].EMailUsername), $($m.ProcessMode), alle $($c.MinIntervalMinutes)-$($c.MaxIntervalMinutes) min, Connect $($m.ConnectApiUrl)"
    Say INFO "Ergebnisse: $runDir"

    if ($c.MinIntervalMinutes -lt 3) { Say WARN 'MinIntervalMinutes < 3: Connect erlaubt nur 25 Einsätze pro Stunde.' }
    Invoke-Preflight $c
    Build-Agent $c $runDir
    Write-AgentSettings $c $runDir
    $env:O365_CLIENT_ID = $c.O365ClientId   # unterdrückt die Warnung im ConfigurationValidator des Agents
    Start-Monitor $c $runDir
    # Stürzt der Agent ab, schreibt die .NET-Runtime selbst einen Crash-Dump in den Lauf-Ordner.
    $env:DOTNET_DbgEnableMiniDump = '1'; $env:DOTNET_DbgMiniDumpType = '2'   # 2 = mit Heap
    $env:DOTNET_DbgMiniDumpName = Join-Path $runDir 'crash-%p.dmp'
    $agent = Start-Process (Join-Path $Bin 'FeuerSoftware.MailAgent.exe') -WorkingDirectory $Bin -PassThru
    Say INFO "Agent gestartet (PID $($agent.Id), eigenes Fenster). Beim ersten Start dort den O365-Login durchführen."

    $opsUrl = Get-OpsUrl $m
    $headers = Get-ConnectHeaders $m
    $alarms = New-Object System.Collections.ArrayList
    $n = @{ polls = 0; reconnects = 0; errors = 0; timeouts = 0 }
    $started = Get-Date; $nextSend = $null
    $logFile = $null; $pos = 0
    $lastPoll = $null; $lastActivity = $null; $stall = 0; $lastStallWarn = $null
    $lastErr = $false; $lastConnectCheck = [datetime]::MinValue; $lastStatus = Get-Date
    $abort = $null; $sendFailures = 0; $filterSeen = @{}

    try {
        while (-not $abort) {
            $now = Get-Date

            if ($agent.HasExited) {
                $crash = Get-ChildItem $runDir -Filter 'crash-*.dmp' -ErrorAction SilentlyContinue | Select-Object -First 1
                $abort = "MailAgent-Prozess ist beendet (Exit $($agent.ExitCode))$(if ($crash) { ", Crash-Dump: $($crash.FullName)" })."; break
            }
            if (-not $lastPoll -and ($now - $started).TotalMinutes -gt $FirstPollTimeoutMinutes) {
                $abort = "Agent pollt nach $FirstPollTimeoutMinutes min noch nicht - Login oder Konfigurationsfehler im Agent-Fenster prüfen."; break
            }

            # Versand (erst, wenn der Agent nachweislich pollt)
            if ($nextSend -and $alarms.Count -lt $c.MailCount -and $now -ge $nextSend) {
                try {
                    [void]$alarms.Add((Send-Alarm $c ($alarms.Count + 1)))
                    $sendFailures = 0
                    $nextSend = $now.AddMinutes((Get-Random -Minimum $c.MinIntervalMinutes -Maximum ($c.MaxIntervalMinutes + 1)))
                    if ($alarms.Count -lt $c.MailCount) { Say INFO ("Nächster Alarm um {0:HH:mm}" -f $nextSend) }
                }
                catch {
                    # Vorübergehende Outlook-Probleme (offener Dialog o. Ä.) sollen einen langen Lauf nicht beenden.
                    $sendFailures++
                    if ($sendFailures -ge $MaxSendFailures) { $abort = "Versand $sendFailures-mal in Folge fehlgeschlagen, zuletzt: $($_.Exception.Message)"; break }
                    Say WARN "Versand fehlgeschlagen ($sendFailures/$MaxSendFailures): $($_.Exception.Message) - neuer Versuch in 1 min."
                    $nextSend = $now.AddMinutes(1)
                }
            }

            # Agent-Log (Serilog rollt täglich -> immer neueste Datei)
            $newest = Get-ChildItem $runDir -Filter 'agent-*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
            if ($newest -and $newest.FullName -ne $logFile) { $logFile = $newest.FullName; $pos = 0 }
            $lines = @()
            if ($logFile) {
                $fs = [IO.File]::Open($logFile, 'Open', 'Read', 'ReadWrite')
                if ($fs.Length -lt $pos) { $pos = 0 }
                [void]$fs.Seek($pos, 'Begin')
                $sr = New-Object IO.StreamReader($fs)
                while ($null -ne ($l = $sr.ReadLine())) { $lines += $l }
                $pos = $fs.Position; $sr.Close()
            }

            foreach ($line in $lines) {
                # Die Einsatznummer steht im (entschlüsselten) Text, den der Agent auf Debug-Level loggt.
                foreach ($a in $alarms) {
                    if (-not $a.Agent -and $line.Contains($a.Nr)) {
                        $a.Agent = $now
                        if ($line -like '*received delayed*') {
                            # Altersgrenze des Agents: als gelesen markiert, nie an Connect übergeben - endgültig verloren.
                            $a.Result = 'vom Agent verworfen (> 15 min alt)'; $a.Warned.connect = $true
                            Say ERR ("{0} vom Agent verworfen: erst nach {1:N0} s gesehen (> 15 min)." -f $a.Nr, ($now - $a.Sent).TotalSeconds)
                        }
                        else { Say OK ("{0} vom Agent verarbeitet nach {1:N0} s." -f $a.Nr, ($now - $a.Sent).TotalSeconds) }
                    }
                }
                if (-not $encrypted -and $line -like '*Part text/plain is null or whitespace*') {
                    $abort = "Mail kam verschlüsselt an, obwohl ProcessMode ConnectPlain ist - die automatische GpgOL-Steuerung hat nicht gegriffen. In GpgOL 'Neue Nachrichten standardmäßig verschlüsseln' ausschalten und neu starten."; break
                }
                if ($line -notmatch '^\S+ \S+ \S+ \[(\w{3})\] (.*)$') {
                    if ($lastErr -and $line -match '^\S*(Exception|Error)\b') { Say ERR "  -> $line"; $lastErr = $false }
                    if ($line -match 'TimeoutException|OperationCanceledException|TaskCanceledException') { $n.timeouts++ }
                    continue
                }
                $lvl = $Matches[1]; $msg = $Matches[2]; $lastErr = $false
                $lastActivity = $now

                if ($msg -like 'Found * unread mails*') {
                    $n.polls++
                    if ($stall -gt 0) { Say OK ("Polling läuft wieder (Stillstand {0:N0} s)." -f ($now - $lastPoll).TotalSeconds) }
                    elseif (-not $lastPoll) { Say OK 'Agent pollt - Überwachung aktiv, erster Alarm geht raus.'; $nextSend = $now }
                    $lastPoll = $now; $stall = 0; $lastStallWarn = $null
                }
                elseif ($encrypted -and $msg -like 'Failed to use ConnectEncryptedProcessor*') {
                    $abort = "PGP-Verarbeitung fehlgeschlagen: Die Mail kam unverschlüsselt an (automatische GpgOL-Steuerung hat nicht gegriffen) oder ließ sich nicht entschlüsseln. In GpgOL 'Neue Nachrichten standardmäßig verschlüsseln' einschalten bzw. Schlüssel/SecretKeyPassphrase prüfen."; break
                }
                elseif ($msg -like 'Reconnecting*') { $n.reconnects++; Say INFO $msg }
                elseif ($msg -like 'Connected to O365*') { Say OK 'O365 verbunden.' }
                elseif ($msg -like 'Successfully published Operation*') { Say OK 'Agent: Einsatz an Connect übergeben.' }
                elseif ($msg -like '*completed unexpectedly*') { Say CRIT "Subscription beendet - Postfach wird NICHT mehr gepollt: $msg"; Save-Diagnostics $agent $runDir 'subscription' }
                elseif ($msg -like '*Silent token acquisition failed*') { Say CRIT 'Token-Refresh fehlgeschlagen - interaktiver Re-Login nötig.' }
                elseif ($msg -like '*received delayed*') { if (-not ($alarms | Where-Object { $msg.Contains($_.Nr) })) { Say WARN "Mail älter als 15 min -> verworfen: $msg" } }
                elseif ($msg -like '*failed subject-filter*') {
                    # Der Agent meldet das bei jedem Poll erneut, solange die Mail ungelesen bleibt - je Mail nur einmal ausgeben.
                    if (-not $filterSeen.ContainsKey($msg)) { $filterSeen[$msg] = $true; Say WARN "Fremde Mail im Posteingang (Betreff-Filter greift nicht, Agent lädt sie bei jedem Poll erneut): $msg" }
                }
                elseif ($msg -like 'Retrying initial connect*') { Say WARN $msg }
                elseif ($lvl -in 'ERR', 'FTL') { $n.errors++; $lastErr = $true; Say ERR $msg }
                elseif ($lvl -eq 'WRN') { Say WARN $msg }
            }
            if ($abort) { break }

            # Connect: offene Alarme gegen die Public API prüfen (alle 30 s, nur wenn etwas offen ist)
            $open = @($alarms | Where-Object { -not $_.Connect })
            if ($open.Count -and ($now - $lastConnectCheck).TotalSeconds -ge 30) {
                $lastConnectCheck = $now
                try {
                    # Klammern + ForEach-Object rollen das JSON-Array unter PowerShell 5.1 und 7 gleich aus.
                    $ops = @((Invoke-RestMethod $opsUrl -Headers $headers) | ForEach-Object { $_ })
                    foreach ($a in $open) {
                        $op = $ops | Where-Object Number -eq $a.Nr | Select-Object -First 1
                        if (-not $op) { continue }
                        $a.Connect = $now
                        $diff = foreach ($k in $a.Expected.Keys) {
                            $is = [string](Get-Field $op $k)
                            if ($is -cne $a.Expected[$k]) { "$k ist '$is' statt '$($a.Expected[$k])'" }
                        }
                        if ($diff) { $a.Result = "Felder falsch: $($diff -join '; ')"; Say ERR "$($a.Nr) in Connect, aber $($a.Result)" }
                        else { $a.Result = 'OK'; Say OK ("{0} in Connect, alle Felder korrekt ({1:N0} s nach Versand)." -f $a.Nr, ($now - $a.Sent).TotalSeconds) }
                    }
                }
                catch { Say WARN "Connect-Abfrage fehlgeschlagen: $($_.Exception.Message)" }
            }

            foreach ($a in $open) {
                $age = ($now - $a.Sent).TotalSeconds
                if (-not $a.Agent -and $age -gt $AgentTimeoutSeconds -and -not $a.Warned.agent) {
                    $a.Warned.agent = $true; Say WARN ("{0} seit {1:N0} s nicht vom Agent verarbeitet." -f $a.Nr, $age)
                }
                if ($age -gt $ConnectTimeoutSeconds -and -not $a.Warned.connect) {
                    $a.Warned.connect = $true; $a.Result = 'fehlt in Connect'; Say ERR ("{0} nach {1:N0} s nicht in Connect." -f $a.Nr, $age)
                }
            }

            # Stillstand: kein erfolgreicher Poll = Verbindung gestört; gar keine Logzeile = Hänger
            if ($lastPoll) {
                $gap = ($now - $lastPoll).TotalSeconds
                $silent = ($now - $lastActivity).TotalSeconds
                if ($silent -gt $StallCriticalSeconds -and $stall -lt 2) {
                    $stall = 2; Say CRIT ("Seit {0:N0} s keine einzige Logzeile - Agent hängt, die Timeouts greifen nicht!" -f $silent)
                    Save-Diagnostics $agent $runDir 'haenger'
                }
                elseif ($gap -gt $StallWarnSeconds -and $stall -lt 2 -and (-not $lastStallWarn -or ($now - $lastStallWarn).TotalSeconds -ge 60)) {
                    $stall = 1; $lastStallWarn = $now
                    Say WARN ("Seit {0:N0} s kein erfolgreicher Poll - Verbindung gestört, Agent arbeitet (letzte Logzeile vor {1:N0} s)." -f $gap, $silent)
                }
            }

            if (($now - $lastStatus).TotalMinutes -ge 5) {
                $lastStatus = $now
                $ago = if ($lastPoll) { '{0:N0} s' -f ($now - $lastPoll).TotalSeconds } else { '-' }
                $next = if ($nextSend -and $alarms.Count -lt $c.MailCount) { '{0:HH:mm}' -f $nextSend } else { '-' }
                Say INFO ("Status: gesendet {0}/{1} | in Connect {2} | offen {3} | nächster {4} | Polls {5} | Reconnects {6} | Fehler {7} | letzter Poll vor {8}" -f `
                    $alarms.Count, $c.MailCount, @($alarms | Where-Object Connect).Count, $open.Count, $next, $n.polls, $n.reconnects, $n.errors, $ago)
            }

            Update-Monitor $alarms

            # Fertig, wenn alles gesendet und jeder Alarm in Connect oder endgültig überfällig ist
            if ($alarms.Count -ge $c.MailCount -and -not @($alarms | Where-Object { -not $_.Connect -and -not $_.Warned.connect }).Count) { break }

            Start-Sleep -Seconds 1
        }
    }
    catch {
        # Alles Unerwartete als Abbruchgrund in den Report (Stop-Test/exit und Strg+C landen nicht hier).
        $abort = "Unerwarteter Fehler: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)"
    }
    finally {
        if ($abort) { Say CRIT "Test abgebrochen: $abort" }
        Update-Monitor $alarms
        Show-Summary $alarms $n
        Stop-Monitor
        if (-not $agent.HasExited) { Stop-Process -Id $agent.Id -Force }
        Say INFO 'Agent und Postfach-Monitor beendet.'
        Say INFO "Report: $script:Report"
    }
    if ($abort -or @($alarms | Where-Object Result -ne 'OK').Count) { exit 1 }
    exit 0
}

# Beim Dot-Sourcing (. .\Run-E2ETest.ps1) nur die Funktionen laden, nicht den Test starten.
if ($MyInvocation.InvocationName -ne '.') { Invoke-E2ETest }
