<p align="center">
  <img src="Insait Edit C Sharp/Icons/AppIconIDE.png" alt="Insait Edit Logo" width="128" height="128"/>
</p>

<h1 align="center">Insait Edit — C# IDE</h1>

<p align="center">
  <b>A modern, cross-platform C# IDE built with Avalonia UI and Roslyn</b><br/>
  <b>Eine moderne, plattformübergreifende C#-IDE auf Basis von Avalonia UI und Roslyn</b>
</p>

<p align="center">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white"/>
  <img alt="Avalonia UI" src="https://img.shields.io/badge/Avalonia_UI-11.3-8B44AC?logo=avalonia&logoColor=white"/>
  <img alt="Roslyn" src="https://img.shields.io/badge/Roslyn-5.0-blue"/>
  <img alt="Platform" src="https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows&logoColor=white"/>
  <img alt="License" src="https://img.shields.io/badge/License-MIT%20(with%20exclusions)-yellow"/>
</p>

<p align="center">
  <a href="#-overview">🇬🇧 English</a> · <a href="#-überblick-deutsch">🇩🇪 Deutsch</a>
</p>

---

# 🇬🇧 English

## 📖 Overview

**Insait Edit** is a lightweight yet powerful integrated development environment (IDE) designed specifically for C# and .NET development. Built on top of [Avalonia UI](https://avaloniaui.net/) with the [Roslyn](https://github.com/dotnet/roslyn) compiler platform, it provides a rich coding experience with IntelliSense, diagnostics, refactoring, and a beautiful fluent orange-purple theme.

---

## ✨ Features

### 🖊️ Code Editor
- **Insait Editor** — a custom-built code editor with syntax highlighting, line numbering, and a modern UI
- **Roslyn-powered IntelliSense** — smart code completion, signature help, and parameter info for C#
- **F# support** — completion engine for F# projects
- **AXAML completion** — IntelliSense for Avalonia XAML files
- **Syntax highlighting**
- **Code snippets** — built-in C# snippet provider

### 🔍 Code Analysis & Refactoring
- **Real-time diagnostics** — errors, warnings, and suggestions powered by Roslyn analyzers
- **Inline diagnostics** — see issues directly in the editor
- **Quick fixes** — apply Roslyn code fixes with one click
- **Rename symbol** — safe project-wide symbol renaming
- **Go to Definition** — navigate to symbol declarations instantly

### 🔨 Build & Run
- **MSBuild integration** — build .NET solutions and projects directly from the IDE
- **Run configurations** — manage multiple launch profiles
- **Compound run** — run several configurations simultaneously
- **Debug support** — start with or without the debugger
- **Publish** — publish projects with a visual progress window

### 📦 Package Management
- **NuGet panel** — search, install, update, and remove NuGet packages
- **MSIX Manager** — create and manage MSIX application packages

### 🔗 Git & GitHub Integration
- **Git panel** — stage, commit, push, pull, and view change diffs
- **Clone repository** — clone repos from URL or GitHub
- **GitHub account** — sign in with GitHub OAuth, view repositories
- **GitHub Copilot CLI** — integrated Copilot CLI assistant (path detection now handles both `gh-copilot` extension and standalone `copilot.exe`; configure path under Settings)

### 🖥️ Terminal
- **Built-in terminal** — a full ConPTY-based terminal emulator with ANSI rendering
- **ANSI grid terminal** — advanced terminal control with grid-based buffer

### 🌐 Localization
- **5 languages** — English, Ukrainian, German, Russian, Turkish
- **Gemini AI translation** — generate translations for custom language names via Google Gemini

### 🤖 AI Integration
- **Gemini API** — AI-powered code assistance and translation
- **Configurable models** — choose Gemini model and language settings

### 🔌 ESP32 / nanoFramework Support
- **nanoFramework projects** — create and build projects for ESP32 microcontrollers
- **Device panel** — detect and manage connected serial devices
- **LED panel designer** — visual designer for LED panel layouts

### 🎨 UI & Design
- **Fluent Orange-Purple theme** — custom dark theme with warm accent colors
- **AXAML live preview** — preview Avalonia UI files in real time
- **Image viewer** — built-in image viewer for project assets
- **Custom title bar** — frameless window with custom minimize / maximize / close buttons

---

## 🛠️ Tech Stack

| Component | Technology |
|---|---|
| **Framework** | .NET 10.0 (Windows) |
| **UI Framework** | Avalonia UI 11.3 |
| **Code Analysis** | Microsoft Roslyn 5.0 |
| **Build System** | MSBuild 18.3 |
| **Version Control** | LibGit2 / Git for Windows |
| **Package Manager** | NuGet.Protocol 7.3 |
| **GitHub API** | Octokit 14.0 |
| **Database** | LiteDB 6.0 |
| **IoT** | nanoFramework |
| **Templating** | Microsoft.TemplateEngine |

---

## 📁 Project Structure

```
Insait Edit C Sharp/
├── Controls/               # Reusable UI controls
│   ├── DiagnosticsPanel     # Error/warning list panel
│   ├── GitPanelControl      # Git source control panel
│   ├── NuGetPanelControl    # NuGet package manager panel
│   ├── RoslynCompletion*    # IntelliSense windows
│   ├── TerminalControl      # Built-in terminal emulator
│   └── ...
├── Esp/                    # ESP32 / nanoFramework support
│   ├── Controls/            # Device panel
│   ├── Models/              # ESP project models
│   ├── Services/            # nanoFramework build service
│   ├── Tools/               # Firmware tools
│   └── Windows/             # LED designer, nano project wizard
├── Icons/                  # Application icons
├── Insait Code Editor/     # Custom code editor component
│   ├── InsaitEditor.axaml   # Editor UI
│   ├── InsaitEditorSurface  # Rendering surface
│   └── InsaitEditorColors   # Color definitions
├── Interface Localization/ # Language resource files
│   ├── English.axaml
│   ├── Ukrainian.axaml
│   ├── German.axaml
│   ├── Russian.axaml
│   └── Turkish.axaml
├── Models/                 # Data models
├── Services/               # Business logic services
│   ├── BuildService         # MSBuild integration
│   ├── CodeAnalysisService  # Roslyn analysis
│   ├── GitService           # Git operations
│   ├── NuGetService         # Package management
│   ├── RoslynWorkspace*     # Roslyn workspace management
│   └── ...
├── ViewModels/             # MVVM view models
├── MainWindow.axaml        # Main IDE window
├── WelcomeWindow.axaml     # Start screen
└── Program.cs              # Application entry point
```

---

## 🚀 Getting Started

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later
- Windows 10/11 (x64)
- Git for Windows (optional, for version control features)

### Build & Run

```bash
# Clone the repository
git clone https://github.com/YourUsername/Insait-Edit-CSharp.git
cd Insait-Edit-CSharp

# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the IDE
dotnet run --project "Insait Edit C Sharp"
```

### Publish

```bash
# Publish a self-contained release
dotnet publish -c Release -r win-x64 --self-contained
```

---

## 📸 Screenshots

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

## 🗺️ Roadmap

- [ ] Linux & macOS support
- [ ] Plugin / extension system
- [ ] Integrated debugger with breakpoints & variable inspection
- [ ] Multi-project solution explorer


---

## 🤝 Contributing

Contributions are welcome! Please open an issue to discuss proposed changes before submitting a pull request.

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

> **Note:** The application's UI styles, icons, and visual assets are **excluded** from the MIT License and remain All Rights Reserved. See the [LICENSE](LICENSE) file for the full exclusion list.

---

<p align="center">
  Made with ❤️ using <b>Avalonia UI</b> and <b>Roslyn</b>
</p>

---

# 🇩🇪 Deutsch

---

## 📖 Überblick (Deutsch)

**Insait Edit** ist eine leichtgewichtige und dennoch leistungsstarke integrierte Entwicklungsumgebung (IDE), die speziell für die C#- und .NET-Entwicklung konzipiert wurde. Aufgebaut auf [Avalonia UI](https://avaloniaui.net/) mit der [Roslyn](https://github.com/dotnet/roslyn)-Compiler-Plattform bietet sie eine umfangreiche Entwicklungserfahrung mit IntelliSense, Diagnose, Refactoring und einem ansprechenden Fluent-Design in Orange-Violett.

---

## ✨ Funktionen

### 🖊️ Code-Editor
- **Insait Editor** — ein maßgeschneiderter Code-Editor mit Syntaxhervorhebung, Zeilennummerierung und modernem UI
- **Roslyn-basiertes IntelliSense** — intelligente Codevervollständigung, Signaturhilfe und Parameterinformationen für C#
- **F#-Unterstützung** — Vervollständigungs-Engine für F#-Projekte
- **AXAML-Vervollständigung** — IntelliSense für Avalonia-XAML-Dateien
- **Code-Snippets** — integrierter C#-Snippet-Anbieter

### 🔍 Codeanalyse & Refactoring
- **Echtzeit-Diagnose** — Fehler, Warnungen und Vorschläge durch Roslyn-Analyzer
- **Inline-Diagnose** — Probleme direkt im Editor sehen
- **Quick Fixes** — Roslyn-Korrekturen mit einem Klick anwenden
- **Symbol umbenennen** — sicheres projektweites Umbenennen von Symbolen
- **Gehe zur Definition** — sofortiges Navigieren zu Symboldeklarationen

### 🔨 Erstellen & Ausführen
- **MSBuild-Integration** — .NET-Lösungen und -Projekte direkt aus der IDE erstellen
- **Ausführungskonfigurationen** — mehrere Startprofile verwalten
- **Verbundstart** — mehrere Konfigurationen gleichzeitig ausführen
- **Debug-Unterstützung** — mit oder ohne Debugger starten
- **Veröffentlichen** — Projekte mit einem visuellen Fortschrittsfenster veröffentlichen

### 📦 Paketverwaltung
- **NuGet-Panel** — NuGet-Pakete suchen, installieren, aktualisieren und entfernen
- **MSIX-Manager** — MSIX-Anwendungspakete erstellen und verwalten

### 🔗 Git- & GitHub-Integration
- **Git-Panel** — Dateien stagen, committen, pushen, pullen und Änderungen vergleichen
- **Repository klonen** — Repos per URL oder von GitHub klonen
- **GitHub-Konto** — mit GitHub OAuth anmelden, Repositories anzeigen
- **GitHub Copilot CLI** — integrierter Copilot-CLI-Assistent

### 🖥️ Terminal
- **Integriertes Terminal** — vollwertiger ConPTY-basierter Terminal-Emulator mit ANSI-Rendering
- **ANSI-Grid-Terminal** — erweiterte Terminal-Steuerung mit gitterbasiertem Puffer

### 🌐 Lokalisierung
- **5 Sprachen** — Englisch, Ukrainisch, Deutsch, Russisch, Türkisch
- **Gemini-KI-Übersetzung** — Übersetzungen für benutzerdefinierte Sprachnamen über Google Gemini generieren

### 🤖 KI-Integration
- **Gemini API** — KI-gestützte Code-Assistenz und Übersetzung
- **Konfigurierbare Modelle** — Gemini-Modell und Spracheinstellungen auswählen

### 🔌 ESP32- / nanoFramework-Unterstützung
- **nanoFramework-Projekte** — Projekte für ESP32-Mikrocontroller erstellen und kompilieren
- **Geräte-Panel** — angeschlossene serielle Geräte erkennen und verwalten
- **LED-Panel-Designer** — visueller Designer für LED-Panel-Layouts

### 🎨 Benutzeroberfläche & Design
- **Fluent-Orange-Violett-Theme** — benutzerdefiniertes dunkles Thema mit warmen Akzentfarben
- **AXAML-Livevorschau** — Avalonia-UI-Dateien in Echtzeit anzeigen
- **Bildbetrachter** — integrierter Bildbetrachter für Projektdateien
- **Benutzerdefinierte Titelleiste** — rahmenloses Fenster mit eigenen Minimieren-/Maximieren-/Schließen-Schaltflächen

---

## 🛠️ Technologie-Stack

| Komponente | Technologie |
|---|---|
| **Framework** | .NET 10.0 (Windows) |
| **UI-Framework** | Avalonia UI 11.3 |
| **Codeanalyse** | Microsoft Roslyn 5.0 |
| **Build-System** | MSBuild 18.3 |
| **Versionskontrolle** | LibGit2 / Git für Windows |
| **Paketmanager** | NuGet.Protocol 7.3 |
| **GitHub API** | Octokit 14.0 |
| **Datenbank** | LiteDB 6.0 |
| **IoT** | nanoFramework |
| **Vorlagen** | Microsoft.TemplateEngine |

---

## 📁 Projektstruktur

```
Insait Edit C Sharp/
├── Controls/               # Wiederverwendbare UI-Steuerelemente
│   ├── DiagnosticsPanel     # Fehler-/Warnungslisten-Panel
│   ├── GitPanelControl      # Git-Quellcodeverwaltungs-Panel
│   ├── NuGetPanelControl    # NuGet-Paketverwaltungs-Panel
│   ├── RoslynCompletion*    # IntelliSense-Fenster
│   ├── TerminalControl      # Integrierter Terminal-Emulator
│   └── ...
├── Esp/                    # ESP32- / nanoFramework-Unterstützung
│   ├── Controls/            # Geräte-Panel
│   ├── Models/              # ESP-Projektmodelle
│   ├── Services/            # nanoFramework-Build-Dienst
│   ├── Tools/               # Firmware-Tools
│   └── Windows/             # LED-Designer, Nano-Projektassistent
├── Icons/                  # Anwendungssymbole
├── Insait Code Editor/     # Benutzerdefinierte Editor-Komponente
│   ├── InsaitEditor.axaml   # Editor-Benutzeroberfläche
│   ├── InsaitEditorSurface  # Rendering-Oberfläche
│   └── InsaitEditorColors   # Farbdefinitionen
├── Interface Localization/ # Sprach-Ressourcendateien
│   ├── English.axaml
│   ├── Ukrainian.axaml
│   ├── German.axaml
│   ├── Russian.axaml
│   └── Turkish.axaml
├── Models/                 # Datenmodelle
├── Services/               # Geschäftslogik-Dienste
│   ├── BuildService         # MSBuild-Integration
│   ├── CodeAnalysisService  # Roslyn-Analyse
│   ├── GitService           # Git-Operationen
│   ├── NuGetService         # Paketverwaltung
│   ├── RoslynWorkspace*     # Roslyn-Workspace-Verwaltung
│   └── ...
├── ViewModels/             # MVVM-ViewModels
├── MainWindow.axaml        # Haupt-IDE-Fenster
├── WelcomeWindow.axaml     # Startbildschirm
└── Program.cs              # Anwendungseinstiegspunkt
```

---

## 🚀 Erste Schritte

### Voraussetzungen

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) oder höher
- Windows 10/11 (x64)
- Git für Windows (optional, für Versionskontrollfunktionen)

### Erstellen & Ausführen

```bash
# Repository klonen
git clone https://github.com/IhrBenutzername/Insait-Edit-CSharp.git
cd Insait-Edit-CSharp

# Abhängigkeiten wiederherstellen
dotnet restore

# Projekt erstellen
dotnet build

# IDE starten
dotnet run --project "Insait Edit C Sharp"
```

### Veröffentlichen

```bash
# Eigenständige Release-Version veröffentlichen
dotnet publish -c Release -r win-x64 --self-contained
```

---

## 📸 Screenshots

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

## 🗺️ Fahrplan

- [ ] Linux- & macOS-Unterstützung
- [ ] Plugin- / Erweiterungssystem
- [ ] Integrierter Debugger mit Haltepunkten & Variableninspektion
- [ ] Multi-Projekt-Projektmappen-Explorer
- [ ] Themes-Marktplatz
- [ ] VB.NET- & C++-Sprachunterstützung

---

## 🤝 Mitwirken

Beiträge sind willkommen! Bitte eröffnen Sie ein Issue, um vorgeschlagene Änderungen zu besprechen, bevor Sie einen Pull-Request einreichen.

1. Repository forken
2. Feature-Branch erstellen (`git checkout -b feature/tolles-feature`)
3. Änderungen committen (`git commit -m 'Tolles Feature hinzufügen'`)
4. Branch pushen (`git push origin feature/tolles-feature`)
5. Pull-Request öffnen

---

## 📄 Lizenz

Dieses Projekt steht unter der **MIT-Lizenz** — Details finden Sie in der Datei [LICENSE](LICENSE).

> **Hinweis:** Die UI-Stile, Icons und visuellen Assets der Anwendung sind von der MIT-Lizenz **ausgenommen** und bleiben Alle Rechte vorbehalten. Siehe die Datei [LICENSE](LICENSE) für die vollständige Ausschlussliste.

---

<p align="center">
  Mit ❤️ entwickelt mit <b>Avalonia UI</b> und <b>Roslyn</b>
</p>

