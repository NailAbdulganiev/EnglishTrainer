/*
 * Озвучивание через Web Speech API.
 *
 * Бесплатно и без файлов: записей слов в словаре нет, а синтез речи есть
 * в браузере. Именно поэтому упражнение «на слух» вообще возможно без
 * добавления в проект хотя бы одного аудиофайла.
 *
 * Модуль намеренно маленький: он умеет произнести текст и остановиться.
 * Всё остальное — решение страницы тренировки.
 */

const synth = window.speechSynthesis ?? null;

/**
 * Английский голос, выбранный один раз.
 *
 * Список голосов в браузере приходит асинхронно, и первый запрос к нему часто
 * возвращает пустой массив. Именно поэтому голос кешируется, а не спрашивается
 * при каждом произношении.
 */
let cachedVoice = null;

/** Есть ли в браузере синтез речи. */
export function canSpeak() {
    return Boolean(synth);
}

/**
 * Готовит синтез к работе.
 *
 * Вызывается один раз при запуске приложения: без этого первое произношение
 * уходит голосом по умолчанию, а он может читать английские слова с русским
 * акцентом — то есть упражнение на слух учило бы неправильному произношению.
 */
export function initSpeech() {
    if (!synth) {
        return;
    }

    englishVoice();

    // Список голосов подгружается асинхронно: сбрасываем кеш и выбираем заново.
    synth.addEventListener?.('voiceschanged', () => {
        cachedVoice = null;
        englishVoice();
    });
}

/**
 * Произносит текст.
 *
 * @param {string} text что произнести
 * @param {number} rate скорость: 1 — обычная, меньше — медленнее
 * @returns {boolean} удалось ли запустить произношение
 */
export function speak(text, rate = 0.9) {
    if (!synth || !text) {
        return false;
    }

    // Предыдущее произношение отменяется: при быстром переходе между карточками
    // два голоса накладываются друг на друга, и разобрать слова невозможно.
    synth.cancel();

    const utterance = new SpeechSynthesisUtterance(text);

    utterance.lang = 'en-US';
    utterance.rate = rate;

    const voice = englishVoice();

    if (voice) {
        utterance.voice = voice;
    }

    synth.speak(utterance);

    return true;
}

/** Прерывает произношение. Вызывается при уходе с карточки. */
export function stopSpeaking() {
    synth?.cancel();
}

function englishVoice() {
    if (cachedVoice !== null) {
        return cachedVoice;
    }

    if (!synth?.getVoices) {
        return null;
    }

    const voices = synth.getVoices() ?? [];

    // Локальный голос предпочтительнее сетевого: он звучит сразу и работает
    // без интернета, а мини-апп вполне может открыться в метро.
    cachedVoice = voices.find((voice) => isEnglish(voice) && voice.localService)
        ?? voices.find(isEnglish)
        ?? null;

    return cachedVoice;
}

function isEnglish(voice) {
    return /^en([-_]|$)/i.test(voice.lang ?? '');
}
