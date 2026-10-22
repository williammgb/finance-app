import { test, expect, type Page } from '@playwright/test';

// Ticket #11 — the Overview page.
// The demo test runs against `./run demo` (DEMO_URL=http://localhost:5081) and takes docs/overview.png.
// The empty test runs against EMPTY_URL when set (an app started on a new temp data folder), else on the
// database `./run ui` starts with, expecting no lines.

const screens = '.work/screens';
const demo = process.env.DEMO_URL;
const euro = /^€\s\d{1,3}(\.\d{3})*,\d{2}$/;

// Collects every console error and uncaught page error.
function watch(page: Page): string[] {
  const problems: string[] = [];
  page.on('console', (m) => {
    if (m.type() === 'error') problems.push(`console error: ${m.text()}`);
  });
  page.on('pageerror', (e) => problems.push(`page error: ${e.message}`));
  return problems;
}

function cents(text: string): number {
  return Number(text.replace(/\D/g, ''));
}

// The line chart's drawn series, by name.
function series(page: Page) {
  return page.locator('.line-chart .apexcharts-line-series .apexcharts-series');
}

function legend(page: Page, name: string) {
  return page.locator('.legend').getByRole('button', { name, exact: true });
}

// No vertical scroll, and the donut card sits right of the line card, as tall.
async function checkLayout(page: Page) {
  const scroll = await page.evaluate(() => document.documentElement.scrollHeight - window.innerHeight);
  expect(scroll).toBeLessThanOrEqual(0);
  const line = (await page.locator('.line-card').boundingBox())!;
  const donut = (await page.locator('.donut-card').boundingBox())!;
  expect(donut.x).toBeGreaterThanOrEqual(line.x + line.width);
  expect(Math.abs(donut.y - line.y)).toBeLessThanOrEqual(1);
  expect(Math.abs(donut.height - line.height)).toBeLessThanOrEqual(1);
  expect(donut.width).toBeGreaterThanOrEqual(300);
  expect(donut.width).toBeLessThanOrEqual(400);
}

// Moves the mouse over the plot area at a fraction of its width.
async function hover(page: Page, at: number) {
  const plot = (await page.locator('.line-chart .apexcharts-grid').boundingBox())!;
  await page.mouse.move(plot.x + plot.width * at, plot.y + plot.height / 2);
}

test('overview on the demo data', async ({ page }) => {
  test.skip(!demo, 'set DEMO_URL to a running ./run demo');
  const problems = watch(page);
  const tooltip = page.locator('.chart-tooltip');

  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto(`${demo}/`);
  await expect(page.locator('main h1')).toHaveText('Overview');
  await expect(page.locator('.line-chart .apexcharts-svg')).toBeVisible();
  await expect(page.locator('.donut .apexcharts-svg')).toBeVisible();

  // Tiles
  await expect(page.locator('.tile-label')).toHaveText(['October 2026', 'vs September 2026', '12-month average']);
  const values = await page.locator('.tile-value').allInnerTexts();
  expect(values[0]).toMatch(euro);
  expect(values[1]).toMatch(/^[+-]\s€\s\d{1,3}(\.\d{3})*,\d{2}$/);
  expect(values[2]).toMatch(euro);
  await expect(page.locator('.tile-sub')).toHaveText(/^[+-]\d+,\d%$/);

  // The donut: total in the middle, the table largest first
  await expect(page.locator('.donut-card h2')).toHaveText('October 2026');
  await expect(page.locator('.donut-total')).toHaveText(values[0]);
  const amounts = (await page.locator('.donut-table td.number:not(.share)').allInnerTexts()).map(cents);
  expect(amounts.length).toBeGreaterThan(3);
  expect([...amounts].sort((a, b) => b - a)).toEqual(amounts);
  expect(amounts.reduce((a, b) => a + b, 0)).toBe(cents(values[0]));
  await expect(page.locator('.donut-table td.share').first()).toHaveText(/^\d+,\d%$/);

  // Total and the top 5 are on, the rest off
  await expect(page.locator('.legend-button')).toHaveCount(12);
  await expect(page.locator('.legend-button').first()).toHaveText('Total');
  await expect(page.locator('.legend-button[aria-pressed=true]')).toHaveCount(6);
  await expect(page.locator('.legend-button[aria-pressed=true]').nth(5)).toBeVisible();
  await expect(series(page)).toHaveCount(6);
  await expect(page.locator('.switch button[aria-pressed=true]')).toHaveText('12 months');
  await expect(page.locator('.line-chart .apexcharts-xaxis-label tspan').first()).toHaveText('Nov 25');
  await expect(page.locator('.line-chart .apexcharts-yaxis-label tspan').last()).toHaveText(/^€\s\d+(\.\d{3})*$/);
  await checkLayout(page);
  await page.screenshot({ path: `${screens}/ticket-11-demo-1280.png` });

  // Hover: dashed crosshair, dots on each line, tooltip: month and year, Total, then largest first
  await hover(page, 0.75);
  await expect(tooltip).toBeVisible();
  await expect(page.locator('.line-chart .apexcharts-xcrosshairs')).toHaveAttribute('stroke-dasharray', '4');
  await expect(page.locator('.line-chart .apexcharts-xcrosshairs')).toHaveClass(/apexcharts-active/);
  await expect(page.locator('.chart-tooltip-title')).toHaveText(/^(January|February|March|April|May|June|July|August|September|October|November|December) 20\d\d$/);
  await expect(page.locator('.chart-tooltip-row')).toHaveCount(6);
  await expect(page.locator('.chart-tooltip-name').first()).toHaveText('Total');
  const rows = await page.locator('.chart-tooltip-value').allInnerTexts();
  for (const value of rows) expect(value).toMatch(euro);
  const categoryValues = rows.slice(1).map(cents);
  expect([...categoryValues].sort((a, b) => b - a)).toEqual(categoryValues);
  expect(cents(rows[0])).toBeGreaterThanOrEqual(categoryValues.reduce((a, b) => a + b, 0));
  await page.screenshot({ path: `${screens}/ticket-11-hover.png` });
  const markers = page.locator('.line-chart .apexcharts-marker');
  expect(await markers.count()).toBeGreaterThanOrEqual(6);

  // The legend switches lines off and on, Total too
  const firstOn = (await page.locator('.legend-button[aria-pressed=true]').nth(1).innerText()).trim();
  const firstOff = (await page.locator('.legend-button[aria-pressed=false]').first().innerText()).trim();
  await legend(page, firstOn).click();
  await expect(legend(page, firstOn)).toHaveAttribute('aria-pressed', 'false');
  await expect(series(page)).toHaveCount(5);
  await legend(page, firstOff).click();
  await expect(legend(page, firstOff)).toHaveAttribute('aria-pressed', 'true');
  await expect(series(page)).toHaveCount(6);
  await legend(page, 'Total').click();
  await expect(legend(page, 'Total')).toHaveAttribute('aria-pressed', 'false');
  await expect(series(page)).toHaveCount(5);
  await page.mouse.move(5, 5);
  await page.screenshot({ path: `${screens}/ticket-11-legend.png` });
  await hover(page, 0.5);
  await expect(page.locator('.chart-tooltip-row')).toHaveCount(5);
  await expect(page.locator('.chart-tooltip-name').first()).not.toHaveText('Total');

  // Every line off: an empty chart area, the page keeps its layout
  const pressed = page.locator('.legend-button[aria-pressed=true]');
  for (let left = await pressed.count(); left > 0; left--) {
    await pressed.first().click();
    await expect(pressed).toHaveCount(left - 1);
  }
  await expect(page.locator('.line-chart .apexcharts-svg')).toHaveCount(0);
  await checkLayout(page);
  await page.screenshot({ path: `${screens}/ticket-11-all-off.png` });

  // All time: from November 2024, Total and a new top 5
  await page.getByRole('button', { name: 'All time' }).click();
  await expect(page.locator('.switch button[aria-pressed=true]')).toHaveText('All time');
  await expect(page.locator('.legend-button[aria-pressed=true]')).toHaveCount(5);
  await legend(page, 'Total').click();
  await expect(series(page)).toHaveCount(6);
  await expect(page.locator('.line-chart .apexcharts-xaxis-label tspan').first()).toHaveText('Nov 24');
  await hover(page, 0.005);
  await expect(page.locator('.chart-tooltip-title')).toHaveText('November 2024');
  await checkLayout(page);
  await page.screenshot({ path: `${screens}/ticket-11-all-time.png` });

  // Back to 12 months, at 1440×900: the README's screenshot
  await page.getByRole('button', { name: '12 months' }).click();
  await expect(page.locator('.line-chart .apexcharts-xaxis-label tspan').first()).toHaveText('Nov 25');
  await expect(series(page)).toHaveCount(6);
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.mouse.move(5, 5);
  await page.waitForTimeout(500);
  await checkLayout(page);
  await page.screenshot({ path: `${screens}/ticket-11-demo-1440.png` });
  await page.screenshot({ path: 'docs/overview.png' });

  expect(problems).toEqual([]);
});

test('overview on an empty database', async ({ page }) => {
  const problems = watch(page);
  await page.goto(`${process.env.EMPTY_URL ?? ''}/`);
  await expect(page.locator('main h1')).toHaveText('Overview');
  await expect(page.locator('.overview-empty .empty')).toHaveText('No spending yet');
  await expect(page.locator('.tile')).toHaveCount(0);
  await expect(page.locator('.line-card')).toHaveCount(0);
  await page.screenshot({ path: `${screens}/ticket-11-empty.png` });
  await page.getByRole('link', { name: 'Add spending' }).click();
  await expect(page).toHaveURL(/\/spending$/);
  await expect(page.locator('main h1')).toHaveText('Spending');
  expect(problems).toEqual([]);
});
