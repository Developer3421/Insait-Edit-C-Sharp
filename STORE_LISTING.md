# Insait Edit — C# IDE · Microsoft Store Listing

---

## App Name

**Insait Edit — C# IDE**

---

## Short Description

*(up to 200 characters)*

A full-cycle Windows IDE for C# and .NET desktop development. Build, run, publish, and package apps with built-in Roslyn analysis, Git panel, NuGet manager, MSIX tools, and a built-in terminal.

---

## Features (Store "What's new in this version" / Feature list — one feature per line)

- Full-cycle C# and .NET desktop IDE for Windows
- Roslyn-powered smart completion, inline diagnostics, quick fixes, go to definition, and rename symbol
- AXAML live preview for Avalonia UI layouts — no build required
- AXAML and XAML completion reflecting real Avalonia assemblies
- F# editing with completion and tooltip support via FSharp.Compiler.Service
- Built-in Git panel — commit, push, pull, branch history, staged/unstaged changes
- Repository cloning from the welcome screen
- GitHub Copilot CLI panel — activates automatically when Copilot CLI is installed on your system
- Built-in NuGet panel — browse, install, update, and remove packages
- Full MSIX packaging workflow — generate manifest, swap icons, edit metadata, sign with certificate
- MSBuild and dotnet build integration with run configurations and compound multi-project runs
- GUI-aware run pipeline for Avalonia, WPF, and Windows Forms applications
- Built-in terminal panel with process control, command history, and Ctrl+C / Ctrl+L shortcuts
- File Explorer, Search in Files, Find File by Name, Image Viewer, Encoding Tools, Notifications
- Project Properties and Solution Properties editors
- Zen Mode for distraction-free editing
- Create new solutions and projects — Avalonia, WPF, Windows Forms, console, class library, F#
- Add projects and items to an existing solution
- Built-in UI localization: English, Ukrainian, German, Russian, Turkish
- Import custom .axaml translation files at runtime
- MIT licensed source code

---

## Description (Short — up to 1000 characters)

> ⚠️ **Preview Version** — This is an early preview release of Insait Edit. Features, UI, and behavior may change between updates. Some functionality may be incomplete or subject to revision.

Insait Edit is a full-cycle Windows IDE for C# and .NET desktop development. It includes a custom Roslyn-powered code editor, project creation tools, build and publish workflows, Git integration, a built-in terminal, NuGet package management, and MSIX packaging. The IDE is modular — optional external tools such as GitHub Copilot CLI can be connected when present on your system. GitHub Copilot CLI and GitHub Desktop are separate products and are not included in this application.

---

## Long Description (up to 10 000 characters)

⚠️ Preview Version — This is an early preview release. Features and UI may change between updates.

Stop switching between a dozen tools. Insait Edit puts everything you need to build, ship, and package a .NET desktop application into a single, focused Windows IDE — and gets out of your way.

---

YOUR CODE DESERVES A REAL IDE.

Most developers working with C# on Windows end up juggling a heavyweight commercial IDE, a separate terminal, a Git client, a NuGet browser, and a packaging tool. Insait Edit is built on the belief that all of this belongs in one place — lightweight, native, and purpose-built for .NET desktop development.

---

ROSLYN IN THE EDITOR. NOT AS AN AFTERTHOUGHT.

The editor in Insait Edit is not a glorified text box. It is built on Microsoft Roslyn — the same compiler platform that powers the biggest IDEs in the industry. Every keystroke is backed by real semantic analysis:

Smart completion lists that know your types, methods, and namespaces. Parameter hints and overload tooltips as you type. Inline error and warning squiggles updated in real time — no build required. One-keystroke quick fixes for common code issues. Auto-fix workflows that batch-apply Roslyn code fixers across a file. Go to definition, rename symbol, hover descriptions — all wired into the editor surface.

For Avalonia AXAML files, the IDE reflects real Avalonia assemblies at runtime and gives you property, event, attached property, and markup extension completion — for the actual version of Avalonia your project uses.

F# is supported too, with completion and tooltip support through FSharp.Compiler.Service.

---

FROM IDEA TO STORE-READY PACKAGE — WITHOUT LEAVING THE IDE.

Insait Edit covers the full lifecycle of a desktop application:

Create a new solution or add a project to an existing one — Avalonia, Windows Forms, WPF, console, class library, or F# starter — directly from the IDE. Write and edit code with full Roslyn intelligence. Build and rebuild with MSBuild integration. Run your app with a single key press. Set up run configurations or compound multi-project runs. Publish with a visual progress window. Package the output into a signed MSIX package ready for the Microsoft Store — generate the manifest, swap icons, edit metadata, and sign with your certificate, all inside the IDE.

No scripts. No separate packaging tools open in another window. One workflow, start to finish.

---

NUGET — PACKAGE MANAGEMENT BUILT IN.

The NuGet panel lets you search the package registry, install, update, and remove packages without leaving the IDE. No separate package manager window, no manual editing of .csproj files. Browse search results, pick a version, and let the IDE handle the rest.

---

MSIX PACKAGING — FROM BUILD TO SIGNED PACKAGE IN ONE WINDOW.

Insait Edit includes a full MSIX packaging workflow:

Publish your project and pack the output into an MSIX in a single operation. Generate AppxManifest.xml with your package identity, version, publisher, and entry point. Open an existing MSIX, read its metadata, edit it, and repack without extracting anything manually. Replace icons inside a package. Sign the final package with SignTool.exe using a certificate from your personal certificate store.

Everything a developer needs to submit a desktop app to the Microsoft Store — done inside the IDE. No PowerShell scripts, no manual manifest editing in Notepad.

Requires Windows SDK for MakeAppx.exe and SignTool.exe.

---

RUN YOUR WAY.

Insait Edit supports flexible run configurations:

Single-project runs with F5. Stop a running process with Shift+F5. Compound run configurations that start multiple projects at once. GUI-aware run pipeline that correctly handles Avalonia, WPF, and WinForms applications. Publish window with real-time visual progress reporting so you can see exactly what is happening during a publish operation.

---

GIT WITHOUT THE CLIENT APP.

The built-in Git panel gives you everything you need for day-to-day source control without installing a separate GUI client. View repository status, review staged and unstaged changes, browse branch history, commit, push, and pull — all from a panel inside the IDE. Clone a repository directly from the welcome screen.

The IDE uses Git for Windows under the hood. GitHub Desktop is not used, not required, and not included.

---

A MODULAR IDE THAT GROWS WITH YOUR TOOLCHAIN.

Insait Edit is designed to be modular. The core editing, build, and packaging experience works on its own. But when you bring in external tools, the IDE meets them where they are:

If you have GitHub Copilot CLI installed and authenticated on your system, the IDE's dedicated Copilot CLI panel activates automatically — giving you an AI-assisted command workflow integrated into your development environment. GitHub Copilot CLI is a separate product by GitHub and is not included in this application. You need to install and sign in to it independently.

The IDE does not force any tool on you. You decide what lives in your environment, and Insait Edit connects to what is available.

---

A TERMINAL THAT STAYS IN CONTEXT.

The built-in terminal panel runs inside the IDE with full process control and command history. No alt-tabbing to a separate window. Run dotnet commands, scripts, or any shell command without breaking your flow. Navigate previous commands with the Up/Down keys. Clear the output with Ctrl+L. Stop a runaway process with Ctrl+C — all without leaving the editor.

---

A FULL WORKSPACE, NOT JUST AN EDITOR.

Insait Edit includes the utility panels you actually use every day:

File Explorer — browse and open any file in your solution tree. Search in Files — find text across your entire project with Ctrl+Shift+F. Find File by Name — jump to any file instantly with Ctrl+P. Image Viewer — open and inspect image assets directly in the IDE. Encoding Tools — inspect and change file encoding without a separate utility. Notifications panel — review IDE messages and build output in one place. Project Properties and Solution Properties windows — edit project metadata without opening .csproj files by hand.

---

ZEN MODE — WHEN YOU NEED TO FOCUS.

Toggle Zen Mode with Ctrl+Shift+Z to collapse all panels and give the editor the entire screen. Press Esc to return. No distractions, no clicking around — just your code.

---

AXAML LIVE PREVIEW.

Working on an Avalonia UI layout? Open the AXAML preview panel with Ctrl+Shift+P and see your markup rendered in real time as you edit. The IDE includes a live-host preview system that renders your AXAML without a full build cycle.

---

KEYBOARD-FIRST WORKFLOW.

Every major action in Insait Edit has a keyboard shortcut:

Ctrl+S — save. Ctrl+Shift+S — save all. Ctrl+B — build. Ctrl+Shift+B — rebuild. F5 — run. Shift+F5 — stop. Ctrl+N — new file. Ctrl+Shift+N — new project. Ctrl+O — open file. Ctrl+W — close tab. Ctrl+Shift+F — search in files. Ctrl+P — find file by name. F12 — go to definition. F2 or Ctrl+R — rename symbol. Alt+Enter or Ctrl+. — show quick fix. Ctrl+Shift+I — format document or toggle the AI panel. Ctrl+Shift+E — toggle file explorer. Ctrl+` — toggle the terminal panel.

---

BUILT FOR WINDOWS. SPEAKS YOUR LANGUAGE.

Insait Edit is a native Windows application targeting .NET 10. The interface ships in five languages out of the box: English, Ukrainian, German, Russian, and Turkish. Need another language? Import a custom .axaml translation file at runtime or edit the translation files directly and reload them without restarting the IDE.

---

WHAT IS NOT INCLUDED IN THIS APPLICATION:

- GitHub Copilot CLI — a separate GitHub product. Install it independently; the IDE panel activates when it is present on your system.
- GitHub Desktop — not used or required. The IDE works with Git for Windows directly.
- .NET SDK — must be installed separately. Download from dot.net.
- Windows SDK — required only for MSIX packaging tools (MakeAppx.exe, SignTool.exe). Not needed for general development.

---

SYSTEM REQUIREMENTS:

- Windows 10 or Windows 11
- .NET 10 SDK installed and on PATH
- Git for Windows installed and on PATH (required for all source control features)
- Windows SDK (optional, required only for MSIX packaging)
- GitHub Copilot CLI (optional, required only for the Copilot CLI panel)
- Application workspace and Git must be accessible from the C: drive

---

The application source code is licensed under the MIT License. UI styles, icons, and visual assets are excluded from the MIT License and remain All Rights Reserved.

Insait Edit — Preview Release · Built with .NET 10, Avalonia UI 12, Roslyn 5

