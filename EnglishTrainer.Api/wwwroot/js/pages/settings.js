/*
 * Настройки обучения: лимиты, цель, напоминание и часовой пояс.
 */

import { api } from '../api.js';
import { state } from '../state.js';
import { esc, icon, toast, setLoading } from '../ui.js';

export async function render(container, context) {
    container.innerHTML = '<p class="hint">Загрузка…</p>';

    try {
        const profile = await api('/api/me');
        const settings = profile.settings;

        state.profile = profile;

        container.innerHTML = `
            <form id="settings-form">
                <div class="card">
                    <div class="section-title" style="margin: 0 0 16px">
                        ${icon('settings', 'icon--sm')} Обучение
                    </div>

                    <div class="form-grid">
                        <label class="field">
                            <span>Новых слов в день</span>
                            <input type="number" id="newWordsPerDay" min="0" max="100" value="${settings.newWordsPerDay}">
                        </label>

                        <label class="field">
                            <span>Карточек за сессию</span>
                            <input type="number" id="maxCardsPerSession" min="5" max="500" value="${settings.maxCardsPerSession}">
                        </label>

                        <label class="field">
                            <span>Дневная цель, ответов</span>
                            <input type="number" id="dailyGoal" min="1" max="1000" value="${settings.dailyGoal}">
                        </label>

                        <label class="field">
                            <span>Время напоминания</span>
                            <input type="time" id="reminderTime" value="${esc(String(settings.reminderTime).slice(0, 5))}">
                        </label>
                    </div>

                    <div class="stack" style="margin-top: 18px">
                        <label class="toggle">
                            <input type="checkbox" id="dailyReminderEnabled" ${settings.dailyReminderEnabled ? 'checked' : ''}>
                            <span>Напоминать о занятиях</span>
                        </label>

                        <label class="toggle">
                            <input type="checkbox" id="showTranscriptionUpfront" ${settings.showTranscriptionUpfront ? 'checked' : ''}>
                            <span>Показывать транскрипцию сразу</span>
                        </label>
                    </div>

                    <p class="hint">
                        Лимит новых слов — защита от выгорания, а не ограничение: при 50 новых словах
                        в день через неделю накопится несколько сотен карточек к повторению.
                    </p>

                    <div class="row" style="margin-top: 18px">
                        <button type="submit" class="btn btn--primary">Сохранить</button>
                        <button type="button" class="btn btn--ghost" id="reset-settings">Вернуть значения</button>
                    </div>
                </div>
            </form>

            <div class="card">
                <div class="section-title" style="margin: 0 0 16px">
                    ${icon('refresh', 'icon--sm')} Часовой пояс
                </div>

                <p class="hint" style="margin-bottom: 14px">
                    От пояса зависят дневная статистика, серия дней и время напоминания.
                    Сейчас на сервере: <b>${esc(profile.user.timeZoneId)}</b>.
                </p>

                <div class="row">
                    <select id="timezone" style="flex: 1; min-width: 220px"></select>
                    <button type="button" class="btn btn--primary" id="save-timezone">Сменить пояс</button>
                    <button type="button" class="btn btn--ghost" id="detect-timezone">Определить по браузеру</button>
                </div>
            </div>`;

        fillTimeZones(container.querySelector('#timezone'), profile.user.timeZoneId);

        container.querySelector('#settings-form').addEventListener('submit', (event) => saveSettings(event, container, context));
        container.querySelector('#reset-settings').addEventListener('click', () => render(container, context));
        container.querySelector('#save-timezone').addEventListener('click', () => saveTimeZone(container, context));
        container.querySelector('#detect-timezone').addEventListener('click', () => {
            container.querySelector('#timezone').value = Intl.DateTimeFormat().resolvedOptions().timeZone;
        });
    } catch (error) {
        toast(error.message, 'error');
        container.innerHTML = `<p class="hint">${esc(error.message)}</p>`;
    }
}

function fillTimeZones(select, current) {
    // Полный список поясов есть не во всех браузерах, поэтому он дополняется
    // текущим значением с сервера: иначе выбранный пояс нельзя было бы увидеть.
    const zones = typeof Intl.supportedValuesOf === 'function'
        ? Intl.supportedValuesOf('timeZone')
        : ['UTC', 'Europe/Moscow', 'Europe/Berlin', 'Europe/London', 'Asia/Almaty', 'Asia/Tbilisi', 'America/New_York'];

    const options = zones.includes(current) ? zones : [current, ...zones];

    select.innerHTML = options
        .map((zone) => `<option value="${esc(zone)}" ${zone === current ? 'selected' : ''}>${esc(zone)}</option>`)
        .join('');
}

async function saveSettings(event, container, context) {
    event.preventDefault();

    const body = {
        newWordsPerDay: Number(container.querySelector('#newWordsPerDay').value),
        maxCardsPerSession: Number(container.querySelector('#maxCardsPerSession').value),
        dailyGoal: Number(container.querySelector('#dailyGoal').value),
        dailyReminderEnabled: container.querySelector('#dailyReminderEnabled').checked,
        showTranscriptionUpfront: container.querySelector('#showTranscriptionUpfront').checked,
        // Секунды добавляются явно: тип time в разметке отдаёт только часы и минуты,
        // а сервер ожидает TimeOnly.
        reminderTime: `${container.querySelector('#reminderTime').value}:00`
    };

    setLoading(true);

    try {
        await api('/api/me/settings', { method: 'PUT', body });
        toast('Настройки сохранены', 'ok');
    } catch (error) {
        toast(error.message, 'error');
    } finally {
        setLoading(false);
    }
}

async function saveTimeZone(container, context) {
    const timeZoneId = container.querySelector('#timezone').value;

    setLoading(true);

    try {
        await api('/api/me/timezone', { method: 'PUT', body: { timeZoneId } });
        toast(`Часовой пояс: ${timeZoneId}`, 'ok');
        await render(container, context);
    } catch (error) {
        toast(error.message, 'error');
    } finally {
        setLoading(false);
    }
}
