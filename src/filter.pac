function FindProxyForURL(url, host) {
    host = host.toLowerCase();

    var FILTER_ENABLED = true;
    if (!FILTER_ENABLED) {
        return "DIRECT";
    }

    if (isPlainHostName(host) ||
        shExpMatch(host, "*.local") ||
        shExpMatch(host, "localhost") ||
        shExpMatch(host, "127.*") ||
        shExpMatch(host, "10.*") ||
        shExpMatch(host, "192.168.*") ||
        shExpMatch(host, "172.16.*") ||
        shExpMatch(host, "172.17.*") ||
        shExpMatch(host, "172.18.*") ||
        shExpMatch(host, "172.19.*") ||
        shExpMatch(host, "172.2*.*") ||
        shExpMatch(host, "172.30.*") ||
        shExpMatch(host, "172.31.*")) {
        return "DIRECT";
    }

    var whitelist = [
        "one-class.co.il",
        "*.one-class.co.il",
        "*.supabase.co",
        "supabase.co",
        "*.supabase.in",
        "supabase.in",
        "*.r2.dev",
        "connector-gateway.lovable.dev",
        "api.elevenlabs.io",
        "gemini.google.com",
        "*.gemini.google.com",
        "bard.google.com",
        "*.bard.google.com",
        "classroom.google.com",
        "*.classroom.google.com",
        "docs.google.com",
        "*.docs.google.com",
        "drive.google.com",
        "*.drive.google.com",
        "sheets.google.com",
        "*.sheets.google.com",
        "slides.google.com",
        "*.slides.google.com",
        "forms.google.com",
        "*.forms.google.com",
        "accounts.google.com",
        "*.accounts.google.com",
        "myaccount.google.com",
        "*.myaccount.google.com",
        "accounts.youtube.com",
        "apis.google.com",
        "*.googleapis.com",
        "googleapis.com",
        "*.gstatic.com",
        "gstatic.com",
        "*.googleusercontent.com",
        "googleusercontent.com",
        "*.clients6.google.com",
        "clients6.google.com",
        "clients1.google.com",
        "clients2.google.com",
        "clients3.google.com",
        "clients4.google.com",
        "clients5.google.com",
        "play.google.com",
        "play.googleapis.com",
        "passkeys.google.com",
        "fido.google.com",
        "csp.withgoogle.com",
        "recaptcha.net",
        "*.recaptcha.net",
        "www.recaptcha.net",
        "lh3.google.com",
        "ogs.google.com",
        "copilot.microsoft.com",
        "*.copilot.microsoft.com",
        "copilot.cloud.microsoft",
        "*.cloud.microsoft",
        "sydney.bing.com",
        "edgeservices.bing.com",
        "*.bing.net",
        "login.live.com",
        "login.microsoftonline.com",
        "account.microsoft.com",
        "*.msauth.net",
        "*.msftauth.net",
        "auth.gfx.ms",
        "aadcdn.msauth.net",
        "challenges.cloudflare.com",
        "edu.gov.il",
        "*.edu.gov.il",
        "education.gov.il",
        "*.education.gov.il",
        "gov.il",
        "*.gov.il",
        "cet.ac.il",
        "*.cet.ac.il"
    ];

    for (var i = 0; i < whitelist.length; i++) {
        var pattern = whitelist[i].toLowerCase();
        if (shExpMatch(host, pattern)) {
            return "DIRECT";
        }
        if (pattern.indexOf("*.") === 0) {
            var baseDomain = pattern.substring(2);
            if (host === baseDomain) {
                return "DIRECT";
            }
        }
    }

    return "PROXY 127.0.0.1:9999";
}
