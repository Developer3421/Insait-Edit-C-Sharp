# Insait Style — Шпаргалка кольорів і стилів
# Тема: Fluent Orange-Purple Dark (Avalonia UI)

## Кольори (hex, ARGB)

| Назва                   | Hex          | Призначення                             |
|-------------------------|--------------|-----------------------------------------|
| AppBgColor              | #FF1F1A24    | Головний фон вікна (майже чорний, з фіолетовим відтінком) |
| AppSurfaceColor         | #FF2A2230    | Фон панелей, карток, сайдбару           |
| AppBorderColor          | #FF3E3050    | Рамки, роздільники, бордери             |
| AppTextColor            | #FFF0E8F4    | Основний текст (ледь лавандовий білий)  |
| AppTextMutedColor       | #FF9E90B0    | Приглушений / підписи / placeholders    |
| TitleBarColor           | #FF2D2438    | Рядок заголовка вікна                   |
| AccentOrangeColor       | #FFFFC09F    | PRIMARY accent — OK-кнопки, активні вкладки, підписи полів |
| AccentOrangeHoverColor  | #FFFFD4B8    | Hover стан помаранчевого                |
| AccentPurpleColor       | #FFDCC4FF    | SECONDARY accent — AI, tools, info      |
| AccentPurpleHoverColor  | #FFECD8FF    | Hover стан фіолетового                  |
| AccentGreenColor        | #FFA6E3A1    | Успіх, запуск, OK-статус                |
| AccentBlueColor         | #FF89B4FA    | Інформація, посилання                   |
| AccentRedColor          | #FFF38BA8    | Помилки, видалення, попередження        |

## Напівпрозорі шари (для hover/active)
- Orange tint hover:  #20FFDAB0  (12% opacity)
- Orange tint active: #30FFC09F  (19% opacity)
- Purple tint hover:  #25DCC4FF  (15% opacity)
- Purple tint active: #40DCC4FF  (25% opacity)
- Red tint hover:     #25F38BA8  (15% opacity)

## Градієнт сайдбару (вертикальний)
- Start: #FFFF8830 (оранж)
- End:   #FFFF6A10 (темний оранж)

## Зовнішня тінь вікна
DropShadowEffect Color="#CC3E1060" BlurRadius="28" OffsetX="0" OffsetY="8" Opacity="0.75"

## Рамка вікна
BorderBrush="#FF7C3AED"  (фіолетовий)  BorderThickness="1"  CornerRadius="10"

## Шрифти
- UI:      Segoe UI, Arial, Tahoma
- Emojis:  Segoe UI Emoji
- Code:    Cascadia Code, Consolas, monospace

## CSS-класи (Button)
| Клас             | Опис                                         |
|------------------|----------------------------------------------|
| .window-control  | Закрити / мінімізувати / розгорнути          |
| .window-control.close | Кнопка ✕ з червоним hover              |
| .primary-btn     | Помаранчева кнопка OK/Save                   |
| .secondary-btn   | Прозора кнопка Cancel з рамкою               |
| .danger-btn      | Червона кнопка Delete                        |
| .icon-btn        | Квадратна іконична кнопка 32×32              |

## Селектори Avalonia (TextBox)
Фон TextBox: #FFF7F2EC  (кремовий) — навмисно СВІТЛИЙ на темному фоні!
Фокус: BorderBrush = AccentOrangeBrush
Курсор: CaretBrush = #FF6C2FA0 (фіолетовий)
Виділення: SelectionBrush = #66E9C9B8

## Структура маленького вікна
Window (SystemDecorations="None", Background="Transparent")
 └─ Border (рамка + тінь + CornerRadius="10")
     └─ Grid (RowDefinitions="36,*,Auto")
         ├─ Border (TitleBar, CornerRadius="10,10,0,0")  ← 36px
         │   └─ Grid [іконка+назва | draggable area | кнопки керування]
         ├─ ScrollViewer (основний вміст)
         └─ Border (footer з кнопками)
             └─ Grid [допом. кнопка | * | secondary-btn | primary-btn]

## Файл шаблону
StyleTemplates/InsaitStyle.WindowTemplate.axaml

