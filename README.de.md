# Insait Edit — C# IDE

<p align="center">
  <img src="Insait Edit C Sharp/Icons/AppIconIDE.png" alt="Insait Edit Logo" width="128" height="128"/>
</p>

<p align="center">
  <b>Eine vollständige Desktop-IDE zum Erstellen, Bearbeiten, Bauen, Veröffentlichen und Paketieren von .NET-Desktopanwendungen.</b>
</p>

<p align="center">
  <a href="README.md">🇬🇧 English documentation</a>
</p>

---

## Überblick

**Insait Edit** ist eine Windows-Desktop-IDE mit Fokus auf C#- und .NET-Desktopentwicklung. Die Anwendung kombiniert einen eigenen Editor, Roslyn-basierte Analyse, Werkzeuge zum Erstellen von Projekten, Build- und Publish-Workflows, Git-Integration, ein integriertes Terminal und MSIX-Paketierung in einer Oberfläche.

Die aktuelle Codebasis konzentriert sich auf Desktop-Anwendungen. Enthalten sind integrierte Projektvorlagen für **Avalonia**, **Windows Forms**, Standardvorlagen für **Konsole/Klassenbibliothek** sowie **F#-Startvorlagen**. Die Build- und Run-Pipeline erkennt außerdem **WPF**, **WinForms** und **Avalonia** als GUI-Projekte.

---

## Funktionsübersicht

### Vollständiger Desktop-Workflow
- Neue Lösungen und Projekte direkt in der IDE erstellen
- Projekte und Elemente zu bestehenden Lösungen hinzufügen
- Mit C#, F#, AXAML, XAML, Bildern und Projektressourcen arbeiten
- Desktopanwendungen bauen, neu bauen, starten, stoppen, veröffentlichen und als Paket erstellen

### Unterstützte Projekt-Workflows
- **Avalonia-Anwendungen** mit AXAML-Bearbeitung und Vorschau
- **Windows-Forms-Anwendungen** über Projektvorlagen und GUI-Erkennung
- **WPF-Projekte** in der Build-/Run-Pipeline
- **Konsolenanwendungen**, **Klassenbibliotheken**, **leere C#-Projekte** und **F#-Startprojekte**

### Editor und Code-Intelligenz
- Eigenentwickelter `Insait Editor` mit Syntaxhervorhebung und eigener Rendering-Oberfläche
- Roslyn-basierte C#-Vervollständigung, Quick Info, Symbolsuche und Refactoring-Helfer
- F#-Vervollständigung und Tooltip-Unterstützung über `FSharp.Compiler.Service`
- AXAML/XAML-Vervollständigung für Avalonia-Elemente, Eigenschaften, Events, Markup Extensions und Namespaces
- Inline-Diagnosen, Quick-Fix-Vorschläge und Auto-Fix-Workflows direkt im Editor

### Build, Run und Publish
- Integration von MSBuild / `dotnet` für Build-Vorgänge
- Run-Konfigurationen und Compound-Run-Unterstützung
- GUI-bewusste Startlogik für Desktopprojekte
- Publish-Fenster mit visueller Fortschrittsanzeige

### Paketverwaltung und Deployment
- NuGet-Panel zum Suchen, Installieren, Aktualisieren und Entfernen von Paketen
- MSIX-Manager für Paketierung, Metadatenbearbeitung, Icon-Austausch und Signierung

### Quellcodeverwaltung und GitHub-Werkzeuge
- Git-Panel für Repository-Status, gestagte/nicht gestagte Änderungen, Branch-Infos, Verlauf, Commit, Push und Pull
- Workflows zum Klonen von Repositories
- GitHub-Integration über Octokit-basierte Dienste
- Integriertes **GitHub Copilot CLI**-Panel und Befehlsworkflow

### Terminal und Workspace-Werkzeuge
- Eingebautes Terminal mit Prozesssteuerung und Befehlsverlauf
- Datei-Explorer, Suche in Dateien, Bildbetrachter, Benachrichtigungen, Encoding-Werkzeuge und Eigenschaftsfenster
- AXAML-Vorschaufenster und Live-Host-ähnliche Vorschauwerkzeuge

### Lokalisierung
- Eingebaute UI-Wörterbücher für **Englisch, Ukrainisch, Deutsch, Russisch und Türkisch**
- Benutzerdefinierte AXAML-Sprachdateien, die zur Laufzeit geladen werden können
- Import externer `.axaml`-Übersetzungsdateien über das Sprachmenü
- Übersetzungsordner für manuelle Bearbeitung oder Übersetzungen mit Unterstützung durch GitHub Copilot CLI

---

## Roslyn-Analyse- und Vervollständigungssystem

Das Coding-Erlebnis basiert im Kern auf **Microsoft Roslyn** und mehreren spezialisierten Diensten im Projekt.

### Implementierte Bausteine
- `RoslynAutoCompleteFactory` delegiert die C#-Vervollständigung direkt an Roslyn `CompletionService`
- Signaturhilfe wird aus semantischen Roslyn-Daten aufgelöst
- Hover-Informationen verwenden Roslyn `QuickInfoService`
- `InlineDiagnosticService` führt entprellte Hintergrundsanalyse aus und aktualisiert Inline-Markierungen
- Quick-Fix-Vorschläge werden an Diagnosen angehängt und im Editor angezeigt
- `RoslynAutoFixService` entdeckt eingebaute Roslyn-`CodeFixProvider`- und `CodeRefactoringProvider`-Implementierungen
- `CSharpCompletionService` unterstützt Symbolumbenennung und Dokument-Highlights

### Ergebnis in der Praxis
- intelligente Vervollständigungslisten
- Parameter- und Überladungshilfe
- Hover-Beschreibungen
- Echtzeit-Fehler- und Warnmeldungen
- Quick Fix mit einem Schritt
- Gehe zu Definition
- Symbol umbenennen

Für AXAML-Dateien verwendet die IDE zusätzlich eine eigene Vervollständigungs-Engine, die reale Avalonia-Assemblies reflektiert und gültige Controls, Eigenschaften, Attached Properties, Events und Markup Extensions vorschlägt.

---

## Hinweise zur MSIX-Paketierung

Das MSIX-Subsystem unterstützt sowohl die **vollständige Paketierung** als auch die **Paketierung aus einem bereits veröffentlichten Output**.

### Was die MSIX-Werkzeuge können
- ein Projekt veröffentlichen und in einem Durchlauf als MSIX paketieren
- `AppxManifest.xml` erzeugen
- Inhalte mit `MakeAppx.exe` packen
- ein vorhandenes MSIX öffnen und Paketmetadaten lesen
- Paketmetadaten bearbeiten und das Paket neu packen
- Icons innerhalb eines vorhandenen MSIX ersetzen
- ein MSIX mit `SignTool.exe` und einem Zertifikat aus `CurrentUser\My` signieren

### Wichtige Anforderungen
- für die MSIX-Paketierung wird das **Windows SDK** benötigt, da `MakeAppx.exe` und `SignTool.exe` verwendet werden
- der **Publisher** des Pakets muss exakt mit dem Zertifikatssubjekt übereinstimmen
- praktisch bedeutet das: der **`CN=...`-Name des Herstellers muss identisch mit dem Distinguished Name des Zertifikats** sein
- Paketicons für MSIX müssen **PNG-basiert** sein
- wenn kein gültiges Logo angegeben wird, kann der Dienst ein Platzhalterbild einsetzen

### Empfohlene Hinweise für die Nutzung
- setzen Sie Ihr eigenes Paketicon explizit; der Standardwert ist nur ein Platzhalter/Mock-Wert
- prüfen Sie vor dem Signieren Paketidentität, Publisher, Version, Executable und EntryPoint
- wenn das Signieren wegen eines Publisher-Mismatches fehlschlägt, muss der Manifest-Publisher auf das Zertifikatssubjekt angepasst werden

---

## Lokalisierung und benutzerdefinierte Übersetzungen

Benutzerdefinierte Übersetzungen werden über einfache AXAML-Wörterbücher verwaltet.

### Eingebautes Verhalten
- Standardsprachen werden aus `Insait Edit C Sharp/Interface Localization/` geladen
- Benutzerdefinierte Wörterbücher werden in `%AppData%\InsaitEdit\GitHubTranslations\` gespeichert
- der Dienst stellt sicher, dass `English.axaml` als Basistemplate verfügbar ist

### Regeln für benutzerdefinierte Übersetzungen
- eine benutzerdefinierte Sprachdatei muss ein einfaches `.axaml`-Wörterbuch sein
- sie sollte die **gleiche `x:String`-Schlüsselstruktur** wie das englische Wörterbuch besitzen
- Werte dürfen übersetzt werden, Schlüssel und Struktur sollten jedoch identisch zu `English.axaml` bleiben

### Nutzung im Programm
- externe AXAML-Datei über das Sprachmenü importieren
- den Übersetzungsordner öffnen und Dateien manuell bearbeiten
- **GitHub Copilot CLI** in diesem Ordner starten, um bei der Übersetzung zu helfen
- das benutzerdefinierte Wörterbuch direkt zur Laufzeit über das Sprachmenü laden

---

## Tastenkombinationen

Die folgende Liste basiert auf den aktuell im Code bestätigten Shortcuts.

### Hauptfenster
| Tastenkombination | Aktion |
|---|---|
| `Strg+S` | Aktuelle Datei speichern |
| `Strg+Umschalt+S` | Alle Dateien speichern |
| `Strg+O` | Datei öffnen |
| `Strg+N` | Neue Datei erstellen |
| `Strg+Umschalt+N` | Neues Projekt erstellen |
| `Strg+B` | Projekt bauen |
| `Strg+Umschalt+B` | Projekt neu bauen |
| `Strg+Umschalt+A` | Projekt analysieren |
| `F5` | Projekt starten |
| `Umschalt+F5` | Laufendes Projekt stoppen |
| `Strg+W` | Aktuellen Tab schließen |
| `Strg+Umschalt+F` | In Dateien suchen |
| `Strg+P` | Datei nach Namen suchen |
| `Strg+Umschalt+Z` | Zen-Modus umschalten |
| `Strg+Umschalt+P` | AXAML-Vorschau öffnen |
| `Strg+Umschalt+E` | Explorer umschalten |
| `Strg+Umschalt+I` | KI-/rechte Seitenleiste umschalten |
| `Strg+\`` | Unteres Panel / Terminalbereich umschalten |
| `Esc` | Zen-Modus verlassen |

### Editor
| Tastenkombination | Aktion |
|---|---|
| `Alt+Enter` | Quick Fix an der Cursorposition anzeigen |
| `Strg+.` | Quick Fix an der Cursorposition anzeigen |
| `Strg+Umschalt+I` | Dokument formatieren |
| `Strg+Umschalt+A` | Auto-Fix-Fenster öffnen |
| `F12` | Gehe zu Definition |
| `F2` | Symbol umbenennen |
| `Strg+R` | Symbol umbenennen |
| `Strg+Umschalt+H` | Hover-Information anzeigen |
| `Tab` / `Enter` | Ausgewählten Completion-Eintrag übernehmen |
| `Esc` | Completion- oder Quick-Fix-Popup schließen |

### Terminal-Panel
| Tastenkombination | Aktion |
|---|---|
| `Strg+C` | Aktuellen Terminalprozess stoppen |
| `Strg+L` | Terminalausgabe leeren |
| `Pfeil hoch / runter` | Terminalverlauf durchsuchen |

---

## Technologieübersicht

| Komponente | Technologie |
|---|---|
| Zielframework | `.NET 10.0` (`net10.0-windows`) |
| UI-Framework | `Avalonia 12.0.0` |
| Codeanalyse | Roslyn 5.x |
| F#-Unterstützung | `FSharp.Compiler.Service` 43.x |
| Build-Layer | `Microsoft.Build` 18.4 |
| NuGet-Integration | `NuGet.Protocol` 7.3 |
| GitHub-Integration | `Octokit` 14.0 |
| Lokaler Speicher | `LiteDB` 6 Prerelease |

---

## Screenshots

<p align="center">
  <img src="Insait%20Edit%20C%20Sharp/Screenshots/English1.png" alt="Insait Edit Screenshot 1" width="100%"/>
</p>
<p align="center">
  <img src="Insait%20Edit%20C%20Sharp/Screenshots/English2.png" alt="Insait Edit Screenshot 2" width="100%"/>
</p>
<p align="center">
  <img src="Insait%20Edit%20C%20Sharp/Screenshots/English3.png" alt="Insait Edit Screenshot 3" width="100%"/>
</p>
<p align="center">
  <img src="Insait%20Edit%20C%20Sharp/Screenshots/English4.png" alt="Insait Edit Screenshot 4" width="100%"/>
</p>

---

## Lizenz

Dieses Projekt steht unter der **MIT-Lizenz** — Details finden Sie in der Datei [LICENSE](LICENSE).

> **Hinweis:** Die UI-Stile, Icons und visuellen Assets der Anwendung sind von der MIT-Lizenz ausgenommen und bleiben All Rights Reserved. Die vollständige Ausschlussliste steht in [LICENSE](LICENSE).
