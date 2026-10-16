# MailAgent E2E-Langzeittest

Prüft die komplette Alarmkette im echten Betrieb über viele Stunden:

**Testalarm-Mail (Outlook, PGP via GpgOL) → O365-Postfach → MailAgent → Einsatz in Connect**

Jeder Testalarm trägt eine eigene Einsatznummer (`MA-E2E-yyyyMMdd-HHmmss`) und wird einzeln verfolgt:
gesendet → im Postfach angekommen → vom Agent verarbeitet → als gelesen markiert → Einsatz in Connect mit allen
Feldern. Ein unabhängiger **Postfach-Monitor** (eigene, nur lesende IMAP-Sitzung) zeigt dabei, ob eine Mail
wirklich im Postfach liegt. So lassen sich Zustellverzögerungen klar von Problemen des Agents trennen.

## Hintergrund
Der Test ist bei der Analyse des „Hängenbleibens“ des O365-Clients entstanden (Branch
`fix/o365-imap-poll-hang-no-timeout`).
- **Ursache:** Exchange Online liefert einer IMAP-Sitzung keine neuen Mails mehr, sobald sie einmal den Posteingang
  beschreibbar geöffnet hat (`SELECT`) und danach wieder nur lesend (`EXAMINE`). Der Agent war deshalb nach jeder
  verarbeiteten Mail blind, bis zum nächsten Reconnect. Die 15-min-Altersgrenze hat die Alarme dann still verworfen.
- **Behebung:** Der Agent öffnet den Posteingang nur noch beschreibbar. Zusätzlich behoben wurden: Timeouts bei
  langsamer Anmeldung, aktive Token-Erneuerung beim stündlichen Reconnect, Laden des gespeicherten Token-Caches
  beim Start.
- **Verifiziert:** zwei Läufe mit je 30 Alarmen über 7 h, 30/30 korrekt, nach allen Fixes 0 Agent-Fehler.

Der Langzeittest soll das über längere Zeit und unter wechselnden Bedingungen absichern.

## Voraussetzungen
- **Windows** mit **klassischem Outlook**. Das neue Outlook hat keine COM-Automatisierung. Das Absenderkonto muss
  in Outlook eingerichtet sein.
- **.NET SDK 9 oder neuer**
- **Gpg4win** mit aktivem Outlook-Add-In **GpgOL**. Öffentlicher und privater Schlüssel des überwachten Postfachs
  liegen in Kleopatra. Nur für `ProcessMode: ConnectEncrypted` nötig.
- **Connect-Test-Standort** mit aktivierter Public-API-Schnittstelle (Standort-API-Key). Jeder Testalarm legt dort
  einen echten Einsatz an und alarmiert die Mitglieder. **Nie einen produktiven Standort verwenden!**
- **Azure-App** (Client-ID) mit IMAP-Berechtigung für O365
- Das **überwachte O365-Postfach** und der **Absender** sollten im selben Tenant liegen (siehe Hinweise).

## Einrichten
1. `.\Run-E2ETest.ps1` einmal starten. Es legt `e2e.config.json` aus der Vorlage an und beendet sich.
2. `e2e.config.json` ausfüllen. Die Datei ist gitignored, weil sie Secrets enthält:

   | Feld | Bedeutung |
   |---|---|
   | `Sender` | Absenderadresse, muss ein Konto im lokalen Outlook sein. Am besten das überwachte Postfach selbst |
   | `MailCount` | Anzahl Testalarme |
   | `MinIntervalMinutes` / `MaxIntervalMinutes` | zufälliger Abstand zwischen zwei Alarmen |
   | `O365ClientId` | Client-ID der Azure-App. Wird beim Build eingebettet und erscheint nicht im Log |
   | `MailAgentOptions` | wie in der `appsettings.json` des Agents, mit **genau einem** Eintrag in `EmailSettings`. Empfänger der Testalarme ist `EMailUsername`, `ApiKey` ist der Standort-API-Key |
   | `MailAgentOptions.ConnectApiUrl` | Standard ist die **Produktions-API**. Für den Preview-Test-Standort: `https://connectapipreview.feuersoftware.com` |
   | `MailAgentOptions.ProcessMode` | `ConnectEncrypted` (PGP via GpgOL, Standard) oder `ConnectPlain` (unverschlüsselt) |

3. **Laufzeit wählen:** Die Dauer ist etwa `MailCount × (Min + Max) / 2`. Die Vorlage (30 Alarme, 10–20 min) läuft rund
   7½ h. Für einen Langzeittest z. B. 100 Alarme mit 10–20 min (≈ 25 h) oder 150 Alarme mit 15–25 min (≈ 50 h).
   Connect erlaubt höchstens 25 Einsätze pro Stunde, deshalb `MinIntervalMinutes` nicht unter 3 setzen.
4. **Posteingang aufräumen:** keine ungelesenen Mails, insbesondere keine alten `MailAgent-E2E`-Testmails.

## Starten
```powershell
.\Run-E2ETest.ps1
```
1. **Vorabprüfungen:** RegEx gegen das Testmailformat, PGP-Roundtrip, Connect-API und Key, Outlook-Konto und GpgOL,
   Diagnose-Werkzeuge
2. **Build** des Agents nach `out\bin\`, self-contained. Die Architektur folgt der installierten GnuPG-Version:
   Gpg4win 4.x → `win-x86`, Gpg4win 5.x → `win-x64`.
3. **Postfach-Monitor** starten. → **Browser-Anmeldung bei jedem Start.**
4. **Agent** in eigenem Fenster starten. → **Browser-Anmeldung nur beim allerersten Start**, danach kommt das Token
   aus dem gespeicherten Cache.
5. Sobald der Agent pollt, geht der erste Alarm raus. Danach folgen die übrigen im zufälligen Abstand.
6. Am Ende stehen eine Zusammenfassung und die Kennzahlen im Report, danach werden Agent und Monitor beendet.
   Exit-Code `0` heißt: alle Alarme korrekt.

Abbrechen geht jederzeit mit **Strg+C**, die Zusammenfassung kommt trotzdem.

### Während des Laufs
- **Erlaubt:** Windows sperren. Getestet: Versand und Verschlüsselung laufen weiter.
- **Nicht erlaubt:**
  - Abmelden (beendet alle Prozesse)
  - Standby/Energiesparmodus
  - Outlook, Test- oder Agent-Fenster schließen
- **Nicht** im überwachten Postfach Mails lesen, verschieben oder löschen, solange der Test läuft.
- Vorübergehende Outlook-Dialoge stören nicht: Ein fehlgeschlagener Versand wird jede Minute wiederholt, erst nach
  10 Fehlversuchen in Folge bricht der Test ab.

## Ergebnisse (`out\`, gitignored)
| Pfad | Inhalt |
|---|---|
| `out\runs\<Zeitstempel>\e2e-report.log` | alle Meldungen, Zusammenfassung und Kennzahlen des Laufs |
| `out\runs\<Zeitstempel>\agent-<Datum>.log` | vollständiges Agent-Log (Debug) |
| `out\runs\<Zeitstempel>\monitor\events.csv` | Ereignisse des Postfach-Monitors (`arrived`, `read`, `connect`, `error`) |
| `out\runs\<Zeitstempel>\build.log`, `monitor-build.log` | Build-Ausgaben, werden nur bei Fehlern angezeigt |
| `out\runs\<Zeitstempel>\stacks-*.txt`, `dump-*.dmp`, `crash-*.dmp` | Diagnose bei Hänger oder Absturz (siehe unten) |
| `out\bin\`, `out\monitor-bin\` | Kompilate. `out\bin\appsettings.json` wird erzeugt und enthält Secrets |
| `out\tools\` | `dotnet-dump`/`dotnet-stack`, falls nicht global installiert. Der Test lädt sie beim ersten Lauf von NuGet |

### Bewertung
Ein Lauf ist **erfolgreich**, wenn:
- alle Alarme `OK` sind (Exit-Code 0),
- keine Meldung `ERR Monitor: … liegt seit N s ungelesen` vorkommt,
- kein `CRIT` vorkommt.

Die Zusammenfassung zeigt je Alarm die Sekunden nach Versand bis: im Postfach, vom Agent verarbeitet, als gelesen
markiert, in Connect. Dazu kommen die Kennzahlen:
- **Zustellung (Versand → Postfach):** Ausreißer hier liegen bei Outlook/Exchange, nicht am Agent.
- **Agent (Postfach → verarbeitet):** Das ist die eigentliche Reaktionszeit des Agents. Sie liegt typisch bei wenigen
  Sekunden.

| Meldung | Bedeutung |
|---|---|
| `OK … in Connect, alle Felder korrekt` | Kette funktioniert für diesen Alarm |
| `ERR Monitor: … liegt seit N s ungelesen im Posteingang` | Die Mail ist da, der Agent sieht sie aber nicht. **Das ursprüngliche Fehlerbild** |
| `ERR … nicht in Connect` / `… vom Agent verworfen (> 15 min alt)` | Alarm verloren |
| `ERR … Felder falsch` | RegEx-/Mapping-Problem zwischen Mail, Agent und Connect |
| `ERR Failed to fetch mails …` + `Reconnecting` | Verbindungsproblem. Unkritisch, wenn danach `Polling läuft wieder` folgt |
| `WARN kein erfolgreicher Poll` | Verbindung gestört, der Agent arbeitet aber noch |
| `WARN Fremde Mail im Posteingang` | Eine nicht zum Test gehörende ungelesene Mail liegt im Postfach (siehe Hinweise) |
| `CRIT` | Hänger (> 4 min keine Logzeile), Subscription beendet, Agent-Prozess weg, Token-Refresh gescheitert oder Testabbruch |

### Diagnose bei Fehlern
- **Hänger** (> 4 min keine Logzeile im Agent) oder **beendete Subscription:** Der Test sichert die Thread-Stacks
  (`dotnet-stack`, zeigt direkt, wo der Agent hängt) und ein Speicherabbild mit Heap (`dotnet-dump`). Auswerten mit
  `dotnet-dump analyze <datei>` (z. B. `clrstack -all`, `dumpasync`) oder in Visual Studio.
- **Absturz:** Die .NET-Runtime schreibt selbst einen Crash-Dump `crash-<PID>.dmp` in den Lauf-Ordner.

## Testmailformat
```
EINSATZNUMMER: MA-E2E-20261009-153012       -> Number
ALARMZEIT: 09.10.2026 15:30:12               -> Start
STICHWORT / SACHVERHALT                      -> Keyword / Facts
STRASSE / HAUSNUMMER / PLZ / ORT / ORTSTEIL  -> Address
MELDER / RUFNUMMER                           -> Reporter
RIC (mehrfach)                               -> Ric ("a; b")
BREITE / LAENGE                              -> Position
BEMERKUNG                                    -> Property "Bemerkung"
```
Die passenden `ConnectPatternOptions` erzeugt der Test selbst. Der Agent verarbeitet nur Mails mit Betreff
`MailAgent-E2E`.

## Hinweise
- **Ungelesene fremde Mails** im Posteingang lädt der Agent bei jedem Poll erneut. Nach 15 min markiert er sie als
  gelesen, auch wenn sie nicht zum Betreff-Filter passen (eigener Fix in Arbeit). Deshalb ein eigenes Testpostfach
  oder einen aufgeräumten Posteingang verwenden.
- **Externer Absender + PGP:** Setzt der Tenant externen Mails einen Hinweis voran (z. B. „CAUTION: … outside“),
  verpackt Exchange verschlüsselte Mails als Anhang in eine neue Mail. Dann erkennen weder GpgOL noch der Agent die
  PGP-Mail. Darum als `Sender` eine interne Adresse verwenden.
- GpgOL wird **pro Mail** gesteuert: verschlüsseln bei `ConnectEncrypted`, nicht verschlüsseln bei `ConnectPlain`.
  Die GpgOL-Einstellungen bleiben unverändert. Greift das nicht, bricht der Test bei der ersten Mail mit Hinweis ab.
- Der Monitor verbindet sich alle 10 min neu und meldet sich bei jedem Teststart einmal im Browser an. Er nutzt
  nicht den Token-Cache des Agents.

## Probleme
| Problem | Lösung |
|---|---|
| `e2e.config.json enthält noch den Platzhalter …` | Konfiguration vollständig ausfüllen |
| `Kein Outlook-Konto für Sender` | `Sender` muss exakt einer Kontoadresse im klassischen Outlook entsprechen |
| `Connect-API nicht nutzbar` (401/403) | Standort-API-Key, aktivierte Public-API-Schnittstelle und `ConnectApiUrl` prüfen |
| `Postfach-Monitor hat sich nicht … angemeldet` | Browser-Anmeldung für den Monitor durchführen |
| `Agent pollt nach 10 min noch nicht` | Im Agent-Fenster nach Login-Aufforderung oder Konfigurationsfehlern schauen |
| Testabbruch wegen GpgOL | GpgOL-Option „Neue Nachrichten standardmäßig verschlüsseln“ passend zum `ProcessMode` von Hand setzen |
| `Versand … fehlgeschlagen` (wiederholt) | In Outlook nach offenen Dialogen oder Anmeldeaufforderungen schauen |
| `Silent token acquisition failed` | Test neu starten und im Agent-Fenster erneut anmelden |
