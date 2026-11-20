#!/usr/bin/env node
/*
  Minimal admin CLI for TempooAnalytics
  Usage:
    node tempoo-admin.js generate <siteKey>
    node tempoo-admin.js rotate <siteKey>
    node tempoo-admin.js revoke <siteKey>

  Environment variables:
    MANAGEMENT_API_KEY - the admin key to use (optional; will prompt if missing)
    API_URL - base URL for the API (default: http://localhost:5000)
    ADMIN_USER - optional admin user name to send as X-Admin-User header
*/

const readline = require('readline');

const argv = process.argv.slice(2);
if (argv.length < 2) {
    console.error('Usage: node tempoo-admin.js <generate|rotate|revoke> <siteKey>');
    process.exit(2);
}

const cmd = argv[0];
const siteKey = argv[1];
const apiUrl = process.env.API_URL || 'http://localhost:5000';
const adminUser = process.env.ADMIN_USER || null;

async function promptSecret(prompt) {
    const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
    return new Promise((resolve) => {
        rl.question(prompt, (ans) => { rl.close(); resolve(ans); });
    });
}

async function getManagementKey() {
    if (process.env.MANAGEMENT_API_KEY && process.env.MANAGEMENT_API_KEY.length > 0) return process.env.MANAGEMENT_API_KEY;
    const k = await promptSecret('Management API key: ');
    if (!k || k.length === 0) {
        console.error('Management API key required. Set MANAGEMENT_API_KEY env var or enter it interactively.');
        process.exit(2);
    }
    return k;
}

async function callAdmin(path, method='POST', body=null) {
    const key = await getManagementKey();
    const headers = {
        'Authorization': 'Bearer ' + key
    };
    if (adminUser) headers['X-Admin-User'] = adminUser;

    const url = apiUrl.replace(/\/$/, '') + path;

    const opts = { method, headers };
    if (body) {
        headers['Content-Type'] = 'application/json';
        opts.body = JSON.stringify(body);
    }

    try {
        const res = await fetch(url, opts);
        if (res.status >= 200 && res.status < 300) {
            const text = await res.text();
            try { const json = JSON.parse(text); console.log(JSON.stringify(json, null, 2)); }
            catch { console.log(text); }
            return 0;
        } else if (res.status === 204) {
            console.log('Success (no content)');
            return 0;
        } else {
            const t = await res.text();
            console.error('Error', res.status, t);
            return 3;
        }
    } catch (err) {
        console.error('Request failed:', err);
        return 4;
    }
}

(async () => {
    if (cmd === 'generate') {
        process.exitCode = await callAdmin(`/admin/sites/${encodeURIComponent(siteKey)}/write-key`, 'POST');
    } else if (cmd === 'rotate') {
        process.exitCode = await callAdmin(`/admin/sites/${encodeURIComponent(siteKey)}/write-key/rotate`, 'POST');
    } else if (cmd === 'revoke') {
        process.exitCode = await callAdmin(`/admin/sites/${encodeURIComponent(siteKey)}/write-key`, 'DELETE');
    } else {
        console.error('Unknown command:', cmd);
        process.exit(2);
    }
})();
