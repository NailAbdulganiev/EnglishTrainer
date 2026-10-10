/*
 * Оболочка приложения: вход, навигация и роутер.
 *
 * Страницы — отдельные модули, каждая умеет только отрисоваться в контейнер.
 * Оболочка отвечает за то, что вокруг: сеанс, меню, выбор страницы по адресу.
 * Роутинг на хеше выбран потому, что не требует настройки сервера: любой
 * адрес вида /#/friends открывается без правки маршрутов на стороне API.
 */

import { api, getToken, setToken, onUnauthorized } from './api.js';
import { state, refreshFavorites, setUser } from './state.js';
import { esc, icon, toast, setLoading, avatar } from './ui.js';
import { stopSession } from './session.js';

import * as study from './pages/study.js';
import * as favorites from './pages/favorites.js';
import * as friends from './pages/friends.js';
import * as settings from './pages/settings.js';
import * as profile from './pages/profile.js';

const routes = [
    { path: 'study', title: 'Тренировка', icon: 'book', page: study },
    { path: 'favorites', title: 'Избранное', icon: 'star', page: favorites, badge: 'favorites' },
    { path: 'friends', title: 'Друзья', icon: 'users', page: friends, badge: 'friends' },
    { path: 'settings', title: 'Настройки', icon: 'settings', page: settings },
    { path: 'profile', title: 'Профиль', icon: 'user', page: profile }
];

const elements = {
    auth: document.getElementById('screen-auth'),
    app: document.getElementById('app'),
    nav: document.getElementById('nav'),
    foot: document.getElementById('sidebar-foot'),
    title: document.getElementById('page-title'),
    content: document.getElementById('content')
};

const context = { navigate, refreshShell };

let currentPath = null;
let authMode = 'login';

/* ── Запуск ─────────────────────────────────────────────────────────────── */

onUnauthorized(() => {
    setToken(null);
    setUser(null);
    showAuth('Сеанс истёк — войдите заново.');
});

boot();

async function boot() {
    bindAuthForm();
    window.addEventListener('hashchange', route);

    if (!getToken()) {
        showAuth();
        return;
    }

    setLoading(true);

    try {
        await loadShell();
        await enterApp();
    } catch {
        // Токен есть, но сервер его не принял — показываем вход.
        showAuth();
    } finally {
        setLoading(false);
    }
}

/* ── Вход ───────────────────────────────────────────────────────────────── */

function bindAuthForm() {
    document.getElementById('auth-form').addEventListener('submit', (event) => {
        event.preventDefault();
        authenticate();
    });

    document.getElementById('register-toggle').addEventListener('click', () => {
        setAuthMode(authMode === 'login' ? 'register' : 'login');
    });
}

function setAuthMode(mode) {
    authMode = mode;

    document.getElementById('username-field').hidden = mode !== 'register';
    document.getElementById('login').textContent = mode === 'register' ? 'Зарегистрироваться' : 'Войти';
    document.getElementById('register-toggle').textContent =
        mode === 'register' ? 'У меня уже есть аккаунт' : 'Регистрация';
}

function showAuth(message) {
    stopSession();

    elements.app.hidden = true;
    elements.auth.hidden = false;
    setAuthMode('login');

    if (message) {
        toast(message, 'error');
    }
}

async function authenticate() {
    const email = document.getElementById('email').value.trim();
    const password = document.getElementById('password').value;
    const username = document.getElementById('username').value.trim();
    const isRegistration = authMode === 'register';

    if (!email || password.length < 8) {
        toast('Укажите email и пароль не короче 8 символов.', 'error');
        return;
    }

    const body = { email, password };

    if (isRegistration) {
        body.displayName = email.split('@')[0];
        // Часовой пояс определяется у браузера: от него зависят дневная статистика
        // и серия дней, и угадывать его на сервере незачем.
        body.timeZoneId = Intl.DateTimeFormat().resolvedOptions().timeZone;

        if (username) {
            body.username = username;
        }
    }

    setLoading(true);

    try {
        const response = await api(
            isRegistration ? '/api/auth/register' : '/api/auth/login',
            { method: 'POST', body, anonymous: true });

        setToken(response.accessToken);
        setUser(response.user);

        await loadShell();
        await enterApp();
    } catch (error) {
        toast(error.message, 'error');
    } finally {
        setLoading(false);
    }
}

/* ── Оболочка ───────────────────────────────────────────────────────────── */

async function loadShell() {
    const profile = await api('/api/me');

    state.profile = profile;
    state.user = profile.user;

    await refreshFavorites();

    // Входящие заявки видны в меню, поэтому счётчик нужен сразу после входа.
    const overview = await api('/api/friends');
    state.badges.friends = overview.incoming.length;

    renderUserChip();
}

async function enterApp() {
    elements.auth.hidden = true;
    elements.app.hidden = false;

    if (!location.hash) {
        location.hash = `#/${routes[0].path}`;
        // Присвоение хеша вызовет hashchange, но только если он действительно изменился.
        if (!location.hash) await route();
        return;
    }

    await route();
}

function refreshShell() {
    renderUserChip();
    renderNav();
}

function renderUserChip() {
    const user = state.user;

    if (!user) {
        elements.foot.innerHTML = '';
        return;
    }

    elements.foot.innerHTML = `
        <div class="user-chip">
            ${avatar(user, 'avatar--sm')}
            <div class="user-chip__name">
                <b>${esc(user.displayName)}</b>
                <span>@${esc(user.username)}</span>
            </div>
            <button type="button" class="star" id="logout" title="Выйти">${icon('logout')}</button>
        </div>`;

    document.getElementById('logout').addEventListener('click', () => {
        setToken(null);
        setUser(null);
        showAuth();
    });
}

function renderNav() {
    elements.nav.innerHTML = routes.map((route) => {
        const count = route.badge ? state.badges[route.badge] ?? 0 : 0;

        return `
            <a class="nav-item ${route.path === currentPath ? 'is-active' : ''}" href="#/${route.path}">
                ${icon(route.icon)}
                <span>${esc(route.title)}</span>
                ${count > 0 ? `<span class="badge badge--brand">${count}</span>` : ''}
            </a>`;
    }).join('');
}

/* ── Роутер ─────────────────────────────────────────────────────────────── */

function navigate(path) {
    const target = `#/${path}`;

    if (location.hash === target) {
        route();
        return;
    }

    location.hash = target;
}

async function route() {
    const requested = location.hash.replace(/^#\/?/, '').split('?')[0];
    const matched = routes.find((route) => route.path === requested) ?? routes[0];

    currentPath = matched.path;
    elements.title.textContent = matched.title;

    stopSession();
    renderNav();

    try {
        await matched.page.render(elements.content, context);
    } catch (error) {
        elements.content.innerHTML = `<p class="hint">${esc(error.message)}</p>`;
    }

    // Страница могла обновить счётчики (например, принять заявку в друзья),
    // поэтому меню перерисовывается ещё раз, уже после её работы.
    renderNav();
}
