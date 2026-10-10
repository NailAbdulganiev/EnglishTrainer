/*
 * Избранное: список отмеченных слов и тренировка только по ним.
 */

import { api } from '../api.js';
import { refreshFavorites, toggleFavorite } from '../state.js';
import { esc, icon, plural, toast, emptyState, phaseBadge, starButton } from '../ui.js';
import { runSession, stopSession } from '../session.js';

export async function render(container, context) {
    stopSession();

    container.innerHTML = '<p class="hint">Загрузка…</p>';

    try {
        const words = await api('/api/favorites');

        if (!words || words.length === 0) {
            container.innerHTML = emptyState({
                iconName: 'star',
                title: 'В избранном пока пусто',
                text: 'Отмечайте слова звёздочкой в словаре колоды или прямо во время тренировки. '
                    + 'Избранные слова идут первыми в очереди повторений, и для них есть отдельная тренировка.',
                action: '<button type="button" class="btn btn--primary" id="to-study">Перейти к колодам</button>'
            });

            container.querySelector('#to-study').addEventListener('click', () => context.navigate('study'));
            return;
        }

        const learned = words.filter((word) => word.isLearned).length;
        const due = words.filter((word) => word.dueAt && new Date(word.dueAt) <= new Date()).length;

        container.innerHTML = `
            <div class="card" style="display: flex; align-items: center; gap: 16px; flex-wrap: wrap">
                <div style="flex: 1; min-width: 220px">
                    <b>${plural(words.length, ['слово', 'слова', 'слов'])} в избранном</b>
                    <p class="hint" style="margin-top: 4px">
                        выучено ${learned} · пора повторить ${due}
                    </p>
                </div>
                <button type="button" class="btn btn--primary" id="train-favorites">
                    ${icon('play', 'icon--sm')} Тренировать избранное
                </button>
            </div>

            <p class="hint" style="margin: 14px 0 6px">
                Тренировка по избранному не ждёт назначенного срока. Текущая незавершённая
                сессия при этом закрывается — две очереди одновременно держать нельзя.
            </p>

            <div class="section-title">${icon('star', 'icon--sm')} Слова</div>

            <div class="word-list">
                ${words.map(favoriteRow).join('')}
            </div>`;

        container.querySelector('#train-favorites').addEventListener('click', () => trainFavorites(container, context));

        container.querySelectorAll('[data-favorite]').forEach((button) => {
            button.addEventListener('click', () => removeFavorite(button, container, context));
        });
    } catch (error) {
        container.innerHTML = emptyState({
            iconName: 'bulb',
            title: 'Не удалось загрузить избранное',
            text: error.message,
            action: '<button type="button" class="btn btn--primary" id="retry">Повторить</button>'
        });

        container.querySelector('#retry').addEventListener('click', () => render(container, context));
    }
}

function favoriteRow(word) {
    const due = word.dueAt && new Date(word.dueAt) <= new Date();

    return `
        <div class="word-row">
            <div class="word-row__main">
                <div>
                    <span class="word-row__lemma">${esc(word.lemma)}</span>
                    ${word.ipaUk ? `<span class="word-row__ipa">${esc(word.ipaUk)}</span>` : ''}
                </div>
                <div class="word-row__translation">${esc(word.primaryTranslation)}</div>
            </div>
            <div class="word-row__side">
                ${phaseBadge(word.phase, word.isLearned)}
                ${due ? '<span class="badge badge--brand">пора</span>' : ''}
                ${starButton(word.wordId, true)}
            </div>
        </div>`;
}

async function removeFavorite(button, container, context) {
    try {
        await toggleFavorite(Number(button.dataset.favorite));
        await refreshFavorites();
        await render(container, context);
    } catch (error) {
        toast(error.message, 'error');
    }
}

async function trainFavorites(container, context) {
    container.innerHTML = '<p class="hint">Собираем очередь из избранного…</p>';

    try {
        const session = await api('/api/study/favorites', { method: 'POST' });

        runSession(container, session, () => render(container, context));
    } catch (error) {
        toast(error.message, 'error');
        await render(container, context);
    }
}
