/**
 * SchoolFilter - Proxy Auto-Configuration (PAC) File
 * Architecture: Whitelist-only filter with sinkhole drop
 * Target: Windows 10/11 - Chrome, Edge, Firefox, WinINet
 */

function FindProxyForURL(url, host) {
    // Normalize host to lowercase
    host = host.toLowerCase();

    // =========================================================================
    // 1. LOCALHOST & INTRANET / LAN BYPASS
    // Crucial: Keeps Veyon Classroom Management (ports 11100/11400) and school LAN
    // fully functional. Local traffic will NEVER be routed to the sinkhole.
    // =========================================================================
    if (isPlainHostName(host) ||
        host === "localhost" ||
        host === "127.0.0.1" ||
        host === "::1") {
        return "DIRECT";
    }

    // Private IPv4 Subnets
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
    // Add any school-approved domains or wildcards here.
    // =========================================================================
    var whitelist = [
        // One-Class (Requested by User)
        "one-class.co.il",
        "*.one-class.co.il",

        // Israeli Ministry of Education (Edu/Education)
        "edu.gov.il",
        "*.edu.gov.il",
        "education.gov.il",
        "*.education.gov.il",

        // Google Classroom & Core Educational Services
        "classroom.google.com",
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

    // Evaluate host against whitelist
    for (var i = 0; i < whitelist.length; i++) {
        var pattern = whitelist[i];
        if (shExpMatch(host, pattern)) {
            return "DIRECT";
        }
    }

    // =========================================================================
    // 3. SINKHOLE DROP (BLOCK ALL OTHER TRAFFIC)
    // Routes unapproved websites to a dead local loopback port.
    // This drops all games, social media, unapproved sites and web proxies instantly.
    // =========================================================================
    return "PROXY 127.0.0.1:9999";
}
