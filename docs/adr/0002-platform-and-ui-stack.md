# ADR-0002 — Платформа и UI-стек: .NET 8 + Avalonia, Linux-only

**Статус:** accepted
**Источник:** `skills/wireguardgui-avalonia-ui/`, Stage 1, Stage 7
**Связано:** ADR-0001, ADR-0012

## Контекст

Целевая платформа — Linux: приложение работает с NM/wg-quick, polkit, systemd-resolved, GTK-треем.
Нужны собственная тема, локализация на 7 языков и упаковка в переносимый AppImage.
Визуальный референс — `../TransmissonNET`.

## Решение

.NET 8, Avalonia, MVVM без стороннего фреймворка (свой `ViewModelBase`). Linux — единственная
поддерживаемая платформа, Windows/macOS не целимся. Компоненты оформления переносятся из
TransmissonNET: `AppButton`, тема и кисти (`AppTheme.axaml`, brush keys), тосты.
Тема и локализация — собственные сервисы (`ThemeService`, `UiSettings`), а не библиотеки.

## Альтернативы

- GTK# / Qt — слабее MVVM-модель и XAML-разметка, больше нативного кода.
- WPF — Windows-only, не подходит.
- Готовый UI-кит сторонней команды — лишняя зависимость там, где достаточно своих контролов.

## Последствия

+ Единая XAML-разметка и предсказуемая тема; UI-компоненты уже проверены в TransmissonNET.
− Ловушка пространства имён: внутри `WireguardGui.App.Avalonia` типы Avalonia требуют `global::Avalonia.*`.
− `AssemblyName` — `WireguardGui.App` (на него рассчитывает AppImage `Exec`), AppImage собирается только в CI по тегу.

## Ссылки

- `skills/wireguardgui-avalonia-ui/SKILL.md`
- `plan/archive/steps/1-2-app-button.md`, `1-3-theme-and-brushes.md`, `7-1-appimage-scripts.md`
