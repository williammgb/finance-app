import { test, expect, type Page } from '@playwright/test';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

// Ticket #9 — the Data page: a refused file, import, download, replace, and empty again.
// Runs on the database `./run ui` starts with, expecting it empty, and leaves it empty.
// With IMPORT_FILE, IMPORT_SUMMARY and IMPORT_TOTAL set, a second test imports that file (the
// converted Excel history): run it only against an app started on a throwaway data folder.

const screens = '.work/screens';
const files = '.work/tmp/ticket-9';
const head = 'Year,Month,Category,Amount,Note\r\n';

// Collects every console error and uncaught page error.
function watch(page: Page): string[] {
  const problems: string[] = [];
  page.on('console', (m) => {
    if (m.type() === 'error') problems.push(`console error: ${m.text()}`);
  });
  page.on('pageerror', (e) => problems.push(`page error: ${e.message}`));
  return problems;
}

// Writes a CSV file for the file chooser and returns its path.
function fixture(name: string, text: string): string {
  mkdirSync(files, { recursive: true });
  const path = join(files, name);
  writeFileSync(path, text, 'utf8');
  return path;
}

function today(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

// Opens Data and waits for the live page: the buttons stay disabled until then.
async function openData(page: Page) {
  await page.goto('/data');
  await expect(page.getByRole('button', { name: 'Download CSV' })).toBeEnabled();
}

async function choose(page: Page, path: string) {
  await page.locator('#csv-file').setInputFiles(path);
}

async function download(page: Page): Promise<{ name: string; path: string }> {
  const waiting = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download CSV' }).click();
  const file = await waiting;
  const path = join(files, file.suggestedFilename());
  await file.saveAs(path);
  return { name: file.suggestedFilename(), path };
}

async function replaceAll(page: Page) {
  await page.getByRole('button', { name: 'Replace all data' }).click();
  await page.getByRole('button', { name: 'Yes, replace' }).click();
}

// Whatever a test left behind is replaced by an empty file.
test.afterEach(async ({ page }) => {
  await openData(page);
  if ((await page.locator('#export-summary').innerText()) === 'No data') return;
  await choose(page, fixture('header-only.csv', head));
  await expect(page.locator('#file-summary')).toHaveText('0 lines · 0 categories');
  await replaceAll(page);
  await expect(page.locator('#export-summary')).toHaveText('No data');
});

test('data: refuse a bad file, import, download, replace, empty again', async ({ page }) => {
  const problems = watch(page);
  const sample = fixture(
    'sample.csv',
    '﻿' + head
      + ',,Wonen,,\r\n'
      + '2025,1,Wonen,500.00,\r\n'
      + '2025,2,Eten & drank,12.50,"tandarts, zorg"\r\n'
      + '2025,3,wonen,500.00,\r\n',
  );
  const bad = fixture(
    'bad-month.csv',
    head + '2025,1,Wonen,1.00,\r\n2025,13,Wonen,1.00,\r\n2025,2,Wonen,abc,\r\n',
  );
  const empty = fixture('header-only.csv', head);
  const exportSummary = page.locator('#export-summary');
  const fileSummary = page.locator('#file-summary');
  const importButton = page.getByRole('button', { name: 'Import', exact: true });
  const replaceButton = page.getByRole('button', { name: 'Replace all data' });
  const imported = page.locator('.imported');
  const summary = '3 lines · 2 categories · Jan 2025 – Mar 2025';

  // An empty app
  await openData(page);
  await expect(page.locator('main h1')).toHaveText('Data');
  await expect(page.locator('.data-card h2')).toHaveText(['Export', 'Import']);
  await expect(exportSummary).toHaveText('No data');
  await expect(page.locator('label[for=csv-file]')).toHaveText('Choose CSV file');
  await page.screenshot({ path: `${screens}/ticket-9-empty.png`, fullPage: true });

  // A bad file: a red list naming the lines, no import button
  await choose(page, bad);
  await expect(page.locator('.file-name')).toHaveText('bad-month.csv');
  await expect(page.locator('.problems li')).toHaveText([
    'Line 3: month must be 1–12',
    'Line 4: amount must look like 12.50',
  ]);
  await expect(page.locator('.problems')).toHaveCSS('color', 'rgb(180, 35, 24)');
  await expect(importButton).toHaveCount(0);
  await expect(replaceButton).toHaveCount(0);
  await expect(fileSummary).toHaveCount(0);
  await page.screenshot({ path: `${screens}/ticket-9-bad-file.png`, fullPage: true });

  // A good file into the empty app: its summary and Import; a double click imports once
  await choose(page, sample);
  await expect(page.locator('.file-name')).toHaveText('sample.csv');
  await expect(fileSummary).toHaveText(summary);
  await expect(page.locator('.problems')).toHaveCount(0);
  await expect(importButton).toBeEnabled();
  await expect(replaceButton).toHaveCount(0);
  await page.screenshot({ path: `${screens}/ticket-9-chosen.png`, fullPage: true });
  await importButton.dblclick();
  await expect(imported).toHaveText('Imported 3 lines and 2 categories');
  await expect(importButton).toHaveCount(0);
  await expect(exportSummary).toHaveText(summary);
  await page.screenshot({ path: `${screens}/ticket-9-imported.png`, fullPage: true });

  // Categories shows what came in
  await page.goto('/categories');
  await expect(page.locator('tbody tr td:nth-child(2)')).toHaveText(['Eten & drank', 'Wonen']);
  await expect(page.locator('tbody tr td:nth-child(3)')).toHaveText(['1', '2']);
  await expect(page.locator('tbody tr td:nth-child(4)')).toHaveText(['€ 12,50', '€ 1.000,00']);

  // Download: one file for a double click, named for today, holding the same data
  await openData(page);
  let downloads = 0;
  page.on('download', () => downloads++);
  const waiting = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download CSV' }).dblclick();
  const file = await waiting;
  expect(file.suggestedFilename()).toBe(`finance-export-${today()}.csv`);
  const exported = join(files, file.suggestedFilename());
  await file.saveAs(exported);
  expect(readFileSync(exported, 'utf8')).toBe(
    '﻿' + head
      + ',,Wonen,,\r\n'
      + ',,Eten & drank,,\r\n'
      + '2025,1,Wonen,500.00,\r\n'
      + '2025,2,Eten & drank,12.50,"tandarts, zorg"\r\n'
      + '2025,3,Wonen,500.00,\r\n',
  );
  await page.waitForTimeout(1500);
  expect(downloads).toBe(1);

  // The download back in: Replace all data asks first; Cancel keeps everything
  await choose(page, exported);
  await expect(fileSummary).toHaveText(summary);
  await expect(importButton).toHaveCount(0);
  await replaceButton.click();
  await expect(page.locator('.confirm-box .confirm-text')).toHaveText('Replace all 3 lines and 2 categories?');
  await expect(page.getByRole('button', { name: 'Yes, replace' })).toHaveCSS('background-color', 'rgb(180, 35, 24)');
  await page.screenshot({ path: `${screens}/ticket-9-confirm.png`, fullPage: true });
  await page.getByRole('button', { name: 'Cancel' }).click();
  await expect(page.locator('.confirm-box')).toHaveCount(0);
  await expect(replaceButton).toBeVisible();

  // Yes, replace: a double click replaces once
  await replaceButton.click();
  await page.getByRole('button', { name: 'Yes, replace' }).dblclick();
  await expect(imported).toHaveText('Imported 3 lines and 2 categories');
  await expect(exportSummary).toHaveText(summary);
  await page.screenshot({ path: `${screens}/ticket-9-replaced.png`, fullPage: true });

  // Phone width: nothing scrolls sideways
  await page.setViewportSize({ width: 390, height: 844 });
  await replaceButton.count();
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(overflow).toBeLessThanOrEqual(0);
  await page.screenshot({ path: `${screens}/ticket-9-phone.png`, fullPage: true });
  await page.setViewportSize({ width: 1280, height: 800 });

  // A header-only file empties the app
  await choose(page, empty);
  await expect(fileSummary).toHaveText('0 lines · 0 categories');
  await replaceAll(page);
  await expect(imported).toHaveText('Imported 0 lines and 0 categories');
  await expect(exportSummary).toHaveText('No data');

  expect(problems).toEqual([]);
});

test('real file: import, category totals, download, replace', async ({ page }) => {
  const file = process.env.IMPORT_FILE;
  const summary = process.env.IMPORT_SUMMARY;
  const total = process.env.IMPORT_TOTAL;
  test.skip(!file || !summary || !total, 'set IMPORT_FILE, IMPORT_SUMMARY and IMPORT_TOTAL, on a throwaway data folder');
  const [, lines, categories] = /^(\d+) lines · (\d+) categories/.exec(summary!)!;
  const importedText = `Imported ${lines} lines and ${categories} categories`;
  const cents = (text: string) => Number(text.replace(/\D/g, ''));
  const problems = watch(page);

  // Import into the empty app
  await openData(page);
  await expect(page.locator('#export-summary')).toHaveText('No data');
  await choose(page, file!);
  await expect(page.locator('#file-summary')).toHaveText(summary!);
  await page.screenshot({ path: `${screens}/ticket-9-real-chosen.png`, fullPage: true });
  await page.getByRole('button', { name: 'Import', exact: true }).click();
  await expect(page.locator('.imported')).toHaveText(importedText);
  await expect(page.locator('#export-summary')).toHaveText(summary!);
  await page.screenshot({ path: `${screens}/ticket-9-real-imported.png`, fullPage: true });

  // The Categories page's totals add up to the conversion's sum
  await page.goto('/categories');
  await expect(page.locator('tbody tr')).toHaveCount(Number(categories));
  const totals = await page.locator('tbody tr td:nth-child(4)').allInnerTexts();
  expect(totals.reduce((sum, t) => sum + cents(t), 0)).toBe(cents(total!));
  await page.screenshot({ path: `${screens}/ticket-9-real-categories.png`, fullPage: true });

  // Download it and replace everything with the download
  await openData(page);
  const { name, path } = await download(page);
  expect(name).toBe(`finance-export-${today()}.csv`);
  await choose(page, path);
  await expect(page.locator('#file-summary')).toHaveText(summary!);
  await replaceAll(page);
  await expect(page.locator('.imported')).toHaveText(importedText);
  await page.screenshot({ path: `${screens}/ticket-9-real-replaced.png`, fullPage: true });

  expect(problems).toEqual([]);
});
