/*
 * Профиль: кто вы, как вас найти и сводка прогресса.
 */

import { api } from '../api.js';
import { state } from '../state.js';
import { esc, icon, plural, toast, avatar, formatDate, setLoading } from '../ui.js';

export async function render(container, context) {
    container.innerHTML = '<p class="hint">Загрузка…</p>';

    try {
        const profile = await api('/api/me');

        state.profile = profile;

        const user = profile.user;

        container.innerHTML = `
            <div class="card" style="display: flex; gap: 18px; align-items: center; flex-wrap: wrap">
                ${avatar(user, 'avatar--lg')}
                <div style="flex: 1; min-width: 200px">
                    <h2>${esc(user.displayName)}</h2>
                    <p class="hint" style="margin-top: 4px">@${esc(user.username)} · ${esc(user.email)}</p>
                </div>
                <div style="text-align: right">
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

            <div class="section-title">${icon('user', 'icon--sm')} Данные аккаунта</div>

            <div class="card">
                <dl class="kv">
                    <div class="kv__row"><dt>Ник</dt><dd>@${esc(user.username)}</dd></div>
                    <div class="kv__row"><dt>Email</dt><dd>${esc(user.email)}</dd></div>
                    <div class="kv__row"><dt>Дата регистрации</dt><dd>${esc(formatDate(user.createdAt))}</dd></div>
                    <div class="kv__row"><dt>Часовой пояс</dt><dd>${esc(user.timeZoneId)}</dd></div>
                    <div class="kv__row"><dt>Telegram</dt><dd>${user.telegramLinked ? 'привязан' : 'не привязан'}</dd></div>
                </dl>
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
                </div>
            </form>`;

        container.querySelector('#profile-form').addEventListener('submit', (event) => save(event, container, context));
        container.querySelector('#reload').addEventListener('click', () => render(container, context));
    } catch (error) {
        toast(error.message, 'error');
        container.innerHTML = `<p class="hint">${esc(error.message)}</p>`;
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
