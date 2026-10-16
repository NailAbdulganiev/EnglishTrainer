/*
 * Клиент API.
 *
 * Единственное место, где выполняется fetch: пути, заголовки, разбор ошибок
 * и работа с токеном собраны здесь, поэтому страницы занимаются только разметкой.
 */

const TOKEN_KEY = 'englishtrainer.token';

/** Ошибка запроса с сохранённым кодом ответа: по нему страницы решают, что делать. */
export class ApiError extends Error {
    constructor(message, status) {
        super(message);
        this.name = 'ApiError';
        this.status = status;
    }
}

export function getToken() {
    return localStorage.getItem(TOKEN_KEY);
}

export function setToken(value) {
    if (value) {
        localStorage.setItem(TOKEN_KEY, value);
    } else {
        localStorage.removeItem(TOKEN_KEY);
    }
}

/** Обработчик истечения сеанса. Назначает оболочка приложения. */
let unauthorizedHandler = () => { };

export function onUnauthorized(handler) {
    unauthorizedHandler = handler;
}

export async function api(path, { method = 'GET', body, anonymous = false, quiet = false } = {}) {
    const headers = {};

    if (body !== undefined) {
        headers['Content-Type'] = 'application/json; charset=utf-8';
    }

    if (!anonymous) {
        const token = getToken();

        if (token) {
            headers.Authorization = `Bearer ${token}`;
        }
    }

    const response = await fetch(path, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body)
    });

    return unwrap(response, method, path, anonymous, quiet);
}

/**
 * Запрос с телом в виде формы — так загружаются файлы.
 *
 * Отдельная функция, а не режим `api()`: у составной формы нельзя выставлять
 * Content-Type вручную — браузер обязан сам добавить в него границу частей,
 * и подставленный «application/json» сломал бы разбор на сервере.
 */
export async function apiUpload(path, formData, method = 'POST') {
    const headers = {};
    const token = getToken();

    if (token) {
        headers.Authorization = `Bearer ${token}`;
    }

    const response = await fetch(path, { method, headers, body: formData });

    return unwrap(response, method, path, false, false);
}

/** Общий разбор ответа: коды, текст ошибки и признак истёкшего сеанса. */
async function unwrap(response, method, path, anonymous, quiet) {
    // 401 означает, что токен истёк или отозван: показать форму входа честнее,
    // чем бесконечно повторять запросы, которые всё равно не пройдут.
    if (response.status === 401 && !anonymous) {
        unauthorizedHandler();
        throw new ApiError('Сеанс истёк, войдите заново.', 401);
    }

    if (response.status === 204) {
        return null;
    }

    const text = await response.text();
    const payload = text ? safeParse(text) : null;

    if (!response.ok) {
        // Сервер отдаёт ProblemDetails: у ожидаемых ошибок есть понятный текст.
        const message = (payload && (payload.detail || payload.title)) || `Ошибка ${response.status}`;

        if (!quiet) {
            console.warn(`API ${method} ${path} → ${response.status}: ${message}`);
        }

        throw new ApiError(message, response.status);
    }

    return payload;
}

function safeParse(text) {
    try {
        return JSON.parse(text);
    } catch {
        // Не-JSON ответ (например, страница ошибки прокси) не должен ломать
        // разбор: лучше показать общий текст, чем исключение парсера.
        return null;
    }
}
