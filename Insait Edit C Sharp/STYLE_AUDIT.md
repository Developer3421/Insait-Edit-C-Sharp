# Style Audit — Insait Edit C#

> Дата перевірки: 2026-03-13  
> Базова тема застосунку: FluentTheme + власна темна палітра Orange-Purple  
> **Дата виправлень: 2026-03-13 — всі пункти нижче виправлено**

---

## 1. Вікна та контроли БЕЗ власних стилів (взагалі без `Window.Styles` / `UserControl.Styles`)

### 1.1 Повністю без стилів

| Файл | Проблема | Статус |
|------|----------|--------|
| `ImageViewerWindow.axaml` | Кнопки `win-btn`, `tool-btn` — є hover-стилі в `Window.Styles` | ✅ Вже виправлено |
| `GenerateMemberWindow.axaml` | `type-btn` — є повний стиль в `Window.Styles` | ✅ Вже виправлено |
| `GenerateTypeWindow.axaml` | `type-btn` — є повний стиль в `Window.Styles` | ✅ Вже виправлено |
| `DiagnosticsPanel.axaml` | Немає TextBox-ів; ризик при майбутніх змінах | ⚠️ Не критично |
| `GoToDefinitionWindow.axaml` | Додано `Window.Styles` з ListBox item, hover/selected стилями, кнопкою | ✅ Виправлено |
| `RoslynToolsWindow.axaml` | `ListBox.Styles` перенесено в `Window.Styles` | ✅ Виправлено |
| `RoslynCompletionWindow.axaml` | `ListBox.Styles` і `Button.Styles` перенесено в `Window.Styles` | ✅ Виправлено |
| `RoslynQuickFixWindow.axaml` | `ListBox.Styles` перенесено в `Window.Styles` | ✅ Виправлено |

### 1.2 Мінімальні стилі (лише один-два елементи, решта — дефолт)

| Файл | Наявні стилі | Статус |
|------|-------------|--------|
| `CloneRepositoryWindow.axaml` | Додано `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush`; hover кнопок | ✅ Виправлено |
| `GeminiLanguageNameWindow.axaml` | Додано `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` | ✅ Виправлено |
| `GeminiModelWindow.axaml` | Додано `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` | ✅ Виправлено |
| `AxamlPreviewWindow.axaml` | Лише `win-btn`, `tool-btn`, `err-btn` — TextBox відсутні | ⚠️ TextBox немає |
| `PreviewErrorWindow.axaml` | Лише `win-btn`, `copy-btn` — TextBox відсутні | ⚠️ TextBox немає |

---

## 2. TextBox-и з помилкою білого виділення при курсорі / білого тексту виділення

### 2.1 Файли, де є `TextBox` стиль, але відсутній `SelectionBrush` — **ВСІ ВИПРАВЛЕНО**

| Файл | Виправлені властивості | Статус |
|------|----------------------|--------|
| `WelcomeWindow.axaml` | Додано `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` до `.search-box` | ✅ Виправлено |
| `RecentProjectsWindow.axaml` | Додано `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` до `.search-box` | ✅ Виправлено |
| `AutoFixWindow.axaml` | Додано `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` | ✅ Виправлено |
| `GeminiSettingsWindow.axaml` | Додано `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` | ✅ Виправлено |
| `CompoundRunWindow.axaml` | Додано `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` | ✅ Виправлено |
| `RunConfigurationsWindow.axaml` | Додано `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` | ✅ Виправлено |
| `PublishWindow.axaml` | Додано `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` | ✅ Виправлено |
| `NewSolutionWindow.axaml` | Додано `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` | ✅ Виправлено |
| `NewProjectWindow.axaml` | Додано `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` | ✅ Виправлено |
| `SolutionPropertiesWindow.axaml` | Додано `SelectionBrush`, `SelectionForegroundBrush` (CaretBrush вже є) | ✅ Виправлено |
| `ProjectPropertiesWindow.axaml` | Додано `SelectionBrush`, `SelectionForegroundBrush` (CaretBrush вже є) | ✅ Виправлено |
| `Controls/SettingsPanelControl.axaml` | Додано `SelectionBrush`, `SelectionForegroundBrush` (CaretBrush вже є) | ✅ Виправлено |
| `Controls/AccountPanelControl.axaml` | Додано `SelectionBrush`, `SelectionForegroundBrush` (CaretBrush вже є) | ✅ Виправлено |

### 2.2 Файли, де TextBox стилів не було

| Файл | Статус |
|------|--------|
| `CloneRepositoryWindow.axaml` | ✅ Виправлено — додано TextBox з `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` |
| `GeminiLanguageNameWindow.axaml` | ✅ Виправлено |
| `GeminiModelWindow.axaml` | ✅ Виправлено |
| `Controls/RenameSymbolDialog.axaml` | ✅ Виправлено — додано `Window.Styles` з повним TextBox стилем та hover кнопок |
| `Controls/GitPanelControl.axaml` | ✅ Виправлено — додано TextBox стиль до `UserControl.Styles` |

---

## 3. Зведення: що є "еталонним" (для довідки)

| Файл | Що є |
|------|------|
| `MainWindow.axaml` | `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` у глобальному TextBox стилі |
| `GitWindow.axaml` | `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` у глобальному TextBox стилі |
| `MsixManagerWindow.axaml` | `SelectionBrush`, `CaretBrush` у глобальному TextBox стилі |
| `Controls/NuGetPanelControl.axaml` | `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` у глобальному TextBox стилі |

---

## 4. Колірна схема виправлень

Для всіх `SelectionBrush` використано: `#60FFC09F` (напівпрозорий акцент-помаранч)  
Для `SelectionForegroundBrush`: `#FF1F1A24` (темний фон — текст читабельний)  
Для `CaretBrush`: колір тексту відповідного вікна (`#FFF0E8F4` або прив'язка до ресурсу)



> Дата перевірки: 2026-03-13  
> Базова тема застосунку: FluentTheme + власна темна палітра Orange-Purple

---

## 1. Вікна та контроли БЕЗ власних стилів (взагалі без `Window.Styles` / `UserControl.Styles`)

Ці файли або взагалі не мають блоку стилів, або мають його настільки мінімальним, що більшість контролів відображаються з дефолтним Avalonia FluentTheme (світлі кнопки, світле виділення, тощо — не відповідають загальній темній темі).

### 1.1 Повністю без стилів

| Файл | Проблема |
|------|----------|
| `ImageViewerWindow.axaml` | **Немає `Window.Styles` взагалі.** Кнопки використовують `Classes="win-btn"` та `Classes="tool-btn"`, але цих класів ніде не визначено — рендеряться з FluentTheme (сіра/світла кнопка на темному фоні). |
| `GenerateMemberWindow.axaml` | **Немає `Window.Styles`.** Кнопки використовують `Classes="type-btn"`, клас ніде не визначений — дефолтний FluentTheme. |
| `GenerateTypeWindow.axaml` | **Немає `Window.Styles`.** Те ж саме: `type-btn` → FluentTheme defaults. |
| `DiagnosticsPanel.axaml` | **Немає `UserControl.Styles`.** Кнопки (ToggleErrors, ToggleWarnings, ToggleInfo) стилізовані лише inline через властивості; немає спільного класу чи стилю. |
| `GoToDefinitionWindow.axaml` | **Немає `Window.Styles`.** Є `Window.Resources` (кольори), але стилів немає. `ListBox` (LocationsList) отримує дефолтне FluentTheme виділення (блакитне на темному). Кнопка Close стилізована тільки inline. |
| `RoslynToolsWindow.axaml` | **Немає `Window.Styles`.** `ListBox` (ResultsList) має стилі лише inline всередині елементу. Close кнопка — inline. |
| `RoslynCompletionWindow.axaml` | **Немає глобального `Window.Styles`.** `ListBox.Styles` описані inline всередині самого ListBox. Close кнопка має власний `<Button.Styles>` всередині. |
| `RoslynQuickFixWindow.axaml` | **Немає глобального `Window.Styles`.** `ListBox.Styles` — inline. Немає жодних стилів для кнопок вікна. |

### 1.2 Мінімальні стилі (лише один-два елементи, решта — дефолт)

| Файл | Наявні стилі | Чого не вистачає |
|------|-------------|-----------------|
| `CloneRepositoryWindow.axaml` | Лише `TextBox /template/ Border#PART_BorderElement` (фон) | Стилів для кнопок (Browse, Cancel, Clone) немає — FluentTheme defaults; TextBox: немає SelectionBrush, CaretBrush |
| `GeminiLanguageNameWindow.axaml` | Лише `TextBox /template/ Border#PART_BorderElement` | TextBox без SelectionBrush та CaretBrush; кнопки без ховера |
| `GeminiModelWindow.axaml` | Лише `TextBox /template/ Border#PART_BorderElement` | Те саме |
| `AxamlPreviewWindow.axaml` | Тільки 3 кнопкові класи (`win-btn`, `tool-btn`, `err-btn`) | Немає TextBox стилів взагалі |
| `PreviewErrorWindow.axaml` | Лише `win-btn`, `copy-btn` | Немає TextBox стилів |

---

## 2. TextBox-и з помилкою білого виділення при курсорі / білого тексту виділення

Ця помилка виникає коли `TextBox` має темний фон (custom Background), але **не перевизначає `SelectionBrush`** — FluentTheme залишає дефолтний блакитний/білий колір виділення, при якому текст стає нечитабельним (білий текст на білому виділенні) або виділення майже невидиме.

Також: якщо `CaretBrush` не встановлено, курсор (caret) може злитися з фоном.

### 2.1 Файли, де є `TextBox` стиль, але відсутній `SelectionBrush`

| Файл | Відсутні властивості | Зачеплені TextBox |
|------|---------------------|-------------------|
| `WelcomeWindow.axaml` | `SelectionBrush`, `CaretBrush` | `SearchBox` (клас `search-box`) |
| `RecentProjectsWindow.axaml` | `SelectionBrush`, `CaretBrush` | `SearchBox` (клас `search-box`) |
| `AutoFixWindow.axaml` | `SelectionBrush`, `CaretBrush` | `DiagSearchBox`, `TemplateSearchBox`, `KeywordSearchBox` |
| `GeminiSettingsWindow.axaml` | `SelectionBrush`, `CaretBrush` | Глобальний TextBox стиль — всі поля вікна |
| `CompoundRunWindow.axaml` | `SelectionBrush` | Глобальний TextBox стиль — всі поля вікна |
| `RunConfigurationsWindow.axaml` | `SelectionBrush` | Глобальний TextBox стиль — всі поля вікна |
| `PublishWindow.axaml` | `SelectionBrush` | Глобальний TextBox стиль — всі поля вікна |
| `NewSolutionWindow.axaml` | `SelectionBrush` | Глобальний TextBox стиль — всі поля вікна |
| `NewProjectWindow.axaml` | `SelectionBrush` | Глобальний TextBox стиль — всі поля вікна |
| `SolutionPropertiesWindow.axaml` | `SelectionBrush` (CaretBrush є) | Всі TextBox у `SolutionGeneralPage`: `NameBox`, `PathBox`, `FormatBox`, `VsVersionBox`, `VsMinVersionBox` |
| `ProjectPropertiesWindow.axaml` | `SelectionBrush` (CaretBrush є) | Всі TextBox у підсторінках: GeneralPage (`AssemblyNameBox`, `DefaultNamespaceBox`, `AppIconPathBox`), BuildPage (`NoWarnBox`, `OutputPathBox`, `IntermediateOutputPathBox`), PackagePage (9 полів), SigningPage (`KeyFileBox`), DebugPage (`DebugArgsBox`, `DebugWorkingDirBox`) |
| `SettingsPanelControl.axaml` | `SelectionBrush` (CaretBrush є) | Глобальний TextBox стиль |
| `AccountPanelControl.axaml` | `SelectionBrush` (CaretBrush є) | Глобальний TextBox стиль |

### 2.2 Файли, де TextBox стилів взагалі немає, а TextBox-и присутні inline

| Файл | TextBox елементи | Відсутні властивості |
|------|-----------------|---------------------|
| `CloneRepositoryWindow.axaml` | `RepoUrlBox`, `LocalPathBox` | `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` |
| `GeminiLanguageNameWindow.axaml` | `LanguageNameBox` | `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` |
| `GeminiModelWindow.axaml` | `ModelBox` | `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` |
| `RenameSymbolDialog.axaml` | `NewNameInput` | `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` |
| `Controls/GitPanelControl.axaml` | `CommitMessageBox`, `LogFilterBox` | `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` — панель не має TextBox стилю в `UserControl.Styles` |

---

## 3. Зведення: що є "еталонним" (для довідки)

Файли, де TextBox правильно налаштований (для порівняння):

| Файл | Що є |
|------|------|
| `MainWindow.axaml` | `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` у глобальному TextBox стилі |
| `GitWindow.axaml` | `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` у глобальному TextBox стилі |
| `MsixManagerWindow.axaml` | `SelectionBrush`, `CaretBrush` у глобальному TextBox стилі |
| `Controls/NuGetPanelControl.axaml` | `SelectionBrush`, `SelectionForegroundBrush`, `CaretBrush` у глобальному TextBox стилі |

---

## 4. Пріоритет виправлень (рекомендація)

### 🔴 Критично (видимий дефект — FluentTheme контролі на темному фоні)
1. `ImageViewerWindow.axaml` — додати стилі `win-btn`, `tool-btn`
2. `GenerateMemberWindow.axaml` — додати стиль `type-btn`
3. `GenerateTypeWindow.axaml` — додати стиль `type-btn`
4. `GoToDefinitionWindow.axaml` — додати `Window.Styles` (ListBox items, кнопки)
5. `RoslynToolsWindow.axaml` — перенести стилі в `Window.Styles`
6. `RoslynCompletionWindow.axaml` — перенести стилі в `Window.Styles`
7. `RoslynQuickFixWindow.axaml` — перенести стилі в `Window.Styles`

### 🟠 Важливо (білий caret / виділення)
8. `CloneRepositoryWindow.axaml` — додати `SelectionBrush`, `CaretBrush` до TextBox
9. `GeminiLanguageNameWindow.axaml` — те саме
10. `GeminiModelWindow.axaml` — те саме
11. `RenameSymbolDialog.axaml` — додати TextBox стиль з Selection/Caret
12. `Controls/GitPanelControl.axaml` — додати TextBox стиль до `UserControl.Styles`

### 🟡 Середній (TextBox стиль є, але не повний)
13. `WelcomeWindow.axaml` — додати `SelectionBrush`, `CaretBrush` до `.search-box`
14. `RecentProjectsWindow.axaml` — те саме
15. `AutoFixWindow.axaml` — додати `SelectionBrush`, `CaretBrush`
16. `GeminiSettingsWindow.axaml` — додати `SelectionBrush`, `CaretBrush`
17. `CompoundRunWindow.axaml` — додати `SelectionBrush`
18. `RunConfigurationsWindow.axaml` — додати `SelectionBrush`
19. `PublishWindow.axaml` — додати `SelectionBrush`
20. `NewSolutionWindow.axaml` — додати `SelectionBrush`
21. `NewProjectWindow.axaml` — додати `SelectionBrush`
22. `SolutionPropertiesWindow.axaml` — додати `SelectionBrush`
23. `ProjectPropertiesWindow.axaml` — додати `SelectionBrush`
24. `SettingsPanelControl.axaml` — додати `SelectionBrush`
25. `AccountPanelControl.axaml` — додати `SelectionBrush`

---

*Примітка: `DiagnosticsPanel.axaml` не має TextBox-ів — лише кнопки та списки. Але відсутній `UserControl.Styles` блок все одно є ризиком при майбутньому додаванні контролів.*

