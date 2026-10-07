/**
 * SchoolFilter - Proxy Auto-Configuration (PAC) File
 * Architecture: Whitelist-only filter with sinkhole drop
 * Managed dynamically via SchoolFilter Teacher Portal
 * Target: Windows 10/11 - Chrome, Edge, Firefox, WinINet
 */

function FindProxyForURL(url, host) {
    // Normalize host to lowercase
    host = host.toLowerCase();

    // =========================================================================
    // 1. LOCALHOST & INTRANET / LAN BYPASS
    // מונע חסימה של Veyon (פורטים 11100/11400) והרשת המקומית הבית-ספרית
    // =========================================================================
    if (isPlainHostName(host) ||
        host === "localhost" ||
        host === "127.0.0.1" ||
        host === "::1") {
        return "DIRECT";
    }

    // Private IPv4 Subnets (רשת בית ספרית פנימית)
    if (shExpMatch(host, "10.*") ||
        shExpMatch(host, "192.168.*") ||
        shExpMatch(host, "172.1[6-9].*") ||
        shExpMatch(host, "172.2[0-9].*") ||
        shExpMatch(host, "172.3[0-1].*") ||
        shExpMatch(host, "*.local")) {
        return "DIRECT";
    }

    // =========================================================================
    // 2. APPROVED EDUCATIONAL WHITELIST (רשימה לבנה מאושרת)
    // =========================================================================
    var whitelist = [
        "*.one-class.co.il",
        "*.edu.gov.il",
        "education.gov.il",
        "*.education.gov.il",
        "accounts.google.com",
        "accounts.youtube.com",
        "ssl.gstatic.com",
        "fonts.gstatic.com",
        "fonts.googleapis.com",
        "apis.google.com",
        "drive.google.com",
        "docs.google.com",
        "lh3.googleusercontent.com"
    ];

    // בדיקה האם הכתובת נמצאת ברשימה הלבנה
    for (var i = 0; i < whitelist.length; i++) {
        var pattern = whitelist[i];
        if (shExpMatch(host, pattern)) {
            return "DIRECT";
        }
    }

    // =========================================================================
    // 3. SINKHOLE DROP (חסימת כל שאר האתרים והמשחקים)
    // =========================================================================
    return "PROXY 127.0.0.1:9999";
}
