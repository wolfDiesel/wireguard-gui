# ADR-0005 — Split routing через policy routing

**Статус:** accepted
**Источник:** `skills/wireguardgui-split-routing/`, [constitution](../../plan/constitution.md) I10, Stage 13
**Связано:** ADR-0003, ADR-0004, ADR-0006, ADR-0007

## Контекст

Через VPN должны идти только адреса выбранных сервисов, а списки (префиксы Google, Twitch CDN)
меняются постоянно. Подход «через AllowedIPs» требует перезаписи конфига и переподключения
на каждом обновлении маршрутов, то есть разрыва активных соединений.
Эталон поведения — скрипт `/usr/local/bin/update-routes-with-domains1.sh`.

## Решение

Основной режим — policy routing:

- в конфиг пишется `[Interface] Table=off` и `[Peer] AllowedIPs=0.0.0.0/0`;
- main-таблица не трогается, дефолт остаётся прямым;
- нужные CIDR отправляются в отдельную таблицу через `ip rule to <CIDR> lookup <table>`;
- наборы адресов — nft sets с fwmark, синхронизируются на refresh — **без reconnect**;
- подключение: `PolicyRoutingSetup.ApplyAsync`, отключение: `TeardownAsync`, apply/refresh: `SyncRoutesAsync`.

Legacy-режим (`AllowedIPs` в конфиге + reconnect при изменении) остаётся fallback там,
где нет `ip`.

## Альтернативы

- Только legacy AllowedIPs — просто, но каждый refresh рвёт соединение и упирается в размер списка.
- Отдельный демон маршрутизации — ещё один процесс и слой привилегий.
- `ipset` вместо `nft` — часть систем без nft, но выбранный путь уже реализован.

## Последствия

+ Обновление маршрутов без разрыва; прямой трафик не деградирует (main default не переписан).
− Сложная привилегированная механика: `ip rule`, fwmark, nft sets — тяжелее диагностировать.
− После сбоев остаются nft/fwmark-артефакты (есть очистка на apply/sync/teardown), режим зависит от наличия `ip` и `nft`.

## Ссылки

- `skills/wireguardgui-split-routing/SKILL.md` (разделы Pipeline, Policy routing)
- `plan/archive/steps/13-1-policy-setup.md`, `13-2-config-baseline.md`, `14-5-policy-routing-split.md`
