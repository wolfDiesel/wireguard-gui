# ADR — архитектурные решения wireguard-gui

Записи о принятых решениях: что решили, почему, чем за это платим и какие альтернативы отвергли.
ADR фиксируют то, что уже реализовано в коде и скиллах, — это не предложения, а констатация.

**Формат:** `Статус` → `Контекст` → `Решение` → `Альтернативы` → `Последствия` → `Ссылки`.
**Статусы:** `accepted` (действует), `superseded by ADR-XXXX` (заменено), `deprecated` (устарело).

**Правило:** решение меняется новым ADR, а не правкой принятого. Ссылки из ADR — на скиллы
и инварианты [constitution.md](../../plan/constitution.md), а не на текст шагов.

| id | Решение | Источник |
|----|---------|----------|
| [ADR-0001](0001-layered-architecture.md) | Слоистая архитектура с однонаправленными зависимостями | I1–I5 |
| [ADR-0002](0002-platform-and-ui-stack.md) | .NET 8 + Avalonia, Linux-only, свой MVVM | skills/avalonia-ui |
| [ADR-0003](0003-two-wireguard-backends.md) | Два бэкенда WireGuard за одним портом | skills/wireguard |
| [ADR-0004](0004-privileged-session.md) | Долгоживущая pkexec-сессия и единый `IProcessRunner` | I6 |
| [ADR-0005](0005-policy-split-routing.md) | Split routing через policy routing (`Table=off` + nft + ip rule) | I10, skills/split-routing |
| [ADR-0006](0006-route-source-priority.md) | Источники маршрутов с приоритетом и общим лимитом | I9 |
| [ADR-0007](0007-dns-interception.md) | Перехват DNS через D-Bus-монитор, прокси — резерв | skills/split-routing |
| [ADR-0008](0008-config-repository.md) | Единственная точка доступа к `wireguard.conf` | I8 |
| [ADR-0009](0009-data-root.md) | Единый корень данных, секреты только в нём | I7 |
| [ADR-0010](0010-operation-results.md) | Результаты операций — DTO с кодом ошибки | Q2, `ConnectionOutcomeResolver` |
| [ADR-0011](0011-plan-driven-process.md) | Процесс: plan-driven со спеками, скиллами и DoD | P1–P7, R1–R3 |
| [ADR-0012](0012-desktop-layer.md) | Системная интеграция вынесена в проект Desktop | I5, Stage 6 |
