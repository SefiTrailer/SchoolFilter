/**
 * SchoolFilter - Proxy Auto-Configuration (PAC) File
 * Architecture: Whitelist-only filter with sinkhole drop
 * Managed dynamically via SchoolFilter Teacher Portal
 * Target: Windows 10/11 - Chrome, Edge, Firefox, WinINet
 * NOTE: Keep this file strictly ASCII for WinINet/WinHTTP compatibility.
 */

var FILTER_ENABLED = false;

function FindProxyForURL(url, host) {
    // Master switch: if teacher set classroom to Full Internet mode in Portal
    if (!FILTER_ENABLED) {
        return "DIRECT";
    }

    // Normalize host to lowercase
    host = host.toLowerCase();

    // =========================================================================
    // 1. LOCALHOST, LAN & PAC HOSTING BYPASS
    // Keeps Veyon Classroom Management (ports 11100/11400), local LAN,
    // and the GitHub PAC cloud host itself always accessible.
    // =========================================================================
    if (isPlainHostName(host) ||
        host === "localhost" ||
        host === "127.0.0.1" ||
        host === "::1" ||
        host === "sefitrailer.github.io" ||
        host === "raw.githubusercontent.com") {
        return "DIRECT";
    }

    // Private IPv4 Subnets (School internal network)
    if (shExpMatch(host, "10.*") ||
        shExpMatch(host, "192.168.*") ||
        shExpMatch(host, "172.1[6-9].*") ||
        shExpMatch(host, "172.2[0-9].*") ||
        shExpMatch(host, "172.3[0-1].*") ||
        shExpMatch(host, "*.local")) {
        return "DIRECT";
    }

    // =========================================================================
    // 2. APPROVED EDUCATIONAL WHITELIST
    // =========================================================================
    var whitelist = [
        "one-class.co.il",
        "*.one-class.co.il",
        "edu.gov.il",
        "*.edu.gov.il",
        "education.gov.il",
        "*.education.gov.il",
        "classroom.google.com",
        "*.classroom.google.com",
        "accounts.google.com",
        "*.accounts.google.com",
        "accounts.youtube.com",
        "*.accounts.youtube.com",
        "ssl.gstatic.com",
        "*.ssl.gstatic.com",
        "gstatic.com",
        "*.gstatic.com",
        "fonts.gstatic.com",
        "*.fonts.gstatic.com",
        "fonts.googleapis.com",
        "*.fonts.googleapis.com",
        "googleapis.com",
        "*.googleapis.com",
        "apis.google.com",
        "*.apis.google.com",
        "drive.google.com",
        "*.drive.google.com",
        "docs.google.com",
        "*.docs.google.com",
        "lh3.googleusercontent.com",
        "*.lh3.googleusercontent.com",
        "googleusercontent.com",
        "*.googleusercontent.com"
    ];

    // Evaluate host against whitelist
    for (var i = 0; i < whitelist.length; i++) {
        var pattern = whitelist[i];
        if (shExpMatch(host, pattern) || host === pattern) {
            return "DIRECT";
        }
    }

    // =========================================================================
    // 3. SINKHOLE DROP (BLOCK ALL OTHER TRAFFIC)
    // =========================================================================
    return "PROXY 127.0.0.1:9999";
}
