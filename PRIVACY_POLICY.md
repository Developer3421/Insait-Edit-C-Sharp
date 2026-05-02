# Privacy Policy — Insait Edit

**Last updated: April 16, 2026**

---

## Overview

Insait Edit is a local desktop IDE for Windows. The application runs entirely on your device. It does not operate any backend servers, does not collect personal data, and does not transmit your source code, files, or usage data to any remote service operated by the developer.

---

## Data We Do NOT Collect

Insait Edit does not collect, store, or transmit:

- Your source code, project files, or solution files
- Personal information such as your name, email address, or account credentials
- Usage analytics, telemetry, or crash reports
- Keyboard input or editor activity
- File system contents outside of the directories you explicitly open in the IDE

---

## Local Storage

The application stores the following data **locally on your device only**:

- **Recent projects list** — a local database of recently opened solution paths, stored in `%AppData%\InsaitEdit\` using LiteDB. This data never leaves your device.
- **Custom translation files** — any `.axaml` language dictionaries you import are stored in `%AppData%\InsaitEdit\GitHubTranslations\`. This data never leaves your device.
- **Application settings and preferences** — stored locally in the application data folder.

All local data can be deleted by removing the `%AppData%\InsaitEdit\` folder.

---

## Third-Party Services and External Tools

Insait Edit integrates with external tools that you install independently. The privacy practices of those tools are governed by their own privacy policies:

### Git for Windows
Used for all source control operations (commit, push, pull, clone). Git communicates with remote repositories according to the credentials and remotes you configure. The developer of Insait Edit has no access to this communication. See [git-scm.com](https://git-scm.com/) for details.

### GitHub Copilot CLI *(optional)*
If you have GitHub Copilot CLI installed on your system, the IDE's Copilot CLI panel sends commands to it locally. Any data transmitted to GitHub's AI services is governed by [GitHub's Privacy Policy](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement) and your GitHub account settings. GitHub Copilot CLI is a separate product not included in this application.

### NuGet
The NuGet panel connects to the NuGet package registry (nuget.org) to browse and download packages. This connection is subject to [Microsoft's Privacy Policy](https://privacy.microsoft.com/). The developer of Insait Edit does not intercept or store NuGet traffic.

### GitHub API (Octokit)
Some GitHub-related features use the GitHub REST API via the Octokit library. Any requests made to the GitHub API are subject to [GitHub's Privacy Policy](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement). Insait Edit does not store GitHub tokens or credentials — authentication is handled by Git's credential manager on your device.

---

## Network Access

Insait Edit itself does not initiate any network connections on its own. Network activity only occurs when you explicitly trigger an action that requires it, such as:

- Cloning or pushing to a remote Git repository
- Browsing or installing NuGet packages
- Using the GitHub Copilot CLI panel

---

## Children's Privacy

Insait Edit does not collect any personal information from anyone, including children under the age of 13.

---

## Changes to This Policy

If a future version of the application introduces features that affect data collection or transmission, this policy will be updated and the "Last updated" date above will be revised. The updated policy will be published together with the new application version.

---

## Contact

If you have questions about this privacy policy, you can open an issue in the project repository on GitHub.

---

*Insait Edit — Privacy Policy · Preview Release*

