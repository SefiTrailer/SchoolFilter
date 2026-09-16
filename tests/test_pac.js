const fs = require('fs');
const path = require('path');

// Standard PAC environment functions provided by browsers
function isPlainHostName(host) {
    return host.indexOf('.') === -1;
}

function shExpMatch(text, pattern) {
    // Convert wildcard pattern (*, ?) to regex
    const regexPattern = '^' + pattern
        .replace(/\./g, '\\.')
        .replace(/\*/g, '.*')
        .replace(/\?/g, '.') + '$';
    const regex = new RegExp(regexPattern, 'i');
    return regex.test(text);
}

// Load filter.pac code
const pacContent = fs.readFileSync(path.join(__dirname, '..', 'src', 'filter.pac'), 'utf8');

// Run within PAC context
const context = {
    isPlainHostName,
    shExpMatch,
    FindProxyForURL: null
};

const vm = require('vm');
vm.createContext(context);
vm.runInContext(pacContent, context);

// Test cases
const testCases = [
    // Approved Educational Sites
    { url: 'https://one-class.co.il', host: 'one-class.co.il', expected: 'DIRECT', desc: 'One-Class Main' },
    { url: 'https://app.one-class.co.il/login', host: 'app.one-class.co.il', expected: 'DIRECT', desc: 'One-Class Subdomain' },
    { url: 'https://edu.gov.il', host: 'edu.gov.il', expected: 'DIRECT', desc: 'Ministry of Education' },
    { url: 'https://students.edu.gov.il', host: 'students.edu.gov.il', expected: 'DIRECT', desc: 'Student Portal (Subdomain)' },
    { url: 'https://education.gov.il', host: 'education.gov.il', expected: 'DIRECT', desc: 'Education Gov' },
    { url: 'https://classroom.google.com', host: 'classroom.google.com', expected: 'DIRECT', desc: 'Google Classroom' },
    { url: 'https://accounts.google.com', host: 'accounts.google.com', expected: 'DIRECT', desc: 'Google Auth' },
    { url: 'https://drive.google.com', host: 'drive.google.com', expected: 'DIRECT', desc: 'Google Drive' },

    // Veyon & Local LAN (Must NEVER be blocked)
    { url: 'http://localhost:11100', host: 'localhost', expected: 'DIRECT', desc: 'Localhost / Veyon' },
    { url: 'http://127.0.0.1:11400', host: '127.0.0.1', expected: 'DIRECT', desc: 'Loopback IP' },
    { url: 'http://192.168.1.50:11100', host: '192.168.1.50', expected: 'DIRECT', desc: 'Veyon Client Lab IP (192.168.x)' },
    { url: 'http://10.0.0.15:11100', host: '10.0.0.15', expected: 'DIRECT', desc: 'Veyon Client Lab IP (10.x)' },
    { url: 'http://server.local', host: 'server.local', expected: 'DIRECT', desc: 'Local Network Server' },
    { url: 'http://printer', host: 'printer', expected: 'DIRECT', desc: 'Plain Hostname' },

    // Blocked Sites (Games, Proxies, Social Media)
    { url: 'https://roblox.com', host: 'roblox.com', expected: 'PROXY 127.0.0.1:9999', desc: 'Roblox (Game)' },
    { url: 'https://crazygames.com', host: 'crazygames.com', expected: 'PROXY 127.0.0.1:9999', desc: 'CrazyGames (Game Portal)' },
    { url: 'https://poki.com', host: 'poki.com', expected: 'PROXY 127.0.0.1:9999', desc: 'Poki (Games)' },
    { url: 'https://kproxy.com', host: 'kproxy.com', expected: 'PROXY 127.0.0.1:9999', desc: 'Web Proxy Bypasser' },
    { url: 'https://tiktok.com', host: 'tiktok.com', expected: 'PROXY 127.0.0.1:9999', desc: 'TikTok' },
    { url: 'https://instagram.com', host: 'instagram.com', expected: 'PROXY 127.0.0.1:9999', desc: 'Instagram' },
    { url: 'https://discord.com', host: 'discord.com', expected: 'PROXY 127.0.0.1:9999', desc: 'Discord' },
    { url: 'https://youtube.com', host: 'youtube.com', expected: 'PROXY 127.0.0.1:9999', desc: 'YouTube' }
];

console.log('====================================================');
console.log(' SchoolFilter PAC Rule Simulation Test');
console.log('====================================================');

let passedCount = 0;
let failedCount = 0;

for (const t of testCases) {
    const result = context.FindProxyForURL(t.url, t.host);
    const passed = result === t.expected;
    if (passed) {
        passedCount++;
        console.log(`[PASS] ${t.desc} (${t.host}) -> ${result}`);
    } else {
        failedCount++;
        console.log(`[FAIL] ${t.desc} (${t.host}) -> Got: ${result}, Expected: ${t.expected}`);
    }
}

console.log('====================================================');
console.log(`Summary: ${passedCount} PASSED, ${failedCount} FAILED out of ${testCases.length} tests.`);
console.log('====================================================');

if (failedCount > 0) {
    process.exit(1);
}
