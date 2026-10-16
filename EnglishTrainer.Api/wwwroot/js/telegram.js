/*
 * Интеграция с Telegram Mini App.
 *
 * Задача модуля — сделать так, чтобы интерфейс вёл себя как родной внутри
 * Telegram: занял всю высоту окна, подчинился его теме и вошёл в аккаунт
 * без пароля. Всё остальное приложение о Telegram не знает: те же страницы
 * и те же эндпоинты работают и в обычном браузере.
 *
 * Скрипт Telegram подключается в index.html. Если сети нет (или страница
 * открыта не в Telegram), window.Telegram не появится — и это нормальный
 * сценарий, а не ошибка: приложение просто работает как веб-версия.
 */

import { api } from './api.js';
import { setForcedTheme } from './theme.js';

/** Объект Telegram.WebApp или null, если страница открыта вне Telegram. */
const webApp = window.Telegram?.WebApp ?? null;

/**
 * Открыта ли страница внутри Telegram.
 *
 * Признак — не наличие объекта (скрипт может быть загружен и в обычном
 * браузере при открытии отладочной ссылки), а непустой initData: именно её
 * Telegram подписывает и передаёт только своему клиенту.
 */
export function isTelegram() {
    return Boolean(webApp?.initData);
}

export function telegramInitData() {
    return webApp?.initData ?? '';
}

/** Имя пользователя Telegram — для приветствия. */
export function telegramUserName() {
    const user = webApp?.initDataUnsafe?.user;

    return user?.first_name ?? user?.username ?? '';
}

/**
 * Готовит интерфейс к работе внутри Telegram.
 *
 * @returns {boolean} true, если страница действительно открыта в Telegram.
 */
export function initTelegram() {
    const active = isTelegram();

    if (!webApp) {
        return false;
    }

    // ready() сообщает клиенту, что показывать заставку больше не нужно,
    // expand() разворачивает окно на полную высоту. Без второго вызова
    // мини-апп открывается половинкой экрана и выглядит обрезанным.
    webApp.ready?.();
    webApp.expand?.();

    if (!active) {
        return false;
    }

    document.body.classList.add('is-telegram');

    // Вертикальный свайп закрывает мини-апп — это поведение клиента, а не наше.
    // Пока человек листает карточку или тянется к полю ввода, оно выглядит как
    // «приложение закрылось само». Отключается там, где клиент это умеет
    // (Bot API 7.7+); на старых клиентах вызов просто ничего не делает.
    webApp.disableVerticalSwipes?.();

    applyTelegramTheme();
    webApp.onEvent?.('themeChanged', applyTelegramTheme);

    applyViewport();
    webApp.onEvent?.('viewportChanged', applyViewport);

    watchKeyboard();

    return true;
}

/**
 * Следит за экранной клавиатурой.
 *
 * Когда клавиатура открыта, нижняя панель разделов оказывается прямо над ней —
 * то есть там же, где поле ввода. Палец попадает по вкладке, а не по полю,
 * и выглядит это как «в поле ничего нельзя ввести». Поэтому на время клавиатуры
 * панель убирается (в CSS, по классу `keyboard-open`), а поле прокручивается
 * в видимую часть экрана.
 *
 * Признаков два, и главный — фокус в поле ввода, а не размер окна: в WebView
 * геометрия зависит от режима окна (adjustResize), и по одной высоте клавиатуру
 * видно не всегда. Уменьшение видимой части берётся вторым признаком — оно нужно,
 * чтобы прокрутить поле уже после того, как клавиатура выехала.
 */
function watchKeyboard() {
    const viewport = window.visualViewport;

    const isFieldFocused = () => {
        const tag = document.activeElement?.tagName;

        return tag === 'INPUT' || tag === 'TEXTAREA';
    };

    const update = () => {
        const shrunk = viewport ? window.innerHeight - viewport.height > 120 : false;
        const typing = isFieldFocused();

        document.body.classList.toggle('keyboard-open', typing || shrunk);

        if (shrunk && document.activeElement?.scrollIntoView) {
            document.activeElement.scrollIntoView({ block: 'center' });
        }
    };

    document.addEventListener('focusin', update);

    // Небольшая задержка: при переходе между полями сначала приходит focusout
    // и только потом focusin, и без неё панель успевает мигнуть.
    document.addEventListener('focusout', () => setTimeout(update, 50));

    if (viewport) {
        viewport.addEventListener('resize', update);
        viewport.addEventListener('scroll', update);
    }
}

/** Закрывает мини-апп. Используется кнопкой «назад» на верхнем уровне. */
export function closeTelegram() {
    webApp?.close?.();
}

/** Открывает ссылку во внешнем браузере. */
export function openExternal(url) {
    if (webApp?.openLink) {
        webApp.openLink(url);
        return;
    }

    window.open(url, '_blank', 'noopener');
}

/**
 * Спрашивает подтверждение действия.
 *
 * Внутри Telegram используется его собственный диалог: window.confirm в webview
 * может быть заблокирован, а если и сработает — выглядит чужеродно на фоне
 * системных окон мессенджера. Возвращается промис, поэтому вызывающий код
 * одинаково ждёт ответа в обоих случаях.
 */
export function confirmAction(message) {
    if (webApp?.showConfirm) {
        return new Promise((resolve) => {
            webApp.showConfirm(message, (confirmed) => resolve(Boolean(confirmed)));
        });
    }

    return Promise.resolve(window.confirm(message));
}

/**
 * Вход по данным Telegram.
 *
 * Пароль здесь не участвует: доказательство — подпись Telegram, которую
 * сервер проверяет токеном бота. Запрос отправляется как анонимный: своего
 * токена в этот момент ещё нет, а старый, если он остался от другого человека,
 * серверу только помешает.
 *
 * Ответ 403 означает, что этот Telegram ещё не привязан ни к одному аккаунту.
 * Это не истёкший сеанс, и обрабатывать его нужно отдельно — иначе вход
 * в мини-апп выглядел бы как выход из веб-версии.
 */
export async function signInWithTelegram() {
    return api('/api/auth/telegram', {
        method: 'POST',
        body: { initData: telegramInitData() },
        anonymous: true
    });
}

/* ── Тема и размеры окна ─────────────────────────────────────────────────── */

/*
 * Цвета берутся не отсюда, а из CSS-переменных --tg-theme-*: их выставляет
 * сам скрипт Telegram и обновляет при смене темы. Дублировать эту работу
 * в JavaScript незачем — оформление остаётся в styles.css.
 *
 * Здесь только то, что переменными не выражается: какую из двух палитр
 * считать активной и насколько окно перекрыто системными панелями.
 */
function applyTelegramTheme() {
    setForcedTheme(webApp.colorScheme === 'dark' ? 'dark' : 'light');
}

/**
 * Высота окна и отступы безопасной зоны.
 *
 * В Telegram окно не равно высоте экрана: сверху — заголовок, снизу — панель
 * с кнопкой. Без учёта этих отступов последний ряд кнопок уезжает под панель,
 * и до него невозможно дотянуться.
 */
function applyViewport() {
    const root = document.documentElement;
    const viewportHeight = webApp.viewportStableHeight || webApp.viewportHeight;

    if (viewportHeight) {
        root.style.setProperty('--app-height', `${viewportHeight}px`);
    }

    const safe = webApp.safeAreaInset;
    const content = webApp.contentSafeAreaInset;

    if (safe) {
        root.style.setProperty('--tg-inset-top', `${safe.top ?? 0}px`);
        root.style.setProperty('--tg-inset-bottom', `${safe.bottom ?? 0}px`);
    }

    if (content) {
        root.style.setProperty('--tg-content-inset-top', `${content.top ?? 0}px`);
        root.style.setProperty('--tg-content-inset-bottom', `${content.bottom ?? 0}px`);
    }
}

/* ── Кнопка «назад» в заголовке Telegram ─────────────────────────────────── */

let backHandler = null;

/**
 * Показывает кнопку «назад» и связывает её с обработчиком.
 *
 * Кнопка принадлежит Telegram, а не странице: своего заголовка у мини-аппа нет,
 * и без неё из вложенного экрана нельзя выйти ничем, кроме жеста.
 */
export function showBackButton(handler) {
    if (!webApp?.BackButton) {
        return;
    }

    hideBackButton();

    backHandler = handler;
    webApp.BackButton.onClick(backHandler);
    webApp.BackButton.show();
}

export function hideBackButton() {
    if (!webApp?.BackButton || backHandler === null) {
        return;
    }

    webApp.BackButton.offClick(backHandler);
    webApp.BackButton.hide();

    backHandler = null;
}
