import { test, expect, type Page } from '@playwright/test';

// Ticket #3 — the app shell: menu, page stubs and the look.

const screens = '.work/screens';

const pages = [
  { path: '/', title: 'Overview', screen: 'overview' },
  { path: '/spending', title: 'Spending', screen: 'spending' },
  { path: '/categories', title: 'Categories', screen: 'categories' },
  { path: '/data', title: 'Data', screen: 'data' },
];

// Collects every console error, uncaught page error and request for a Bootstrap file.
function watch(page: Page): string[] {
  const problems: string[] = [];
  page.on('console', (m) => {
    if (m.type() === 'error') problems.push(`console error: ${m.text()}`);
  });
  page.on('pageerror', (e) => problems.push(`page error: ${e.message}`));
  page.on('request', (r) => {
    if (r.url().toLowerCase().includes('bootstrap')) problems.push(`bootstrap request: ${r.url()}`);
  });
  return problems;
}

function style(page: Page, selector: string, property: string): Promise<string> {
  return page.locator(selector).first().evaluate(
    (el, p) => getComputedStyle(el).getPropertyValue(p),
    property,
  );
}

test('desktop: each menu link opens its titled page and is highlighted', async ({ page }) => {
  const problems = watch(page);
  await page.goto('/');

  await expect(page).toHaveTitle('Finance');
  expect(await style(page, 'body', 'background-color')).toBe('rgb(246, 246, 244)');
  expect(await style(page, '.sidebar', 'background-color')).toBe('rgb(255, 255, 255)');
  await expect(page.locator('.sidebar .brand')).toHaveText('Finance');
  expect(await style(page, '.sidebar .brand', 'font-size')).toBe('20px');
  await expect(page.locator('.sidebar .nav-link')).toHaveText(pages.map((p) => p.title));
  const menuWidth = await page.locator('.sidebar').evaluate((el) => el.getBoundingClientRect().width);
  expect(menuWidth).toBeGreaterThanOrEqual(200);
  expect(menuWidth).toBeLessThanOrEqual(240);

  for (const p of pages) {
    await page.locator('.sidebar .nav-link', { hasText: p.title }).click();
    await expect(page).toHaveURL(new RegExp(`${p.path}$`));
    const h1 = page.locator('main h1');
    await expect(h1).toHaveText(p.title);
    // An interactive page replaces its prerendered markup when the circuit starts: retry the read.
    await expect.poll(() => style(page, 'main h1', 'font-size')).toBe('28px');
    await expect.poll(() => style(page, 'main h1', 'font-weight')).toBe('700');
    await expect(page).toHaveTitle('Finance');

    const active = page.locator('.sidebar .nav-link.active');
    await expect(active).toHaveCount(1);
    await expect(active).toHaveText(p.title);
    expect(await style(page, '.sidebar .nav-link.active', 'background-color')).toBe('rgb(233, 232, 228)');
    expect(await style(page, '.sidebar .nav-link.active', 'font-weight')).toBe('700');
    const inactive = page.locator('.sidebar .nav-link:not(.active)').first();
    expect(await inactive.evaluate((el) => getComputedStyle(el).fontWeight)).toBe('400');

    await page.screenshot({ path: `${screens}/ticket-3-${p.screen}.png`, fullPage: true });
  }

  expect(problems).toEqual([]);
});

test('phone: the menu stacks above the content with no sideways scroll', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const problems = watch(page);

  for (const p of pages) {
    await page.goto(p.path);
    await expect(page.locator('main h1')).toHaveText(p.title);
    const menu = await page.locator('.sidebar').boundingBox();
    const main = await page.locator('main').boundingBox();
    expect(menu).not.toBeNull();
    expect(main).not.toBeNull();
    expect(menu!.y + menu!.height).toBeLessThanOrEqual(main!.y + 1);
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(0);
    if (p.path === '/') {
      await page.screenshot({ path: `${screens}/ticket-3-phone.png`, fullPage: true });
    }
  }

  expect(problems).toEqual([]);
});

test('an unknown address shows Page not found inside the layout', async ({ page }) => {
  // The menu's Spending link matches any /spending/… address by prefix, a month that does not exist too.
  for (const [path, screen, active] of [
    ['/nope', 'not-found', []],
    ['/spending/2026/13', 'not-found-month', ['Spending']],
  ] as const) {
    await page.goto(path);
    await expect(page.locator('main h1')).toHaveText('Page not found');
    await expect(page.locator('.sidebar .brand')).toHaveText('Finance');
    await expect(page.locator('.sidebar .nav-link')).toHaveCount(4);
    await expect(page.locator('.sidebar .nav-link.active')).toHaveText([...active]);
    await expect(page).toHaveTitle('Finance');
    await page.screenshot({ path: `${screens}/ticket-3-${screen}.png`, fullPage: true });
  }
});
