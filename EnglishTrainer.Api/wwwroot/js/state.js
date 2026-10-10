/*
 * Общее состояние интерфейса.
 *
 * Вынесено отдельным модулем, чтобы страницы и оболочка не импортировали
 * друг друга: оболочка рисует навигацию по этим данным, страницы их обновляют.
 */

import { api } from './api.js';

export const state = {
    user: null,
    profile: null,
    /** Идентификаторы избранных слов: набор нужен сразу нескольким страницам. */
    favorites: new Set(),
    /** Счётчики для навигации: входящие заявки, размер избранного. */
    badges: { friends: 0, favorites: 0 }
};

export function isFavorite(wordId) {
    return state.favorites.has(wordId);
}

export function setFavorite(wordId, value) {
    if (value) {
        state.favorites.add(wordId);
    } else {
        state.favorites.delete(wordId);
    }

    state.badges.favorites = state.favorites.size;
}

/** Перечитывает набор избранного с сервера. */
export async function refreshFavorites() {
    const ids = await api('/api/favorites/ids');

    state.favorites = new Set(ids ?? []);
    state.badges.favorites = state.favorites.size;

    return state.favorites;
}

/** Переключает отметку и синхронизирует состояние. */
export async function toggleFavorite(wordId) {
    const next = !isFavorite(wordId);

    const result = await api(`/api/words/${wordId}/favorite`, {
        method: 'PUT',
        body: { isFavorite: next }
    });

    setFavorite(wordId, result.isFavorite);
    return result.isFavorite;
}

/** Подставляет имя и аватар в блок пользователя и обновляет счётчики навигации. */
export function setUser(user) {
    state.user = user;
}
