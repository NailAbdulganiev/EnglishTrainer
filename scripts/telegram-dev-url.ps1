<#
.SYNOPSIS
    Печатает ссылку, открывающую мини-апп в обычном браузере, без Telegram и туннеля.

.DESCRIPTION
    Telegram открывает мини-апп только по HTTPS и только внутри своего клиента,
    поэтому разрабатывать интерфейс «через Telegram» неудобно: нужен публичный
    адрес, туннель и телефон. Но скрипт telegram-web-app.js читает данные
    мини-аппа из адреса страницы — ровно так же, как их подставляет сам Telegram.
    Значит, подписанную строку можно положить в адрес вручную и увидеть
    мини-апп в браузере: с темой мессенджера, нижней панелью и настоящим
    автоматическим входом по подписи.

    Подпись делается тем же алгоритмом, что у Telegram: пары «ключ=значение»
    через амперсанд, подписывается строка из тех же пар через перевод строки,
    ключ подписи — HMAC-SHA256 от токена бота с ключом WebAppData.

    Чего этот способ не проверяет: как мини-апп выглядит на телефоне и как
    ведёт себя настоящий клиент Telegram. Всё остальное — вход, привязка
    аккаунта, вёрстка, запросы к API — работает по-настоящему.

.PARAMETER BaseUrl
    Адрес запущенного API. По умолчанию совпадает с профилем http из launchSettings.json.

.PARAMETER TelegramId
    Идентификатор аккаунта Telegram, от имени которого открывается мини-апп.
    Может быть любым: это тестовый идентификатор, а не ваш настоящий. Привязать
    к нему аккаунт тренажёра можно прямо из мини-аппа — кодом из профиля.

.PARAMETER BotToken
    Токен бота. По умолчанию берётся из переменной окружения Telegram__BotToken
    или из appsettings.Local.json — из того же места, что читает приложение.

.EXAMPLE
    dotnet run --project EnglishTrainer.Api      # в одном окне
    .\scripts\telegram-dev-url.ps1               # в другом
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:5201',
    [long] $TelegramId = 900000001,
    [string] $BotToken,
    [string] $FirstName = 'Тест',
    [string] $Username = 'dev_test'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

if (-not $BotToken) {
    $BotToken = $env:Telegram__BotToken
}

if (-not $BotToken) {
    $localSettings = Join-Path (Split-Path $PSScriptRoot -Parent) 'appsettings.Local.json'

    if (Test-Path $localSettings) {
        $BotToken = [regex]::Match(
            (Get-Content $localSettings -Raw), '"BotToken"\s*:\s*"([^"]*)"').Groups[1].Value
    }
}

if (-not $BotToken) {
    throw "Не задан токен бота. Укажите его в appsettings.Local.json (Telegram:BotToken) " +
          "или передайте параметром -BotToken."
}

# Подписываемая строка собирается из раскодированных значений и сортируется
# по ключу — порядок здесь часть алгоритма, а не деталь оформления.
$authDate = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString([Globalization.CultureInfo]::InvariantCulture)
$user = (@{ id = $TelegramId; first_name = $FirstName; username = $Username } | ConvertTo-Json -Compress)

$pairs = @(
    [pscustomobject]@{ Key = 'auth_date'; Value = $authDate },
    [pscustomobject]@{ Key = 'user'; Value = $user }
) | Sort-Object -Property Key

$checkString = ($pairs | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "`n"

$secretBuilder = [System.Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes('WebAppData'))

try {
    $secret = $secretBuilder.ComputeHash([Text.Encoding]::UTF8.GetBytes($BotToken))
}
finally {
    $secretBuilder.Dispose()
}

$signer = [System.Security.Cryptography.HMACSHA256]::new($secret)

try {
    $hash = ($signer.ComputeHash([Text.Encoding]::UTF8.GetBytes($checkString)) |
        ForEach-Object { $_.ToString('x2') }) -join ''
}
finally {
    $signer.Dispose()
}

# В адресе значения кодируются как в query string — так же, как их передаёт Telegram.
$query = ($pairs | ForEach-Object { "$($_.Key)=$([Uri]::EscapeDataString($_.Value))" }) -join '&'
$initData = "$query&hash=$hash"

# Параметры передаются во фрагменте (после #) — так их подставляет сам Telegram.
# Но приложение использует фрагмент для своих маршрутов, поэтому печатается
# и вариант с query string: если первый не подхватится, сработает второй.
$fragment = "tgWebAppData=$([Uri]::EscapeDataString($initData))&tgWebAppVersion=8.0&tgWebAppPlatform=web"
$queryUrl = "$BaseUrl/?$fragment"

Write-Host ''
Write-Host 'Откройте в браузере:' -ForegroundColor Cyan
Write-Host ''
Write-Host "  $BaseUrl/#$fragment"
Write-Host ''
Write-Host 'Если мини-апп открылся как обычный сайт (без нижней панели) — попробуйте вариант:'
Write-Host ''
Write-Host "  $queryUrl"
Write-Host ''
Write-Host 'Что должно получиться:' -ForegroundColor Cyan
Write-Host '  * страница откроется в оформлении Telegram: нижняя панель вместо бокового меню;'
Write-Host '  * в консоли браузера window.Telegram.WebApp.initData — непустая строка;'
Write-Host '  * так как аккаунт ещё не привязан, появится экран «Осталось привязать аккаунт».'
Write-Host ''
Write-Host 'Как войти:' -ForegroundColor Cyan
Write-Host "  1. Откройте $BaseUrl, зарегистрируйтесь и войдите."
Write-Host '  2. Профиль → «Привязать Telegram» → скопируйте код.'
Write-Host '  3. Вернитесь на страницу мини-аппа и вставьте код в поле «Код привязки».'
Write-Host ''
Write-Host 'Идентификатор Telegram для теста:' -NoNewline
Write-Host " $TelegramId" -ForegroundColor Yellow
Write-Host '  Он не имеет отношения к вашему настоящему аккаунту — это тестовый.'
Write-Host ''
Write-Host 'Строка initData:' -ForegroundColor DarkGray
Write-Host "  $initData" -ForegroundColor DarkGray
Write-Host ''
