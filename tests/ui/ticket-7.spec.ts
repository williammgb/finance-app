import { test, expect, type Page } from '@playwright/test';

// Ticket #7 — the Categories page: add, rename in place, delete an unused one.
// Runs on the fresh database `./run ui` starts with, and leaves it empty again.

const screens = '.work/screens';

// Collects every console error and uncaught page error.
function watch(page: Page): string[] {
  const problems: string[] = [];
  page.on('console', (m) => {
    if (m.type() === 'error') problems.push(`console error: ${m.text()}`);
  });
  page.on('pageerror', (e) => problems.push(`page error: ${e.message}`));
  return problems;
}

// A row whose Name cell reads exactly `name` (case matters).
function row(page: Page, name: string) {
  return page.locator('tbody tr').filter({ has: page.getByRole('cell', { name, exact: true }) });
}

// The names these tests add. Whatever a failed test left behind is deleted again, so
// the next run still starts on an empty database.
const added = ['Pets', 'pets', 'Food', 'Boodschappen en huishouden'];

test.afterEach(async ({ page }) => {
  await page.goto('/categories');
  await expect(page.getByRole('button', { name: 'Add category' })).toBeEnabled();
  for (const name of added) {
    const leftover = row(page, name);
    if ((await leftover.count()) === 0) continue;
    await leftover.getByRole('button', { name: 'Delete' }).click();
    await expect(leftover).toHaveCount(0);
  }
});

test('categories: empty state, add, rename, duplicate, delete', async ({ page }) => {
  const problems = watch(page);
  await page.goto('/categories');
  await expect(page.locator('main h1')).toHaveText('Categories');

  const name = page.locator('#new-category');
  const add = page.getByRole('button', { name: 'Add category' });
  const addCard = page.locator('.add-form');
  const listCard = page.locator('.list-card');
  await expect(add).toBeEnabled(); // the circuit is interactive

  // Empty state
  await expect(listCard).toHaveText('No categories yet');
  await expect(page.locator('table')).toHaveCount(0);
  await page.screenshot({ path: `${screens}/ticket-7-empty.png`, fullPage: true });

  // Empty name is refused under the input
  await add.click();
  await expect(addCard.locator('.validation-message')).toHaveText("Name can't be empty");

  // Add Pets with Enter: a row with 0 lines, € 0,00 and an enabled Delete; the input clears
  await name.fill('Pets');
  await name.press('Enter');
  const pets = row(page, 'Pets');
  await expect(pets).toHaveCount(1);
  await expect(pets.locator('td')).toHaveText(['', 'Pets', '0', '€ 0,00', /^Rename\s+Delete$/]);
  await expect(pets.getByRole('button', { name: 'Delete' })).toBeEnabled();
  await expect(pets.locator('.dot')).toHaveCSS('border-radius', '50%');
  await expect(name).toHaveValue('');
  await expect(addCard.locator('.validation-message')).toHaveCount(0);
  await page.screenshot({ path: `${screens}/ticket-7-added.png`, fullPage: true });

  // A double click on Add saves once
  await name.fill('Food');
  await add.dblclick();
  await expect(row(page, 'Food')).toHaveCount(1);
  await expect(page.locator('tbody tr')).toHaveCount(2);
  await expect(addCard.locator('.validation-message')).toHaveCount(0);
  await expect(page.locator('tbody tr').first()).toContainText('Food'); // alphabetical

  // Only one row in rename mode: starting another cancels the first
  await row(page, 'Food').getByRole('button', { name: 'Rename' }).click();
  await expect(page.locator('.rename-input')).toHaveValue('Food');
  await expect(page.locator('.rename-input')).toBeFocused();
  await pets.getByRole('button', { name: 'Rename' }).click();
  await expect(page.locator('.rename-input')).toHaveCount(1);
  await expect(page.locator('.rename-input')).toHaveValue('Pets');
  await expect(row(page, 'Food')).toHaveCount(1);

  // Rename to empty: message under the rename input, row stays in rename mode
  const renameInput = page.locator('.rename-input');
  await renameInput.fill('');
  await renameInput.press('Enter');
  await expect(page.locator('.rename-cell .validation-message')).toHaveText("Name can't be empty");
  await expect(renameInput).toHaveCount(1);

  // Escape cancels
  await renameInput.press('Escape');
  await expect(page.locator('.rename-input')).toHaveCount(0);
  await expect(row(page, 'Pets')).toHaveCount(1);

  // Rename Pets to pets with Save
  const totalColumn = await page.locator('thead th').nth(3).boundingBox();
  await row(page, 'Pets').getByRole('button', { name: 'Rename' }).click();
  await page.locator('.rename-input').fill('pets');
  expect(await page.locator('thead th').nth(3).boundingBox()).toEqual(totalColumn); // columns stay put
  await page.screenshot({ path: `${screens}/ticket-7-rename.png`, fullPage: true });
  await page.getByRole('button', { name: 'Save' }).dblclick(); // a double click saves once
  await expect(page.locator('.rename-input')).toHaveCount(0);
  await expect(page.locator('.validation-message')).toHaveCount(0);
  await expect(row(page, 'pets')).toHaveCount(1);
  await expect(row(page, 'Pets')).toHaveCount(0);

  // Add Pets again: refused under the Name input, nothing added
  await name.fill('Pets');
  await add.click();
  await expect(addCard.locator('.validation-message')).toHaveText('A category with that name already exists');
  await expect(page.locator('tbody tr')).toHaveCount(2);
  await page.screenshot({ path: `${screens}/ticket-7-duplicate.png`, fullPage: true });

  // The rename survives a reload
  await page.reload();
  await expect(add).toBeEnabled();
  await expect(row(page, 'pets')).toHaveCount(1);

  // Delete pets and Food: rows go, and the empty state is back
  await row(page, 'pets').getByRole('button', { name: 'Delete' }).dblclick(); // deletes once, no error
  await expect(row(page, 'pets')).toHaveCount(0);
  await expect(page.locator('tbody tr')).toHaveCount(1);
  await expect(page.locator('.validation-message')).toHaveCount(0);
  await page.screenshot({ path: `${screens}/ticket-7-deleted.png`, fullPage: true });
  await row(page, 'Food').getByRole('button', { name: 'Delete' }).click();
  await expect(listCard).toHaveText('No categories yet');

  expect(problems).toEqual([]);
});

test('categories at phone width: no sideways scroll', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const problems = watch(page);
  await page.goto('/categories');
  const add = page.getByRole('button', { name: 'Add category' });
  await expect(add).toBeEnabled();
  await page.locator('#new-category').fill('Boodschappen en huishouden');
  await add.click();
  const added = row(page, 'Boodschappen en huishouden');
  await expect(added).toHaveCount(1);
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(overflow).toBeLessThanOrEqual(0);
  const tableOverflow = await page.locator('.table-scroll').evaluate((el) => el.scrollWidth - el.clientWidth);
  expect(tableOverflow).toBeLessThanOrEqual(0);
  const card = (await page.locator('.list-card').boundingBox())!;
  for (const button of ['Rename', 'Delete']) {
    const box = (await added.getByRole('button', { name: button }).boundingBox())!;
    expect(box.x + box.width).toBeLessThanOrEqual(card.x + card.width);
  }
  await page.screenshot({ path: `${screens}/ticket-7-phone.png`, fullPage: true });
  await added.getByRole('button', { name: 'Delete' }).click();
  await expect(page.locator('.list-card')).toHaveText('No categories yet');
  expect(problems).toEqual([]);
});
