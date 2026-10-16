<#
.SYNOPSIS
    Сквозная проверка API на запущенном сервере.

.DESCRIPTION
    Проходит полный путь пользователя: регистрация, профиль, колоды, сессия обучения,
    ответы на карточки, статистика, избранное, друзья, аватар и Telegram. Дополнительно
    проверяет обработку ошибок: неверный пароль даёт 401, отсутствие токена 401,
    несуществующее слово 404, файл не-картинка 400.

    Скрипт только ходит по HTTP и ничего не меняет в репозитории. Пользователь
    создаётся со случайным email, поэтому запускать можно многократно.

    Раздел проверок Telegram требует токена бота: он берётся из переменной
    окружения Telegram__BotToken или из appsettings.Local.json. Без токена раздел
    пропускается с явным сообщением — подпись initData иначе проверить нечем.

    Совместим с Windows PowerShell 5.1 (в нём нет -SkipHttpErrorCheck,
    а строковое тело запроса искажает кириллицу, поэтому тело кодируется в байты).

    ВАЖНО: файл должен оставаться в UTF-8 с BOM. Без BOM Windows PowerShell 5.1
    читает его как ANSI, кириллица превращается в мусор, и скрипт перестаёт
    разбираться — падает с «Unexpected token» на ровном месте. Редакторы
    и утилиты, снимающие BOM, ломают запуск: проверяйте первые три байта (EF BB BF).

.PARAMETER BaseUrl
    Адрес запущенного API. По умолчанию совпадает с профилем http из launchSettings.json.

.PARAMETER MaxCards
    Сколько карточек пройти в сессии. Ограничение нужно потому, что ответ «не помню»
    возвращает карточку в конец сессии, и цикл теоретически может не закончиться.

.EXAMPLE
    dotnet run --project EnglishTrainer.Api
    .\scripts\smoke-test.ps1
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:5201',
    [int] $MaxCards = 10,
    [string] $Email,
    [string] $Password = 'SmokeTest-12345'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$script:passed = 0
$script:failed = 0
$script:token = $null

if (-not $Email) {
    $Email = "smoke-$([Guid]::NewGuid().ToString('N').Substring(0, 8))@example.com"
}

function Write-Step([string] $title) {
    Write-Host ''
    Write-Host "--- $title" -ForegroundColor Cyan
}

function Assert-That([bool] $condition, [string] $message) {
    if ($condition) {
        $script:passed++
        Write-Host "   OK      $message" -ForegroundColor Green
    }
    else {
        $script:failed++
        Write-Host "   ПРОВАЛ  $message" -ForegroundColor Red
    }
}

function New-RequestParameters {
    param(
        [string] $Method,
        [string] $Path,
        $Body,
        [switch] $Anonymous
    )

    $parameters = @{
        Method           = $Method
        Uri              = "$BaseUrl$Path"
        UseBasicParsing  = $true
        Headers          = if ($Anonymous) { @{} } else { @{ Authorization = "Bearer $script:token" } }
    }

    if ($null -ne $Body) {
        # Тело отправляется байтами: Windows PowerShell 5.1 иначе кодирует
        # строку не в UTF-8, и русские переводы доезжают до сервера испорченными.
        $json = $Body | ConvertTo-Json -Depth 6 -Compress
        $parameters.ContentType = 'application/json; charset=utf-8'
        $parameters.Body = [Text.Encoding]::UTF8.GetBytes($json)
    }

    return $parameters
}

function Invoke-Api {
    param(
        [string] $Method,
        [string] $Path,
        $Body,
        [switch] $Anonymous
    )

    $parameters = New-RequestParameters -Method $Method -Path $Path -Body $Body -Anonymous:$Anonymous
    return Invoke-RestMethod @parameters
}

function Get-StatusCode {
    param(
        [string] $Method,
        [string] $Path,
        $Body,
        [switch] $Anonymous
    )

    $parameters = New-RequestParameters -Method $Method -Path $Path -Body $Body -Anonymous:$Anonymous

    try {
        $response = Invoke-WebRequest @parameters
        return [int] $response.StatusCode
    }
    catch {
        $response = $_.Exception.Response
        if ($null -eq $response) { throw }
        return [int] $response.StatusCode
    }
}

<#
.SYNOPSIS
    Отправляет файл на эндпоинт, принимающий multipart/form-data.

.DESCRIPTION
    Windows PowerShell 5.1 не умеет -Form (это PowerShell 6+), поэтому
    составное тело собирается через System.Net.Http. Статус возвращается
    числом: проверяются и удачные загрузки, и отказы.
#>
function Send-AvatarFile {
    param(
        [Parameter(Mandatory = $true)][byte[]] $Bytes,
        [Parameter(Mandatory = $true)][string] $FileName,
        [Parameter(Mandatory = $true)][string] $ContentType
    )

    Add-Type -AssemblyName System.Net.Http

    $client = New-Object System.Net.Http.HttpClient
    $client.DefaultRequestHeaders.Authorization =
        New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $script:token)

    $form = New-Object System.Net.Http.MultipartFormDataContent
    $part = New-Object System.Net.Http.ByteArrayContent(, $Bytes)
    $part.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($ContentType)
    $form.Add($part, 'file', $FileName)

    try {
        $response = $client.PostAsync("$BaseUrl/api/me/avatar", $form).Result
        $body = $response.Content.ReadAsStringAsync().Result

        return [pscustomobject]@{ Status = [int] $response.StatusCode; Body = $body }
    }
    finally {
        $form.Dispose()
        $client.Dispose()
    }
}

<#
.SYNOPSIS
    Собирает строку initData, подписанную так же, как её подписывает Telegram.

.DESCRIPTION
    Это единственный способ проверить подпись без настоящего клиента Telegram:
    строка собирается по документированному алгоритму — пары «ключ=значение»
    соединены амперсандами, а подписывается другая строка, из тех же пар
    через перевод строки. Ключ подписи — HMAC-SHA256 от токена бота
    с ключом WebAppData.
#>
function New-TelegramInitData {
    param(
        [Parameter(Mandatory = $true)][string] $BotToken,
        [Parameter(Mandatory = $true)][long] $UserId,
        [string] $FirstName = 'Проверка',
        [string] $Username = 'smoke_test'
    )

    $authDate = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString([Globalization.CultureInfo]::InvariantCulture)
    $user = (@{ id = $UserId; first_name = $FirstName; username = $Username } | ConvertTo-Json -Compress)

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

    $query = ($pairs | ForEach-Object { "$($_.Key)=$([Uri]::EscapeDataString($_.Value))" }) -join '&'

    return "$query&hash=$hash"
}

# Токен бота нужен только разделу проверок Telegram. Берётся из переменной
# окружения или из локального файла секретов — того же, что читает приложение.
# Если его нет, раздел пропускается с явным сообщением, а не падает.
$botToken = $env:Telegram__BotToken

if (-not $botToken) {
    $localSettings = Join-Path (Split-Path $PSScriptRoot -Parent) 'appsettings.Local.json'

    if (Test-Path $localSettings) {
        $botToken = [regex]::Match((Get-Content $localSettings -Raw), '"BotToken"\s*:\s*"([^"]*)"').Groups[1].Value
    }
}

Write-Host ''
Write-Host "Проверка API: $BaseUrl" -ForegroundColor White
Write-Host "Пользователь: $Email"

# --- Живость ---------------------------------------------------------------
Write-Step 'Служебные эндпоинты'

$health = Invoke-Api -Method GET -Path '/health' -Anonymous
Assert-That ($health.status -eq 'ok') 'GET /health отвечает ok'

$dbHealth = Get-StatusCode -Method GET -Path '/health/db' -Anonymous
Assert-That ($dbHealth -eq 200) "GET /health/db видит базу (код $dbHealth)"

# --- Регистрация и вход ----------------------------------------------------
Write-Step 'Регистрация и вход'

$registration = Invoke-Api -Method POST -Path '/api/auth/register' -Anonymous -Body @{
    email       = $Email
    password    = $Password
    displayName = 'Проверочный пользователь'
    timeZoneId  = 'Europe/Moscow'
}
$script:token = $registration.accessToken
Assert-That ($script:token.Length -gt 20) 'Регистрация вернула токен доступа'
Assert-That ($registration.user.email -eq $Email) 'Email сохранён в нижнем регистре'

$duplicate = Get-StatusCode -Method POST -Path '/api/auth/register' -Anonymous -Body @{
    email    = $Email
    password = $Password
}
Assert-That ($duplicate -eq 409) "Повторная регистрация того же email отклонена (код $duplicate)"

$badPassword = Get-StatusCode -Method POST -Path '/api/auth/login' -Anonymous -Body @{
    login    = $Email
    password = 'NeverRightPassword-1'
}
Assert-That ($badPassword -eq 401) "Неверный пароль даёт 401 (код $badPassword)"

$shortPassword = Get-StatusCode -Method POST -Path '/api/auth/register' -Anonymous -Body @{
    email    = "short-$Email"
    password = '123'
}
Assert-That ($shortPassword -eq 400) "Слишком короткий пароль отклонён (код $shortPassword)"

$login = Invoke-Api -Method POST -Path '/api/auth/login' -Anonymous -Body @{
    login    = $Email
    password = $Password
}
Assert-That ($login.accessToken.Length -gt 20) 'Вход по email вернул токен'

$byNickname = Invoke-Api -Method POST -Path '/api/auth/login' -Anonymous -Body @{
    login    = $registration.user.username
    password = $Password
}
Assert-That ($byNickname.accessToken.Length -gt 20) "Вход по нику @$($registration.user.username) работает"

# --- Восстановление пароля -------------------------------------------------
Write-Step 'Восстановление пароля'

$forgotKnown = Get-StatusCode -Method POST -Path '/api/auth/forgot-password' -Anonymous -Body @{ login = $Email }
$forgotUnknown = Get-StatusCode -Method POST -Path '/api/auth/forgot-password' -Anonymous -Body @{ login = "no-such-$Email" }

Assert-That ($forgotKnown -eq 204) "Ссылка запрошена для существующего пользователя (код $forgotKnown)"
Assert-That ($forgotUnknown -eq 204) "Ответ для несуществующего тот же (код $forgotUnknown) — адреса не раскрываются"

$forgedToken = Get-StatusCode -Method POST -Path '/api/auth/reset-password' -Anonymous -Body @{
    token       = 'поддельный-токен'
    newPassword = 'Another-Password-1'
}
Assert-That ($forgedToken -eq 400) "Поддельный токен отклонён (код $forgedToken)"

$shortNewPassword = Get-StatusCode -Method POST -Path '/api/auth/reset-password' -Anonymous -Body @{
    token       = 'поддельный-токен'
    newPassword = '123'
}
Assert-That ($shortNewPassword -eq 400) "Короткий новый пароль отклонён (код $shortNewPassword)"

# --- Смена пароля ----------------------------------------------------------
Write-Step 'Смена пароля'

$wrongCurrent = Get-StatusCode -Method PUT -Path '/api/me/password' -Body @{
    currentPassword = 'Not-The-Password'
    newPassword     = 'Another-Password-1'
}
Assert-That ($wrongCurrent -eq 400) "Неверный текущий пароль отклонён (код $wrongCurrent)"

$samePassword = Get-StatusCode -Method PUT -Path '/api/me/password' -Body @{
    currentPassword = $Password
    newPassword     = $Password
}
Assert-That ($samePassword -eq 400) "Пароль, совпадающий с текущим, отклонён (код $samePassword)"

$changed = Get-StatusCode -Method PUT -Path '/api/me/password' -Body @{
    currentPassword = $Password
    newPassword     = 'New-Password-2026'
}
Assert-That ($changed -eq 204) "Пароль изменён (код $changed)"

$oldPasswordFails = Get-StatusCode -Method POST -Path '/api/auth/login' -Anonymous -Body @{
    login    = $Email
    password = $Password
}
Assert-That ($oldPasswordFails -eq 401) "Старый пароль больше не подходит (код $oldPasswordFails)"

$newPasswordWorks = Invoke-Api -Method POST -Path '/api/auth/login' -Anonymous -Body @{
    login    = $Email
    password = 'New-Password-2026'
}
Assert-That ($newPasswordWorks.accessToken.Length -gt 20) 'Вход с новым паролем работает'

# --- Профиль и настройки ---------------------------------------------------
Write-Step 'Профиль и настройки'

$anonymous = Get-StatusCode -Method GET -Path '/api/me' -Anonymous
Assert-That ($anonymous -eq 401) "Профиль без токена недоступен (код $anonymous)"

$profile = Invoke-Api -Method GET -Path '/api/me'
Assert-That ($profile.user.email -eq $Email) 'Профиль вернул текущего пользователя'
Assert-That ($profile.settings.newWordsPerDay -eq 10) 'Лимит новых слов по умолчанию равен 10'
Assert-That ($profile.user.timeZoneId -eq 'Europe/Moscow') 'Часовой пояс сохранён'

$updated = Invoke-Api -Method PUT -Path '/api/me/settings' -Body @{
    newWordsPerDay = 8
    dailyGoal      = 25
}
Assert-That ($updated.newWordsPerDay -eq 8) 'Лимит новых слов изменён на 8'
Assert-That ($updated.dailyGoal -eq 25) 'Дневная цель изменена на 25'

$badLimit = Get-StatusCode -Method PUT -Path '/api/me/settings' -Body @{ newWordsPerDay = 5000 }
Assert-That ($badLimit -eq 400) "Недопустимый лимит новых слов отклонён (код $badLimit)"

$badZone = Get-StatusCode -Method PUT -Path '/api/me/timezone' -Body @{ timeZoneId = 'Mars/Olympus' }
Assert-That ($badZone -eq 400) "Несуществующий часовой пояс отклонён (код $badZone)"

# --- Колоды и словарь ------------------------------------------------------
Write-Step 'Колоды и словарь'

# Массив от Invoke-RestMethod берётся без @(): в Windows PowerShell 5.1
# обёртка @() вокруг вызова функции кладёт весь массив одним элементом.
$decks = Invoke-Api -Method GET -Path '/api/decks'
Assert-That ($decks.Count -ge 18) "Колод не меньше восемнадцати (получено $($decks.Count))"
Assert-That ((($decks | Measure-Object -Property totalWords -Sum).Sum) -gt 400) 'В колодах больше четырёхсот слов'

$deck = $decks | Sort-Object slug | Select-Object -First 1
$deckWords = Invoke-Api -Method GET -Path "/api/decks/$($deck.slug)/words"
Assert-That ($deckWords.Count -gt 0) "Колода '$($deck.slug)' содержит слова"

$sample = $deckWords | Select-Object -First 1
$word = Invoke-Api -Method GET -Path "/api/words/$($sample.id)"
Assert-That ($word.translations.Count -gt 0) 'У слова есть перевод'
Assert-That ($word.examples.Count -gt 0) 'У слова есть примеры употребления'
Assert-That ($word.ipaUk -like '/*/') "Есть транскрипция IPA: $($word.ipaUk)"

$missing = Get-StatusCode -Method GET -Path '/api/words/99999999'
Assert-That ($missing -eq 404) "Несуществующее слово даёт 404 (код $missing)"

# --- Сессия обучения -------------------------------------------------------
Write-Step 'Сессия обучения'

$session = Invoke-Api -Method GET -Path "/api/study/session?deckId=$($deck.id)"
Assert-That ($session.totalItems -gt 0) "Сессия собрана, карточек: $($session.totalItems)"
Assert-That ($null -ne $session.currentCard) 'Есть карточка для показа'
Assert-That ($session.currentCard.options.Count -ne 1) 'Карточка не предлагает выбор из одного варианта'

$firstCard = $session.currentCard
Assert-That ($firstCard.prompt.Length -gt 0) "В карточке есть вопрос: $($firstCard.prompt)"
Assert-That ($null -eq $firstCard.PSObject.Properties['lemma']) 'В карточке нет поля с ответом (lemma)'

# Текст для произношения — это тоже правильный ответ, и уходить клиенту он может
# только в упражнении «на слух». Проверка ловит регрессию, при которой озвучку
# добавят «для удобства» и в задания на ввод: там браузер произнёс бы ответ
# вслух до попытки, и упражнение потеряло бы смысл.
Assert-That ($null -eq $firstCard.spokenText) `
    'В задании на ввод текст для произношения клиенту не уходит'

# Правильный ответ берётся из словарной статьи слова — так же, как это сделал бы
# настоящий интерфейс с кнопкой «показать перевод».
function Get-CorrectAnswer($card) {
    $details = Invoke-Api -Method GET -Path "/api/words/$($card.wordId)"

    if ($card.promptLanguage -eq 'en') {
        $primary = $details.translations | Where-Object { $_.isPrimary } | Select-Object -First 1
        if ($primary) { return $primary.text }
        return $details.translations[0].text
    }

    return $details.lemma
}

$answered = 0
$correct = 0
$requeued = 0
$card = $session.currentCard

while ($null -ne $card -and $answered -lt $MaxCards) {
    # Второй ответ даётся намеренно неверным: так проверяется ветка «не помню»,
    # в которой карточка возвращается в конец сессии и её проходят заново.
    $answer = if ($answered -eq 1) { 'deliberately-wrong-answer' } else { Get-CorrectAnswer $card }

    $result = Invoke-Api -Method POST -Path '/api/study/review' -Body @{
        itemId    = $card.itemId
        answer    = $answer
        elapsedMs = 2500
    }

    if ($result.isCorrect) { $correct++ }
    if ($result.requeued) { $requeued++ }

    Assert-That ([bool] $result.correctAnswer) "Разбор ответа содержит эталон: $($result.correctAnswer)"
    $answered++
    $card = $result.nextCard
}

Assert-That ($answered -gt 0) "Отправлено ответов: $answered"
Assert-That ($correct -gt 0) "Верных ответов распознано: $correct"
Assert-That ($requeued -gt 0) "Ответ «не помню» вернул карточку в сессию: $requeued"

# --- Пустая очередь --------------------------------------------------------
Write-Step 'Пустая очередь'

# Воспроизводим ситуацию, в которую попал живой пользователь: очередь пуста,
# потому что повторения назначены на будущее, а дневной лимит новых слов
# израсходован. Раньше сервер и в этом случае создавал сессию, оставлял её
# активной и потом возвращал её же — человек навсегда упирался в «сессия
# закончена» и не мог учиться, хотя просроченные карточки копились.
Invoke-Api -Method PUT -Path '/api/me/settings' -Body @{ newWordsPerDay = 0 } | Out-Null

$empty = Invoke-Api -Method GET -Path '/api/study/session'
Assert-That ($empty.totalItems -eq 0) "Повторять нечего: карточек $($empty.totalItems)"
Assert-That ($empty.sessionId -eq 0) 'Пустая сессия не сохраняется в базу (sessionId = 0)'
Assert-That ($null -ne $empty.nextDueAtUtc) "Указано ближайшее повторение: $($empty.nextDueAtUtc)"

$stillActive = Get-StatusCode -Method GET -Path '/api/study/session/active'
Assert-That ($stillActive -eq 204) "Активной сессии не осталось (код $stillActive)"

$cram = Invoke-Api -Method GET -Path '/api/study/session?cram=true'
Assert-That ($cram.totalItems -gt 0) "Тренировка вне расписания даёт карточки: $($cram.totalItems)"
Assert-That ($cram.sessionId -gt 0) 'Вне расписания создаётся настоящая сессия'

$abandoned = Get-StatusCode -Method DELETE -Path '/api/study/session'
Assert-That ($abandoned -eq 204) "Сессия вне расписания закрыта (код $abandoned)"

# Лимит возвращаем с запасом: следующие разделы проверок должны получать
# настоящие карточки, а не пустую очередь.
Invoke-Api -Method PUT -Path '/api/me/settings' -Body @{ newWordsPerDay = 20 } | Out-Null


# --- Статистика ------------------------------------------------------------
Write-Step 'Статистика'

$summary = Invoke-Api -Method GET -Path '/api/stats/summary'
Assert-That ($summary.totalCards -gt 0) "Слов в изучении: $($summary.totalCards)"
Assert-That ($summary.reviewedToday -gt 0) "Ответов сегодня: $($summary.reviewedToday)"
Assert-That ($summary.currentStreak -ge 1) "Серия дней считается: $($summary.currentStreak)"
Assert-That ($summary.accuracyLast30Days -gt 0) "Удержание за 30 дней: $($summary.accuracyLast30Days)%"

$today = Get-Date
$from = $today.AddDays(-29).ToString('yyyy-MM-dd')
$to = $today.ToString('yyyy-MM-dd')
$activity = Invoke-Api -Method GET -Path "/api/stats/activity?from=$from&to=$to"
Assert-That ($activity.days.Count -eq 30) "Активность вернула 30 дней (получено $($activity.days.Count))"
Assert-That ($activity.retention.Count -gt 0) 'Кривая удержания не пуста'

$decksAfter = Invoke-Api -Method GET -Path '/api/decks'
$progressed = @($decksAfter | Where-Object { $_.startedWords -gt 0 })
Assert-That ($progressed.Count -gt 0) "Прогресс виден в колодах, затронуто колод: $($progressed.Count)"

# --- Достижения ------------------------------------------------------------
Write-Step 'Достижения'

$achievements = Invoke-Api -Method GET -Path '/api/achievements'
Assert-That ($achievements.total -ge 9) "В каталоге достижений: $($achievements.total)"
Assert-That ($achievements.items.Count -eq $achievements.total) 'Список достижений полный'

# «100 слов», «7 дней подряд» и «колода пройдена» — те, ради которых раздел
# и делался. Проверяются по коду: название может поменяться, код — нет.
foreach ($code in 'words-100', 'streak-7', 'deck-master') {
    Assert-That ($null -ne ($achievements.items | Where-Object { $_.code -eq $code })) "Есть достижение '$code'"
}

# Пользователь уже прошёл сессию, поэтому первое достижение обязано быть выдано —
# причём именно чтением, без отдельной кнопки «проверить».
$firstStep = $achievements.items | Where-Object { $_.code -eq 'first-step' }
Assert-That ($firstStep.unlocked -eq $true) 'Достижение за первое слово выдано'
Assert-That ($null -ne $firstStep.unlockedAt) 'У выданного достижения есть дата'

Assert-That ($achievements.unlockedCount -ge 1) "Получено достижений: $($achievements.unlockedCount)"
Assert-That ($null -ne $achievements.nextGoal) 'Ближайшая незакрытая цель определена'
Assert-That ($achievements.nextGoal.unlocked -eq $false) 'Ближайшая цель — незакрытая'
Assert-That ($achievements.nextGoal.progress -le $achievements.nextGoal.target) `
    'Прогресс не превышает порог'

# Прогресс обрезан по порогу: «120 из 100» читалось бы как ошибка, а не как успех.
Assert-That (($achievements.items | Where-Object { $_.progress -gt $_.target }).Count -eq 0) `
    'Ни у одного достижения прогресс не выше порога'

# Повторное чтение не выдаёт то же достижение второй раз.
$again = Invoke-Api -Method GET -Path '/api/achievements'
Assert-That ($again.unlockedCount -eq $achievements.unlockedCount) 'Повторная проверка идемпотентна'

# --- Профиль ---------------------------------------------------------------
Write-Step 'Профиль'

$startProfile = Invoke-Api -Method GET -Path '/api/me'
Assert-That ($startProfile.user.username.Length -ge 3) "У пользователя есть ник: @$($startProfile.user.username)"
Assert-That ($null -ne $startProfile.user.createdAt) 'В профиле есть дата регистрации'
Assert-That ($startProfile.favoriteCount -eq 0) 'Изначально избранное пусто'
Assert-That ($startProfile.friendCount -eq 0) 'Изначально друзей нет'

$newUsername = "smoke$([Guid]::NewGuid().ToString('N').Substring(0, 6))"
$renamed = Invoke-Api -Method PUT -Path '/api/me/profile' -Body @{
    username    = $newUsername
    displayName = 'Переименованный'
}
Assert-That ($renamed.username -eq $newUsername) "Ник изменён на @$newUsername"
Assert-That ($renamed.displayName -eq 'Переименованный') 'Отображаемое имя изменено'

$badNick = Get-StatusCode -Method PUT -Path '/api/me/profile' -Body @{ username = 'Ник С Пробелом' }
Assert-That ($badNick -eq 400) "Некорректный ник отклонён (код $badNick)"

# --- Избранное -------------------------------------------------------------
Write-Step 'Избранное'

# Массив берётся без @(): обёртка @() вокруг вызова функции кладёт весь
# массив одним элементом, и пустой список выглядел бы как один элемент.
$favoriteIds = Invoke-Api -Method GET -Path '/api/favorites/ids'
Assert-That (@($favoriteIds).Count -eq 0) 'Изначально избранное пусто'

$wordId = $word.id

$marked = Invoke-Api -Method PUT -Path "/api/words/$wordId/favorite" -Body @{ isFavorite = $true }
Assert-That ($marked.isFavorite -eq $true) 'Слово отмечено избранным'

$markedAgain = Invoke-Api -Method PUT -Path "/api/words/$wordId/favorite" -Body @{ isFavorite = $true }
Assert-That ($markedAgain.isFavorite -eq $true) 'Повторная отметка идемпотентна'

$favoriteList = Invoke-Api -Method GET -Path '/api/favorites'
Assert-That (@($favoriteList).Count -eq 1) "В избранном одно слово (получено $(@($favoriteList).Count))"
Assert-That ($favoriteList[0].wordId -eq $wordId) 'В избранном именно отмеченное слово'
Assert-That ($favoriteList[0].primaryTranslation.Length -gt 0) 'У избранного слова есть перевод'

$favoriteSession = Invoke-Api -Method POST -Path '/api/study/favorites'
Assert-That ($favoriteSession.totalItems -eq 1) "Сессия по избранному собрана: карточек $($favoriteSession.totalItems)"
Assert-That ($favoriteSession.currentCard.kind -eq 2) 'Карточка помечена как избранная'

# --- Кнопка «Не знаю» ------------------------------------------------------
Write-Step 'Кнопка «Не знаю»'

$dontKnow = Invoke-Api -Method POST -Path '/api/study/review' -Body @{
    itemId = $favoriteSession.currentCard.itemId
    answer = $null
    rating = 1
}
Assert-That ($dontKnow.isCorrect -eq $false) 'Ответ «не знаю» засчитан как неверный'
Assert-That ($dontKnow.rating -eq 1) 'Оценка — «забыл»'
Assert-That ($dontKnow.requeued -eq $true) 'Карточка вернулась в конец сессии'
Assert-That ($dontKnow.correctAnswer.Length -gt 0) "Ответ показан: $($dontKnow.correctAnswer)"

# --- Друзья ----------------------------------------------------------------
Write-Step 'Друзья'

$otherEmail = "buddy-$([Guid]::NewGuid().ToString('N').Substring(0, 8))@example.com"
$otherUsername = "buddy$([Guid]::NewGuid().ToString('N').Substring(0, 6))"

$other = Invoke-Api -Method POST -Path '/api/auth/register' -Anonymous -Body @{
    email      = $otherEmail
    password   = $Password
    username   = $otherUsername
    timeZoneId = 'Europe/Berlin'
}
Assert-That ($other.user.username -eq $otherUsername) "Второй пользователь зарегистрирован с ником @$otherUsername"

$search = Invoke-Api -Method GET -Path "/api/users/search?query=$otherUsername"
Assert-That (@($search).Count -eq 1) 'Поиск по нику нашёл человека'
Assert-That ($search[0].relation -eq 0) 'До заявки связи нет'

Invoke-Api -Method POST -Path '/api/friends' -Body @{ username = $otherUsername } | Out-Null
$afterRequest = Invoke-Api -Method GET -Path "/api/users/search?query=$otherUsername"
Assert-That ($afterRequest[0].relation -eq 1) 'После отправки заявки связь — исходящая'

$repeatRequest = Get-StatusCode -Method POST -Path '/api/friends' -Body @{ username = $otherUsername }
Assert-That ($repeatRequest -eq 409) "Повторная заявка отклонена (код $repeatRequest)"

# Принять заявку может только адресат: отправитель получает конфликт.
$ownRequest = Get-StatusCode -Method POST -Path "/api/friends/$otherUsername/accept"
Assert-That ($ownRequest -eq 409) "Принять собственную заявку нельзя (код $ownRequest)"

# Дальше действуем от имени второго пользователя.
$ownerToken = $script:token
$script:token = $other.accessToken

$incoming = Invoke-Api -Method GET -Path '/api/friends'
Assert-That ((@($incoming.incoming).Count) -eq 1) 'Адресат видит входящую заявку'

# Отклонение не блокирует навсегда: заявку можно отправить снова.
Invoke-Api -Method POST -Path "/api/friends/$newUsername/decline" | Out-Null
$afterDecline = Invoke-Api -Method GET -Path '/api/friends'
Assert-That ((@($afterDecline.incoming).Count) -eq 0) 'После отказа заявка ушла из входящих'

$script:token = $ownerToken
Invoke-Api -Method POST -Path '/api/friends' -Body @{ username = $otherUsername } | Out-Null
$script:token = $other.accessToken

Invoke-Api -Method POST -Path "/api/friends/$newUsername/accept" | Out-Null

$accepted = Invoke-Api -Method GET -Path '/api/friends'
Assert-That ((@($accepted.friends).Count) -eq 1) 'После повторной заявки дружба оформлена'
Assert-That ($accepted.friends[0].username -eq $newUsername) 'В друзьях тот, кто отправил заявку'
Assert-That ($accepted.friends[0].displayName.Length -gt 0) 'У друга есть отображаемое имя'

$script:token = $ownerToken

$mine = Invoke-Api -Method GET -Path '/api/friends'
Assert-That ((@($mine.friends).Count) -eq 1) 'Отправитель тоже видит друга'
Assert-That ($mine.friends[0].username -eq $otherUsername) 'У друга верный ник'

$profileWithFriend = Invoke-Api -Method GET -Path '/api/me'
Assert-That ($profileWithFriend.friendCount -eq 1) 'Счётчик друзей в профиле обновился'

Invoke-Api -Method DELETE -Path "/api/friends/$otherUsername" | Out-Null
$afterRemove = Invoke-Api -Method GET -Path '/api/friends'
Assert-That ((@($afterRemove.friends).Count) -eq 0) 'Друг удалён'

$removedStatus = Get-StatusCode -Method DELETE -Path "/api/friends/$otherUsername"
Assert-That ($removedStatus -eq 409) "Повторное удаление даёт конфликт (код $removedStatus)"

# --- Рейтинг недели --------------------------------------------------------
Write-Step 'Рейтинг недели'

# Дружба к этому моменту расторгнута, поэтому рейтинг снова из одной строки:
# проверяем и этот случай, и то, что своя строка помечена.
$soloBoard = Invoke-Api -Method GET -Path '/api/friends/leaderboard'
Assert-That ($soloBoard.entries.Count -eq 1) "Без друзей в рейтинге одна строка (получено $($soloBoard.entries.Count))"
Assert-That ($soloBoard.entries[0].isMe -eq $true) 'Единственная строка — своя'
Assert-That ($soloBoard.friendCount -eq 0) 'Друзей в рейтинге нет'
Assert-That ($soloBoard.entries[0].rank -eq 1) 'Первое место у единственного участника'
Assert-That ($soloBoard.entries[0].weeklyReviews -gt 0) `
    "Ответы за неделю посчитаны: $($soloBoard.entries[0].weeklyReviews)"

# Окно — семь дней: от сегодняшнего дня назад на шесть.
$windowDays = ([datetime]$soloBoard.to - [datetime]$soloBoard.from).Days + 1
Assert-That ($windowDays -eq 7) "Окно рейтинга — семь дней (получено $windowDays)"

# Теперь возвращаем дружбу и проверяем рейтинг вдвоём.
Invoke-Api -Method POST -Path '/api/friends' -Body @{ username = $otherUsername } | Out-Null
$script:token = $other.accessToken
Invoke-Api -Method POST -Path "/api/friends/$newUsername/accept" | Out-Null
$script:token = $ownerToken

$board = Invoke-Api -Method GET -Path '/api/friends/leaderboard'
Assert-That ($board.entries.Count -eq 2) "В рейтинге я и друг (получено $($board.entries.Count))"
Assert-That ($board.friendCount -eq 1) 'Один друг учтён'
Assert-That ((@($board.entries | Where-Object { $_.isMe })).Count -eq 1) 'Своя строка ровно одна'
Assert-That ($board.entries[0].rank -eq 1) 'Места начинаются с первого'

# Места не убывают: список отсортирован, и «2, 1» в нём появиться не может.
$ranks = @($board.entries | ForEach-Object { $_.rank })
$sorted = @($ranks | Sort-Object)
Assert-That (($ranks -join ',') -eq ($sorted -join ',')) "Места идут по возрастанию: $($ranks -join ', ')"

# Оба участника — живые люди со своими данными, а не заглушки.
Assert-That (($board.entries | Where-Object { $_.username.Length -lt 3 }).Count -eq 0) `
    'У каждого участника есть ник'

# Рейтинг доступен только участнику: без токена — 401.
$anonymousBoard = Get-StatusCode -Method GET -Path '/api/friends/leaderboard' -Anonymous
Assert-That ($anonymousBoard -eq 401) "Без токена рейтинг недоступен (код $anonymousBoard)"

# --- Свои колоды и слова ---------------------------------------------------
Write-Step 'Свои колоды и слова'

$emptyTitle = Get-StatusCode -Method POST -Path '/api/decks' -Body @{ title = '   ' }
Assert-That ($emptyTitle -eq 400) "Колода без названия отклонена (код $emptyTitle)"

$ownDeck = Invoke-Api -Method POST -Path '/api/decks' -Body @{
    title       = 'Слова с работы'
    description = 'То, что встречается на созвонах'
}
Assert-That ($ownDeck.isOwned -eq $true) 'Созданная колода помечена своей'
Assert-That ($ownDeck.title -eq 'Слова с работы') 'Название сохранено как есть'
Assert-That ($ownDeck.slug.Length -gt 4) "У колоды есть адрес: $($ownDeck.slug)"
Assert-That ($ownDeck.totalWords -eq 0) 'Новая колода пуста'

$decksWithOwn = Invoke-Api -Method GET -Path '/api/decks'
Assert-That ((@($decksWithOwn | Where-Object { $_.slug -eq $ownDeck.slug })).Count -eq 1) `
    'Своя колода видна в общем списке'

# Новое слово. «landscape» в поставляемом словаре нет, поэтому первый прогон
# добавляет его по-настоящему — и это корректное слово с корректным переводом,
# а не мусор, который потом попадался бы в неверных вариантах.
$afterWord = Invoke-Api -Method POST -Path "/api/decks/$($ownDeck.slug)/words" -Body @{
    word = @{ lemma = 'landscape'; translation = 'пейзаж'; partOfSpeech = 'noun' }
}
Assert-That ($afterWord.totalWords -eq 1) "Новое слово добавлено (слов: $($afterWord.totalWords))"

$ownWords = Invoke-Api -Method GET -Path "/api/decks/$($ownDeck.slug)/words"
Assert-That ($ownWords.Count -eq 1) 'Список слов колоды содержит слово'
Assert-That ($ownWords[0].lemma -eq 'landscape') 'Слово сохранено с тем же написанием'

$found = Invoke-Api -Method GET -Path '/api/words?query=landscape'
Assert-That ((@($found | Where-Object { $_.lemma -eq 'landscape' })).Count -ge 1) `
    'Слово попало в общий словарь и находится поиском'

# Повторное добавление того же слова в ту же колоду — конфликт, а не дубль.
$duplicateWord = Get-StatusCode -Method POST -Path "/api/decks/$($ownDeck.slug)/words" -Body @{
    word = @{ lemma = 'landscape'; translation = 'пейзаж'; partOfSpeech = 'noun' }
}
Assert-That ($duplicateWord -eq 409) "Повторное добавление того же слова отклонено (код $duplicateWord)"

$badPart = Get-StatusCode -Method POST -Path "/api/decks/$($ownDeck.slug)/words" -Body @{
    word = @{ lemma = 'whatever'; translation = 'что-то'; partOfSpeech = 'существительное' }
}
Assert-That ($badPart -eq 400) "Часть речи вне списка отклонена (код $badPart)"

$noTranslation = Get-StatusCode -Method POST -Path "/api/decks/$($ownDeck.slug)/words" -Body @{
    word = @{ lemma = 'whatever'; translation = '  '; partOfSpeech = 'noun' }
}
Assert-That ($noTranslation -eq 400) "Слово без перевода отклонено (код $noTranslation)"

$noWord = Get-StatusCode -Method POST -Path "/api/decks/$($ownDeck.slug)/words" -Body @{ }
Assert-That ($noWord -eq 400) "Запрос без слова и без идентификатора отклонён (код $noWord)"

# Существующее слово добавляется по идентификатору и не создаётся заново.
$withExisting = Invoke-Api -Method POST -Path "/api/decks/$($ownDeck.slug)/words" -Body @{ wordId = $wordId }
Assert-That ($withExisting.totalWords -eq 2) "Существующее слово добавлено (слов: $($withExisting.totalWords))"

$afterRemoveWord = Invoke-Api -Method DELETE -Path "/api/decks/$($ownDeck.slug)/words/$wordId"
Assert-That ($afterRemoveWord.totalWords -eq 1) 'Слово убрано из колоды'

$removeMissing = Get-StatusCode -Method DELETE -Path "/api/decks/$($ownDeck.slug)/words/$wordId"
Assert-That ($removeMissing -eq 404) "Повторное удаление слова даёт 404 (код $removeMissing)"

# Системную колоду править нельзя — и она отвечает 404, а не 403: 403
# подтверждал бы, что колода существует.
$systemDeckWrite = Get-StatusCode -Method POST -Path "/api/decks/$($deck.slug)/words" -Body @{ wordId = $wordId }
Assert-That ($systemDeckWrite -eq 404) "В системную колоду слово не добавить (код $systemDeckWrite)"

$systemDeckDelete = Get-StatusCode -Method DELETE -Path "/api/decks/$($deck.slug)"
Assert-That ($systemDeckDelete -eq 404) "Системную колоду не удалить (код $systemDeckDelete)"

# Чужая личная колода не видна и не правится: второй пользователь получает 404
# и на добавление слова, и на удаление колоды.
$script:token = $other.accessToken

$foreignAdd = Get-StatusCode -Method POST -Path "/api/decks/$($ownDeck.slug)/words" -Body @{ wordId = $wordId }
Assert-That ($foreignAdd -eq 404) "В чужую колоду слово не добавить (код $foreignAdd)"

$foreignDelete = Get-StatusCode -Method DELETE -Path "/api/decks/$($ownDeck.slug)"
Assert-That ($foreignDelete -eq 404) "Чужую колоду не удалить (код $foreignDelete)"

$script:token = $ownerToken

$deleted = Get-StatusCode -Method DELETE -Path "/api/decks/$($ownDeck.slug)"
Assert-That ($deleted -eq 204) "Своя колода удалена (код $deleted)"

$goneDeck = Get-StatusCode -Method GET -Path "/api/decks/$($ownDeck.slug)"
Assert-That ($goneDeck -eq 404) "Удалённая колода больше не отдаётся (код $goneDeck)"

$wordSurvived = Invoke-Api -Method GET -Path '/api/words?query=landscape'
Assert-That ((@($wordSurvived | Where-Object { $_.lemma -eq 'landscape' })).Count -ge 1) `
    'Слово осталось в общем словаре после удаления колоды'

# --- Прерывание сессии -----------------------------------------------------
Write-Step 'Прерывание сессии'

$noContent = Get-StatusCode -Method DELETE -Path '/api/study/session'
Assert-That ($noContent -eq 204) "Сессия прервана (код $noContent)"

$newSession = Invoke-Api -Method GET -Path "/api/study/session?deckId=$($deck.id)"
Assert-That ($newSession.sessionId -ne $session.sessionId) 'После прерывания создаётся новая сессия'

# --- Аватар ----------------------------------------------------------------
Write-Step 'Аватар'

# Картинка 1x1: проверяется не содержимое, а путь файла через сервер —
# от проверки формата до отдачи статикой. Настоящий PNG нужен потому,
# что формат определяется по сигнатуре, и «почти PNG» сервер отклонит.
$pngBytes = [Convert]::FromBase64String(
    'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==')

$garbage = Send-AvatarFile -Bytes ([Text.Encoding]::UTF8.GetBytes('<?php echo 1; ?>')) `
    -FileName 'evil.png' -ContentType 'image/png'
Assert-That ($garbage.Status -eq 400) "Файл, только притворяющийся картинкой, отклонён (код $($garbage.Status))"

$uploaded = Send-AvatarFile -Bytes $pngBytes -FileName 'avatar.png' -ContentType 'image/png'
Assert-That ($uploaded.Status -eq 200) "Аватар загружен (код $($uploaded.Status))"

$avatarUser = $uploaded.Body | ConvertFrom-Json
Assert-That ($avatarUser.avatarUrl -like '/avatars/*') "Профиль вернул адрес аватара: $($avatarUser.avatarUrl)"

$avatarFile = Invoke-WebRequest -Uri "$BaseUrl$($avatarUser.avatarUrl)" -UseBasicParsing
Assert-That ($avatarFile.StatusCode -eq 200) 'Аватар отдаётся статикой'
Assert-That ($avatarFile.Headers['Content-Type'] -like 'image/*') "Тип содержимого — изображение ($($avatarFile.Headers['Content-Type']))"

$firstAvatarUrl = $avatarUser.avatarUrl
$replaced = Send-AvatarFile -Bytes $pngBytes -FileName 'avatar2.png' -ContentType 'image/png'
$secondAvatarUrl = ($replaced.Body | ConvertFrom-Json).avatarUrl
Assert-That ($secondAvatarUrl -ne $firstAvatarUrl) 'Повторная загрузка меняет адрес — кеш браузера не покажет старое фото'
Assert-That ((Get-StatusCode -Method GET -Path $firstAvatarUrl -Anonymous) -eq 404) 'Прежний файл удалён'

$removedAvatar = Invoke-Api -Method DELETE -Path '/api/me/avatar'
Assert-That ($null -eq $removedAvatar.avatarUrl) 'Аватар удалён из профиля'
Assert-That ((Get-StatusCode -Method GET -Path $secondAvatarUrl -Anonymous) -eq 404) 'Файл аватара удалён'
Assert-That ((Get-StatusCode -Method DELETE -Path '/api/me/avatar') -eq 200) 'Повторное удаление не ошибка'

# --- Telegram --------------------------------------------------------------
Write-Step 'Telegram'

if (-not $botToken) {
    Write-Host '   ПРОПУЩЕНО  Токен бота не задан (Telegram:BotToken) — проверки подписи пропущены.' -ForegroundColor Yellow
}
else {
    # Идентификатор Telegram берётся случайным: проверку можно запускать
    # многократно, и уникальный индекс по telegram_id не помешает.
    $telegramId = 900000000000 + (Get-Random -Minimum 100000 -Maximum 999999)

    $status = Invoke-Api -Method GET -Path '/api/me/telegram'
    Assert-That ($status.linked -eq $false) 'Изначально Telegram не привязан'

    $garbageInit = Get-StatusCode -Method POST -Path '/api/auth/telegram' -Anonymous `
        -Body @{ initData = 'user=%7B%22id%22%3A1%7D&auth_date=1&hash=deadbeef' }
    Assert-That ($garbageInit -eq 401) "Неподписанные данные отклонены (код $garbageInit)"

    # Подпись настоящая, но сделана другим токеном: так выглядела бы попытка
    # выдать себя за чужого пользователя.
    $foreignToken = "0000000000:$([Guid]::NewGuid().ToString('N'))"
    $foreignInit = New-TelegramInitData -BotToken $foreignToken -UserId $telegramId
    $foreignStatus = Get-StatusCode -Method POST -Path '/api/auth/telegram' -Anonymous `
        -Body @{ initData = $foreignInit }
    Assert-That ($foreignStatus -eq 401) "Подпись чужим токеном отклонена (код $foreignStatus)"

    $initData = New-TelegramInitData -BotToken $botToken -UserId $telegramId

    $notLinked = Get-StatusCode -Method POST -Path '/api/auth/telegram' -Anonymous -Body @{ initData = $initData }
    Assert-That ($notLinked -eq 403) "Непривязанный Telegram получает 403, а не 401 (код $notLinked)"

    $link = Invoke-Api -Method POST -Path '/api/me/telegram/link'
    Assert-That ($link.code.Length -ge 20) "Выдан код привязки длиной $($link.code.Length)"
    Assert-That ($link.expiresAtUtc -gt (Get-Date)) 'Срок действия кода в будущем'

    $linked = Invoke-Api -Method POST -Path '/api/auth/telegram/link' -Anonymous `
        -Body @{ initData = $initData; code = $link.code }
    Assert-That ($linked.accessToken.Length -gt 20) 'Привязка из мини-аппа вернула токен доступа'
    Assert-That ($linked.user.telegramLinked -eq $true) 'Профиль подтверждает привязку'

    $signedIn = Invoke-Api -Method POST -Path '/api/auth/telegram' -Anonymous -Body @{ initData = $initData }
    Assert-That ($signedIn.accessToken.Length -gt 20) 'Повторный вход по данным Telegram работает'

    $staleCode = Invoke-Api -Method POST -Path '/api/me/telegram/link'
    $reused = Get-StatusCode -Method POST -Path '/api/auth/telegram/link' -Anonymous `
        -Body @{ initData = $initData; code = $link.code }
    Assert-That ($reused -eq 400) "Использованный код не срабатывает второй раз (код $reused)"
    Assert-That ($staleCode.code -ne $link.code) 'Новый код отличается от прежнего'

    # Один Telegram — один аккаунт: иначе непонятно, чей прогресс показывать.
    $ownerToken = $script:token
    $script:token = $other.accessToken
    $otherLink = Invoke-Api -Method POST -Path '/api/me/telegram/link'
    $conflict = Get-StatusCode -Method POST -Path '/api/auth/telegram/link' -Anonymous `
        -Body @{ initData = $initData; code = $otherLink.code }
    Assert-That ($conflict -eq 409) "Тот же Telegram нельзя привязать к другому аккаунту (код $conflict)"
    $script:token = $ownerToken

    $unlinked = Invoke-Api -Method DELETE -Path '/api/me/telegram'
    Assert-That ($unlinked.linked -eq $false) 'Telegram отвязан'

    $afterUnlink = Get-StatusCode -Method POST -Path '/api/auth/telegram' -Anonymous -Body @{ initData = $initData }
    Assert-That ($afterUnlink -eq 403) "После отвязки вход в мини-апп снова недоступен (код $afterUnlink)"
}

# --- Итог ------------------------------------------------------------------
Write-Host ''
Write-Host ('-' * 60)
if ($script:failed -eq 0) {
    Write-Host "Все проверки пройдены: $script:passed" -ForegroundColor Green
    exit 0
}

Write-Host "Пройдено: $script:passed, провалено: $script:failed" -ForegroundColor Red
exit 1
