/*
 * Тема оформления: светлая, тёмная и «как в системе».
 *
 * Выбранный режим хранится в localStorage, а на <html> выставляется уже
 * разрешённое значение — `light` или `dark`. Так сделано намеренно: иначе
 * тёмную палитру пришлось бы описывать дважды, отдельно для ручного выбора
 * и отдельно для системного предпочтения.
 *
 * Внутри Telegram тема не спрашивается у пользователя: её диктует клиент
 * (`colorScheme`), и спорить с ним бессмысленно — мини-апп живёт в его окне.
 */

const STORAGE_KEY = 'englishtrainer.theme';

/** Режимы, которые можно выбрать. `auto` — следовать за системой. */
const MODES = ['auto', 'light', 'dark'];

/** Режим, выбранный человеком. */
let mode = 'auto';

/**
 * Тема, навязанная извне (Telegram). `null` — выбор остаётся за пользователем.
 */
let forced = null;

/** Системное предпочтение. */
const systemPrefersDark = window.matchMedia('(prefers-color-scheme: dark)');

export function initTheme() {
    mode = readStoredMode();

    // Смена системной темы должна применяться на лету: человек переключает
    // оформление вечером и ждёт, что открытая вкладка тоже потемнеет.
    systemPrefersDark.addEventListener('change', () => {
        if (mode === 'auto' && forced === null) {
            apply();
        }
    });

    apply();
}

/** Режим, выбранный пользователем: `auto`, `light` или `dark`. */
export function getThemeMode() {
    return mode;
}

/** Что видно на экране прямо сейчас: `light` или `dark`. */
export function resolvedTheme() {
    if (forced !== null) {
        return forced;
    }

    if (mode === 'auto') {
        return systemPrefersDark.matches ? 'dark' : 'light';
    }

    return mode;
}

export function setThemeMode(next) {
    mode = MODES.includes(next) ? next : 'auto';

    try {
        localStorage.setItem(STORAGE_KEY, mode);
    } catch {
        // Приватный режим браузера запрещает запись. Тема при этом работает,
        // просто не запоминается до следующего открытия — это не повод падать.
    }

    apply();
}

/**
 * Переключает между светлой и тёмной, отталкиваясь от того, что видно сейчас.
 *
 * Отталкиваться от текущего вида, а не от сохранённого режима, важно: при
 * режиме «как в системе» тёмная тема уже может быть активна, и переключение
 * «в тёмную» выглядело бы как «ничего не произошло».
 */
export function toggleTheme() {
    setThemeMode(resolvedTheme() === 'dark' ? 'light' : 'dark');

    return mode;
}

/** Навязывает тему извне. `null` возвращает управление пользователю. */
export function setForcedTheme(value) {
    forced = value === 'dark' || value === 'light' ? value : null;

    apply();
}

function apply() {
    const root = document.documentElement;

    root.dataset.theme = resolvedTheme();
    root.dataset.themeMode = mode;

    document.dispatchEvent(new CustomEvent('themechange'));
}

function readStoredMode() {
    try {
        const stored = localStorage.getItem(STORAGE_KEY);

        return MODES.includes(stored) ? stored : 'auto';
    } catch {
        return 'auto';
    }
}
