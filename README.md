<p align="center">
  <img src="Insait Edit C Sharp/Icons/AppIconIDE.png" alt="Insait Edit Logo" width="128" height="128"/>
</p>

<h1 align="center">Insait Edit — C# IDE</h1>

<p align="center">
  <b>A full-cycle desktop IDE for creating, editing, building, publishing, and packaging .NET desktop applications.</b>
</p>

<p align="center">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white"/>
  <img alt="Avalonia UI" src="https://img.shields.io/badge/Avalonia_UI-12.0-8B44AC?logo=avalonia&logoColor=white"/>
  <img alt="Roslyn" src="https://img.shields.io/badge/Roslyn-5.0-blue"/>
  <img alt="Platform" src="https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows&logoColor=white"/>
  <img alt="License" src="https://img.shields.io/badge/License-MIT%20(with%20exclusions)-yellow"/>
</p>

<p align="center">
  <a href="#english">🇬🇧 English</a> · <a href="#deutsch">🇩🇪 Deutsch</a> · <a href="#turkce">🇹🇷 Türkçe</a> · <a href="#russian">🇷🇺 Русский</a> · <a href="#ukrainian">🇺🇦 Українська</a>
</p>

---

<a id="english"></a>

## English

## ⚠️ System Requirements
For the IDE to function correctly, ensure your environment meets these criteria:
* **Git for Windows:** Must be installed and added to your system `PATH`. It is required for source control, repository cloning, and GitHub Copilot CLI features.
* **System Drive:** The Windows installation and application workspace must reside on the **C:** drive for internal path resolution.
* **Windows SDK:** Required for MSIX packaging, metadata signing, and application deployment.
  
## Overview

**Insait Edit** is a Windows desktop IDE focused on C# and .NET desktop development. The application combines a custom editor, Roslyn-based analysis, project creation tools, build and publish workflows, Git integration, a built-in terminal, and MSIX packaging tools in one interface.

The current codebase is centered on desktop application workflows. It includes built-in project creation for **Avalonia**, **Windows Forms**, standard **console/class library** templates, and **F#** starter templates. The build and run pipeline also recognizes **WPF**, **WinForms**, and **Avalonia** GUI projects.

---

## Feature Highlights

### Full-cycle desktop workflow
- Create new solutions and projects from the IDE
- Add projects and items to an existing solution
- Work with C#, F#, AXAML, XAML, images, and project assets
- Build, rebuild, run, stop, publish, and package desktop applications

### Supported project workflows
- **Avalonia applications** with AXAML editing and preview support
- **Windows Forms applications** via project templates and GUI project detection
- **WPF projects** in the build/run pipeline
- **Console apps**, **class libraries**, **empty C# projects**, and **F# starter projects**

### Editor and code intelligence
- Custom `Insait Editor` with syntax highlighting and editor surface rendering
- Roslyn-backed C# completion, quick info, symbol lookup, and refactoring helpers
- F# completion and tooltip support through `FSharp.Compiler.Service`
- AXAML/XAML completion for Avalonia elements, properties, events, markup extensions, and namespaces
- Inline diagnostics, quick-fix suggestions, and auto-fix workflows inside the editor

### Build, run, and publish
- MSBuild / `dotnet` integration for project build operations
- Run configurations and compound run support
- GUI-aware run pipeline for desktop app projects
- Publish window with visual progress reporting

### Package management and deployment
- NuGet panel for browsing, installing, updating, and removing packages
- MSIX manager for packaging, metadata editing, icon replacement, and signing

### Source control and GitHub tooling
- Git panel for repository status, staged/unstaged changes, branch info, history, commit, push, and pull operations
- Repository cloning workflows
- GitHub integration through Octokit-based services
- Integrated **GitHub Copilot CLI** panel and command workflow

### Terminal and workspace utilities
- Built-in terminal panel with process control and command history
- File explorer, search in files, image viewer, notifications, encoding tools, and project property windows
- AXAML preview windows and live-host style preview tooling

### Localization
- Built-in UI dictionaries for **English, Ukrainian, German, Russian, and Turkish**
- Custom AXAML-based language dictionaries loaded at runtime
- Import of external `.axaml` translation files from the language menu
- Translation workspace folder for manual editing or GitHub Copilot CLI-assisted translation

---

## Roslyn Analysis and Completion System

The core coding experience is based on **Microsoft Roslyn** and several dedicated services in the project.

### What is implemented
- `RoslynAutoCompleteFactory` delegates C# completion directly to Roslyn `CompletionService`
- Signature help is resolved from Roslyn semantic data
- Hover information uses Roslyn `QuickInfoService`
- `InlineDiagnosticService` runs debounced background analysis and updates inline squiggles
- Quick-fix suggestions are attached to diagnostics and shown in the editor
- `RoslynAutoFixService` discovers built-in Roslyn `CodeFixProvider` and `CodeRefactoringProvider` implementations
- `CSharpCompletionService` supports rename operations and document highlights

### Practical result in the IDE
- smart completion lists
- parameter and overload help
- hover descriptions
- real-time error/warning reporting
- one-step quick fixes
- go to definition
- rename symbol

For AXAML files, the IDE uses a separate completion engine that reflects real Avalonia assemblies and suggests valid controls, properties, attached properties, events, and markup extensions.

---

## MSIX Packaging Notes

The MSIX subsystem supports both **full packaging** and **packaging from an already published output**.

### What the MSIX tools can do
- publish a project and pack it into MSIX in one flow
- generate `AppxManifest.xml`
- pack content with `MakeAppx.exe`
- open an existing MSIX and read package metadata
- edit package metadata and repack the package
- replace icons inside an existing MSIX
- sign an MSIX with `SignTool.exe` and a certificate from `CurrentUser\My`

### Important requirements
- **Windows SDK is required** for MSIX packaging because `MakeAppx.exe` and `SignTool.exe` are used
- the **package Publisher must match the certificate subject exactly**
- in practice this means the **`CN=...` publisher name must be identical to the certificate subject distinguished name**
- package icons for MSIX must be **PNG-based**
- if no valid logo is supplied, the service can fall back to a placeholder image

### Recommended usage notes
- set your own package icon explicitly; the default fallback is only a placeholder/mock value
- verify the package identity, publisher, version, executable, and entry point before signing
- if signing fails because of a publisher mismatch, align the manifest publisher with the certificate subject

---

## Localization and Custom Translation Workflow

Custom translations are handled through plain AXAML dictionaries.

### Built-in behavior
- Standard interface languages are loaded from `Insait Edit C Sharp/Interface Localization/`
- Custom dictionaries are stored in `%AppData%\InsaitEdit\GitHubTranslations\`
- The service ensures that a copy of `English.axaml` is available as the base template

### Custom translation rules
- a custom language file must be a plain `.axaml` dictionary
- it should keep the **same `x:String` key structure** as the English dictionary
- values can be translated, but keys and structure should remain identical to `English.axaml`

### How custom translations are used in the app
- import an external AXAML file from the language menu
- open the translations folder and edit the files manually
- launch **GitHub Copilot CLI** in that folder to assist with translation work
- load the custom dictionary directly from the language menu at runtime

---

## Keyboard Shortcuts

Below is the shortcut list confirmed by the current code.

### Main window
| Shortcut | Action |
|---|---|
| `Ctrl+S` | Save current file |
| `Ctrl+Shift+S` | Save all files |
| `Ctrl+O` | Open file |
| `Ctrl+N` | Create a new file |
| `Ctrl+Shift+N` | Create a new project |
| `Ctrl+B` | Build project |
| `Ctrl+Shift+B` | Rebuild project |
| `Ctrl+Shift+A` | Analyze project |
| `F5` | Run project |
| `Shift+F5` | Stop running project |
| `Ctrl+W` | Close current tab |
| `Ctrl+Shift+F` | Find in files |
| `Ctrl+P` | Find file by name |
| `Ctrl+Shift+Z` | Toggle Zen Mode |
| `Ctrl+Shift+P` | Open AXAML preview |
| `Ctrl+Shift+E` | Toggle Explorer panel |
| `Ctrl+Shift+I` | Toggle AI / right panel |
| `Ctrl+\`` | Toggle bottom panel / terminal area |
| `Esc` | Exit Zen Mode |

### Editor
| Shortcut | Action |
|---|---|
| `Alt+Enter` | Show quick fix at cursor |
| `Ctrl+.` | Show quick fix at cursor |
| `Ctrl+Shift+I` | Format document |
| `Ctrl+Shift+A` | Open Auto Fix window |
| `F12` | Go to definition |
| `F2` | Rename symbol |
| `Ctrl+R` | Rename symbol |
| `Ctrl+Shift+H` | Show hover info |
| `Tab` / `Enter` | Commit selected completion item |
| `Esc` | Close completion or quick-fix popup |

### Terminal panel
| Shortcut | Action |
|---|---|
| `Ctrl+C` | Stop the current terminal process |
| `Ctrl+L` | Clear terminal output |
| `Up / Down` | Navigate terminal history |

---

## Technology Summary

| Component | Technology |
|---|---|
| Target framework | `.NET 10.0` (`net10.0-windows`) |
| UI framework | `Avalonia 12.0.0` |
| Code analysis | Roslyn 5.x |
| F# support | `FSharp.Compiler.Service` 43.x |
| Build layer | `Microsoft.Build` 18.4 |
| NuGet integration | `NuGet.Protocol` 7.3 |
| GitHub integration | `Octokit` 14.0 |
| Local storage | `LiteDB` 6 prerelease |

---

## License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

> **Note:** the application's UI styles, icons, and visual assets are excluded from the MIT License and remain All Rights Reserved. See [LICENSE](LICENSE) for the full exclusion list.

---

<a id="deutsch"></a>

## Deutsch

## ⚠️ Systemanforderungen
Damit die IDE ordnungsgemäß funktioniert, stellen Sie sicher, dass Ihre Umgebung die folgenden Kriterien erfüllt:
* **Git für Windows:** Muss installiert und zum System-`PATH` hinzugefügt sein. Erforderlich für die Quellcodeverwaltung, das Klonen von Repositories und GitHub Copilot CLI-Funktionen.
* **Systemlaufwerk:** Die Windows-Installation und der Anwendungs-Workspace müssen sich auf dem Laufwerk **C:** befinden, um die interne Pfadauflösung zu gewährleisten.
* **Windows SDK:** Erforderlich für MSIX-Paketierung, Signierung von Metadaten und Deployment.
**Insait Edit** ist eine Windows-Desktop-IDE mit Fokus auf C#- und .NET-Desktopentwicklung. Die Anwendung kombiniert einen eigenen Editor, Roslyn-basierte Analyse, Werkzeuge zum Erstellen von Projekten, Build- und Publish-Workflows, Git-Integration, ein integriertes Terminal und MSIX-Paketierung in einer Oberfläche.

## Überblick

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

## Lizenz

Dieses Projekt steht unter der **MIT-Lizenz** — Details finden Sie in der Datei [LICENSE](LICENSE).

> **Hinweis:** Die UI-Stile, Icons und visuellen Assets der Anwendung sind von der MIT-Lizenz ausgenommen und bleiben All Rights Reserved. Die vollständige Ausschlussliste steht in [LICENSE](LICENSE).

---

<a id="turkce"></a>

## Türkçe

## ⚠️ Sistem Gereksinimleri
IDE'nin doğru çalışabilmesi için ortamınızın aşağıdaki koşulları karşıladığından emin olun:
* **Windows için Git:** Kurulu ve sistem `PATH`'ine eklenmiş olmalıdır. Kaynak denetimi, depo klonlama ve GitHub Copilot CLI özellikleri için gereklidir.
* **Sistem Sürücüsü:** Windows kurulumu ve uygulama çalışma alanı, dahili yol çözümlemesi için **C:** sürücüsünde bulunmalıdır.
* **Windows SDK:** MSIX paketleme, meta veri imzalama ve uygulama dağıtımı için gereklidir.

## Genel Bakış

**Insait Edit**, C# ve .NET masaüstü geliştirmeye odaklanan bir Windows masaüstü IDE'sidir. Uygulama; özel bir editör, Roslyn tabanlı analiz, proje oluşturma araçları, derleme ve yayınlama iş akışları, Git entegrasyonu, yerleşik terminal ve MSIX paketleme araçlarını tek bir arayüzde birleştirir.

Mevcut kod tabanı masaüstü uygulama iş akışlarına odaklanmaktadır. **Avalonia**, **Windows Forms**, standart **konsol/sınıf kütüphanesi** şablonları ve **F#** başlangıç şablonları için yerleşik proje oluşturma özellikleri içerir. Derleme ve çalıştırma hattı aynı zamanda **WPF**, **WinForms** ve **Avalonia** GUI projelerini tanır.

---

## Özellik Öne Çıkanları

### Tam döngülü masaüstü iş akışı
- IDE içinden yeni çözümler ve projeler oluşturma
- Mevcut bir çözüme proje ve öğe ekleme
- C#, F#, AXAML, XAML, görseller ve proje varlıklarıyla çalışma
- Masaüstü uygulamalarını derleme, yeniden derleme, çalıştırma, durdurma, yayınlama ve paketleme

### Desteklenen proje iş akışları
- AXAML düzenleme ve önizleme desteğiyle **Avalonia uygulamaları**
- Proje şablonları ve GUI proje algılama ile **Windows Forms uygulamaları**
- Derleme/çalıştırma hattında **WPF projeleri**
- **Konsol uygulamaları**, **sınıf kütüphaneleri**, **boş C# projeleri** ve **F# başlangıç projeleri**

### Editör ve kod zekası
- Söz dizimi vurgulama ve editör yüzeyi oluşturma ile özel `Insait Editor`
- Roslyn destekli C# tamamlama, hızlı bilgi, sembol arama ve yeniden düzenleme yardımcıları
- `FSharp.Compiler.Service` aracılığıyla F# tamamlama ve araç ipucu desteği
- Avalonia öğeleri, özellikleri, olayları, işaretleme uzantıları ve ad alanları için AXAML/XAML tamamlama
- Satır içi tanılamalar, hızlı düzeltme önerileri ve editör içinde otomatik düzeltme iş akışları

### Derleme, çalıştırma ve yayınlama
- Proje derleme işlemleri için MSBuild / `dotnet` entegrasyonu
- Çalıştırma yapılandırmaları ve bileşik çalıştırma desteği
- Masaüstü uygulama projeleri için GUI farkında çalıştırma hattı
- Görsel ilerleme raporlamasıyla yayınlama penceresi

### Paket yönetimi ve dağıtım
- Paketlere göz atma, yükleme, güncelleme ve kaldırma için NuGet paneli
- Paketleme, meta veri düzenleme, simge değiştirme ve imzalama için MSIX yöneticisi

### Kaynak denetimi ve GitHub araçları
- Depo durumu, aşamalı/aşamasız değişiklikler, dal bilgisi, geçmiş, commit, push ve pull işlemleri için Git paneli
- Depo klonlama iş akışları
- Octokit tabanlı hizmetler aracılığıyla GitHub entegrasyonu
- Entegre **GitHub Copilot CLI** paneli ve komut iş akışı

### Terminal ve çalışma alanı yardımcı programları
- İşlem denetimi ve komut geçmişiyle yerleşik terminal paneli
- Dosya gezgini, dosyalarda arama, görüntü görüntüleyici, bildirimler, kodlama araçları ve proje özellik pencereleri
- AXAML önizleme pencereleri ve canlı ana bilgisayar tarzı önizleme araçları

### Yerelleştirme
- **İngilizce, Ukraynaca, Almanca, Rusça ve Türkçe** için yerleşik UI sözlükleri
- Çalışma zamanında yüklenen özel AXAML tabanlı dil sözlükleri
- Dil menüsünden harici `.axaml` çeviri dosyalarının içe aktarılması
- Manuel düzenleme veya GitHub Copilot CLI destekli çeviri için çeviri çalışma alanı klasörü

---

## Roslyn Analiz ve Tamamlama Sistemi

Temel kodlama deneyimi **Microsoft Roslyn** ve projedeki birkaç özel hizmete dayanmaktadır.

### Uygulananlar
- `RoslynAutoCompleteFactory`, C# tamamlamayı doğrudan Roslyn `CompletionService`'e devreder
- İmza yardımı Roslyn anlamsal verilerinden çözümlenir
- Üzerine gelme bilgileri Roslyn `QuickInfoService` kullanır
- `InlineDiagnosticService`, geri tepme önlemi uygulanmış arka plan analizi çalıştırır ve satır içi dalgalı çizgileri günceller
- Hızlı düzeltme önerileri tanılamalara eklenir ve editörde gösterilir
- `RoslynAutoFixService`, yerleşik Roslyn `CodeFixProvider` ve `CodeRefactoringProvider` uygulamalarını keşfeder
- `CSharpCompletionService`, yeniden adlandırma işlemlerini ve belge vurgularını destekler

### IDE'deki pratik sonuç
- akıllı tamamlama listeleri
- parametre ve aşırı yükleme yardımı
- üzerine gelme açıklamaları
- gerçek zamanlı hata/uyarı bildirimi
- tek adımlı hızlı düzeltmeler
- tanıma git
- sembolü yeniden adlandır

AXAML dosyaları için IDE, gerçek Avalonia derlemelerini yansıtan ve geçerli denetimleri, özellikleri, ekli özellikleri, olayları ve işaretleme uzantılarını öneren ayrı bir tamamlama motoru kullanır.

---

## MSIX Paketleme Notları

MSIX alt sistemi hem **tam paketlemeyi** hem de **zaten yayınlanmış bir çıktıdan paketlemeyi** destekler.

### MSIX araçlarının yapabilecekleri
- bir projeyi yayınlama ve tek akışta MSIX olarak paketleme
- `AppxManifest.xml` oluşturma
- `MakeAppx.exe` ile içeriği paketleme
- mevcut bir MSIX'i açma ve paket meta verilerini okuma
- paket meta verilerini düzenleme ve paketi yeniden paketleme
- mevcut bir MSIX içindeki simgeleri değiştirme
- `CurrentUser\My` deposundan bir sertifikayla `SignTool.exe` kullanarak MSIX imzalama

### Önemli gereksinimler
- `MakeAppx.exe` ve `SignTool.exe` kullanıldığından MSIX paketleme için **Windows SDK gereklidir**
- **paketin Yayıncısı, sertifika konusuyla tam olarak eşleşmelidir**
- pratikte bu **`CN=...` yayıncı adının sertifika konu ayırt edici adıyla aynı olması gerektiği** anlamına gelir
- MSIX için paket simgeleri **PNG tabanlı** olmalıdır
- geçerli bir logo sağlanmazsa hizmet bir yer tutucu görüntüye geri dönebilir

### Önerilen kullanım notları
- kendi paket simgenizi açıkça ayarlayın; varsayılan geri dönüş yalnızca bir yer tutucu/sahte değerdir
- imzalamadan önce paket kimliğini, yayıncıyı, sürümü, yürütülebilir dosyayı ve giriş noktasını doğrulayın
- yayıncı uyuşmazlığı nedeniyle imzalama başarısız olursa, manifest yayıncısını sertifika konusuyla hizalayın

---

## Yerelleştirme ve Özel Çeviri İş Akışı

Özel çeviriler düz AXAML sözlükleri aracılığıyla yönetilir.

### Yerleşik davranış
- Standart arayüz dilleri `Insait Edit C Sharp/Interface Localization/` konumundan yüklenir
- Özel sözlükler `%AppData%\InsaitEdit\GitHubTranslations\` konumunda depolanır
- Hizmet, `English.axaml` dosyasının temel şablon olarak kullanılabilir olmasını sağlar

### Özel çeviri kuralları
- özel bir dil dosyası düz bir `.axaml` sözlüğü olmalıdır
- İngilizce sözlükle **aynı `x:String` anahtar yapısını** korumalıdır
- değerler çevrilebilir, ancak anahtarlar ve yapı `English.axaml` ile özdeş kalmalıdır

### Uygulamada özel çevirilerin kullanımı
- dil menüsünden harici bir AXAML dosyası içe aktarma
- çeviriler klasörünü açma ve dosyaları manuel olarak düzenleme
- çeviri çalışmasına yardımcı olmak için o klasörde **GitHub Copilot CLI** başlatma
- özel sözlüğü çalışma zamanında doğrudan dil menüsünden yükleme

---

## Klavye Kısayolları

Aşağıda mevcut kod tarafından onaylanan kısayol listesi bulunmaktadır.

### Ana pencere
| Kısayol | Eylem |
|---|---|
| `Ctrl+S` | Geçerli dosyayı kaydet |
| `Ctrl+Shift+S` | Tüm dosyaları kaydet |
| `Ctrl+O` | Dosya aç |
| `Ctrl+N` | Yeni dosya oluştur |
| `Ctrl+Shift+N` | Yeni proje oluştur |
| `Ctrl+B` | Projeyi derle |
| `Ctrl+Shift+B` | Projeyi yeniden derle |
| `Ctrl+Shift+A` | Projeyi analiz et |
| `F5` | Projeyi çalıştır |
| `Shift+F5` | Çalışan projeyi durdur |
| `Ctrl+W` | Geçerli sekmeyi kapat |
| `Ctrl+Shift+F` | Dosyalarda ara |
| `Ctrl+P` | Dosyayı ada göre bul |
| `Ctrl+Shift+Z` | Zen Modunu aç/kapat |
| `Ctrl+Shift+P` | AXAML önizlemesini aç |
| `Ctrl+Shift+E` | Gezgin panelini aç/kapat |
| `Ctrl+Shift+I` | AI / sağ paneli aç/kapat |
| `Ctrl+\`` | Alt panel / terminal alanını aç/kapat |
| `Esc` | Zen Modundan çık |

### Editör
| Kısayol | Eylem |
|---|---|
| `Alt+Enter` | İmlec konumunda hızlı düzeltmeyi göster |
| `Ctrl+.` | İmlec konumunda hızlı düzeltmeyi göster |
| `Ctrl+Shift+I` | Belgeyi biçimlendir |
| `Ctrl+Shift+A` | Otomatik Düzeltme penceresini aç |
| `F12` | Tanıma git |
| `F2` | Sembolü yeniden adlandır |
| `Ctrl+R` | Sembolü yeniden adlandır |
| `Ctrl+Shift+H` | Üzerine gelme bilgisini göster |
| `Tab` / `Enter` | Seçili tamamlama öğesini onayla |
| `Esc` | Tamamlama veya hızlı düzeltme açılır penceresini kapat |

### Terminal paneli
| Kısayol | Eylem |
|---|---|
| `Ctrl+C` | Geçerli terminal işlemini durdur |
| `Ctrl+L` | Terminal çıktısını temizle |
| `Yukarı / Aşağı` | Terminal geçmişinde gezin |

---

## Teknoloji Özeti

| Bileşen | Teknoloji |
|---|---|
| Hedef çerçeve | `.NET 10.0` (`net10.0-windows`) |
| UI çerçevesi | `Avalonia 12.0.0` |
| Kod analizi | Roslyn 5.x |
| F# desteği | `FSharp.Compiler.Service` 43.x |
| Derleme katmanı | `Microsoft.Build` 18.4 |
| NuGet entegrasyonu | `NuGet.Protocol` 7.3 |
| GitHub entegrasyonu | `Octokit` 14.0 |
| Yerel depolama | `LiteDB` 6 ön sürüm |

---

## Lisans

Bu proje **MIT Lisansı** kapsamında lisanslanmıştır — ayrıntılar için [LICENSE](LICENSE) dosyasına bakın.

> **Not:** Uygulamanın UI stilleri, simgeleri ve görsel varlıkları MIT Lisansı'nın dışında tutulmuştur ve Tüm Hakları Saklıdır. Tam hariç tutma listesi için [LICENSE](LICENSE) belgesine bakın.

---

<a id="russian"></a>

## Русский

## ⚠️ Системные требования
Для корректной работы IDE убедитесь, что ваша среда соответствует следующим требованиям:
* **Git для Windows:** Должен быть установлен и добавлен в системный `PATH`. Необходим для управления исходным кодом, клонирования репозиториев и функций GitHub Copilot CLI.
* **Системный диск:** Windows и рабочее пространство приложения должны находиться на диске **C:** для корректного разрешения внутренних путей.
* **Windows SDK:** Необходим для упаковки MSIX, подписи метаданных и развёртывания приложений.

## Обзор

**Insait Edit** — это IDE для рабочего стола Windows, ориентированная на разработку на C# и .NET. Приложение объединяет в одном интерфейсе собственный редактор, анализ на основе Roslyn, инструменты создания проектов, рабочие процессы сборки и публикации, интеграцию с Git, встроенный терминал и инструменты упаковки MSIX.

Текущая кодовая база ориентирована на рабочие процессы настольных приложений. Включает встроенное создание проектов для **Avalonia**, **Windows Forms**, стандартных шаблонов **консоль/библиотека классов** и стартовых шаблонов **F#**. Конвейер сборки и запуска также распознаёт **WPF**, **WinForms** и **Avalonia** как GUI-проекты.

---

## Основные возможности

### Полный цикл разработки настольных приложений
- Создание новых решений и проектов прямо в IDE
- Добавление проектов и элементов в существующее решение
- Работа с C#, F#, AXAML, XAML, изображениями и ресурсами проекта
- Сборка, пересборка, запуск, остановка, публикация и упаковка настольных приложений

### Поддерживаемые рабочие процессы проектов
- **Приложения Avalonia** с редактированием AXAML и поддержкой предварительного просмотра
- **Приложения Windows Forms** через шаблоны проектов и определение GUI-проектов
- **Проекты WPF** в конвейере сборки/запуска
- **Консольные приложения**, **библиотеки классов**, **пустые проекты C#** и **стартовые проекты F#**

### Редактор и интеллект кода
- Собственный `Insait Editor` с подсветкой синтаксиса и пользовательской поверхностью рендеринга
- Завершение кода C# на основе Roslyn, быстрая информация, поиск символов и помощники рефакторинга
- Поддержка завершения F# и всплывающих подсказок через `FSharp.Compiler.Service`
- Завершение AXAML/XAML для элементов Avalonia, свойств, событий, расширений разметки и пространств имён
- Встроенная диагностика, предложения быстрых исправлений и рабочие процессы автоматического исправления в редакторе

### Сборка, запуск и публикация
- Интеграция MSBuild / `dotnet` для операций сборки проектов
- Конфигурации запуска и поддержка составного запуска
- Конвейер запуска с поддержкой GUI для проектов настольных приложений
- Окно публикации с визуальным отображением прогресса

### Управление пакетами и развёртывание
- Панель NuGet для просмотра, установки, обновления и удаления пакетов
- Менеджер MSIX для упаковки, редактирования метаданных, замены значков и подписи

### Управление исходным кодом и инструменты GitHub
- Панель Git для статуса репозитория, проиндексированных/непроиндексированных изменений, информации о ветках, истории, коммита, push и pull
- Рабочие процессы клонирования репозиториев
- Интеграция с GitHub через сервисы на основе Octokit
- Встроенная панель **GitHub Copilot CLI** и рабочий процесс команд

### Терминал и утилиты рабочего пространства
- Встроенная панель терминала с управлением процессами и историей команд
- Проводник файлов, поиск в файлах, просмотрщик изображений, уведомления, инструменты кодировки и окна свойств проекта
- Окна предварительного просмотра AXAML и инструменты предварительного просмотра в стиле live-host

### Локализация
- Встроенные словари UI для **английского, украинского, немецкого, русского и турецкого** языков
- Пользовательские словари языков на основе AXAML, загружаемые во время выполнения
- Импорт внешних файлов перевода `.axaml` из меню языка
- Папка рабочего пространства переводов для ручного редактирования или перевода с помощью GitHub Copilot CLI

---

## Система анализа и завершения кода Roslyn

Основной опыт написания кода основан на **Microsoft Roslyn** и нескольких специализированных сервисах в проекте.

### Что реализовано
- `RoslynAutoCompleteFactory` делегирует завершение C# напрямую в Roslyn `CompletionService`
- Помощь с сигнатурами разрешается из семантических данных Roslyn
- Информация при наведении курсора использует Roslyn `QuickInfoService`
- `InlineDiagnosticService` выполняет дебаунсированный фоновый анализ и обновляет встроенные подчёркивания
- Предложения быстрых исправлений прикрепляются к диагностике и отображаются в редакторе
- `RoslynAutoFixService` обнаруживает встроенные реализации `CodeFixProvider` и `CodeRefactoringProvider` в Roslyn
- `CSharpCompletionService` поддерживает операции переименования и выделение в документе

### Практический результат в IDE
- умные списки завершения
- помощь с параметрами и перегрузками
- описания при наведении курсора
- отчёты об ошибках/предупреждениях в реальном времени
- быстрые исправления в один шаг
- перейти к определению
- переименовать символ

Для файлов AXAML IDE использует отдельный движок завершения, который отражает реальные сборки Avalonia и предлагает допустимые элементы управления, свойства, присоединённые свойства, события и расширения разметки.

---

## Заметки об упаковке MSIX

Подсистема MSIX поддерживает как **полную упаковку**, так и **упаковку из уже опубликованных выходных данных**.

### Возможности инструментов MSIX
- публикация проекта и его упаковка в MSIX в одном рабочем процессе
- создание `AppxManifest.xml`
- упаковка содержимого с помощью `MakeAppx.exe`
- открытие существующего MSIX и чтение метаданных пакета
- редактирование метаданных пакета и повторная упаковка
- замена значков внутри существующего MSIX
- подпись MSIX с помощью `SignTool.exe` и сертификата из `CurrentUser\My`

### Важные требования
- **Windows SDK необходим** для упаковки MSIX, поскольку используются `MakeAppx.exe` и `SignTool.exe`
- **издатель пакета должен точно совпадать с субъектом сертификата**
- на практике это означает, что **имя издателя `CN=...` должно быть идентично различающемуся имени субъекта сертификата**
- значки пакета для MSIX должны быть **основаны на PNG**
- если допустимый логотип не предоставлен, сервис может использовать изображение-заполнитель

### Рекомендации по использованию
- явно задавайте собственный значок пакета; значение по умолчанию является лишь заполнителем/фиктивным значением
- проверяйте идентификатор пакета, издателя, версию, исполняемый файл и точку входа перед подписью
- если подпись не удаётся из-за несоответствия издателя, приведите издателя манифеста в соответствие с субъектом сертификата

---

## Локализация и пользовательский рабочий процесс перевода

Пользовательские переводы управляются через простые словари AXAML.

### Встроенное поведение
- Стандартные языки интерфейса загружаются из `Insait Edit C Sharp/Interface Localization/`
- Пользовательские словари хранятся в `%AppData%\InsaitEdit\GitHubTranslations\`
- Сервис обеспечивает доступность `English.axaml` в качестве базового шаблона

### Правила пользовательских переводов
- файл пользовательского языка должен быть простым словарём `.axaml`
- он должен сохранять **ту же структуру ключей `x:String`**, что и английский словарь
- значения можно переводить, но ключи и структура должны оставаться идентичными `English.axaml`

### Как пользовательские переводы используются в приложении
- импортировать внешний файл AXAML из меню языка
- открыть папку переводов и редактировать файлы вручную
- запустить **GitHub Copilot CLI** в этой папке для помощи с переводом
- загрузить пользовательский словарь непосредственно из меню языка во время выполнения

---

## Сочетания клавиш

Ниже приведён список сочетаний клавиш, подтверждённых текущим кодом.

### Главное окно
| Сочетание клавиш | Действие |
|---|---|
| `Ctrl+S` | Сохранить текущий файл |
| `Ctrl+Shift+S` | Сохранить все файлы |
| `Ctrl+O` | Открыть файл |
| `Ctrl+N` | Создать новый файл |
| `Ctrl+Shift+N` | Создать новый проект |
| `Ctrl+B` | Собрать проект |
| `Ctrl+Shift+B` | Пересобрать проект |
| `Ctrl+Shift+A` | Анализировать проект |
| `F5` | Запустить проект |
| `Shift+F5` | Остановить работающий проект |
| `Ctrl+W` | Закрыть текущую вкладку |
| `Ctrl+Shift+F` | Поиск в файлах |
| `Ctrl+P` | Найти файл по имени |
| `Ctrl+Shift+Z` | Переключить режим Zen |
| `Ctrl+Shift+P` | Открыть предварительный просмотр AXAML |
| `Ctrl+Shift+E` | Переключить панель проводника |
| `Ctrl+Shift+I` | Переключить панель AI / правую панель |
| `Ctrl+\`` | Переключить нижнюю панель / область терминала |
| `Esc` | Выйти из режима Zen |

### Редактор
| Сочетание клавиш | Действие |
|---|---|
| `Alt+Enter` | Показать быстрое исправление на позиции курсора |
| `Ctrl+.` | Показать быстрое исправление на позиции курсора |
| `Ctrl+Shift+I` | Форматировать документ |
| `Ctrl+Shift+A` | Открыть окно автоматического исправления |
| `F12` | Перейти к определению |
| `F2` | Переименовать символ |
| `Ctrl+R` | Переименовать символ |
| `Ctrl+Shift+H` | Показать информацию при наведении |
| `Tab` / `Enter` | Подтвердить выбранный элемент завершения |
| `Esc` | Закрыть всплывающее окно завершения или быстрого исправления |

### Панель терминала
| Сочетание клавиш | Действие |
|---|---|
| `Ctrl+C` | Остановить текущий процесс терминала |
| `Ctrl+L` | Очистить вывод терминала |
| `Вверх / Вниз` | Навигация по истории терминала |

---

## Сводка технологий

| Компонент | Технология |
|---|---|
| Целевой фреймворк | `.NET 10.0` (`net10.0-windows`) |
| UI-фреймворк | `Avalonia 12.0.0` |
| Анализ кода | Roslyn 5.x |
| Поддержка F# | `FSharp.Compiler.Service` 43.x |
| Слой сборки | `Microsoft.Build` 18.4 |
| Интеграция NuGet | `NuGet.Protocol` 7.3 |
| Интеграция GitHub | `Octokit` 14.0 |
| Локальное хранилище | `LiteDB` 6 предварительная версия |

---

## Лицензия

Этот проект лицензирован по **лицензии MIT** — подробности см. в файле [LICENSE](LICENSE).

> **Примечание:** Стили UI, значки и визуальные ресурсы приложения исключены из лицензии MIT и остаются Все права защищены. Полный список исключений см. в [LICENSE](LICENSE).

---

<a id="ukrainian"></a>

## Українська

## ⚠️ Системні вимоги
Для коректної роботи IDE переконайтесь, що ваше середовище відповідає таким вимогам:
* **Git для Windows:** Має бути встановлений і доданий до системного `PATH`. Необхідний для керування вихідним кодом, клонування репозиторіїв та функцій GitHub Copilot CLI.
* **Системний диск:** Windows та робочий простір застосунку мають розміщуватись на диску **C:** для коректного розпізнавання внутрішніх шляхів.
* **Windows SDK:** Необхідний для пакування MSIX, підписання метаданих та розгортання застосунків.

## Огляд

**Insait Edit** — це IDE для робочого стола Windows, орієнтована на розробку на C# та .NET. Застосунок поєднує в одному інтерфейсі власний редактор, аналіз на основі Roslyn, інструменти створення проектів, робочі процеси збірки та публікації, інтеграцію з Git, вбудований термінал та інструменти пакування MSIX.

Поточна кодова база орієнтована на робочі процеси настільних застосунків. Включає вбудоване створення проектів для **Avalonia**, **Windows Forms**, стандартних шаблонів **консоль/бібліотека класів** та стартових шаблонів **F#**. Конвеєр збірки та запуску також розпізнає **WPF**, **WinForms** та **Avalonia** як GUI-проекти.

---

## Основні можливості

### Повний цикл розробки настільних застосунків
- Створення нових рішень і проектів безпосередньо в IDE
- Додавання проектів і елементів до наявного рішення
- Робота з C#, F#, AXAML, XAML, зображеннями та ресурсами проекту
- Збірка, перезбірка, запуск, зупинка, публікація та пакування настільних застосунків

### Підтримувані робочі процеси проектів
- **Застосунки Avalonia** з редагуванням AXAML та підтримкою попереднього перегляду
- **Застосунки Windows Forms** через шаблони проектів і визначення GUI-проектів
- **Проекти WPF** у конвеєрі збірки/запуску
- **Консольні застосунки**, **бібліотеки класів**, **порожні C# проекти** та **стартові проекти F#**

### Редактор та інтелект коду
- Власний `Insait Editor` з підсвічуванням синтаксису та поверхнею рендерингу
- Автодоповнення C# на основі Roslyn, швидка інформація, пошук символів та помічники рефакторингу
- Підтримка автодоповнення F# та спливаючих підказок через `FSharp.Compiler.Service`
- Автодоповнення AXAML/XAML для елементів Avalonia, властивостей, подій, розширень розмітки та просторів імен
- Вбудована діагностика, пропозиції швидких виправлень та автоматичні виправлення безпосередньо в редакторі

### Збірка, запуск та публікація
- Інтеграція MSBuild / `dotnet` для операцій збірки проектів
- Конфігурації запуску та підтримка складеного запуску
- Конвеєр запуску з урахуванням GUI для проектів настільних застосунків
- Вікно публікації з візуальним відображенням прогресу

### Керування пакетами та розгортання
- Панель NuGet для перегляду, встановлення, оновлення та видалення пакетів
- Менеджер MSIX для пакування, редагування метаданих, заміни значків та підписання

### Керування вихідним кодом та інструменти GitHub
- Панель Git для статусу репозиторію, проіндексованих/непроіндексованих змін, інформації про гілки, історії, коміту, push та pull
- Робочі процеси клонування репозиторіїв
- Інтеграція з GitHub через сервіси на основі Octokit
- Вбудована панель **GitHub Copilot CLI** та робочий процес команд

### Термінал та утиліти робочого простору
- Вбудована панель терміналу з керуванням процесами та історією команд
- Провідник файлів, пошук у файлах, переглядач зображень, сповіщення, інструменти кодування та вікна властивостей проекту
- Вікна попереднього перегляду AXAML та інструменти попереднього перегляду у стилі live-host

### Локалізація
- Вбудовані словники UI для **англійської, української, німецької, російської та турецької** мов
- Власні словники мов на основі AXAML, що завантажуються під час виконання
- Імпорт зовнішніх файлів перекладу `.axaml` з меню мови
- Папка робочого простору перекладів для ручного редагування або перекладу за допомогою GitHub Copilot CLI

---

## Система аналізу та автодоповнення Roslyn

Основний досвід написання коду базується на **Microsoft Roslyn** та кількох спеціалізованих сервісах у проекті.

### Що реалізовано
- `RoslynAutoCompleteFactory` делегує автодоповнення C# безпосередньо до Roslyn `CompletionService`
- Допомога з підписами розпізнається із семантичних даних Roslyn
- Інформація при наведенні курсору використовує Roslyn `QuickInfoService`
- `InlineDiagnosticService` виконує дебаунсований фоновий аналіз та оновлює вбудовані підкреслення
- Пропозиції швидких виправлень прикріплюються до діагностики та відображаються в редакторі
- `RoslynAutoFixService` виявляє вбудовані реалізації `CodeFixProvider` та `CodeRefactoringProvider` у Roslyn
- `CSharpCompletionService` підтримує операції перейменування та виділення в документі

### Практичний результат в IDE
- розумні списки автодоповнення
- допомога з параметрами та перевантаженнями
- описи при наведенні курсору
- звіти про помилки/попередження в реальному часі
- швидкі виправлення в один крок
- перейти до визначення
- перейменувати символ

Для файлів AXAML IDE використовує окремий рушій автодоповнення, який відображає реальні збірки Avalonia та пропонує допустимі елементи керування, властивості, приєднані властивості, події та розширення розмітки.

---

## Примітки щодо пакування MSIX

Підсистема MSIX підтримує як **повне пакування**, так і **пакування з уже опублікованих вихідних даних**.

### Можливості інструментів MSIX
- публікація проекту та його пакування в MSIX в одному робочому процесі
- створення `AppxManifest.xml`
- пакування вмісту за допомогою `MakeAppx.exe`
- відкриття наявного MSIX та читання метаданих пакета
- редагування метаданих пакета та повторне пакування
- заміна значків всередині наявного MSIX
- підписання MSIX за допомогою `SignTool.exe` та сертифіката з `CurrentUser\My`

### Важливі вимоги
- **Windows SDK необхідний** для пакування MSIX, оскільки використовуються `MakeAppx.exe` та `SignTool.exe`
- **видавець пакета повинен точно збігатися з суб'єктом сертифіката**
- на практиці це означає, що **ім'я видавця `CN=...` має бути ідентичним розрізнювальному імені суб'єкта сертифіката**
- значки пакета для MSIX мають бути **на основі PNG**
- якщо допустимий логотип не надано, сервіс може використати зображення-заповнювач

### Рекомендації щодо використання
- явно задавайте власний значок пакета; значення за замовчуванням є лише заповнювачем/фіктивним значенням
- перевіряйте ідентифікатор пакета, видавця, версію, виконуваний файл та точку входу перед підписанням
- якщо підписання не вдається через невідповідність видавця, приведіть видавця маніфесту у відповідність до суб'єкта сертифіката

---

## Локалізація та власний робочий процес перекладу

Власні переклади управляються через прості словники AXAML.

### Вбудована поведінка
- Стандартні мови інтерфейсу завантажуються з `Insait Edit C Sharp/Interface Localization/`
- Власні словники зберігаються в `%AppData%\InsaitEdit\GitHubTranslations\`
- Сервіс забезпечує доступність `English.axaml` як базового шаблону

### Правила власних перекладів
- файл власної мови має бути простим словником `.axaml`
- він повинен зберігати **таку саму структуру ключів `x:String`**, що й англійський словник
- значення можна перекладати, але ключі та структура мають залишатись ідентичними `English.axaml`

### Як власні переклади використовуються в застосунку
- імпортувати зовнішній файл AXAML з меню мови
- відкрити папку перекладів і редагувати файли вручну
- запустити **GitHub Copilot CLI** у цій папці для допомоги з перекладом
- завантажити власний словник безпосередньо з меню мови під час виконання

---

## Клавіатурні скорочення

Нижче наведено список скорочень, підтверджених поточним кодом.

### Головне вікно
| Скорочення | Дія |
|---|---|
| `Ctrl+S` | Зберегти поточний файл |
| `Ctrl+Shift+S` | Зберегти всі файли |
| `Ctrl+O` | Відкрити файл |
| `Ctrl+N` | Створити новий файл |
| `Ctrl+Shift+N` | Створити новий проект |
| `Ctrl+B` | Зібрати проект |
| `Ctrl+Shift+B` | Перезібрати проект |
| `Ctrl+Shift+A` | Аналізувати проект |
| `F5` | Запустити проект |
| `Shift+F5` | Зупинити запущений проект |
| `Ctrl+W` | Закрити поточну вкладку |
| `Ctrl+Shift+F` | Пошук у файлах |
| `Ctrl+P` | Знайти файл за іменем |
| `Ctrl+Shift+Z` | Увімкнути/вимкнути режим Zen |
| `Ctrl+Shift+P` | Відкрити попередній перегляд AXAML |
| `Ctrl+Shift+E` | Увімкнути/вимкнути панель провідника |
| `Ctrl+Shift+I` | Увімкнути/вимкнути AI / праву панель |
| `Ctrl+\`` | Увімкнути/вимкнути нижню панель / область терміналу |
| `Esc` | Вийти з режиму Zen |

### Редактор
| Скорочення | Дія |
|---|---|
| `Alt+Enter` | Показати швидке виправлення на позиції курсора |
| `Ctrl+.` | Показати швидке виправлення на позиції курсора |
| `Ctrl+Shift+I` | Відформатувати документ |
| `Ctrl+Shift+A` | Відкрити вікно автовиправлення |
| `F12` | Перейти до визначення |
| `F2` | Перейменувати символ |
| `Ctrl+R` | Перейменувати символ |
| `Ctrl+Shift+H` | Показати інформацію при наведенні |
| `Tab` / `Enter` | Підтвердити вибраний елемент автодоповнення |
| `Esc` | Закрити спливаюче вікно автодоповнення або швидкого виправлення |

### Панель терміналу
| Скорочення | Дія |
|---|---|
| `Ctrl+C` | Зупинити поточний процес терміналу |
| `Ctrl+L` | Очистити вивід терміналу |
| `Вгору / Вниз` | Навігація по історії терміналу |

---

## Зведення технологій

| Компонент | Технологія |
|---|---|
| Цільовий фреймворк | `.NET 10.0` (`net10.0-windows`) |
| UI-фреймворк | `Avalonia 12.0.0` |
| Аналіз коду | Roslyn 5.x |
| Підтримка F# | `FSharp.Compiler.Service` 43.x |
| Шар збірки | `Microsoft.Build` 18.4 |
| Інтеграція NuGet | `NuGet.Protocol` 7.3 |
| Інтеграція GitHub | `Octokit` 14.0 |
| Локальне сховище | `LiteDB` 6 попередня версія |

---

## Ліцензія

Цей проект ліцензований за **ліцензією MIT** — подробиці дивіться у файлі [LICENSE](LICENSE).

> **Примітка:** Стилі UI, значки та візуальні ресурси застосунку виключені з ліцензії MIT і залишаються Усі права захищені. Повний список винятків дивіться в [LICENSE](LICENSE).
