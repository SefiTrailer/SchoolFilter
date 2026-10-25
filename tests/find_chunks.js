const https = require('https');

https.get('https://one-class.co.il/assets/index-aYzgagY0.js', res => {
    let data = '';
    res.on('data', chunk => data += chunk);
    res.on('end', () => {
        const regex = /["']([^"']+\.js)["']/g;
        const jsFiles = new Set();
        let match;
        while ((match = regex.exec(data)) !== null) {
            if (match[1].startsWith('assets/') || match[1].includes('-')) {
                jsFiles.add(match[1]);
            }
        }
        console.log('DYNAMIC CHUNKS:', Array.from(jsFiles));
    });
});
