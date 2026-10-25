const https = require('https');

https.get('https://one-class.co.il/assets/Auth-B95uhQbr.js', res => {
    let data = '';
    res.on('data', chunk => data += chunk);
    res.on('end', () => {
        const regex = /https?:\/\/([a-zA-Z0-9\.\-_]+)/g;
        const domains = new Set();
        let match;
        while ((match = regex.exec(data)) !== null) {
            domains.add(match[1]);
        }
        console.log('DOMAINS IN AUTH CHUNK:');
        console.log(Array.from(domains));

        // Look for any mention of oauth, edu, gov, mashov, cet, captcha, cloudflare, turnstile
        const keywords = ['oauth', 'login', 'redirect', 'provider', 'turnstile', 'recaptcha', 'captcha', 'hcaptcha', 'cloudflare'];
        keywords.forEach(kw => {
            const r = new RegExp(kw, 'gi');
            let m;
            let count = 0;
            while ((m = r.exec(data)) !== null && count < 3) {
                console.log(`[KEYWORD ${kw}]:`, data.substring(Math.max(0, m.index - 40), Math.min(data.length, m.index + 80)));
                count++;
            }
        });
    });
});
