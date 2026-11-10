// Read-only UI fixtures: no uploads, OCR calls or application data changes.
const assert = require('node:assert/strict');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.HAU_TEST_WEB || 'http://localhost:5227';
const id = 'bbbbbbbb-0000-0000-0000-000000000001';
const editor = 'bbbbbbbb-0000-0000-0000-000000000002';
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aOhkAAAAASUVORK5CYII=', 'base64');
async function run(browser, width, type) {
    const context = await browser.newContext({ viewport: { width, height: 850 } });
    const token = [Buffer.from('{"alg":"none"}').toString('base64url'), Buffer.from(JSON.stringify({
        sub: id, name: 'Specialist fixture', role: 'Specialist', exp: Math.floor(Date.now() / 1000) + 3600
    })).toString('base64url'), 'fixture'].join('.');
    await context.addInitScript(t => { if (window === window.top) localStorage.setItem('auth_token', t); }, token);
    const edits = [1, 2].map(revision => ({ id: editor, action: 'OcrEdited', processedByName: 'Văn thư kiểm thử',
        timestamp: `2026-11-04T0${revision}:00:00Z`, comment: `Tải lên bản Word OCR đã chỉnh sửa (lần ${revision}): ban-sua-${revision}.docx.` }));
    const doc = { id, title: 'Lịch sử hiệu chỉnh', docTypeName: type, status: 'Draft', createdById: id,
        concurrencyVersion: editor, minioPath: 'documents/source.png', fileName: 'source.png', fileContentType: 'image/png',
        processes: edits, ocrDataRaw: JSON.stringify({ provider: 'gemini', selectedPages: [1], createdAt: '2026-11-04T00:00:00Z',
            layout: { blocks: [{ type: 'paragraph', text: 'Nội dung kiểm thử' }] },
            editedDocument: { id: editor, sourcePath: 'documents/source.png', fileName: 'ban-sua-2.docx',
                editedByName: 'Văn thư kiểm thử', editorRole: 'Clerk', editedAt: edits[1].timestamp, revision: 2 } }) };
    const unexpected = [], errors = [];
    await context.route('**/api/**', async route => {
        const req = route.request(), p = new URL(req.url()).pathname;
        if (req.method() !== 'GET') { unexpected.push(req.method() + ' ' + p); return route.abort(); }
        if (p === '/api/notifications/stream') return route.fulfill({ contentType: 'text/event-stream', body: 'data: {"unreadCount":0}\n\n' });
        if (p === '/api/documents/' + id) return route.fulfill({ json: { success: true, data: doc } });
        if (p.endsWith('/file')) return route.fulfill({ contentType: 'image/png', body: png });
        if (p.endsWith('/ocr-jobs/latest')) return route.fulfill({ json: { success: true, data: null } });
        unexpected.push(p); return route.abort();
    });
    const page = await context.newPage(); page.setDefaultTimeout(60000); page.on('pageerror', e => errors.push(e.message));
    try {
        await page.goto(base + '/documents/' + id, { waitUntil: 'domcontentloaded' });
        await page.getByRole('heading', { name: doc.title, exact: true }).waitFor();
        const history = page.locator('.card').filter({ has: page.locator('.card-title', { hasText: 'Lịch sử xử lý' }) });
        assert.equal(await history.locator('.timeline-item').count(), 2);
        assert.equal(await history.getByText('Hiệu chỉnh kết quả OCR', { exact: true }).count(), 2);
        assert.match(await history.innerText(), /Văn thư kiểm thử/);
        assert.match(await history.locator('.timeline-item').first().innerText(), /lần 2.*ban-sua-2.docx/);
        assert.match(await history.locator('.timeline-item').first().innerText(), /04\/11\/2026 09:00/);
        assert.equal(await page.getByRole('button', { name: 'Tải lên bản chỉnh sửa', exact: true }).count(), 0);
        assert.equal(await page.getByRole('button', { name: 'Chạy OCR', exact: true }).count(), 0);
        await page.reload({ waitUntil: 'domcontentloaded' });
        await history.getByText(/ban-sua-2.docx/).waitFor();
        assert.equal(await history.locator('.timeline-item').count(), 2);
        assert.deepEqual(unexpected, []); assert.deepEqual(errors, []);
        assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
        console.log(`PASS Specialist ${type} ${width}px: editor, time, revisions, reload, no OCR edit permission`);
    } finally { await context.close(); }
}
(async () => {
    const browser = await chromium.launch({ headless: true, executablePath: process.env.HAU_TEST_CHROMIUM });
    try { for (const width of [1366, 300]) for (const type of ['CongVanDi', 'CongVanDen']) await run(browser, width, type); }
    finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
