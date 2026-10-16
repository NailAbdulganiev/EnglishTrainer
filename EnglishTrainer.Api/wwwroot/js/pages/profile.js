/*
 * Профиль: кто вы, как вас найти и сводка прогресса.
 */

import { api, apiUpload } from '../api.js';
import { state } from '../state.js';
import { isTelegram } from '../telegram.js';
import {
    esc, icon, plural, toast, avatar, formatDate, setLoading, achievementBadge, achievementGoal
} from '../ui.js';

export async function render(container, context) {
    container.innerHTML = '<p class="hint">Загрузка…</p>';

    try {
        const [profile, achievements] = await Promise.all([
            api('/api/me'),
            // Чтение достижений проверяет пороги и выдаёт заработанное,
            // поэтому страница профиля — ещё одна точка, где награда
            // подсвечивается без отдельной кнопки «проверить».
            api('/api/achievements')
        ]);

        state.profile = profile;

        const user = profile.user;

        container.innerHTML = `
            <div class="card avatar-editor">
                ${avatar(user, 'avatar--lg')}

                <div class="avatar-editor__actions">
                    <div>
                        <h2>${esc(user.displayName)}</h2>
                        <p class="hint" style="margin-top: 4px">@${esc(user.username)} · ${esc(user.email)}</p>
                    </div>

                    <div class="row">
                        <button type="button" class="btn btn--ghost btn--sm" id="avatar-upload">
                            ${icon('plus', 'icon--sm')} ${user.avatarUrl ? 'Заменить фото' : 'Загрузить фото'}
                        </button>
                        <button type="button" class="btn btn--ghost btn--sm" id="avatar-remove"
                                ${user.avatarUrl ? '' : 'hidden'}>
                            ${icon('close', 'icon--sm')} Удалить
                        </button>
                        <!-- Поле выбора файла скрыто, его открывает кнопка выше. -->
                        <input type="file" id="avatar-file"
                               accept="image/jpeg,image/png,image/webp">
                    </div>

                    <p class="hint">JPEG, PNG или WebP, не больше 2 МиБ.</p>
                </div>

                <div style="margin-left: auto; text-align: right">
                    <div class="badge badge--flame">🔥 серия ${plural(profile.currentStreak, ['день', 'дня', 'дней'])}</div>
                </div>
            </div>

            <div class="section-title">${icon('chart', 'icon--sm')} Прогресс</div>

            <div class="stat-grid">
                ${tile(profile.learnedCards, 'выучено', 'accent')}
                ${tile(profile.cardsInProgress, 'в работе')}
                ${tile(profile.totalCards, 'всего начато')}
                ${tile(profile.reviewedTotal, 'ответов всего')}
                ${tile(profile.longestStreak, 'лучшая серия')}
                ${tile(profile.favoriteCount, 'в избранном')}
                ${tile(profile.friendCount, 'друзей')}
            </div>

            <div class="section-title">${icon('star', 'icon--sm')} Достижения
                <span class="badge">${achievements.unlockedCount} из ${achievements.total}</span>
            </div>

            ${achievementGoal(achievements)}

            <div class="achievement-grid">
                ${achievements.items.map(achievementBadge).join('')}
            </div>

            <div class="section-title">${icon('user', 'icon--sm')} Данные аккаунта</div>

            <div class="card">
                <dl class="kv">
                    <div class="kv__row"><dt>Ник</dt><dd>@${esc(user.username)}</dd></div>
                    <div class="kv__row"><dt>Email</dt><dd>${esc(user.email)}</dd></div>
                    <div class="kv__row"><dt>Дата регистрации</dt><dd>${esc(formatDate(user.createdAt))}</dd></div>
                    <div class="kv__row"><dt>Часовой пояс</dt><dd>${esc(user.timeZoneId)}</dd></div>
                </dl>
            </div>

            <div class="section-title">${icon('send', 'icon--sm')} Telegram</div>

            <div class="card">
                <p class="hint" id="telegram-state">
                    ${user.telegramLinked
                        ? 'Telegram привязан: напоминания и слово дня приходят в чат с ботом.'
                        : 'Telegram не привязан. Без него напоминания присылать некуда — браузерные не работают, когда вкладка закрыта.'}
                </p>

                <div class="row" style="margin-top: 12px">
                    <button type="button" class="btn btn--ghost btn--sm" id="telegram-link">
                        ${icon('send', 'icon--sm')} ${user.telegramLinked ? 'Привязать другой аккаунт' : 'Привязать Telegram'}
                    </button>
                    <button type="button" class="btn btn--ghost btn--sm" id="telegram-unlink"
                            ${user.telegramLinked ? '' : 'hidden'}>
                        ${icon('close', 'icon--sm')} Отвязать
                    </button>
                </div>

                <div id="telegram-code-slot"></div>
            </div>

            <div class="section-title">${icon('refresh', 'icon--sm')} Приложение</div>

            <div class="card">
                <div class="row" style="align-items: center; gap: 12px; flex-wrap: wrap">
                    <div style="flex: 1; min-width: 200px">
                        <b>Что нового</b>
                        <p class="hint" style="margin-top: 4px">
                            Что исправлено и что появилось — по версиям.
                        </p>
                    </div>

                    <button type="button" class="btn btn--ghost" id="to-updates">
                        ${icon('refresh', 'icon--sm')} Открыть
                        <span class="muted" id="app-version"></span>
                    </button>
                </div>
            </div>

            <div class="section-title">${icon('settings', 'icon--sm')} Изменить</div>
            <form class="card" id="profile-form">
                <div class="form-grid">
                    <label class="field">
                        <span>Ник <em class="muted">— по нему вас находят друзья</em></span>
                        <input type="text" id="username" value="${esc(user.username)}"
                               minlength="3" maxlength="32" autocomplete="off">
                    </label>

                    <label class="field">
                        <span>Отображаемое имя</span>
                        <input type="text" id="displayName" value="${esc(user.displayName)}" maxlength="64">
                    </label>
                </div>

                <p class="hint">
                    В нике допустимы латинские буквы, цифры, дефис и подчёркивание.
                    Регистр не важен: «Ivan» и «ivan» — один и тот же ник.
                </p>

                <div class="row" style="margin-top: 18px">
                    <button type="submit" class="btn btn--primary">Сохранить</button>
                    <button type="button" class="btn btn--ghost" id="reload">Отменить</button>

                    <!-- В мини-аппе кнопки выхода нет: выход означал бы лишь сброс
                         токена, а войти обратно можно только тем же аккаунтом
                         Telegram. Кнопка, которая ничего не меняет, хуже её
                         отсутствия. -->
                    <button type="button" class="btn btn--ghost" id="logout"
                            ${isTelegram() ? 'hidden' : ''}>Выйти</button>
                </div>
            </form>`;

        container.querySelector('#profile-form').addEventListener('submit', (event) => save(event, container, context));
        container.querySelector('#reload').addEventListener('click', () => render(container, context));

        container.querySelector('#logout')?.addEventListener('click', context.logout);

        container.querySelector('#to-updates').addEventListener('click', () => context.navigate('updates'));

        // Версия спрашивается у сервера: номер в интерфейсе должен совпадать
        // с той сборкой, которая работает. Не ответил — кнопка остаётся без номера.
        api('/api/version', { anonymous: true, quiet: true })
            .then((info) => {
                const slot = container.querySelector('#app-version');

                if (slot && info?.version) {
                    slot.textContent = `v${info.version}`;
                }
            })
            .catch(() => { });

        bindAvatar(container, context);
        bindTelegram(container, context);
    } catch (error) {
        toast(error.message, 'error');
        container.innerHTML = `<p class="hint">${esc(error.message)}</p>`;
    }
}

/* ── Привязка Telegram ──────────────────────────────────────────────────── */

function bindTelegram(container, context) {
    container.querySelector('#telegram-link')
        .addEventListener('click', () => requestLinkCode(container, context));

    container.querySelector('#telegram-unlink')
        ?.addEventListener('click', () => unlinkTelegram(container, context));
}

async function requestLinkCode(container, context) {
    setLoading(true);

    try {
        const link = await api('/api/me/telegram/link', { method: 'POST' });

        container.querySelector('#telegram-code-slot').innerHTML = `
            <div class="link-code">
                <p class="hint">Код действует 15 минут и срабатывает один раз.</p>
                <p class="link-code__value">${esc(link.code)}</p>
                ${link.deepLink
                    ? `<p class="hint">Откройте бота и отправьте ему этот код — или нажмите
                           <a href="${esc(link.deepLink)}" target="_blank" rel="noopener">ссылку на бота</a>,
                           она подставит код сама.</p>`
                    : `<p class="hint">Отправьте боту команду <code>/link ${esc(link.code)}</code>.</p>`}
            </div>`;
    } catch (error) {
        toast(error.message, 'error');
    } finally {
        setLoading(false);
    }
}

async function unlinkTelegram(container, context) {
    setLoading(true);

    try {
        await api('/api/me/telegram', { method: 'DELETE' });
        toast('Telegram отвязан', 'ok');

        await render(container, context);
    } catch (error) {
        toast(error.message, 'error');
    } finally {
        setLoading(false);
    }
}

/* ── Аватар ─────────────────────────────────────────────────────────────── */

function bindAvatar(container, context) {
    const input = container.querySelector('#avatar-file');

    // Клик по кнопке открывает системный выбор файла. Так вид карточки
    // не зависит от того, как браузер рисует собственный input.
    container.querySelector('#avatar-upload')
        .addEventListener('click', () => input.click());

    input.addEventListener('change', () => uploadAvatar(input.files[0], container, context));

    const remove = container.querySelector('#avatar-remove');

    if (remove) {
        remove.addEventListener('click', () => removeAvatar(container, context));
    }
}

/**
 * Загрузка фото.
 *
 * Размер проверяется до отправки: гонять по сети двухмегабайтный файл, чтобы
 * получить отказ, — плохой тон, а на медленном соединении это ещё и заметно.
 */
async function uploadAvatar(file, container, context) {
    if (!file) {
        return;
    }

    if (!file.type.startsWith('image/')) {
        toast('Нужен файл изображения.', 'error');
        return;
    }

    const form = new FormData();
    form.append('file', file);

    setLoading(true);

    try {
        const user = await apiUpload('/api/me/avatar', form);

        state.user = user;
        toast('Аватар обновлён', 'ok');

        // Страница перерисовывается целиком: кружок аватара есть и в профиле,
        // и в блоке пользователя внизу меню, и оба должны показать новый файл.
        await render(container, context);
        context.refreshShell();
    } catch (error) {
        toast(error.message, 'error');
    } finally {
        setLoading(false);
    }
}

async function removeAvatar(container, context) {
    setLoading(true);

    try {
        const user = await api('/api/me/avatar', { method: 'DELETE' });

        state.user = user;
        toast('Аватар удалён', 'ok');

        await render(container, context);
        context.refreshShell();
    } catch (error) {
        toast(error.message, 'error');
    } finally {
        setLoading(false);
    }
}

function tile(value, label, modifier = '') {
    return `<div class="stat ${modifier ? `stat--${modifier}` : ''}"><b>${esc(value)}</b><span>${esc(label)}</span></div>`;
}

async function save(event, container, context) {
    event.preventDefault();

    const body = {
        username: container.querySelector('#username').value.trim(),
        displayName: container.querySelector('#displayName').value.trim()
    };

    setLoading(true);

    try {
        const user = await api('/api/me/profile', { method: 'PUT', body });

        state.user = user;
        toast('Профиль обновлён', 'ok');

        // Ник показывается в навигации и в списке друзей, поэтому страницу
        // перерисовываем целиком, а не правим одно поле.
        await render(container, context);
        context.refreshShell();
    } catch (error) {
        // Занятый ник и некорректные данные — ожидаемые ошибки: сервер уже
        // сформулировал понятный текст, показываем его как есть.
        toast(error.message, 'error');
    } finally {
        setLoading(false);
    }
}
