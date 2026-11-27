window.awykenAccountSession = {
    async signIn(email, password) {
        const response = await fetch("/api/v1/auth/web-sign-in", {
            method: "POST",
            credentials: "same-origin",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ email, password })
        });
        if (!response.ok) return false;
        const session = await response.json();
        return !session.error;
    },

    async restore() {
        const response = await fetch("/api/v1/auth/session", {
            credentials: "same-origin"
        });
        if (!response.ok) throw new Error("Could not restore the browser sign-in session.");
        const session = await response.json();
        return session.isAuthenticated ? session.profileId : null;
    },

    async signOut() {
        const response = await fetch("/api/v1/auth/sign-out", {
            method: "POST",
            credentials: "same-origin"
        });
        if (!response.ok) throw new Error("Could not clear the sign-in session.");
    }
};
