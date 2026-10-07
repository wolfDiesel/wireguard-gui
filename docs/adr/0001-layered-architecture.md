# ADR-0001 — Слоистая архитектура с однонаправленными зависимостями

**Статус:** accepted
**Источник:** `skills/wireguardgui-architecture/`, [constitution](../../plan/constitution.md) I1–I5
**Связано:** ADR-0003, ADR-0008, ADR-0010, ADR-0012

## Контекст

Приложение управляет системными компонентами Linux (`nmcli`, `wg-quick`, `ip`, `nft`, D-Bus)
и должно тестироваться без root, без реальной сети и без установленного WireGuard.
При этом есть два разных сетевых бэкенда и растущий набор источников маршрутов.

## Решение

Пять проектов со зависимостями строго внутрь:

```
App.Avalonia → {Application, Infrastructure, Desktop}
Desktop      → Application
Infrastructure → Application
Application  → Domain
```

Domain — сущности и правила без единой внешней зависимости. Порты (`I*`) объявляются
в Application, реализуются в Infrastructure, регистрируются в `DependencyInjection.cs`.
Внешний мир доступен только через порты: `IProcessRunner`, `IWireGuardBackend`,
`IProfileStore`, `ISystemCapabilityProbe`, `IWireGuardConfigRepository`.

## Альтернативы

- Один проект или вертикальные срезы — быстрее старт, но системные вызовы расползаются по UI, тестировать нечем.
- CQRS/MediatR поверх слоёв — лишний слой и зависимость при одном разработчике.

## Последствия

+ handlers и источники маршрутов проверяются на фейковом `IProcessRunner` (шаг 10-22), Application не знает о Linux.
+ Появилось место для правил: бизнес-логика не может «утечь» в ViewModel без нарушения графа.
− DTO и маппинг на границе Application↔UI, DI-композиция в одном файле, нужна дисциплина ревью (PR checklist).

## Ссылки

- `skills/wireguardgui-architecture/SKILL.md`, `reference.md`
- [constitution.md](../../plan/constitution.md) — I1–I5
