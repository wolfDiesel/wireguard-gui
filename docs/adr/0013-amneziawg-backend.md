# ADR-0013 — AmneziaWG как третий бэкенд

**Статус:** accepted
**Источник:** `skills/wireguardgui-wireguard/`, `skills/wireguardgui-architecture/`
**Связано:** ADR-0003 (заменён), ADR-0004, ADR-0005

## Контекст

AmneziaWG (AWG) — форк WireGuard с обфускацией трафика. Конфиг остаётся WireGuard-конфигом
(`[Interface]` + `[Peer]`), но в `[Interface]` появляются ключи обфускации: `Jc`, `Jmin`, `Jmax`,
`S1`–`S4`, `H1`–`H4`, `I1`–`I5`, `HeaderProtectionKey`, `ContentPaddingAddition`. Клиент Amnezia
экспортирует такие туннели обычным `.conf`.

Особенности, которые нельзя закрыть существующими бэкендами:

- свои утилиты `awg`/`awg-quick` (форк `wireguard-tools`) и собственный тип интерфейса `amneziawg`;
- NetworkManager создаёт `type wireguard`, поэтому `nmcli` для AWG непригоден;
- `wg show` не видит AWG-интерфейсы — код split routing, читавший только `wg`, слепнет на AWG
  (endpoint-гард теряет защиту от петли, проба живости отдаёт `Unknown` и снимает split-DNS);
- транспорт — либо модуль ядра `amneziawg`, либо userspace `amneziawg-go`; `awg-quick` сам добавляет
  нужный тип интерфейса и откатывается на userspace, если модуля нет;
- `awg-quick` требует, чтобы имя файла конфига было валидным именем интерфейса
  (`^[a-zA-Z0-9_=+.-]{1,15}$`), и передаёт неизвестные ключи в `awg setconf` — строка `Name =`
  должна сниматься при импорте.

## Решение

- `BackendKind.AmneziaWg` — третье значение рядом с `Native` и `Nmcli`.
- `AmneziaWgBackend` — подкласс `QuickWireGuardBackend` (`awg-quick up/down`, статус `awg show`);
  `NativeWireGuardBackend` — второй подкласс той же базы с `wg-quick`/`wg`.
- Импорт: `IWireGuardConfigParser.IsAmneziaWg` детектит AWG по ключам обфускации; импорт AWG-конфига
  в `Native`/`Nmcli` отклоняется; `ConnectionName` нормализуется под лимит 15 символов и
  уникализируется по существующим профилям; `Name =` снимается.
- Проба (`ISystemCapabilityProbe.ProbeAmneziaWgAsync`): доступно, если есть `awg` и `awg-quick` **и**
  (`/sys/module/amneziawg` **или** `amneziawg-go` в `PATH`).
- Split routing: `TunnelToolset.For(backend)` — единственное место выбора инструмента чтения
  (`awg` для AWG, `wg` для остальных); его используют `EndpointRouteGuard` и `TunnelHealthProbe`.
- UI: третья радио-кнопка импорта; тип конфига определяется до диалога (`ConfigKind`, `IConfigKindProbe`),
  AWG-конфиг по умолчанию предлагает AmneziaWG; без установленных tools импорт блокируется
  с подсказками установки.

## Альтернативы

- **AWG внутри `Native`** с авто-выбором бинаря по содержимому конфига: скрывает бэкенд от пробы и UI,
  ломает правило «профиль импортирован в конкретный бэкенд», делает подсказки установки неоднозначными.
- **Только userspace `amneziawg-go`**: не требует DKMS, но добавляет ещё одну зависимость и отнимает
  выбор; `awg-quick` и так умеет fallback на userspace.
- **Не поддерживать AWG**: пользователи Amnezia не смогут импортировать свои туннели.
- **Остальные протоколы Amnezia (Xray/VLESS-REALITY, OpenVPN+Cloak, Shadowsocks)** в этой же задаче:
  это другой клиент — свои бинари, генерация конфигов, серверные контейнеры, роутинг — а не бэкенд
  WireGuard-клиента. Вынесено за рамки стадии.

## Последствия

+ Импорт AWG-конфигов без конвертации: ключи обфускации сохраняются, правится только `AllowedIPs`/`Table`/`DNS`.
+ Split routing (policy и legacy), DNS-перехват и endpoint-гард работают для AWG без изменения парсера.
+ Проба и UI объясняют, чего не хватает и что поставить (COPR `amneziavpn/amneziawg`, PPA `amnezia/ppa`).
− Третий бэкенд: ещё одна ветка в UI и свой набор тестов.
− AWG ставится из пакетов вне стандартных репозиториев; модуль ядра может не собраться под свежее
  ядро — тогда нужен `amneziawg-go`.
− Набор интерфейсов для защиты от петли собирается только инструментом своего бэкенда: при двух
  одновременно поднятых туннелях разных бэкендов чужой интерфейс не учитывается.

## Ссылки

- `skills/wireguardgui-wireguard/SKILL.md`, `skills/wireguardgui-architecture/SKILL.md`
- `plan/archive/steps/18-1-amneziawg-detection.md` … `plan/archive/steps/18-4-tunnel-tools.md`
- [constitution.md](../../plan/constitution.md) — I1–I10, Q3, Q4
