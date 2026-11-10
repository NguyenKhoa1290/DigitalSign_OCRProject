// Server preferences are authoritative. This per-account cache only avoids a
// light flash; never use the unverified JWT fields below for access control.
(() => {
    const prefix = 'hau.theme.account.';
    const normalizeId = id => /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(id || '') ? id.toLowerCase() : null;
    let account = null, current = 'light';
    try {
        const payload = localStorage.getItem('auth_token')?.split('.')[1];
        if (payload) {
            const part = payload.replace(/-/g, '+').replace(/_/g, '/');
            const claims = JSON.parse(atob(part.padEnd(Math.ceil(part.length/4)*4, '=')));
            account = normalizeId(claims.sub || claims.nameid || claims['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier']);
        }
        if (account && localStorage.getItem(prefix+account) === 'dark') current = 'dark';
    } catch { /* No usable session/cache: start with light mode. */ }
    const apply = value => {
        current = value === 'dark' ? 'dark' : 'light';
        document.documentElement.dataset.theme = current;
        document.documentElement.style.colorScheme = current;
    };
    apply(current);
    window.hauThemePreview = {
        isDark: () => current === 'dark',
        setAccount: (userId, theme) => {
            const id = normalizeId(userId);
            if (!id) return current === 'dark';
            const next = theme === 'dark' ? 'dark' : 'light', changed = next !== current;
            account = id; apply(next);
            try { localStorage.setItem(prefix+account, current); } catch { /* Saving on the server still works. */ }
            if (changed) window.dispatchEvent(new CustomEvent('hau-theme-change', { detail: { theme: current } }));
            return current === 'dark';
        },
        clearAccount: () => {
            const changed = current !== 'light'; account = null; apply('light');
            if (changed) window.dispatchEvent(new CustomEvent('hau-theme-change', { detail: { theme: current } }));
        },
        restoreSwitchFocus: () => {
            const button = document.getElementById('profile-dark-mode');
            if (button && !button.disabled && button.dataset.input === 'keyboard'
                && (document.activeElement === document.body || document.activeElement === button))
                button.focus({ preventScroll:true });
        }
    };
})();
