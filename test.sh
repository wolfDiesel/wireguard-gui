#!/usr/bin/env bash
# Красивый запуск тестов: зелёные галочки, человекочитаемый вывод.
# Использование: ./test.sh [фильтр]
set -euo pipefail
cd "$(dirname "$0")"

dotnet run --project tests/WireguardGui.TestReporter/WireguardGui.TestReporter.csproj -- "$@"