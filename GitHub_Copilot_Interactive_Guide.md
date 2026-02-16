# 🤖 GitHub Copilot CLI - Посібник

## 📋 Огляд

GitHub Copilot CLI - це розширення для GitHub CLI, яке дозволяє отримувати пояснення команд та генерувати команди за допомогою AI безпосередньо в терміналі.

**Важливо:** GitHub Copilot CLI працює тільки з командами `explain`, `suggest` та `config`. Окремого інтерактивного режиму без аргументів не існує - при виклику без аргументів показується довідка.

---

## 🚀 Швидкий старт

### Використання GitHub Copilot CLI

```bash
# Показати довідку по командах
gh-copilot

# Отримати пояснення команди
gh-copilot explain "git rebase"

# Згенерувати команду для задачі  
gh-copilot suggest "install docker"

# Налаштування
gh-copilot config get model
```

---

## 📖 Детальний опис

### 1. Команда gh-copilot (довідка)

**Команда:**
```bash
gh-copilot
```

**Що робить:**
- Показує довідку з доступними командами GitHub Copilot CLI
- Виводить приклади використання
- Інформує про інші пов'язані команди (gh-t, gh-m)

**Примітка:** GitHub Copilot CLI працює тільки з підкомандами `explain`, `suggest` та `config`. Самостійного інтерактивного режиму не існує.

---

### 2. Команда explain (пояснення)

**Синтаксис:**
```bash
gh-copilot explain "<команда або концепція>"
```

**Приклади:**
```bash
gh-copilot explain "git rebase -i HEAD~3"
gh-copilot explain "docker compose up -d"
gh-copilot explain "kubectl get pods"
gh-copilot explain "what is a pull request"
```

**Результат:**
- Детальне пояснення команди
- Опис кожного параметра
- Потенційні ризики
- Альтернативи

---

### 3. Команда suggest (генерація команд)

**Синтаксис:**
```bash
gh-copilot suggest "<опис що ви хочете зробити>"
```

**Приклади:**
```bash
gh-copilot suggest "install node.js"
gh-copilot suggest "create a new git branch"
gh-copilot suggest "find all large files"
gh-copilot suggest "compress all images in folder"
```

**Результат:**
- Запропонована команда
- Пояснення що вона робить
- Опції для виконання або модифікації

---

### 4. Команда config (конфігурація)

**Синтаксис:**
```bash
gh-copilot config <get|set> <parameter> [value]
```

**Приклади:**
```bash
gh-copilot config get model
gh-copilot config set model gpt-4
gh-copilot config get alias
```

---

## 🎯 Нові агентні команди

### gh-t - Агентний режим з контекстом проекту

**Синтаксис:**
```bash
gh-t "<опис задачі>"
```

**Приклади:**
```bash
gh-t "Add authentication to the API"
gh-t "Fix the bug in UserService"
gh-t "Create unit tests for PaymentController"
gh-t "Refactor the database access layer"
```

**Що робить:**
- 📂 Автоматично збирає контекст проекту
- 🔍 Сканує файли (.cs, .csproj, .axaml, .json, .md)
- 🤖 Передає контекст у Copilot
- ⚡ Виконує задачу без запиту підтвердження
- ✅ Застосовує зміни автоматично

**Особливості:**
- Обмеження: до 50 файлів для контексту
- Ігнорує: bin/, obj/, build/ директорії
- Показує: структуру проекту та список файлів

---

### gh-m - Зміна моделі Copilot

**Синтаксис:**
```bash
gh-m [назва моделі]
```

**Без аргументів - показує поточну модель:**
```bash
gh-m
```

**Зміна моделі:**
```bash
gh-m gpt-4
gh-m gpt-4-32k
gh-m gpt-3.5-turbo
gh-m claude-3-opus
gh-m claude-3-sonnet
gh-m claude-3-haiku
```

**Доступні моделі:**
- `gpt-4` - GPT-4 (найкраща якість)
- `gpt-4-32k` - GPT-4 32K (великий контекст)
- `gpt-3.5-turbo` - GPT-3.5 Turbo (швидка)
- `claude-3-opus` - Claude 3 Opus (потужна)
- `claude-3-sonnet` - Claude 3 Sonnet (збалансована)
- `claude-3-haiku` - Claude 3 Haiku (швидка)

---

## 🔧 Встановлення та налаштування

### Крок 1: Встановлення GitHub CLI

```bash
gh-install
```

Або вручну:
```powershell
winget install GitHub.cli
```

### Крок 2: Автентифікація

```bash
gh-auth login
```

Слідуйте інструкціям на екрані для входу через браузер.

### Крок 3: Встановлення розширення Copilot

```bash
gh-extension install github/gh-copilot
```

### Крок 4: Перевірка

```bash
gh-copilot
```

Якщо все налаштовано правильно - відкриється інтерактивна оболонка!

---

## 💡 Поради та найкращі практики

### 1. Використання інтерактивного режиму

✅ **Добре:**
- Задавайте чіткі питання
- Використовуйте контекст ("в git", "для Docker")
- Перевіряйте згенеровані команди перед виконанням

❌ **Погано:**
- Розпливчаті питання без контексту
- Виконання команд не розуміючи що вони роблять

### 2. Режим explain

```bash
# Добре - конкретна команда
gh-copilot explain "git cherry-pick abc123"

# Погано - занадто загально
gh-copilot explain "git"
```

### 3. Режим suggest

```bash
# Добре - чітке завдання
gh-copilot suggest "create a backup of database"

# Погано - незрозуміло
gh-copilot suggest "do something with files"
```

### 4. Агентний режим gh-t

```bash
# Добре - конкретна задача
gh-t "Add input validation to LoginForm"

# Добре - чітка мета
gh-t "Implement caching for API responses"

# Погано - занадто широко
gh-t "Fix everything"
```

---

## 🚨 Усунення проблем

### Проблема: "GitHub CLI (gh) not found"

**Рішення:**
```bash
gh-install
```

### Проблема: "Copilot extension is not installed"

**Рішення:**
```bash
gh-extension install github/gh-copilot
```

### Проблема: "Authentication required"

**Рішення:**
```bash
gh-auth login
```

### Проблема: Команда gh copilot показує тільки help

**Це нормальна поведінка!** GitHub Copilot CLI не має інтерактивного режиму.  
Ви повинні використовувати підкоманди:

```bash
# Пояснення команди
gh-copilot explain "ваша команда"

# Генерація команди  
gh-copilot suggest "опис задачі"
```

### Проблема: Помилки при роботі

**Рішення:**
```bash
# Перевірте версію
gh --version

# Оновіть gh CLI
winget upgrade GitHub.cli

# Перевстановіть розширення
gh extension remove gh-copilot
gh extension install github/gh-copilot

# Повторно авторизуйтесь
gh auth login
```

---

## 📊 Порівняння режимів

| Режим | Коли використовувати | Переваги | Недоліки |
|-------|---------------------|----------|----------|
| **Help** (`gh-copilot`) | Швидка довідка | Показує всі команди | Тільки інформація |
| **Explain** (`gh-copilot explain`) | Розуміння команд | Детальні пояснення | Тільки пояснення, не виконання |
| **Suggest** (`gh-copilot suggest`) | Генерація команд | Точні команди | Треба вибирати та виконувати |
| **Агентний** (`gh-t`) | Автоматизація задач | Автоматичне виконання, контекст проекту | Може змінити код без підтвердження |
| **Model** (`gh-m`) | Налаштування AI | Вибір кращої моделі | Деякі моделі можуть бути недоступні |

---

## 🎓 Приклади використання

### Приклад 1: Вивчення нової команди

```bash
# Показати довідку
gh-copilot

# Отримати пояснення команди
gh-copilot explain "git rebase"
gh-copilot explain "git cherry-pick abc123"
```

### Приклад 2: Генерація команди для завдання

```bash
# Знайти файли більші 100MB
gh-copilot suggest "find files larger than 100MB"

# Створити бекап бази даних
gh-copilot suggest "create a backup of database"
```

### Приклад 3: Автоматична реалізація функції

```bash
# Агентний режим з контекстом проекту
gh-t "Add JWT token validation middleware"

# Copilot збере контекст, проаналізує проект та додасть функціональність
```

### Приклад 4: Зміна моделі для складної задачі

```bash
# Перевіряємо поточну модель
gh-m

# Змінюємо на потужнішу для складної задачі
gh-m gpt-4

# Виконуємо складну задачу
gh-t "Implement comprehensive error handling across all controllers"
```

---

## 🔗 Додаткові ресурси

### Документація
- [GitHub Copilot CLI Documentation](https://docs.github.com/en/copilot/github-copilot-in-the-cli)
- [GitHub CLI Manual](https://cli.github.com/manual/)

### Команди для довідки
```bash
help              # Всі доступні команди
help gh-copilot   # Довідка по gh-copilot
help gh-t         # Довідка по агентному режиму
help gh-m         # Довідка по зміні моделі
gh-status         # Статус GitHub CLI
```

---

## ✨ Висновок

GitHub Copilot Interactive Mode - це потужний інструмент для:
- 🚀 Швидкого отримання допомоги
- 💡 Вивчення нових команд та концепцій
- ⚡ Автоматизації рутинних задач
- 🎯 Генерації точних команд для ваших потреб

**Почніть прямо зараз:**
```bash
gh-copilot
```

І нехай AI асистент допоможе вам з будь-якими питаннями! 🎉

