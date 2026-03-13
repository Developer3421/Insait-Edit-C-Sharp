# 🌐 Localization Progress — Insait Edit C# IDE

> **Reference language:** English (`English.axaml`, 924 lines, ~410 string keys)  
> **Localization files:** `Interface Localization/` folder  
> **Last updated:** 2026-03-12 (AxamlLiveHost.axaml.cs code-behind fallback messages localized via `LiveHost.*` keys — 4 keys × 5 languages; **all** windows & controls now 100% localized)

---

## 📂 Project Windows & Controls

### 🪟 Main Windows (root directory)

| # | Window / File | Localization Prefix | Description |
|---|---|---|---|
| 1 | `MainWindow.axaml` | `App.*`, `TitleBar.*`, `Tooltip.*`, `Sidebar.*`, `Panel.*`, `Explorer.*`, `Tab.*`, `Status.*`, `Context.*`, `AI.*`, `Search.*`, `Problems.*`, `Output.*`, `Cli.*` | Main IDE window with sidebar, panels, terminal, CLI |
| 2 | `WelcomeWindow.axaml` | `Welcome.*`, `RecentProjects`, `DefaultTitle`, `WelcomeScreen.*` | Startup welcome screen (standalone) |
| 3 | `MenuWindow.axaml` | `Menu.*` | Hamburger-style menu with File / Edit / View / Build / Debug / Tools / Help |
| 4 | `NewProjectWindow.axaml` | `NewProject.*` | Create new C# / F# project dialog |
| 5 | `NewSolutionWindow.axaml` | `NewSolution.*` | Create new .sln solution dialog |
| 6 | `AddNewItemWindow.axaml` | `AddItem.*` | Add new file / class / interface / template to project |
| 7 | `AddProjectToSolutionWindow.axaml` | `AddProject.*` | Add a new project into an existing solution |
| 8 | `CloneRepositoryWindow.axaml` | `Clone.*` | Clone a Git repository |
| 9 | `GitWindow.axaml` | `Git.*` | Full Git operations window (pull, push, stash, rollback…) |
| 10 | `ImageViewerWindow.axaml` | `ImageViewer.*` | Built-in image preview (PNG, JPG, ICO…) |
| 11 | `AxamlPreviewWindow.axaml` | `AxamlPreview.*` | Live AXAML design preview |
| 12 | `AxamlLiveHost.axaml` | `LiveHost.*` | AXAML live renderer host — fallback error messages in code-behind |
| 13 | `PreviewErrorWindow.axaml` | `PreviewError.*` | Shows AXAML preview compile errors |
| 14 | `CompoundRunWindow.axaml` | `Compound.*` | Compound (multi-project) run configurations |
| 15 | `RunConfigurationsWindow.axaml` | `RunConfig.*` | Single & compound run/debug configurations |
| 16 | `PublishWindow.axaml` | `Publish.*` | Publish project wizard (deployment, runtime, options) |
| 17 | `PublishProgressWindow.axaml` | `PublishProgress.*` | Real-time publish progress & result |
| 18 | `ProjectPropertiesWindow.axaml` | `ProjectProps.*` | Project properties (general, build, package, signing, debug) |
| 19 | `SolutionPropertiesWindow.axaml` | `SolProps.*` | Solution properties shell |
| 20 | `MsixManagerWindow.axaml` | `Msix.*` | MSIX package builder, signer, manifest editor |
| 21 | `AutoFixWindow.axaml` | `AutoFix.*` | Roslyn quick-fix browser & code template inserter |
| 22 | `GeminiLanguageNameWindow.axaml` | `Gemini.Lang.*` | Gemini AI — language name prompt |
| 23 | `GeminiModelWindow.axaml` | `Gemini.Model.*` | Gemini AI — model selector |
| 24 | `GeminiSettingsWindow.axaml` | `Gemini.Settings.*` | Gemini AI — settings |

---

### 🎛️ Controls (`Controls/` directory)

| # | Control / File | Localization Prefix | Description |
|---|---|---|---|
| 1 | `AccountPanelControl.axaml` | `Account.*` | GitHub account sign-in, repos list, profile |
| 2 | `DiagnosticsPanel.axaml` | `Diag.*` | Code diagnostics panel (errors, warnings) |
| 3 | `GitPanelControl.axaml` | `GitPanel.*` | Sidebar Git panel (local changes, log, console) |
| 4 | `NuGetPanelControl.axaml` | `NuGet.*` | NuGet package browse / install / update / uninstall |
| 5 | `SettingsPanelControl.axaml` | `Settings.*` | IDE settings panel |
| 6 | `GenerateMemberWindow.axaml` | `GenMember.*` | Roslyn — generate member dialog |
| 7 | `GenerateTypeWindow.axaml` | `GenType.*` | Roslyn — generate type dialog |
| 8 | `GoToDefinitionWindow.axaml` | `GotoDef.*` | Roslyn — go-to-definition symbol picker |
| 9 | `RenameSymbolDialog.axaml` | `Rename.*` | Roslyn — rename symbol inline dialog |
| 10 | `RoslynCompletionWindow.axaml` | `Completion.*` | IntelliSense completion popup |
| 11 | `RoslynQuickFixWindow.axaml` | `QuickFix.*` | Roslyn inline quick-fix popup |
| 12 | `RoslynToolsWindow.axaml` | `RoslynTools.*` | Roslyn refactor / extract / generate tools |

---

### 🗂️ Project Properties Pages (`Controls/ProjectProps/`)

| # | Page / File | Description |
|---|---|---|
| 1 | `GeneralPage.axaml` | Assembly name, namespace, framework, output type, language version, nullable |
| 2 | `BuildPage.axaml` | Warnings, optimization, constants, platform |
| 3 | `DebugPage.axaml` | Launch profile, args, env vars, working dir |
| 4 | `PackagePage.axaml` | NuGet package metadata (ID, version, authors, license…) |
| 5 | `SigningPage.axaml` | Strong-name key signing (`SignAssembly`, `DelaySign`) |
| 6 | `SolutionBuildCfgPage.axaml` | Per-project build/deploy in solution configurations |
| 7 | `SolutionGeneralPage.axaml` | Solution-level general settings |
| 8 | `SolutionProjectsPage.axaml` | Projects list inside solution properties |

> ℹ️ All Project Properties pages are fully externalized. They reuse shared `ProjectProps.*` keys (tabs/labels) and page-specific `ProjectProps.General.*`, `ProjectProps.Build.*`, `ProjectProps.Debug.*`, `ProjectProps.Sign.*`, `ProjectProps.Pkg.*` keys. Solution pages use `SolProps.*` keys.

---

## 🌍 Localization Status

### Supported Languages

| Language | File | Native Name | Lines | Estimated Keys |
|---|---|---|---|---|
| 🇬🇧 English | `English.axaml` | English | ~1060 | ~530 |
| 🇺🇦 Ukrainian | `Ukrainian.axaml` | Українська | ~1060 | ~530 |
| 🇩🇪 German | `German.axaml` | Deutsch | ~1060 | ~530 |
| 🇷🇺 Russian | `Russian.axaml` | Русский | ~1060 | ~530 |
| 🇹🇷 Turkish | `Turkish.axaml` | Türkçe | ~1045 | ~530 |

---

### Per-Section Coverage

| Section / Window | Keys | 🇬🇧 EN | 🇺🇦 UK | 🇩🇪 DE | 🇷🇺 RU | 🇹🇷 TR |
|---|---|---|---|---|---|---|
| **1. MainWindow** — TitleBar | 9 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **1. MainWindow** — Tooltips | 15 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **1. MainWindow** — Sidebar | 6 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **1. MainWindow** — Panels & Explorer | 6 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **1. MainWindow** — Tabs & Bottom Panel | 8 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **1. MainWindow** — Status bar | 9 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **1. MainWindow** — Context menu | 24 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **1. MainWindow** — AI / Copilot CLI panel | 8 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **1. MainWindow** — CLI messages | 40 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **1. MainWindow** — CLI usage strings | 18 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **1. MainWindow** — CLI info/exists labels | 10 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **1. MainWindow** — CLI help strings | 32 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **1. MainWindow** — Search panel | 10 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **1. MainWindow** — Problems counters | 3 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **2. WelcomeWindow** | 16 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **2. WelcomeWindow** — XAML aliases | 2 | ✅ | ✅ | ✅ | ✅ | ⚠️ |
| **2. WelcomeScreen** (in-IDE start page) | 5 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **3. MenuWindow** | 58 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **4. NewProjectWindow** | 10 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **5. NewSolutionWindow** | 9 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **6. AddNewItemWindow** | 18 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **7. AddProjectToSolutionWindow** | 7 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **8. CloneRepositoryWindow** | 6 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **9. GitWindow** | 11 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **10. ImageViewerWindow** | 6 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **11. AxamlPreviewWindow** (+ extras) | 12 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **12. PreviewErrorWindow** | 3 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **13. CompoundRunWindow** | 13 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **14. RunConfigurationsWindow** | 23 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **15. PublishWindow** | 26 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **15b. PublishProgressWindow** | 9 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **16. ProjectPropertiesWindow** | 14 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **16b. PackagePage** (NuGet metadata) | 16 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **16c. GeneralPage** (app icon, assembly, code, app) | 8 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **16d. BuildPage** (config, compiler, output) | 12 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **16e. DebugPage** (launch, env vars) | 13 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **16f. SigningPage** (assembly signing) | 7 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **16g. SolutionBuildCfgPage** | 2 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **16h. SolutionGeneralPage** | 6 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **16i. SolutionProjectsPage** | 2 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **19. SolutionPropertiesWindow** | 9 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **SettingsPanelControl** | 14 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **GeminiLanguageNameWindow** | 6 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **GeminiModelWindow** | 9 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **GeminiSettingsWindow** | 6 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **GenerateMemberWindow** | 8 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **GenerateTypeWindow** | 8 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **GoToDefinitionWindow** | 2 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **RenameSymbolDialog** | 5 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **RoslynCompletionWindow** | 3 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **RoslynQuickFixWindow** | 2 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **RoslynToolsWindow** | 2 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **AxamlLiveHost** (code-behind fallback) | 4 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **17. MsixManagerWindow** | 62 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **18. NuGetPanelControl** (basic) | 9 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **18. NuGetPanelControl** (details & status) | 37 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **19. AccountPanelControl** | 11 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **20. GitPanelControl** | 9 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **21. Diagnostics / Editor** | 3 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **AutoFixWindow** | 18 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Common buttons** | 7 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Language switcher** | 6 | ✅ | ✅ | ✅ | ✅ | ✅ |

**Legend:** ✅ Complete &nbsp;&nbsp; ⚠️ Partial (some keys missing) &nbsp;&nbsp; ❌ Not started

---

### Overall Summary

| Language | Coverage | Status | Missing keys |
|---|---|---|---|
| 🇬🇧 **English** | 100% | ✅ Reference | — |
| 🇺🇦 **Ukrainian** | 100% | ✅ Complete | — |
| 🇩🇪 **German** | 100% | ✅ Complete | — |
| 🇷🇺 **Russian** | 100% | ✅ Complete | — |
| 🇹🇷 **Turkish** | 100% | ✅ Complete | — |

---

## 🚫 Windows/Controls Without Localization

The following windows/controls have **no string keys** in any localization file (strings are either hardcoded in `.axaml.cs` or currently not externalized):

| Window / Control | Reason |
|---|---|
| *(none)* | All windows and controls are fully localized ✅ |

---

## 📋 Key Group Reference

| Key Prefix | Window / Component |
|---|---|
| `App.*` | Application-level |
| `TitleBar.*` | MainWindow title bar buttons |
| `Tooltip.*` | MainWindow toolbar tooltips |
| `Sidebar.*` | Left sidebar icon tooltips |
| `Panel.*` | Panel header labels |
| `Explorer.*` | File explorer toolbar |
| `Tab.*` | Bottom panel tabs |
| `Status.*` | Status bar actions |
| `Context.*` | Right-click context menu |
| `AI.*` | Copilot CLI panel |
| `Cli.*` | CLI command messages, help, usage |
| `Search.*` | Search panel |
| `Problems.*` | Problems tab |
| `Output.*` | Build/Run output placeholders |
| `Welcome.*` | WelcomeWindow |
| `WelcomeScreen.*` | In-IDE start page |
| `RecentProjects` | WelcomeWindow alias |
| `DefaultTitle` | AutoFixWindow fallback title alias |
| `Menu.*` | MenuWindow (all sub-menus) |
| `NewProject.*` | NewProjectWindow |
| `NewSolution.*` | NewSolutionWindow |
| `AddItem.*` | AddNewItemWindow |
| `AddProject.*` | AddProjectToSolutionWindow |
| `Clone.*` | CloneRepositoryWindow |
| `Git.*` | GitWindow |
| `ImageViewer.*` | ImageViewerWindow |
| `AxamlPreview.*` | AxamlPreviewWindow |
| `PreviewError.*` | PreviewErrorWindow |
| `Compound.*` | CompoundRunWindow |
| `RunConfig.*` | RunConfigurationsWindow |
| `Publish.*` | PublishWindow |
| `PublishProgress.*` | PublishProgressWindow |
| `ProjectProps.*` | ProjectPropertiesWindow (+ pages) |
| `ProjectProps.Pkg.*` | PackagePage — NuGet metadata fields |
| `ProjectProps.General.*` | GeneralPage — app icon, assembly, code headers |
| `ProjectProps.Build.*` | BuildPage — compiler, output settings |
| `ProjectProps.Debug.*` | DebugPage — launch, env vars |
| `ProjectProps.Sign.*` | SigningPage — strong-name key signing |
| `SolProps.*` | SolutionPropertiesWindow + all solution pages |
| `Settings.*` | SettingsPanelControl — tool paths |
| `Gemini.*` | Gemini AI windows (Lang, Model, Settings) |
| `GenMember.*` | GenerateMemberWindow |
| `GenType.*` | GenerateTypeWindow |
| `GotoDef.*` | GoToDefinitionWindow |
| `Rename.*` | RenameSymbolDialog |
| `Completion.*` | RoslynCompletionWindow |
| `QuickFix.*` | RoslynQuickFixWindow |
| `RoslynTools.*` | RoslynToolsWindow |
| `LiveHost.*` | AxamlLiveHost — fallback renderer error messages |
| `Msix.*` | MsixManagerWindow |
| `NuGet.*` | NuGetPanelControl |
| `Account.*` | AccountPanelControl |
| `GitPanel.*` | GitPanelControl |
| `Diag.*` | DiagnosticsPanel |
| `Editor.*` | Editor status messages |
| `AutoFix.*` | AutoFixWindow |
| `Common.*` | Shared button labels (OK, Cancel…) |
| `Lang.*` | Language switcher labels |

