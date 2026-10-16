# ─────────────────────────────────────────────────────────────────────────────
# Образ приложения: два процесса из одной сборки.
#
# Стадии api и bot отличаются только тем, что в них копируется и чем они
# запускаются: сборка у них общая. Так сделано потому, что API и бот — части
# одного приложения и используют одни и те же слои; две независимые сборки
# разошлись бы версиями.
# ─────────────────────────────────────────────────────────────────────────────

# ── Сборка ───────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Сначала только файлы проектов: восстановление пакетов — самый долгий шаг,
# и он должен оставаться в кеше, пока не изменились сами зависимости.
# Правка исходников после этого не запускает скачивание пакетов заново.
COPY Directory.Build.props nuget.config EnglishTrainer.sln ./
COPY EnglishTrainer.Domain/*.csproj EnglishTrainer.Domain/
COPY EnglishTrainer.Application/*.csproj EnglishTrainer.Application/
COPY EnglishTrainer.Infrastructure/*.csproj EnglishTrainer.Infrastructure/
COPY EnglishTrainer.Api/*.csproj EnglishTrainer.Api/
COPY EnglishTrainer.Bot/*.csproj EnglishTrainer.Bot/

# Тестовые проекты восстанавливаются отдельно и в образ не попадают: в образе
# им делать нечего, а их пакеты (xunit, тестовый хост) — лишние сотни мегабайт.
RUN dotnet restore EnglishTrainer.Api/EnglishTrainer.Api.csproj \
    && dotnet restore EnglishTrainer.Bot/EnglishTrainer.Bot.csproj

COPY . .

RUN dotnet publish EnglishTrainer.Api/EnglishTrainer.Api.csproj \
        --configuration Release --no-restore --output /out/api \
    && dotnet publish EnglishTrainer.Bot/EnglishTrainer.Bot.csproj \
        --configuration Release --no-restore --output /out/bot

# ── API: сайт и мини-апп ─────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api

WORKDIR /app

COPY --from=build /out/api ./

# Порт внутри контейнера. Наружу его публикует только обратный прокси:
# если открыть его напрямую, клиент сможет подделать заголовки X-Forwarded-*.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Каталог аватаров создаётся при первом обращении и переживает пересоздание
# контейнера только потому, что объявлен томом в compose-файле.
VOLUME ["/app/wwwroot/avatars"]

CMD ["dotnet", "EnglishTrainer.Api.dll"]

# ── Бот: напоминания и слово дня ─────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS bot

WORKDIR /app

COPY --from=build /out/bot ./

# Портов нет: бот сам ходит в Telegram (long polling), входящие соединения
# ему не нужны — поэтому наружу он не публикуется вообще.
CMD ["dotnet", "EnglishTrainer.Bot.dll"]
