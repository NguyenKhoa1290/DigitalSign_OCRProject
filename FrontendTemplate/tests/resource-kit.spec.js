import { test, expect } from '@playwright/test';
import { existsSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

test('downloaded resource kit runs independently and preserves the original Kotlin bytes', async ({ page }) => {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const archive = await readFile(path.join(root,'dist/resources.zip'));
  const entries = new Map();
  let cursor = 0;
  while (archive.readUInt32LE(cursor) === 0x04034b50) {
    expect(archive.readUInt16LE(cursor+8)).toBe(0); // Stored ZIP, no decompressor dependency.
    const size = archive.readUInt32LE(cursor+18);
    const nameSize = archive.readUInt16LE(cursor+26);
    const extraSize = archive.readUInt16LE(cursor+28);
    const name = archive.subarray(cursor+30,cursor+30+nameSize).toString('utf8');
    const start = cursor+30+nameSize+extraSize;
    entries.set(name,archive.subarray(start,start+size)); cursor = start+size;
  }
  expect(entries.size).toBe(58);
  for (const reference of JSON.parse(entries.get('sources.json').toString())) {
    const original = await readFile(path.resolve(root,reference.source));
    expect(entries.get(`original-kotlin/${reference.file}`).equals(original)).toBe(true);
  }
  const errors = []; page.on('pageerror',error=>errors.push(error.message));
  await page.route('**/standalone-kit/**', route => {
    const name = new URL(route.request().url()).pathname.split('/standalone-kit/')[1] || 'index.html';
    const body = entries.get(name);
    return route.fulfill({status:body ? 200 : 404,body:body || '',contentType:name.endsWith('.js') ? 'text/javascript' : name.endsWith('.webp') ? 'image/webp' : name.endsWith('.woff2') ? 'font/woff2' : 'text/html'});
  });
  await page.goto('/standalone-kit/');
  await expect(page.locator('flight-taskbar')).toHaveAttribute('data-renderer','webgl2');
  await expect(page.getByRole('switch',{name:'Thông báo',exact:true})).toHaveAttribute('aria-checked','true');
  await page.getByRole('switch',{name:'Thông báo',exact:true}).click();
  await expect(page.getByRole('switch',{name:'Thông báo',exact:true})).toHaveAttribute('aria-checked','false');
  await expect(page.locator('adaptive-glass')).toHaveAttribute('data-renderer','webgl2');
  await page.locator('#resource-choice').selectOption('control-center');
  await page.getByRole('button',{name:'Chế độ máy bay',exact:true}).click();
  expect(await page.locator('liquid-control-center').evaluate(el=>el.state.airplane)).toBe(true);
  expect(await page.evaluate(()=>document.querySelector('liquid-control-center').shadowRoot.querySelectorAll('glass-surface').length)).toBe(11);
  await page.locator('#resource-choice').selectOption('lock-screen');
  await expect(page.locator('liquid-lock-screen')).toHaveAttribute('data-asset','loaded');
  await expect(page.locator('liquid-lock-screen').locator('glass-surface')).toHaveAttribute('data-renderer','webgl2');
  const clockSource = ['../AndroidLiquidGlass-kmp/AndroidLiquidGlass-kmp','../AndroidLiquidGlass-kmp']
    .map(directory => path.resolve(root,directory,'app/src/commonMain/composeResources/drawable/clock_sdf.webp'))
    .find(existsSync);
  expect(entries.get('src/assets/clock_sdf.webp').equals(await readFile(clockSource))).toBe(true);
  expect(entries.get('src/assets/inter-vietnamese.woff2').length).toBeGreaterThan(10_000);
  expect(entries.get('src/assets/inter-latin.woff2').length).toBeGreaterThan(40_000);
  expect(entries.get('INTER-OFL.txt').toString()).toContain('SIL Open Font License');
  expect(entries.get('Huongdancomponent.md').toString()).toContain('Hướng dẫn AI sử dụng');
  expect(errors).toEqual([]);
});
