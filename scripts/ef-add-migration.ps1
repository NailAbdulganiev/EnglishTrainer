<#
.SYNOPSIS
    Готовит файлы миграции EF Core, когда инструмент dotnet-ef недоступен.

.DESCRIPTION
    Обычный путь — `dotnet ef migrations add <Имя>`. Он требует установки
    инструмента из nuget.org, а в окружении без доступа к сети это невозможно.
    Скрипт делает ту же работу вручную:

      * файл снимка модели (<Имя>.Designer.cs) собирается из
        AppDbContextModelSnapshot.cs — тело у них совпадает строка в строку;
      * сама миграция создаётся каркасом, в который остаётся вписать Up и Down.

    Порядок работы:

      1. Правится модель и AppDbContextModelSnapshot.cs — он описывает модель
         ПОСЛЕ новой миграции.
      2. Запускается скрипт.
      3. В созданном файле заполняются Up и Down.

    Пока Up не заполнен, он намеренно бросает исключение: миграция, которая
    «ничего не делает», опаснее падения — схема молча остаётся старой.

.EXAMPLE
    powershell -File ./scripts/ef-add-migration.ps1 -Name AddUserAvatar
#>
param(
    [Parameter(Mandatory = $true)][string] $Name,
    [string] $Timestamp = (Get-Date -Format 'yyyyMMddHHmmss')
)

$ErrorActionPreference = 'Stop'

$migrations = Join-Path $PSScriptRoot '..\EnglishTrainer.Infrastructure\Migrations'
$snapshot = Join-Path $migrations 'AppDbContextModelSnapshot.cs'
$migrationId = "${Timestamp}_${Name}"

if (-not (Test-Path $snapshot)) {
    throw "Не найден снимок модели: $snapshot"
}

$existing = Get-ChildItem $migrations -Filter "*_$Name.cs" -ErrorAction SilentlyContinue

if ($existing) {
    throw "Миграция с именем '$Name' уже есть: $($existing[0].Name)"
}

# ── Снимок модели для новой миграции ────────────────────────────────────────
# Отличия от AppDbContextModelSnapshot.cs ровно три: атрибут [Migration],
# имя класса и имя метода. Всё остальное берётся из снимка как есть.
$designerLines = New-Object System.Collections.Generic.List[string]

foreach ($line in (Get-Content $snapshot -Encoding UTF8)) {
    if ($line -eq 'using Microsoft.EntityFrameworkCore.Infrastructure;') {
        $designerLines.Add($line)
        $designerLines.Add('using Microsoft.EntityFrameworkCore.Migrations;')
    }
    elseif ($line -eq '    partial class AppDbContextModelSnapshot : ModelSnapshot') {
        $designerLines.Add("    [Migration(`"$migrationId`")]")
        $designerLines.Add("    partial class $Name")
    }
    elseif ($line -eq '        protected override void BuildModel(ModelBuilder modelBuilder)') {
        $designerLines.Add('        /// <inheritdoc />')
        $designerLines.Add('        protected override void BuildTargetModel(ModelBuilder modelBuilder)')
    }
    else {
        $designerLines.Add($line)
    }
}

# Файлы миграций EF пишет с BOM — сохраняем ту же кодировку, чтобы diff
# не показывал изменение всего файла из-за одной добавленной строки.
$utf8Bom = New-Object System.Text.UTF8Encoding($true)

$designerPath = Join-Path $migrations "$migrationId.Designer.cs"
[System.IO.File]::WriteAllLines($designerPath, $designerLines, $utf8Bom)

# ── Каркас миграции ─────────────────────────────────────────────────────────
$migrationPath = Join-Path $migrations "$migrationId.cs"

$skeleton = @"
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishTrainer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class $Name : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Заполните операции. Пока тело пустое, миграция намеренно падает:
            // пустая миграция опаснее исключения — схема молча остаётся старой.
            throw new NotSupportedException("Миграция $Name не заполнена.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Миграция $Name не заполнена.");
        }
    }
}
"@

[System.IO.File]::WriteAllText($migrationPath, $skeleton, $utf8Bom)

Write-Host "Создано:"
Write-Host "  $migrationPath"
Write-Host "  $designerPath"
