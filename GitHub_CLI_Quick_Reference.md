# GitHub CLI - Швидкий довідник 🚀

## ⚡ Найпопулярніші команди

### 🔐 Автентифікація
```bash
gh-install          # Встановити GitHub CLI
gh-auth login       # Увійти в GitHub
gh-auth status      # Перевірити статус
```

### 📂 Репозиторії
```bash
gh-repo create my-app --private    # Створити приватний репозиторій
gh-repo clone owner/repo           # Клонувати репозиторій
gh-repo view --web                 # Відкрити в браузері
gh-repo list                       # Мої репозиторії
```

### 🔀 Pull Requests
```bash
gh-pr create --title "Fix" --body "Description"   # Створити PR
gh-pr list                                        # Список PR
gh-pr view 42                                     # Переглянути PR #42
gh-pr checkout 42  або  gh-co 42                  # Checkout PR
gh-pr merge 42                                    # Злити PR
```

### 🐛 Issues
```bash
gh-issue create --title "Bug" --body "Details"   # Створити issue
gh-issue list --state open                       # Відкриті issues
gh-issue view 123                                # Переглянути issue #123
gh-issue close 123                               # Закрити issue
```

### ⚙️ GitHub Actions
```bash
gh-workflow list                    # Список workflows
gh-workflow run build.yml           # Запустити workflow
gh-run list                         # Список запусків
gh-cache list                       # Список кешів
```

### 🚀 Releases
```bash
gh-release list                                    # Список релізів
gh-release create v1.0.0 --title "Release 1.0"    # Створити реліз
gh-release upload v1.0.0 app.exe                  # Завантажити файл
```

### 🔍 Пошук
```bash
gh-search repos "react typescript"     # Шукати репозиторії
gh-search issues "bug"                 # Шукати issues
gh-search prs "feature"                # Шукати PR
gh-search code "function main"         # Шукати код
```

### 🌐 Інші корисні команди
```bash
gh-browse                # Відкрити репозиторій в браузері
gh-gist list             # Мої gists
gh-org list              # Мої організації
gh-status                # Статус GitHub CLI
```

### 🔧 API і конфігурація
```bash
gh-api user                        # API запит
gh-config list                     # Налаштування
gh-extension list                  # Розширення
gh-alias set co "pr checkout"      # Створити alias
```

### 🔐 Секрети і змінні
```bash
gh-secret list                     # Список секретів
gh-secret set MY_SECRET            # Встановити секрет
gh-variable list                   # Список змінних
gh-variable set VAR --value "123"  # Встановити змінну
```

## 📖 Детальна документація

Дивіться повну документацію в `GitHub_CLI_Commands_Examples.md`

## 💡 Підказки

- Використовуйте `--web` або `-w` для відкриття в браузері
- Команда `help <command>` показує детальну інформацію
- Всі команди підтримують автодоповнення
- 40+ команд GitHub CLI доступні!

## 🎯 Типові сценарії

**Швидке створення PR:**
```bash
gh-pr create --title "Feature: Add login" --body "Implements user authentication"
```

**Перегляд та merge PR:**
```bash
gh-pr list
gh-pr view 42
gh-pr checkout 42
# ... тестування ...
gh-pr merge 42 --squash
```

**Робота з issues:**
```bash
gh-issue create --title "Bug: Crash on startup" --body "Steps to reproduce..."
gh-issue list --state open
gh-issue close 123
```

**Запуск CI/CD:**
```bash
gh-workflow list
gh-workflow run deploy.yml
gh-run list
```

---

**Статус реалізації:** ✅ Всі команди GitHub CLI повністю реалізовані!

