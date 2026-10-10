/*
 * Страница тренировки: колоды, словарь колоды и прохождение сессии.
 */

import { api } from '../api.js';
import { isFavorite, toggleFavorite, refreshFavorites } from '../state.js';
import {
    esc, icon, plural, toast, emptyState, starButton
} from '../ui.js';
import { runSession, stopSession } from '../session.js';

export async function render(container, context) {
    await showDecks(container, context);
}

/* ── Колоды ─────────────────────────────────────────────────────────────── */

async function showDecks(container, context) {
    stopSession();

    container.innerHTML = '<p class="hint">Загрузка…</p>';

    try {
        const [decks, summary, active] = await Promise.all([
            api('/api/decks'),
            api('/api/stats/summary'),
            // Активная сессия запрашивается отдельным эндпоинтом: обычный
            // GET /api/study/session создал бы новую и израсходовал дневной лимит
            // новых слов, даже если пользователь просто открыл страницу.
            api('/api/study/session/active')
        ]);

        container.innerHTML = `
            ${active && active.currentCard ? `
                <div class="card" style="margin-bottom: 20px; display: flex; align-items: center; gap: 16px; flex-wrap: wrap">
                    <div style="flex: 1; min-width: 200px">
                        <b>Незавершённая сессия</b>
                        <p class="hint" style="margin-top: 4px">
                            ${plural(active.totalItems, ['карточка', 'карточки', 'карточек'])}
                            в очереди · начата ${esc(active.localDate)}
                        </p>
                    </div>
                    <button type="button" class="btn btn--primary" id="resume">
                        ${icon('play', 'icon--sm')} Продолжить
                    </button>
                </div>` : ''}

            <div class="stat-grid">
                ${statTile(summary.learnedCards, 'выучено', 'accent')}
                ${statTile(summary.cardsInProgress, 'в работе')}
                ${statTile(summary.dueNow, 'пора повторить')}
                ${statTile(summary.reviewedToday, 'ответов сегодня')}
                ${statTile(`${summary.accuracyLast30Days}%`, 'удержание')}
                ${statTile(`🔥 ${summary.currentStreak}`, 'серия дней', 'flame')}
            </div>

            <div class="section-title">
                ${icon('book', 'icon--sm')} Колоды
                <span class="badge">${decks.length}</span>
            </div>

            <div class="deck-grid">
                ${decks.map(deckCard).join('')}
            </div>`;

        if (active && active.currentCard) {
            container.querySelector('#resume').addEventListener('click', () => {
                runSession(container, active, (reason) => afterSession(container, context, reason));
            });
        }

        container.querySelectorAll('[data-start]').forEach((button) => {
            button.addEventListener('click', () => startSession(container, context, Number(button.dataset.start)));
        });

        container.querySelectorAll('[data-words]').forEach((button) => {
            button.addEventListener('click', () => showDeckWords(container, context, button.dataset.words));
        });
    } catch (error) {
        container.innerHTML = emptyState({
            iconName: 'bulb',
            title: 'Не удалось загрузить колоды',
            text: error.message,
            action: '<button type="button" class="btn btn--primary" id="retry">Повторить</button>'
        });

        container.querySelector('#retry').addEventListener('click', () => showDecks(container, context));
    }
}

function statTile(value, label, modifier = '') {
    return `<div class="stat ${modifier ? `stat--${modifier}` : ''}"><b>${esc(value)}</b><span>${esc(label)}</span></div>`;
}

function deckCard(deck) {
    const percent = deck.totalWords ? Math.round(deck.startedWords * 100 / deck.totalWords) : 0;

    return `
        <article class="deck">
            <div class="deck__head">
                <div class="deck__title">${esc(deck.title)}</div>
            </div>
            <p class="deck__desc">${esc(deck.description ?? '')}</p>
            <div class="progress ${percent === 100 ? 'progress--ok' : ''}"><span style="width:${percent}%"></span></div>
            <div class="deck__meta">
                <span>начато ${deck.startedWords} из ${deck.totalWords}</span>
                <span>выучено ${deck.learnedWords}</span>
            </div>
            <div class="deck__foot">
                <button type="button" class="btn btn--primary btn--sm" data-start="${deck.id}">
                    ${icon('play', 'icon--sm')} Тренироваться
                </button>
                <button type="button" class="btn btn--ghost btn--sm" data-words="${esc(deck.slug)}">
                    ${icon('book', 'icon--sm')} Слова
                </button>
            </div>
        </article>`;
}

/* ── Слова колоды ───────────────────────────────────────────────────────── */

async function showDeckWords(container, context, slug) {
    stopSession();

    container.innerHTML = '<p class="hint">Загрузка…</p>';

    try {
        const [deck, words] = await Promise.all([
            api(`/api/decks/${encodeURIComponent(slug)}`),
            api(`/api/decks/${encodeURIComponent(slug)}/words`)
        ]);

        container.innerHTML = `
            <div class="row" style="margin-bottom: 18px">
                <button type="button" class="btn btn--ghost btn--sm" id="back">
                    ${icon('close', 'icon--sm')} К колодам
                </button>
            </div>

            <div class="section-title">
                ${icon('book', 'icon--sm')} ${esc(deck.title)}
                <span class="badge">${words.length}</span>
            </div>

            <p class="hint" style="margin-bottom: 16px">
                Звёздочка добавляет слово в избранное: такие слова идут первыми в очереди
                повторений и доступны отдельной тренировкой.
            </p>

            <div class="word-list">
                ${words.map(wordRow).join('')}
            </div>`;

        container.querySelector('#back').addEventListener('click', () => showDecks(container, context));

        container.querySelectorAll('[data-favorite]').forEach((button) => {
            button.addEventListener('click', () => toggleStar(button, button.dataset.favorite));
        });
    } catch (error) {
        container.innerHTML = emptyState({
            iconName: 'bulb',
            title: 'Не удалось загрузить слова',
            text: error.message,
            action: '<button type="button" class="btn btn--primary" id="back">К колодам</button>'
        });

        container.querySelector('#back').addEventListener('click', () => showDecks(container, context));
    }
}

function wordRow(word) {
    const translation = word.translations?.map((item) => item.text).join(', ') ?? '';

    return `
        <div class="word-row">
            <div class="word-row__main">
                <div>
                    <span class="word-row__lemma">${esc(word.lemma)}</span>
                    ${word.ipaUk ? `<span class="word-row__ipa">${esc(word.ipaUk)}</span>` : ''}
                </div>
                <div class="word-row__translation">${esc(translation)}</div>
            </div>
            <div class="word-row__side">
                ${word.level && word.level !== 0 ? `<span class="badge">${esc(levelName(word.level))}</span>` : ''}
                ${starButton(word.id, isFavorite(word.id))}
            </div>
        </div>`;
}

function levelName(level) {
    // CefrLevel на сервере — перечисление, в JSON приходит числом.
    return ['—', 'A1', 'A2', 'B1', 'B2', 'C1', 'C2'][level] ?? '';
}

async function toggleStar(button, rawWordId) {
    const wordId = Number(rawWordId);

    try {
        const value = await toggleFavorite(wordId);

        button.classList.toggle('is-on', value);
        button.setAttribute('aria-pressed', String(value));

        // Набор избранного изменился — пусть следующая отрисовка его учтёт.
        await refreshFavorites();
    } catch (error) {
        toast(error.message, 'error');
    }
}

/* ── Сессия ─────────────────────────────────────────────────────────────── */

async function startSession(container, context, deckId) {
    container.innerHTML = '<p class="hint">Собираем очередь…</p>';

    try {
        const session = await api(`/api/study/session?deckId=${deckId}`);

        runSession(container, session, (reason) => afterSession(container, context, reason));
    } catch (error) {
        toast(error.message, 'error');
        await showDecks(container, context);
    }
}

async function afterSession(container, context, reason) {
    if (reason === 'finished') {
        await showSummary(container, context);
        return;
    }

    await showDecks(container, context);
}

async function showSummary(container, context) {
    stopSession();

    try {
        const summary = await api('/api/stats/summary');
        const profile = await api('/api/me');

        container.innerHTML = `
            <div class="card" style="text-align: center; padding: 32px">
                <div class="empty__icon" style="margin-bottom: 16px">${icon('check')}</div>
                <h2>Сессия закончена</h2>
                <p class="hint" style="margin-bottom: 22px">
                    Прогресс сохранён, время следующих повторений пересчитано.
                </p>
                <div class="row" style="justify-content: center">
                    <button type="button" class="btn btn--primary" id="again">
                        ${icon('refresh', 'icon--sm')} Ещё сессию
                    </button>
                    <button type="button" class="btn btn--ghost" id="favorites">
                        ${icon('star', 'icon--sm')} Избранное
                    </button>
                </div>
            </div>

            <div class="stat-grid" style="margin-top: 20px">
                ${statTile(summary.learnedCards, 'выучено', 'accent')}
                ${statTile(summary.cardsInProgress, 'в работе')}
                ${statTile(summary.dueNow, 'пора повторить')}
                ${statTile(summary.reviewedToday, 'ответов сегодня')}
                ${statTile(`${summary.accuracyLast30Days}%`, 'удержание')}
                ${statTile(`🔥 ${summary.currentStreak}`, 'серия дней', 'flame')}
            </div>

            <p class="hint">
                Всего в изучении ${plural(profile.totalCards, ['слово', 'слова', 'слов'])}
                · серия ${plural(profile.currentStreak, ['день', 'дня', 'дней'])}.
            </p>`;

        container.querySelector('#again').addEventListener('click', () => showDecks(container, context));
        container.querySelector('#favorites').addEventListener('click', () => context.navigate('favorites'));
    } catch (error) {
        toast(error.message, 'error');
        await showDecks(container, context);
    }
}

export { showDecks, showDeckWords };
