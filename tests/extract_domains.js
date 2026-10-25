const https = require('https');

https.get('https://one-class.co.il/assets/index-aYzgagY0.js', res => {
    let data = '';
    res.on('data', chunk => data += chunk);
    res.on('end', () => {
        const regex = /https?:\/\/([a-zA-Z0-9\.\-_]+)/g;
        const domains = new Set();
        let match;
        while ((match = regex.exec(data)) !== null) {
            domains.add(match[1]);
        }
        console.log('DOMAINS IN ONE-CLASS INDEX:');
        console.log(Array.from(domains));
    });
});
