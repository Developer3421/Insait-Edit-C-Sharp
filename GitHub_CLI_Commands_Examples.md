# GitHub CLI Commands - Приклади використання

## 🚀 Всі реалізовані команди GitHub CLI в Copilot IDE

### 📦 Встановлення та автентифікація

```bash
# Встановити GitHub CLI
gh-install

# Увійти в GitHub
gh-auth login

# Перевірити статус автентифікації
gh-auth status

# Оновити токен
gh-auth refresh

# Налаштувати Git
gh-auth setup-git

# Показати токен
gh-auth token

# Вийти
gh-auth logout
```

### 📂 Управління репозиторіями (gh-repo)

```bash
# Створити новий репозиторій
gh-repo create my-project --private --description "My awesome project"

# Клонувати репозиторій
gh-repo clone owner/repository

# Переглянути поточний репозиторій
gh-repo view
gh-repo view --web

# Список ваших репозиторіїв
gh-repo list
gh-repo list --limit 20

# Зробити fork
gh-repo fork owner/repository

# Видалити репозиторій
gh-repo delete owner/repository

# Заархівувати репозиторій
gh-repo archive owner/repository

# Розархівувати репозиторій
gh-repo unarchive owner/repository
```

### 🔀 Pull Requests (gh-pr)

```bash
# Створити PR
gh-pr create --title "Fix bug" --body "This fixes issue #123"

# Список PR
gh-pr list
gh-pr list --state open
gh-pr list --state closed
gh-pr list --state merged

# Переглянути PR
gh-pr view 42
gh-pr view 42 --web

# Переключитися на PR (checkout)
gh-pr checkout 42
gh-co 42  # Коротка версія

# Злити PR
gh-pr merge 42
gh-pr merge 42 --squash

# Закрити PR
gh-pr close 42

# Відкрити знову PR
gh-pr reopen 42
```

### 🐛 Issues (gh-issue)

```bash
# Створити issue
gh-issue create --title "Bug report" --body "Description of the bug"

# Список issues
gh-issue list
gh-issue list --state open
gh-issue list --state closed
gh-issue list --state all

# Переглянути issue
gh-issue view 123
gh-issue view 123 --web

# Закрити issue
gh-issue close 123

# Відкрити знову issue
gh-issue reopen 123
```

### ⚙️ GitHub Actions

#### Workflows (gh-workflow)

```bash
# Список workflows
gh-workflow list

# Переглянути workflow
gh-workflow view build.yml
gh-workflow view ci.yml --web

# Запустити workflow
gh-workflow run build.yml

# Увімкнути workflow
gh-workflow enable ci.yml

# Вимкнути workflow
gh-workflow disable old-workflow.yml
```

#### Workflow Runs (gh-run)

```bash
# Список запусків
gh-run list

# Переглянути запуск
gh-run view 123456

# Повторно запустити
gh-run rerun 123456

# Скасувати запуск
gh-run cancel 123456

# Завантажити артефакти
gh-run download 123456

# Видалити запуск
gh-run delete 123456
```

#### Cache (gh-cache)

```bash
# Список кешів
gh-cache list

# Видалити кеш
gh-cache delete cache-key
```

### 🚀 Releases (gh-release)

```bash
# Список релізів
gh-release list

# Переглянути реліз
gh-release view v1.0.0

# Створити реліз
gh-release create v1.0.0 --title "Version 1.0.0" --notes "Release notes"

# Завантажити файли
gh-release download v1.0.0

# Завантажити файл до релізу
gh-release upload v1.0.0 binary.exe

# Редагувати реліз
gh-release edit v1.0.0 --title "New title"

# Видалити реліз
gh-release delete v1.0.0
```

### 📝 Gists (gh-gist)

```bash
# Список gists
gh-gist list

# Переглянути gist
gh-gist view abc123

# Створити gist
gh-gist create file.txt --description "My gist"

# Редагувати gist
gh-gist edit abc123

# Клонувати gist
gh-gist clone abc123

# Видалити gist
gh-gist delete abc123
```

### 🌐 Інші команди

#### Browse (gh-browse)

```bash
# Відкрити репозиторій в браузері
gh-browse

# Відкрити конкретну гілку
gh-browse --branch develop

# Відкрити settings
gh-browse --settings
```

#### Codespaces (gh-codespace)

```bash
# Список codespaces
gh-codespace list

# Створити codespace
gh-codespace create

# Підключитися до codespace
gh-codespace ssh

# Видалити codespace
gh-codespace delete
```

#### Organizations (gh-org)

```bash
# Список організацій
gh-org list
```

#### Projects (gh-project)

```bash
# Список проектів
gh-project list

# Переглянути проект
gh-project view 1

# Створити проект
gh-project create --title "My Project"
```

#### Search (gh-search)

```bash
# Шукати репозиторії
gh-search repos "machine learning"

# Шукати issues
gh-search issues "bug"

# Шукати PR
gh-search prs "feature"

# Шукати код
gh-search code "function main"

# Шукати коміти
gh-search commits "fix bug"
```

#### Secrets (gh-secret)

```bash
# Список secrets
gh-secret list

# Встановити secret
gh-secret set MY_SECRET

# Видалити secret
gh-secret remove MY_SECRET
```

#### Variables (gh-variable)

```bash
# Список variables
gh-variable list

# Встановити variable
gh-variable set MY_VAR --value "my value"

# Отримати variable
gh-variable get MY_VAR

# Видалити variable
gh-variable delete MY_VAR
```

#### Labels (gh-label)

```bash
# Список labels
gh-label list

# Створити label
gh-label create "bug" --color FF0000

# Редагувати label
gh-label edit "bug" --color FF5500

# Видалити label
gh-label delete "bug"
```

### 🔧 Додаткові інструменти

#### API (gh-api)

```bash
# Запит до GitHub API
gh-api user
gh-api repos/owner/repo
gh-api repos/owner/repo/issues
```

#### Alias (gh-alias)

```bash
# Список aliases
gh-alias list

# Створити alias
gh-alias set co "pr checkout"

# Видалити alias
gh-alias delete co
```

#### Config (gh-config)

```bash
# Список налаштувань
gh-config list

# Встановити налаштування
gh-config set editor vim

# Отримати налаштування
gh-config get editor
```

#### Extensions (gh-extension)

```bash
# Список extensions
gh-extension list

# Встановити extension
gh-extension install owner/extension

# Оновити extension
gh-extension upgrade extension

# Видалити extension
gh-extension remove extension

# Шукати extensions
gh-extension search keyword
```

#### GitHub Copilot CLI (gh-copilot)

```bash
# Пояснити команду
gh-copilot explain "git rebase -i HEAD~3"

# Запропонувати команду
gh-copilot suggest "найти все файлы больше 100MB"
```

#### Attestation (gh-attestation)

```bash
# Перевірити attestation
gh-attestation verify artifact.tar.gz

# Завантажити attestation
gh-attestation download artifact.tar.gz
```

#### Completion (gh-completion)

```bash
# Згенерувати completion для PowerShell
gh-completion powershell

# Для bash
gh-completion bash

# Для zsh
gh-completion zsh
```

#### GPG Keys (gh-gpg-key)

```bash
# Список GPG keys
gh-gpg-key list

# Додати GPG key
gh-gpg-key add key.asc

# Видалити GPG key
gh-gpg-key delete KEY_ID
```

#### SSH Keys (gh-ssh-key)

```bash
# Список SSH keys
gh-ssh-key list

# Додати SSH key
gh-ssh-key add key.pub

# Видалити SSH key
gh-ssh-key delete KEY_ID
```

#### Ruleset (gh-ruleset)

```bash
# Список rulesets
gh-ruleset list

# Переглянути ruleset
gh-ruleset view ruleset-name

# Перевірити ruleset
gh-ruleset check
```

#### Preview (gh-preview)

```bash
# Виконати preview функції
gh-preview feature-name
```

#### Agent Task (gh-agent-task)

```bash
# Список завдань агента
gh-agent-task list

# Переглянути завдання
gh-agent-task view task-id

# Створити завдання
gh-agent-task create
```

### 📊 Статус (gh-status)

```bash
# Показати статус GitHub CLI
gh-status

# Статус для організації
gh-status --org my-org

# Виключити репозиторії
gh-status --exclude "archived-*"
```

## 💡 Корисні поради

1. **Використовуйте прапорці `--web`** для відкриття в браузері
2. **Комбінуйте команди** для автоматизації робочих процесів
3. **Перевіряйте статус** перед виконанням операцій: `gh-auth status`
4. **Використовуйте `help`** для деталей: `help gh-pr`

## 🎯 Популярні робочі процеси

### Створення PR з нуля

```bash
# 1. Створити нову гілку і переключитись на неї (через git)
# 2. Зробити зміни і закомітити
# 3. Створити PR
gh-pr create --title "Feature: Add new functionality" --body "Description of changes"
```

### Робота з Issues

```bash
# 1. Створити issue
gh-issue create --title "Bug: Login not working" --body "Steps to reproduce..."

# 2. Розпочати роботу над issue
gh-issue develop 123

# 3. Закрити issue після вирішення
gh-issue close 123
```

### Управління релізами

```bash
# 1. Список релізів
gh-release list

# 2. Створити новий реліз
gh-release create v1.2.0 --title "Version 1.2.0" --notes "Bug fixes and improvements"

# 3. Завантажити бінарники
gh-release upload v1.2.0 app.exe app-linux app-mac
```

---

## ✅ Всього реалізовано: 40+ команд GitHub CLI!

Всі команди працюють через нативний GitHub CLI (`gh`) і надають зручний інтерфейс для роботи з GitHub безпосередньо з вашого IDE.

