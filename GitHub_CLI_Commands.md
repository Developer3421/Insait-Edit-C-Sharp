# GitHub CLI Commands Available in Copilot

## ✅ Вже реалізовані команди

### CORE COMMANDS
- ✅ **gh-auth** - Authenticate gh and git with GitHub
  - `login` - Увійти в GitHub
  - `logout` - Вийти з GitHub
  - `status` - Перевірити статус автентифікації
  - `refresh` - Оновити токен автентифікації
  - `setup-git` - Налаштувати Git для використання GitHub CLI
  - `token` - Показати токен автентифікації

- ✅ **gh-repo** - Manage repositories
  - `create <name> [--private] [--description "..."]` - Створити репозиторій
  - `clone <owner/repo> [path]` - Клонувати репозиторій
  - `view [--web]` - Переглянути поточний репозиторій
  - `list [--limit N]` - Список ваших репозиторіїв
  - `fork <owner/repo>` - Зробити fork репозиторію
  - `delete <owner/repo>` - Видалити репозиторій
  - `archive <owner/repo>` - Заархівувати репозиторій
  - `unarchive <owner/repo>` - Розархівувати репозиторій

- ✅ **gh-pr** - Manage pull requests
  - `create --title "..." [--body "..."]` - Створити PR
  - `list [--state open|closed|merged]` - Список PR
  - `view <number> [--web]` - Переглянути PR
  - `checkout <number>` - Переключитися на PR
  - `merge <number>` - Злити PR
  - `close <number>` - Закрити PR
  - `reopen <number>` - Відкрити знову PR

- ✅ **gh-issue** - Manage issues
  - `create --title "..." [--body "..."]` - Створити issue
  - `list [--state open|closed|all]` - Список issues
  - `view <number> [--web]` - Переглянути issue
  - `close <number>` - Закрити issue
  - `reopen <number>` - Відкрити знову issue

- ✅ **gh-workflow** - GitHub Actions workflows
  - `list` - Список workflows
  - `view <workflow> [--web]` - Переглянути workflow
  - `run <workflow>` - Запустити workflow
  - `enable <workflow>` - Увімкнути workflow
  - `disable <workflow>` - Вимкнути workflow

- ✅ **gh-status** - Show GitHub CLI status

- ✅ **gh-install** - Install GitHub CLI via winget

### ДОДАТКОВІ КОМАНДИ - УСІ РЕАЛІЗОВАНІ! ✅

#### CORE COMMANDS (що залишилися)
- ✅ **gh-browse** - Open repositories, issues, PR in browser
- ✅ **gh-codespace** - Connect to and manage codespaces
- ✅ **gh-gist** - Manage gists
- ✅ **gh-org** - Manage organizations
- ✅ **gh-project** - Work with GitHub Projects
- ✅ **gh-release** - Manage releases

#### GITHUB ACTIONS COMMANDS
- ✅ **gh-cache** - Manage GitHub Actions caches
- ✅ **gh-run** - View details about workflow runs

#### ALIAS COMMANDS
- ✅ **gh-co** - Alias for "pr checkout"

#### ADDITIONAL COMMANDS
- ✅ **gh-agent-task** - Work with agent tasks (preview)
- ✅ **gh-alias** - Create command shortcuts
- ✅ **gh-api** - Make authenticated GitHub API request
- ✅ **gh-attestation** - Work with artifact attestations
- ✅ **gh-completion** - Generate shell completion scripts
- ✅ **gh-config** - Manage configuration for gh
- ✅ **gh-copilot** - Run GitHub Copilot CLI (preview)
- ✅ **gh-extension** - Manage gh extensions
- ✅ **gh-gpg-key** - Manage GPG keys
- ✅ **gh-label** - Manage labels
- ✅ **gh-preview** - Execute previews for gh features
- ✅ **gh-ruleset** - View info about repo rulesets
- ✅ **gh-search** - Search for repositories, issues, and PRs
- ✅ **gh-secret** - Manage GitHub secrets
- ✅ **gh-ssh-key** - Manage SSH keys
- ✅ **gh-variable** - Manage GitHub Actions variables

## 📋 Як використовувати

### Приклади команд:

```bash
# Автентифікація
gh-auth login
gh-auth status

# Робота з репозиторіями
gh-repo create my-new-repo --private
gh-repo clone owner/repo
gh-repo list --limit 20
gh-repo view --web

# Pull Requests
gh-pr create --title "Fix bug" --body "This fixes issue #123"
gh-pr list --state all
gh-pr view 42 --web
gh-pr checkout 42
gh-pr merge 42 --squash

# Issues
gh-issue create --title "Bug report" --body "Description..."
gh-issue list --state open
gh-issue view 123
gh-issue close 123

# GitHub Actions
gh-workflow list
gh-workflow run build.yml
gh-workflow enable ci.yml
gh-workflow disable old-workflow.yml

# Статус
gh-status
```

## 🎉 СТАТУС: ВСІ КОМАНДИ РЕАЛІЗОВАНІ!

Усі GitHub CLI команди успішно реалізовані в CopilotCliService.cs:

✅ **CORE COMMANDS** - Повністю реалізовано (auth, browse, codespace, gist, issue, org, pr, project, release, repo)
✅ **GITHUB ACTIONS COMMANDS** - Повністю реалізовано (cache, run, workflow)
✅ **ALIAS COMMANDS** - Повністю реалізовано (gh-co)
✅ **ADDITIONAL COMMANDS** - Повністю реалізовано (усі 16 команд)

Загалом реалізовано: **40+ команд GitHub CLI!**

## 💡 Рекомендації

- Команди повинні надавати чіткі повідомлення про помилки
- Використовувати емодзі для кращої візуальної відмінності (✅ ❌ 📋 🚀 тощо)
- Підтримувати як короткі (`-w`), так і довгі (`--web`) прапорці
- Перевіряти наявність обов'язкових аргументів
- Надавати корисні підказки у випадку помилок

