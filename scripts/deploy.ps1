<#
.SYNOPSIS
    Разворачивает приложение на сервере: сайт, мини-апп и бот.

.DESCRIPTION
    Копирует рабочую копию на сервер, готовит .env, собирает образы и запускает
    всё через docker compose. Скрипт идемпотентен: повторный запуск обновляет
    приложение, а не ломает работающее.

    Пароль SSH вводится в самом ssh, а не в этот скрипт: секреты не проходят
    через командную строку и не попадают в историю. Если установить ключ, дальше
    всё будет без пароля — скрипт предложит готовую команду.

    Файл .env создаётся один раз и сохраняется в рабочей копии (он в .gitignore).
    Это важно: в нём пароль базы, а у уже созданной базы PostgreSQL смена пароля
    в .env не меняет сам пароль — приложение просто перестало бы подключаться.
    Поэтому при повторных запусках берётся существующий .env.

.PARAMETER Server
    Адрес для ssh, например root@203.0.113.10. Обязателен: адреса по умолчанию
    у скрипта нет намеренно — иначе развёртывание, запущенное без параметра,
    ушло бы на чужой или уже не существующий сервер.

.PARAMETER PublicUrl
    Публичный адрес приложения. Обязателен HTTPS: по HTTP Telegram кнопку
    мини-аппа не открывает. Домена нет — подойдёт sslip.io: он резолвит любой
    вписанный в имя IP, например https://203.0.113.10.sslip.io

.PARAMETER RemotePath
    Каталог на сервере, по умолчанию /opt/english-trainer.

.EXAMPLE
    .\scripts\deploy.ps1 -Server root@203.0.113.10 -PublicUrl https://203.0.113.10.sslip.io
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Server,
    [Parameter(Mandatory)] [string] $PublicUrl,
    [string] $RemotePath = '/opt/english-trainer',
    [int] $HttpsPort = 443,
    [string] $BotToken,
    [string] $BotUsername,
    [switch] $ForceNewSecrets
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

# Корень репозитория.
#
# Обычный запуск — файлом, и тогда скрипт лежит в scripts\ внутри репозитория.
# Но на машинах с политикой целостности кода загрузка неподписанного .ps1
# запрещена, и скрипт запускают содержимым:
#   & ([scriptblock]::Create((Get-Content .\scripts\deploy.ps1 -Raw))) -PublicUrl …
# В этом случае $PSScriptRoot пуст, а текущим каталогом оказывается корень
# репозитория — из него скрипт и запускают.
if ($PSScriptRoot) {
    $root = Split-Path $PSScriptRoot -Parent
}
else {
    $root = (Get-Location).Path
}

$envFile = Join-Path $root '.env'
$configFile = Join-Path $root 'appsettings.Local.json'

function Write-Step($text) { Write-Host ''; Write-Host "== $text" -ForegroundColor Cyan }
function Write-Note($text) { Write-Host "   $text" -ForegroundColor DarkGray }

# Криптостойкая случайная строка в base64.
#
# Через Create()+GetBytes, а не статическим RandomNumberGenerator.Fill: Fill
# появился только в .NET Core, а Windows PowerShell работает на .NET Framework,
# где такого метода нет — вызов падал бы с «метода с именем Fill не существует»
# на первом же запуске.
function New-RandomBase64([int] $byteCount) {
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()

    try {
        $bytes = New-Object byte[] $byteCount
        $rng.GetBytes($bytes)
        return [Convert]::ToBase64String($bytes)
    }
    finally {
        $rng.Dispose()
    }
}

# ── Проверки ─────────────────────────────────────────────────────────────────
if ($PublicUrl -notmatch '^https://') {
    throw "PublicUrl должен начинаться с https:// — по HTTP Telegram мини-апп не открывает."
}

foreach ($tool in 'ssh', 'scp', 'tar') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "Не найдена команда $tool. В Windows она входит в состав OpenSSH и tar."
    }
}

$PublicUrl = $PublicUrl.TrimEnd('/')

# ── Токен бота ───────────────────────────────────────────────────────────────
if (-not $BotToken -or -not $BotUsername) {
    if (Test-Path $configFile) {
        $json = Get-Content $configFile -Raw

        if (-not $BotToken) {
            $BotToken = [regex]::Match($json, '"BotToken"\s*:\s*"([^"]*)"').Groups[1].Value
        }

        if (-not $BotUsername) {
            $BotUsername = [regex]::Match($json, '"BotUsername"\s*:\s*"([^"]*)"').Groups[1].Value
        }
    }
}

if (-not $BotToken) {
    throw "Не задан токен бота. Укажите его в appsettings.Local.json или параметром -BotToken."
}

# ── Ключ SSH ─────────────────────────────────────────────────────────────────
Write-Step "Ключ SSH"

$keyPath = Join-Path $env:USERPROFILE '.ssh\id_ed25519'

if (-not (Test-Path $keyPath)) {
    Write-Note "Ключа нет, создаю $keyPath"

    # Пустая фраза передаётся как '""': PowerShell не передаёт пустую строку
    # аргументом, а ssh-keygen без -N спрашивает её интерактивно.
    #
    # Подавляется только стандартный вывод. Перенаправлять stderr у внешней
    # команды нельзя: при $ErrorActionPreference = 'Stop' любая её жалоба
    # становится прерыванием скрипта, а нам нужен код возврата, а не исключение.
    & ssh-keygen -t ed25519 -N '""' -f $keyPath -C 'english-trainer-deploy' > $null

    if (-not (Test-Path "$keyPath.pub")) {
        throw "Не удалось создать ключ $keyPath. Создайте его вручную: ssh-keygen -t ed25519"
    }
}

$passwordless = $false

if (-not $ForceNewSecrets) {
    # stderr не подавляется намеренно: при $ErrorActionPreference = 'Stop'
    # перенаправление stderr у внешней команды прерывает скрипт, а неудача
    # здесь — ожидаемая. Поэтому сообщение «Permission denied» просто
    # печатается, а решение принимается по коду возврата.
    & ssh -o BatchMode=yes -o ConnectTimeout=10 -o StrictHostKeyChecking=accept-new $Server 'true'
    $passwordless = $LASTEXITCODE -eq 0
}

if (-not $passwordless) {
    $publicKey = Get-Content "$keyPath.pub" -Raw

    Write-Host ''
    Write-Host 'Пароль будет спрошен у ssh. Чтобы больше его не вводить, выполните' -ForegroundColor Yellow
    Write-Host 'одну команду — она добавит ключ на сервер (пароль спросят один раз):' -ForegroundColor Yellow
    Write-Host ''
    Write-Host "  type `"$keyPath.pub`" | ssh $Server `"mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 700 ~/.ssh && chmod 600 ~/.ssh/authorized_keys`""
    Write-Host ''
    Read-Host 'Нажмите Enter, когда будете готовы (или сразу — пароль спросят по ходу)'

    & ssh -o ConnectTimeout=15 $Server 'echo connected'
    if ($LASTEXITCODE -ne 0) { throw "Не удалось подключиться к $Server." }
}

# ── Настройки развёртывания ──────────────────────────────────────────────────
Write-Step "Настройки"

$freshSecrets = $false

if ((Test-Path $envFile) -and -not $ForceNewSecrets) {
    Write-Note "Использую существующий .env (пароль базы и секрет токенов сохраняются)"
    $envText = Get-Content $envFile -Raw
}
else {
    # Пароля базы может не быть в локальном .env, а на сервере уже стоять
    # запущенная база. Забрать .env с сервера дешевле, чем обнаружить, что
    # приложение больше не подключается к своей же базе.
    #
    # Без перенаправления stderr — по той же причине, что и у проверки выше:
    # «нет файла» здесь нормальный ответ, а не ошибка скрипта.
    & ssh -o BatchMode=yes -o ConnectTimeout=10 $Server "test -f $RemotePath/.env"

    if ($LASTEXITCODE -eq 0 -and -not $ForceNewSecrets) {
        Write-Note "На сервере уже есть .env — забираю его, чтобы не сломать базу"
        & scp "${Server}:$RemotePath/.env" $envFile
        $envText = Get-Content $envFile -Raw
    }
    else {
        Write-Note "Создаю новый .env со случайными секретами"
        $freshSecrets = $true

        $jwtSecret = New-RandomBase64 48

        # Из пароля убираются +, / и = : он попадает в строку подключения
        # и в .env, где эти символы требуют экранирования или кавычек.
        # Алфавит base64 без них остаётся достаточно широким.
        $dbPassword = (New-RandomBase64 24) -replace '[+/=]', 'x'

        $envText = @"
PUBLIC_URL=$PublicUrl
POSTGRES_DB=english_trainer
POSTGRES_USER=postgres
POSTGRES_PASSWORD=$dbPassword
JWT_SECRET=$jwtSecret
TELEGRAM_BOT_TOKEN=$BotToken
TELEGRAM_BOT_USERNAME=$BotUsername
"@
    }
}

# Публичный адрес обновляется всегда: он меняется чаще всего остального
# (сменился домен, появился постоянный вместо sslip.io).
if ($envText -match '(?m)^PUBLIC_URL=') {
    $envText = [regex]::Replace($envText, '(?m)^PUBLIC_URL=.*$', "PUBLIC_URL=$PublicUrl")
}
else {
    $envText = "PUBLIC_URL=$PublicUrl`n" + $envText
}

# Порт HTTPS тоже задаётся при каждом запуске: его меняют, когда 443 занят
# чем-то другим (VPN, панель). Порт 80 при этом остаётся за Caddy — через него
# Let's Encrypt подтверждает владение доменом.
#
# Значение проверяется явно: пустая строка в .env выглядит безобидно, но
# подстановка ${HTTPS_PORT:-443} в compose считает пустое значение пустым,
# а не отсутствующим, и порт молча уезжает на 443 — что верно по случайности,
# а не по замыслу.
$httpsPort = if ($HttpsPort -gt 0) { $HttpsPort } else { 443 }

if ($envText -match '(?m)^HTTPS_PORT=') {
    $envText = [regex]::Replace($envText, '(?m)^HTTPS_PORT=.*$', "HTTPS_PORT=$httpsPort")
}
else {
    $envText = $envText.TrimEnd("`r", "`n") + "`nHTTPS_PORT=$httpsPort`n"
}

[System.IO.File]::WriteAllText($envFile, $envText, [System.Text.UTF8Encoding]::new($false))
Write-Note "Адрес: $PublicUrl (порт HTTPS: $httpsPort)"

if ($freshSecrets) {
    $dbPasswordShown = [regex]::Match($envText, '(?m)^POSTGRES_PASSWORD=(.*)$').Groups[1].Value
    Write-Host ''
    Write-Host "   Пароль базы: $dbPasswordShown" -ForegroundColor Yellow
    Write-Note 'Он сохранён в .env (файл в .gitignore) — запишите его отдельно, если понадобится доступ к базе.'
}

# ── Передача файлов ──────────────────────────────────────────────────────────
Write-Step "Передача файлов"

$tarball = Join-Path $env:TEMP 'english-trainer-deploy.tar.gz'

# Исключения: результаты сборки, репозиторий, локальные данные и секреты.
# appsettings.Local.json не нужен на сервере: там настройки приходят
# переменными окружения, а этот файл содержит токен бота и пароль базы.
$excludeArgs = @(
    '--exclude=bin', '--exclude=obj', '--exclude=*/bin/*', '--exclude=*/obj/*',
    '--exclude=.git', '--exclude=.vs', '--exclude=.pgdata', '--exclude=.dotnet-home',
    '--exclude=tests', '--exclude=uploads',
    '--exclude=appsettings.Local.json', '--exclude=EnglishTrainer.Api/wwwroot/avatars'
)

& tar -czf $tarball @excludeArgs -C $root .
if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать архив.' }

$sizeMb = [Math]::Round((Get-Item $tarball).Length / 1MB, 1)
Write-Note "Архив собран: $sizeMb МБ"

& scp $tarball "${Server}:/tmp/english-trainer-deploy.tar.gz"
if ($LASTEXITCODE -ne 0) { throw 'Не удалось скопировать архив на сервер.' }

# ── Установка на сервере ─────────────────────────────────────────────────────
Write-Step "Развёртывание на сервере"
Write-Note 'Первый раз это несколько минут: скачиваются образы .NET и PostgreSQL.'

$remoteCommand = @"
set -e
mkdir -p '$RemotePath'
tar xzf /tmp/english-trainer-deploy.tar.gz -C '$RemotePath'
rm -f /tmp/english-trainer-deploy.tar.gz
cd '$RemotePath'
bash deploy/remote-setup.sh
"@

& ssh $Server $remoteCommand
$exitCode = $LASTEXITCODE

Remove-Item $tarball -Force -ErrorAction SilentlyContinue

if ($exitCode -ne 0) {
    Write-Host ''
    Write-Host 'Развёртывание завершилось с ошибкой. Журналы на сервере:' -ForegroundColor Red
    Write-Host "  ssh $Server 'cd $RemotePath && docker compose -f docker-compose.prod.yml logs --tail 50'"
    exit $exitCode
}

Write-Host ''
Write-Host 'Готово.' -ForegroundColor Green
Write-Host "  Сайт и мини-апп: $PublicUrl"
Write-Host "  Журналы:         ssh $Server 'cd $RemotePath && docker compose -f docker-compose.prod.yml logs -f api bot'"
Write-Host ''
Write-Host 'Если мини-апп не открывается с телефона, а сайт открывается —' -ForegroundColor Yellow
Write-Host 'значит IP сервера недоступен из вашей мобильной сети, и дело не в приложении.' -ForegroundColor Yellow
