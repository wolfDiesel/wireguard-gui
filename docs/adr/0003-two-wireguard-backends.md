# ADR-0003 — Два бэкенда WireGuard за одним портом

**Статус:** accepted
**Источник:** `skills/wireguardgui-wireguard/`, `skills/wireguardgui-architecture/reference.md`
**Связано:** ADR-0001, ADR-0004

## Контекст

NetworkManager есть не на каждом дистрибутиве, а часть пользователей держит туннели напрямую
через `wg-quick`. Бэкенды по-разному импортируют конфиг, поднимают интерфейс и отдают статус.

## Решение

Порт `IWireGuardBackend` с двумя реализациями:

| Backend | Импорт | Connect/Disconnect | Статус |
|---------|--------|--------------------|--------|
| Nmcli | `nmcli connection import type wireguard file <path>` | `nmcli connection up/down` | `nmcli connection show` |
| Native | только копия конфига в хранилище | `wg-quick up/down <abs-path>` | `wg show` |

`BackendKind` выбирается при импорте и сохраняется в профиле — дальше handlers получают
бэкенд через фабрику по этому полю. `ISystemCapabilityProbe` сообщает доступность
(`wg` + `wg-quick` в PATH, наличие модуля) и что доустановить.

## Альтернативы

- Только nmcli — отсекает системы без NM.
- Только wg-quick — теряем интеграцию с NM (DNS, автозапуск, `ipv4.routes`).
- Определять бэкенд в рантайме каждый раз — профиль уже импортирован в конкретный бэкенд, менять его на ходу нельзя.

## Последствия

+ Работает на обеих конфигурациях, пользователь видит, чего не хватает, и как это поставить.
− Двойная реализация и двойные тесты; статус собирается из разных источников; часть ветвлений по `BackendKind` в split routing.

## Ссылки

- `skills/wireguardgui-wireguard/SKILL.md`
- `plan/archive/steps/4-1-backend-abstraction.md`, `4-2-nmcli-backend.md`, `4-3-native-backend.md`
