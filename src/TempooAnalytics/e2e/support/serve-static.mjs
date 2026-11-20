// Minimal static server with SPA fallback: node serve-static.mjs <dir> <port>
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, normalize, resolve } from 'node:path';

const [dir, port] = [resolve(process.argv[2]), Number(process.argv[3])];
const types = {
    '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.mjs': 'text/javascript',
    '.css': 'text/css', '.json': 'application/json', '.svg': 'image/svg+xml', '.png': 'image/png',
    '.ico': 'image/x-icon', '.woff2': 'font/woff2', '.woff': 'font/woff', '.txt': 'text/plain',
};

createServer(async (req, res) => {
    const path = normalize(decodeURIComponent(new URL(req.url, 'http://x').pathname)).replace(/^(\.\.[/\\])+/, '');
    let file = join(dir, path);
    try {
        if (!(await stat(file)).isFile()) throw new Error('not a file');
    } catch {
        file = join(dir, 'index.html'); // client-side routes
    }
    try {
        res.writeHead(200, { 'content-type': types[extname(file)] ?? 'application/octet-stream' });
        res.end(await readFile(file));
    } catch {
        res.writeHead(404).end();
    }
}).listen(port, '127.0.0.1');
