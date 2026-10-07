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
        "gemini.google.com",
        "*.gemini.google.com",
        "copilot.microsoft.com",
        "*.copilot.microsoft.com",
        "edu.gov.il",
        "*.edu.gov.il",
        "education.gov.il",
        "*.education.gov.il",
        "classroom.google.com",
        "docs.google.com",
        "drive.google.com",
        "accounts.google.com"
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
