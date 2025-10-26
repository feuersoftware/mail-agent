# MailAgent Einrichtung

## Allgemeines
Der MailAgent ist eine Software (Konsolenanwendung), die zur Auswertung von E-Mails dient. Es können beliebig viele Postfächer ausgewertet werden. Aufgrund von Abhängigkeiten von Drittanbietern läuft der MailAgent aktuell nur auf Windows. Unterstützt werden `IMAP` und `Microsoft Exchange`. Pro Postfach kann ein Standort (API-Key) zugeordnet werden.
Die Auswertung der E-Mails ist in folgenden Modi möglich:
1. PGP-Verschlüsselte Klartextmails mit direkter Auswertung nach Connect (`ConnectEncrypted`)
2. Unverschlüsselte Klartext-E-Mails (`ConnectPlain`)
3. PGP-Verschlüsselte E-Mails mit PDF-Anhang ohne Klartext (`Pdf`)
4. PGP-Verschlüsselte E-Mails mit Klatext mit Ausgabe einer Textdatei (`Text`)
5. PGP-Verschlüsselte Anhänge von Klartext-E-Mails mit direkter Auswertung nach Connect. (`ConnectPgpAttachment`)

Die direkte Auswertung nach Connect erfolgt mit regulären Ausdrücken (`RegEx`). Falls dieser Modus verwendet wird, muss auch der Abschnitt `ConnectPatternOptions` gepflegt werden.

## Einrichtung

### Voraussetzungen
   Vor der Inbetriebnahme des MailAgents muss das Tool GnuPG for Windows (`GPG4Win`) installiert werden. Dies kann unter https://gnupg.org/download/index.html im unteren Bereich kostenfrei heruntergeladen. In diesem Toolset ist das Programm `Kleopatra` enthalten. Dort werden die öffentlichen und privaten Schlüssel verwaltet.

Das Standard-Installationsverzeichnis für GnuPG ist `C:\Program Files (x86)\GnuPG\bin`, dies sollte auch nach Möglichkeit so bleiben.

In Kleopatra muss dann der öffentliche und private Schlüssel importiert werden und eine Vertrauensstellung für diese hergestellt werden. Um Fehler zu vermeiden empfiehlt es sich, beim privaten Schlüssel das Passwort zu entfernen, sollte eines gesetzt sein.

## Konfiguration MailAgent
Die Konfiguration des MailAgents muss in der im gleichen Verzeichnis befindlichen `appsettings.json`-Datei vorgenommen werden.

## Inbetriebnahme
Der MailAgent kann sowohl als Windows-Dienst oder als normale Anwendung betrieben werden. Für die Registrierung als Windows-Dienst muss die Batchdatei `install.bat` ausgeführt werden. Danach muss der Dienst in den Windows-Einstellungen noch überprüft werden, ob der automatische Start eingestellt ist.s

## Endbestimmungen
Die Weitergabe dieser Software ist ohne ausdrückliche Genehmigung der Feuer Software GmbH nicht gestattet. Desweiteren wird der MailAgent ausschließlich für Connect-Kunden verfügbar.