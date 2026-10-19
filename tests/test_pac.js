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
    // Approved Educational Sites & Core Services
    { url: 'https://one-class.co.il', host: 'one-class.co.il', expected: 'DIRECT', desc: 'One-Class Main' },
    { url: 'https://app.one-class.co.il/login', host: 'app.one-class.co.il', expected: 'DIRECT', desc: 'One-Class Subdomain' },
    { url: 'https://ddbesbyojxuhsubpibvc.supabase.co/auth/v1/token', host: 'ddbesbyojxuhsubpibvc.supabase.co', expected: 'DIRECT', desc: 'One-Class Supabase Auth & API' },
    { url: 'wss://ddbesbyojxuhsubpibvc.supabase.co/realtime/v1/websocket', host: 'ddbesbyojxuhsubpibvc.supabase.co', expected: 'DIRECT', desc: 'One-Class Supabase WebSockets' },
    { url: 'https://pub-bb2e103a32db4e198524a2e9ed8f35b4.r2.dev/logo.png', host: 'pub-bb2e103a32db4e198524a2e9ed8f35b4.r2.dev', expected: 'DIRECT', desc: 'One-Class R2 CDN Assets' },
    { url: 'https://connector-gateway.lovable.dev/api', host: 'connector-gateway.lovable.dev', expected: 'DIRECT', desc: 'One-Class Lovable Gateway' },

    // Google Suite & Authentication
    { url: 'https://gemini.google.com', host: 'gemini.google.com', expected: 'DIRECT', desc: 'Google Gemini AI' },
    { url: 'https://jnn-pa.googleapis.com', host: 'jnn-pa.googleapis.com', expected: 'DIRECT', desc: 'Gemini Chat API (jnn-pa)' },
    { url: 'https://accounts.google.com', host: 'accounts.google.com', expected: 'DIRECT', desc: 'Google Accounts Sign-in' },
    { url: 'https://myaccount.google.com', host: 'myaccount.google.com', expected: 'DIRECT', desc: 'Google MyAccount / 2FA' },
    { url: 'https://accounts.youtube.com/accounts/SetSID', host: 'accounts.youtube.com', expected: 'DIRECT', desc: 'Google Login Session Sync (SetSID)' },
    { url: 'https://play.google.com/log', host: 'play.google.com', expected: 'DIRECT', desc: 'Google Clearcut Risk Logger' },
    { url: 'https://recaptcha.net/recaptcha/api.js', host: 'recaptcha.net', expected: 'DIRECT', desc: 'Google reCAPTCHA Endpoint' },
    { url: 'https://fonts.googleapis.com/css2', host: 'fonts.googleapis.com', expected: 'DIRECT', desc: 'Google Fonts CSS' },
    { url: 'https://fonts.gstatic.com/s/heebo/v26/test.woff2', host: 'fonts.gstatic.com', expected: 'DIRECT', desc: 'Google Fonts Static' },
    { url: 'https://ssl.gstatic.com/accounts/ui/avatar.png', host: 'ssl.gstatic.com', expected: 'DIRECT', desc: 'Google Accounts Static UI' },
    { url: 'https://encrypted-tbn0.gstatic.com/images', host: 'encrypted-tbn0.gstatic.com', expected: 'DIRECT', desc: 'Google Static Images' },
    { url: 'https://lh3.googleusercontent.com/a/photo', host: 'lh3.googleusercontent.com', expected: 'DIRECT', desc: 'Google User Profile Photos' },
    { url: 'https://doc-0k-docs.googleusercontent.com', host: 'doc-0k-docs.googleusercontent.com', expected: 'DIRECT', desc: 'Google Docs Export CDN' },
    { url: 'https://classroom.google.com', host: 'classroom.google.com', expected: 'DIRECT', desc: 'Google Classroom' },
    { url: 'https://docs.google.com', host: 'docs.google.com', expected: 'DIRECT', desc: 'Google Docs' },
    { url: 'https://drive.google.com', host: 'drive.google.com', expected: 'DIRECT', desc: 'Google Drive' },

    // Microsoft Copilot & Cloudflare Turnstile
    { url: 'https://copilot.microsoft.com', host: 'copilot.microsoft.com', expected: 'DIRECT', desc: 'Microsoft Copilot' },
    { url: 'https://copilot.cloud.microsoft', host: 'copilot.cloud.microsoft', expected: 'DIRECT', desc: 'Microsoft 365 Copilot Unified' },
    { url: 'https://challenges.cloudflare.com/turnstile', host: 'challenges.cloudflare.com', expected: 'DIRECT', desc: 'Cloudflare Turnstile Verification' },
    { url: 'https://login.microsoftonline.com', host: 'login.microsoftonline.com', expected: 'DIRECT', desc: 'Microsoft Online Auth' },

    // Ministry of Education & Government SSO
    { url: 'https://edu.gov.il', host: 'edu.gov.il', expected: 'DIRECT', desc: 'Ministry of Education' },
    { url: 'https://students.edu.gov.il', host: 'students.edu.gov.il', expected: 'DIRECT', desc: 'Student Portal (Subdomain)' },
    { url: 'https://my.edu.gov.il/idp/profile/SAML2/Redirect/SSO', host: 'my.edu.gov.il', expected: 'DIRECT', desc: 'EduLogin Identity Provider' },
    { url: 'https://education.gov.il', host: 'education.gov.il', expected: 'DIRECT', desc: 'Education Gov' },
    { url: 'https://login.gov.il/nidp/saml2/sso', host: 'login.gov.il', expected: 'DIRECT', desc: 'National Government SSO Login' },
    { url: 'https://sso.cet.ac.il/as/authorization.oauth2', host: 'sso.cet.ac.il', expected: 'DIRECT', desc: 'CET SSO OAuth2' },
    { url: 'https://web.mashov.info/students/login', host: 'web.mashov.info', expected: 'DIRECT', desc: 'Mashov Students Login' },
    { url: 'https://www.google.com/recaptcha/api.js', host: 'www.google.com', expected: 'DIRECT', desc: 'Google reCAPTCHA Script on Google.com' },

    // Veyon & Local LAN (Must NEVER be blocked)
    { url: 'http://localhost:11100', host: 'localhost', expected: 'DIRECT', desc: 'Localhost / Veyon' },
    { url: 'http://127.0.0.1:11400', host: '127.0.0.1', expected: 'DIRECT', desc: 'Loopback IP' },
    { url: 'http://192.168.1.50:11100', host: '192.168.1.50', expected: 'DIRECT', desc: 'Veyon Client Lab IP (192.168.x)' },
    { url: 'http://10.0.0.15:11100', host: '10.0.0.15', expected: 'DIRECT', desc: 'Veyon Client Lab IP (10.x)' },
    { url: 'http://server.local', host: 'server.local', expected: 'DIRECT', desc: 'Local Network Server' },
    { url: 'http://printer', host: 'printer', expected: 'DIRECT', desc: 'Plain Hostname' },

    // Blocked Sites (Google Search, YouTube Videos, Games, Proxies, Social Media)
    { url: 'https://www.google.com', host: 'www.google.com', expected: 'PROXY 127.0.0.1:9999', desc: 'Google Search Engine (Blocked)' },
    { url: 'https://google.com', host: 'google.com', expected: 'PROXY 127.0.0.1:9999', desc: 'Google Root Domain (Blocked)' },
    { url: 'https://www.youtube.com', host: 'www.youtube.com', expected: 'PROXY 127.0.0.1:9999', desc: 'YouTube Web (Blocked)' },
    { url: 'https://youtube.com', host: 'youtube.com', expected: 'PROXY 127.0.0.1:9999', desc: 'YouTube Root Domain (Blocked)' },
    { url: 'https://roblox.com', host: 'roblox.com', expected: 'PROXY 127.0.0.1:9999', desc: 'Roblox (Game)' },
    { url: 'https://crazygames.com', host: 'crazygames.com', expected: 'PROXY 127.0.0.1:9999', desc: 'CrazyGames (Game Portal)' },
    { url: 'https://poki.com', host: 'poki.com', expected: 'PROXY 127.0.0.1:9999', desc: 'Poki (Games)' },
    { url: 'https://kproxy.com', host: 'kproxy.com', expected: 'PROXY 127.0.0.1:9999', desc: 'Web Proxy Bypasser' },
    { url: 'https://tiktok.com', host: 'tiktok.com', expected: 'PROXY 127.0.0.1:9999', desc: 'TikTok' },
    { url: 'https://instagram.com', host: 'instagram.com', expected: 'PROXY 127.0.0.1:9999', desc: 'Instagram' },
    { url: 'https://discord.com', host: 'discord.com', expected: 'PROXY 127.0.0.1:9999', desc: 'Discord' }
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
