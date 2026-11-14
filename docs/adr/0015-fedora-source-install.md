# ADR-0015 — Fedora: сборка AWG из исходников вместо COPR

**Статус:** accepted
**Источник:** `skills/wireguardgui-wireguard/`
**Связано:** ADR-0014 (уточняет), ADR-0013

## Контекст

План установки для «Fedora» в ADR-0014 повторял upstream-инструкцию
(`dnf copr enable amneziavpn/amneziawg` + `amneziawg-dkms amneziawg-tools`), но эта инструкция
написана под RHEL/EPEL. В проекте COPR `amneziavpn/amneziawg` собираются только chroots
`epel-*`, `rhel-*`, `centos-stream*` и `amazonlinux`; chroot `fedora-*` отсутствует.

На Fedora 44 проверка это подтвердила: `dnf copr enable` печатает «Chroot не найден в данном проекте
Copr (fedora-44-x86_64)», а `dnf install amneziawg-dkms amneziawg-tools` завершается
«Нет соответствия для аргумента». Пакетов `amneziawg*` в репозиториях Fedora тоже нет.

## Решение

- `DistroFamily` разделён: `Fedora` (`ID=fedora`) и `Rhel` (`rhel`, `centos`, `rocky`, `almalinux`, `ol`).
  `ID_LIKE="fedora"` у RHEL не перебивает `ID` — сначала смотрится `ID`.
- **RHEL/CentOS**: прежний план — `dnf install dnf-plugins-core`, `epel-release` (best-effort),
  `dnf copr enable amneziavpn/amneziawg`, `dnf install amneziawg-dkms amneziawg-tools`.
- **Fedora**: план «из исходников» — `dnf install git make gcc golang`, клон `amneziawg-tools` и
  `amneziawg-go` в `/tmp/amneziawg-build`, `make && make install`, `amneziawg-go` в `/usr/bin`
  (pkexec-сессия не видит `/usr/local/bin` в своём `PATH`), финальная проверка
  `command -v awg && command -v awg-quick && command -v amneziawg-go`.
  Выбран userspace-транспорт: DKMS-модуль на свежем ядре (7.3) может не собраться, а `awg-quick`
  и так умеет откатываться на `amneziawg-go`.
- Fallback при неизвестном семействе остаётся прежним — только ручная инструкция.
- Ручные инструкции в модалке разделены на пять блоков: Debian/Ubuntu, Fedora (сборка), RHEL/CentOS
  (COPR), ALT Linux, «из исходников» (DKMS).

## Альтернативы

- **Собирать DKMS-модуль на Fedora** — быстрее в работе, но зависит от совместимости модуля с ядром;
  при неудаче пользователь получает ошибку сборки вместо рабочего VPN.
- **Оставить Fedora только с ручной инструкцией** — надёжно, но ломает требование «установка в два клика».
- **Искать сторонний COPR с Fedora-сборками** — источник не под нашим контролем, проверять нечем.
- **Тянуть готовые бинарники из чужих релизов** — непроверяемая цепочка поставки.

## Последствия

+ На Fedora установка снова работает и не зависит от сборки модуля под новое ядро.
+ Ошибка «COPR без chroot» больше не воспроизводится: на Fedora COPR не вызывается вовсе.
− Сборка идёт из `master` под root: нужны `git`, `make`, `gcc`, `golang`, и это дольше установки пакетов.
− Пользователь получает userspace-транспорт (немного медленнее kernel module).
− План надо перепроверять при изменениях в upstream-репозиториях (`make install` в `src` tools).

## Ссылки

- `skills/wireguardgui-wireguard/SKILL.md` — раздел «Backend installation»
- `plan/steps/19-4-fedora-source-install.md`
- [constitution.md](../../plan/constitution.md) — I6, Q3
