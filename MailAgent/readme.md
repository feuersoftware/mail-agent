# MailAgent

## Beschreibung

Der MailAgent dient der Verarbeitung von E-Mails von mehreren E-Mailadressen bzw. Standorten, die zur Alarmierung genutzt werden. 

Es können auch PGP-verschlüsselte E-Mails empfangen und verarbeitet werden.

## Abhängigkeiten
Wenn PGP-verschlüsselte Mails empfangen werden sollen, muss auf der betreffenden Maschine muss das Tool "GnuPG" installiert sein. (https://www.gnupg.org/) Mit dem GnuPG Privacy Guard wird auch das Tool "Kleopatra" installiert. Dort muss der jeweilige private Schlüssel zur Entschlüsselung importiert sein.

## Funktionsweise
Die Mails können sowohl via IMAP als auch Microsoft Exchange abgerufen werden. Dies wird in den Einstellungen unter `MailAgentOptions`->`EMailMode` eingestellt. Gültige Werte sind `Imap` oder `Exchange`.

### Authentifizierung
Der MailAgent unterstützt zwei Authentifizierungsmethoden:

**Basic Authentication:** Klassische Authentifizierung mit Benutzername und Passwort. Diese Methode funktioniert mit IMAP und Exchange (EWS).

**O365 Modern Authentication:** OAuth2-basierte Authentifizierung für Office 365 Postfächer. Diese Methode ist besonders empfohlen für O365-Umgebungen, da Microsoft die Basic Authentication schrittweise deaktiviert.
* Beim ersten Start des MailAgents wird für jedes konfigurierte O365-Postfach eine interaktive Browser-Anmeldung durchgeführt.
* Die erhaltenen Tokens werden sicher verschlüsselt auf dem System gespeichert (unter Windows mit Data Protection API).
* Bei nachfolgenden Starts werden die gespeicherten Tokens automatisch verwendet und bei Bedarf erneuert.
* Für O365-Authentifizierung muss nur der `EMailUsername` konfiguriert werden, das `EMailPassword`-Feld wird nicht benötigt.

### Prozessoren
Um verschiedene Anwendungsfälle abdecken zu können, können beliebig viele Prozessoren registriert werden, um Mails zu verarbeiten. Aktuell sind folgende Prozessoren implementiert:
Der Prozessor wird in den Einstellungen unter `MailAgentOptions`->`ProcessMode` eingestellt. Gültige Werte sind:
* `Text` (PGP-Verschlüsselter Text mit Ausgabe in Datei)
* `Pdf` (PGP-Verschlüsselte E-Mail mit PDF-Anhang mit Ausgabe in Datei)
* `ConnectPlain` -> ConnectPlain-Prozessor (unverschlüsselt, Auswertung mit RegEx direkt nach Connect)
* `ConnectPlain` (Unverschlüsselte Klartext-E-Mail mit direkter Verarbeitung nach Connect)
* `ConnectEncrypted` (PGP-Verschlüsselte E-Mail mit direkter Verarbeitung nach Connect)
* `ConnectEncryptedHtml` -> ConnectEncryptedHtml-Prozessor (PGP-Verschlüsselt, HTML Mail, Auswertung mit RegEx direkt nach Connect)
* `ConnectPgpAttachment` -> ConnectPgpAttachment-Prozessor (PGP-Anhänge, Auswertung mit RegEx direkt nach Connect)


### Text-Prozessor (PGP)
Der verschlüsselte "text/plain" Part der Mail wird extrahiert und an GnuPG übergeben. Dort wird dieser entschlüsselt. Der entschlüsselte Text wird in einer Textdatei im konfigurierten Ausgabeverzeichnis abgelegt.

### PDF-Prozessor (PGP)
Es handelt sich um eine PGP-Verschlüsselte E-Mail mit einem Base64-Kodierten PDF-Anhang. Die entschlüsselten Daten sind Base64-kodiert. 
Die konvertierten Daten werden dann als PDF-Datei ins konfigurierte Ausgabeverzeichnis geschrieben. 
Danach erfolgt die Verarbeitung mit z.B. EM-OCR.

### ConnectPlain-Prozessor (Unverschlüsselt) bzw. ConnectEncrypted-Prozessor (PGP)
Der "text/plain" Part der Mail wird extrahiert. Unter `ConnectPatternOptions` müssen unter den einzelnen Unterpunkten reguläre Ausdrücke (Regex) angegeben werden, um die Informationen zu extrahieren. 
Dabei wird für die Extraktion die erste Capture-Group genutzt.
* `NumberPattern` -> Extrahiert die Einsatznummer
* `KeywordPattern` -> Extrahiert das Einsatzstichwort
* `FactsPattern` -> Extrahiert den Sachverhalt
* `StreetPattern`  -> Extrahiert die Straße
* `HouseNumberPattern` -> Extrahiert die Hausnummer
* `CityPattern` -> Extrahiert die Stadt/Gemeinde
* `DistrictPattern` -> Extrahiert den Stadt-/Ortsteil
* `ZipCodePattern` -> Extrahiert die Postleitzahl
* `RicPattern` -> Extrahiert die Schleifen (RIC)
* `ReporterNamePattern` -> Extrahiert den Namen des Meldenden
* `ReporterPhonePattern` -> Extrahiert die Telefonnummer des Meldenden
* `LatitudePattern` -> Extrahiert die den Lat-Wert der Koordinaten
* `LongitudePattern` -> Extrahiert die Lng-Wert der Koordinaten
Unter `AdditionalProperties` können beliebig viele Zusatzinformationen angegeben werden, die ausgewertet werden sollen. Für jede Zusatzinformation muss ein Name (der später die Feldbezeichnung in Connect ist) und ein Pattern (Regex) angegeben werden.

Konfigurationsbeispiel für eine Mail mit semikolongetrennten Werten:

```
"ConnectPatternOptions": {
    "NumberPattern": "",    
    "KeywordPattern": "Alarmstichwort:[^;]*;[^;]*;([^;]*);",
    "FactsPattern": "Alarmstichwort:[^;]*;[^;]*;[^;]*;([^;]*);",
    "StreetPattern": "Adresse:([^;]*)",
    "HouseNumberPattern": "Adresse:[^;]*;([^;]*)",
    "CityPattern": "Einsatzort:([^;]*);",
    "DistrictPattern": "Einsatzort:[^;]*;([^;]*);",
    "ZipCodePattern": "",
    "RicPattern": "Alarmierte Einheiten:([^;]*)",
    "ReporterNamePattern": "",
    "ReporterPhonePattern": "",
    "LatitudePattern": "",
    "LongitudePattern": "",
    "AdditionalProperties": [
        {
        "Name": "Einsatzart",
        "Pattern": "Alarmstichwort:([^;]*);"
        }
    ]
},
```

Für das entwickeln der regulären Ausdrücke sind folgende Seiten/Tools hilfreich:
https://regex101.com/ (testen und entwickeln)
https://developer.mozilla.org/en-US/docs/Web/JavaScript/Guide/Regular_Expressions/Cheatsheet (allgemeine Informationen)

## Konfiguration

### Abschnitt `Serilog`
Dies ist die Konfiguration der Protokollierung. Unter  `MinimumLevel`  kann die Protokollierungebene gewählt werden. Gültige Werte sind:  `Debug`,  `Information`,  `Warning`,  `Error`  und  `Critical`. 
Im Unterabschnitt  `WriteTo`  unter "File" kann der Pfad für die Logdateien und die Anzahl der Logdateien, die aufbewahrt werden sollen, gewählt werden. Im Pfad müssen doppelte  `\`  verwendet werden!

### Abschnitt `ConnectPatternOptions`
Siehe ConnectPlain-Prozessor bzw. ConnectEncrypted-Prozessor. Wird nur verwendet, wenn einer dieser Prozessoren mit direkter Verarbeitung nach Connect verwendet wird.

### Abschnitt `MailAgentOptions`
Hier werden die allgemeinen Einstellungen zum Programm verwaltet.
Unter `EmailSettings` können beliebig viele E-Mail-Accounts mit den zugehörigen Connect-Standorten hinterlegt werden. 
* `Name` -> Der Name des Standorts (dient nur der besseren Zuordnung)
* `ApiKey` -> Der Schlüssel für die öffentliche Connect-Schnittstelle (Nur wenn benötigt bei direkter Auswertung nach Connect)
* `EMailHost` -> Der Hostname des Mailservers (z.B. `imap.strato.de` oder bei Exchange z.B. `exchange.meinedomain.de`  (Ohne HTTPS und auch ohne Pfad!)
* `EMailPort` -> Port des Mailservers (Bei IMAP standardmäßig 993, bei Exchange 443)
* `EMailUsername` -> Benutzername (Bei Exchange `xy@meinedomain.de`, nicht `meinedomain\xy`)
* `EMailPassword` -> Passwort (Nur bei `Basic` Authentifizierung erforderlich, bei `O365` nicht benötigt)
* `EMailSubjectFilter` -> Text, der im Betreff der Mail vorhanden sein muss, damit sie verarbeitet wird. Alles andere wird ignoriert.
* `EMailSenderFilter` -> Text, der im Absender der Mail vorhanden sein muss, damit sie verarbeitet wird. Alles andere wird ignoriert.
* `AuthenticationType` -> Authentifizierungsart für das Postfach. Gültige Werte sind:
  * `Basic` -> Klassische Authentifizierung mit Benutzername und Passwort (Standard)
  * `O365` -> Modern Authentication für Office 365 mit OAuth2. Beim ersten Start erfolgt eine interaktive Anmeldung über den Browser. Die Tokens werden sicher gespeichert und bei Bedarf automatisch erneuert.

Weitere Einstellungen:
* `EMailPollingIntervalSeconds` -> Abrufintervall der Mails in Sekunden (Standard: 5 Sekunden)
* `PGPGnuPGPath` -> Der Pfad zu GnuPG (nur benötigt, wenn PGP verwendet wird)
* `SecretKeyPassphrase` -> Die Passphrase des Private-Keys für PGP (nur benötigt, wenn PGP verwendet wird, kann auch bei PGP leer sein)
* `OutputPath` -> Der Ausgabepfad (nur benötigt, wenn entsprechender Prozessor verwendet wird)
* `ProcessMode` -> Die Einstellung, welcher Prozessor zum Verarbeiten der Mails verwendet werden soll. 
	* `Pdf` -> PDF-Prozessor (PGP)
	* `Text` -> Text-Prozessor (PGP)
	* `ConnectPlain` -> ConnectPlain-Prozessor (unverschlüsselt, Auswertung mit RegEx direkt nach Connect)
    * `ConnectEncrypted` -> ConnectEncrypted-Prozessor (PGP-Verschlüsselt, Auswertung mit RegEx direkt nach Connect)
    * `ConnectPlainHtml` -> ConnectPlainHtml-Prozessor (unverschlüsselt, HTML Mail, Auswertung mit RegEx direkt nach Connect)
    * `ConnectEncryptedHtml` -> ConnectEncryptedHtml-Prozessor (PGP-Verschlüsselt, HTML Mail, Auswertung mit RegEx direkt nach Connect)
    * `ConnectPgpAttachment` -> ConnectPgpAttachment-Prozessor (PGP-Anhänge, Auswertung mit RegEx direkt nach Connect)
* `EMailMode` -> Einstellung ob `Imap` oder `Exchange` verwendet werden soll.
* `IgnoreCertificateErrors` -> Auf `true`, wenn Zertifikatsfehler (z.B. für Exchange) ignoriert werden sollen.
* `HeartbeatInterval` -> Intervall für das Senden von Heartbeats. (z.B. an UptimeRobot)
* `HeartbeatUrl` -> HTTP-GET Endpunkt, der für Heartbeats aufgerufen werden soll
* `O365ClientId` -> Die Client-ID für die O365 OAuth2-Authentifizierung (nur erforderlich bei Verwendung von `O365` Authentifizierung). Der Standardwert ist eine öffentliche Client-ID.

## Copyright
Copyright Feuer Software GmbH, Karlsbader Str. 16, Eschborn

Internet: https://feuersoftware.com 
Mail: info@feuersoftware.com

Alle Rechte vorbehalten.