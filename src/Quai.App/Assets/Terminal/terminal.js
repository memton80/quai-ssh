// Pont entre xterm.js et l'application (messages WebView2).
//   Application -> page : { type: 'output', data: base64 } | 'paste' | 'options' | 'focus'
//   Page -> application : 'ready' | 'input' | 'binary' | 'resize' | 'copy' | 'paste'
(() => {
    'use strict';

    const THEMES = {
        dark: {
            background: '#0c0c0c',
            foreground: '#cccccc',
            cursor: '#ffffff',
            cursorAccent: '#0c0c0c',
            selectionBackground: '#264f78',
            black: '#0c0c0c',
            red: '#c50f1f',
            green: '#13a10e',
            yellow: '#c19c00',
            blue: '#3b78ff',
            magenta: '#881798',
            cyan: '#3a96dd',
            white: '#cccccc',
            brightBlack: '#767676',
            brightRed: '#e74856',
            brightGreen: '#16c60c',
            brightYellow: '#f9f1a5',
            brightBlue: '#6ea0ff',
            brightMagenta: '#b4009e',
            brightCyan: '#61d6d6',
            brightWhite: '#f2f2f2',
        },
        light: {
            background: '#fafafa',
            foreground: '#383a42',
            cursor: '#4f525d',
            cursorAccent: '#fafafa',
            selectionBackground: '#bfceff',
            black: '#383a42',
            red: '#e45649',
            green: '#3d8b3c',
            yellow: '#a8730a',
            blue: '#0184bc',
            magenta: '#a626a4',
            cyan: '#0997b3',
            white: '#a0a1a7',
            brightBlack: '#4f525d',
            brightRed: '#df6c75',
            brightGreen: '#50a14f',
            brightYellow: '#c18301',
            brightBlue: '#4078f2',
            brightMagenta: '#c577dd',
            brightCyan: '#56b5c1',
            brightWhite: '#090a0b',
        },
    };

    const DEFAULT_FONT = '"Cascadia Mono", "Cascadia Code", Consolas, "Courier New", monospace';
    const host = window.chrome && window.chrome.webview ? window.chrome.webview : null;

    function post(message) {
        if (host) {
            host.postMessage(message);
        }
    }

    function themeName(value) {
        return value === 'light' ? 'light' : 'dark';
    }

    function fontStack(family) {
        const name = (family || '').trim();
        if (!name) {
            return DEFAULT_FONT;
        }

        // La police choisie d'abord, puis les polices de repli à chasse fixe.
        return '"' + name.replace(/["\\]/g, '') + '", ' + DEFAULT_FONT;
    }

    function fontSize(value) {
        const size = Number(value);
        return Number.isFinite(size) ? Math.min(Math.max(Math.round(size), 6), 72) : 14;
    }

    // Réglages initiaux passés dans l'URL : la taille annoncée par « ready » est ainsi la bonne.
    const query = new URLSearchParams(window.location.search);
    let currentTheme = themeName(query.get('theme'));
    document.body.className = 'theme-' + currentTheme;

    const container = document.getElementById('terminal');
    const term = new Terminal({
        allowProposedApi: false,
        cursorBlink: true,
        cursorStyle: 'bar',
        fontFamily: fontStack(query.get('fontFamily')),
        fontSize: fontSize(query.get('fontSize')),
        scrollback: 10000,
        theme: THEMES[currentTheme],
    });
    const fitAddon = new FitAddon.FitAddon();
    term.loadAddon(fitAddon);
    term.open(container);

    function fit() {
        try {
            fitAddon.fit();
        } catch (error) {
            // La zone n'a pas encore de taille (onglet masqué) : on réessaiera au prochain redimensionnement.
        }
    }

    function decode(base64) {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }

        return bytes;
    }

    function copySelection() {
        if (!term.hasSelection()) {
            return false;
        }

        post({ type: 'copy', text: term.getSelection() });
        term.clearSelection();
        return true;
    }

    function requestPaste() {
        post({ type: 'paste' });
    }

    function applyOptions(options) {
        if (options.theme) {
            currentTheme = themeName(options.theme);
            document.body.className = 'theme-' + currentTheme;
            term.options.theme = THEMES[currentTheme];
        }

        if (options.fontFamily !== undefined) {
            term.options.fontFamily = fontStack(options.fontFamily);
        }

        if (options.fontSize !== undefined) {
            term.options.fontSize = fontSize(options.fontSize);
        }

        fit();
    }

    term.onData((data) => post({ type: 'input', data }));
    term.onBinary((data) => post({ type: 'binary', data }));
    term.onResize((size) => post({ type: 'resize', cols: size.cols, rows: size.rows }));

    // Raccourcis à la Windows Terminal : Ctrl+C copie s'il y a une sélection (sinon ^C), Ctrl+V colle.
    term.attachCustomKeyEventHandler((event) => {
        if (event.type !== 'keydown') {
            return true;
        }

        const ctrl = event.ctrlKey && !event.altKey && !event.metaKey;
        const isCopy = (ctrl && event.code === 'KeyC' && (event.shiftKey || term.hasSelection()))
            || (ctrl && !event.shiftKey && event.code === 'Insert');
        const isPaste = (ctrl && event.code === 'KeyV')
            || (event.shiftKey && !event.ctrlKey && !event.altKey && event.code === 'Insert');

        if (isCopy) {
            event.preventDefault();
            copySelection();
            return false;
        }

        if (isPaste) {
            event.preventDefault();
            requestPaste();
            return false;
        }

        return true;
    });

    // Clic droit : copie la sélection, sinon colle.
    container.addEventListener('contextmenu', (event) => {
        event.preventDefault();
        if (!copySelection()) {
            requestPaste();
        }
    });

    if (host) {
        host.addEventListener('message', (event) => {
            const message = event.data;
            if (!message || typeof message.type !== 'string') {
                return;
            }

            switch (message.type) {
                case 'output':
                    term.write(decode(message.data));
                    break;
                case 'paste':
                    if (typeof message.text === 'string' && message.text.length > 0) {
                        term.paste(message.text);
                    }

                    break;
                case 'options':
                    applyOptions(message);
                    break;
                case 'focus':
                    term.focus();
                    break;
                default:
                    break;
            }
        });
    }

    let fitPending = false;
    new ResizeObserver(() => {
        if (!fitPending) {
            fitPending = true;
            requestAnimationFrame(() => {
                fitPending = false;
                fit();
            });
        }
    }).observe(container);

    function announceReady() {
        fit();
        term.focus();
        post({ type: 'ready', cols: term.cols, rows: term.rows });
    }

    // On attend les polices pour mesurer les cellules correctement (sans bloquer si elles tardent).
    let announced = false;
    function announceOnce() {
        if (!announced) {
            announced = true;
            announceReady();
        }
    }

    if (document.fonts && document.fonts.ready) {
        document.fonts.ready.then(announceOnce, announceOnce);
        setTimeout(announceOnce, 1500);
    } else {
        announceOnce();
    }
})();
