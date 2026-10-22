import { test, expect, type Page } from '@playwright/test';

// Ticket #8 — the Spending page: add, refuse, edit, delete, months, and the link with Categories.
// Runs on the database `./run ui` starts with, expecting no categories, and leaves none behind.

const screens = '.work/screens';
const october = '/spending/2026/10';

// Collects every console error and uncaught page error.
function watch(page: Page): string[] {
  const problems: string[] = [];
  page.on('console', (m) => {
    if (m.type() === 'error') problems.push(`console error: ${m.text()}`);
  });
  page.on('pageerror', (e) => problems.push(`page error: ${e.message}`));
  return problems;
}

// A category's group on the Spending page, by its exact name.
function group(page: Page, name: string) {
  return page.locator('.group').filter({ has: page.locator('.group-name').getByText(name, { exact: true }) });
}

// A row of the Categories table whose Name cell reads exactly `name`.
function categoryRow(page: Page, name: string) {
  return page.locator('tbody tr').filter({ has: page.getByRole('cell', { name, exact: true }) });
}

function addButton(page: Page) {
  return page.getByRole('button', { name: 'Add', exact: true });
}

// Whatever a test left behind is deleted again: October's lines first, then the categories.
test.afterEach(async ({ page }) => {
  await page.goto(october);
  if ((await addButton(page).count()) > 0) {
    await expect(addButton(page)).toBeEnabled();
    const lines = page.locator('.line');
    for (let left = await lines.count(); left > 0; left--) {
      await lines.first().getByRole('button', { name: 'Delete' }).click();
      await lines.first().getByRole('button', { name: 'Yes' }).click();
      await expect(lines).toHaveCount(left - 1);
    }
  }
  await page.goto('/categories');
  await expect(page.getByRole('button', { name: 'Add category' })).toBeEnabled();
  for (const name of ['Uitjes', 'Uitgaan', 'Boodschappen']) {
    const leftover = categoryRow(page, name);
    if ((await leftover.count()) === 0) continue;
    await leftover.getByRole('button', { name: 'Delete' }).click();
    await expect(leftover).toHaveCount(0);
  }
});

test('spending: add, refuse, edit, delete, change month, rename on Categories', async ({ page }) => {
  const problems = watch(page);

  // No categories: a link to Categories instead of the form
  await page.goto('/spending');
  await expect(page.locator('main h1')).toHaveText('Spending');
  await expect(page.getByRole('link', { name: 'Add a category first' })).toHaveAttribute('href', '/categories');
  await expect(page.locator('form.entry-form')).toHaveCount(0);
  await page.screenshot({ path: `${screens}/ticket-8-no-categories.png`, fullPage: true });

  // Two categories
  await page.goto('/categories');
  const addCategory = page.getByRole('button', { name: 'Add category' });
  await expect(addCategory).toBeEnabled();
  for (const name of ['Uitjes', 'Boodschappen']) {
    await page.locator('#new-category').fill(name);
    await addCategory.click();
    await expect(categoryRow(page, name)).toHaveCount(1);
  }

  // An empty month
  await page.goto(october);
  const add = addButton(page);
  await expect(add).toBeEnabled();
  const category = page.locator('#add-category');
  const amount = page.locator('#add-amount');
  const note = page.locator('#add-note');
  const amountMessage = page.locator('form.entry-form .field-amount .validation-message').first();
  const noteMessage = page.locator('form.entry-form .field-note .validation-message').first();
  const total = page.locator('.total-amount');
  await expect(page.locator('.month-name')).toHaveText('October 2026');
  await expect(page.locator('.spending-lines')).toHaveText('No spending in October 2026');
  await expect(page.locator('.total-row')).toHaveCount(0);
  await expect(category.locator('option')).toHaveText(['Boodschappen', 'Uitjes']);
  await expect(amount).toHaveAttribute('placeholder', '0,00');
  await page.screenshot({ path: `${screens}/ticket-8-empty-month.png`, fullPage: true });

  // Add 12,50 and 7.5 to Uitjes: two lines, € 20,00; Uitjes stays selected, amount and note clear
  await category.selectOption({ label: 'Uitjes' });
  await amount.fill('12,50');
  await note.fill('bioscoop');
  await add.click();
  const uitjes = group(page, 'Uitjes');
  await expect(uitjes.locator('.line')).toHaveCount(1);
  await expect(amount).toHaveValue('');
  await expect(note).toHaveValue('');
  await amount.fill('7.5');
  await add.dblclick(); // a double click adds once
  await expect(uitjes.locator('.line-amount')).toHaveText(['€ 12,50', '€ 7,50']);
  await expect(uitjes.locator('.line-note')).toHaveText(['bioscoop', '']);
  await expect(uitjes.locator('.group-subtotal')).toHaveText('€ 20,00');
  await expect(total).toHaveText('€ 20,00');
  await expect(category.locator('option:checked')).toHaveText('Uitjes');
  await expect(amount).toHaveValue('');
  await expect(note).toHaveValue('');
  await expect(page.locator('.validation-message')).toHaveCount(0);
  await page.screenshot({ path: `${screens}/ticket-8-added.png`, fullPage: true });

  // Refused: a bad amount, a note too long; nothing added
  await amount.fill('abc');
  await add.click();
  await expect(amountMessage).toHaveText('Enter an amount like 12,50');
  await expect(page.locator('.line')).toHaveCount(2);
  await page.screenshot({ path: `${screens}/ticket-8-error.png`, fullPage: true });
  for (const bad of ['12,5,0', '-3', '1.234,56', '1000000,01']) {
    await amount.fill(bad);
    await add.click();
    await expect(amountMessage).toHaveText('Enter an amount like 12,50');
  }
  await amount.fill('1');
  await note.fill('n'.repeat(201));
  await add.click();
  await expect(noteMessage).toHaveText('Note is too long');
  await expect(amountMessage).toHaveCount(0);
  await expect(page.locator('.line')).toHaveCount(2);
  await note.fill('');

  // A second category: groups are alphabetical, the total adds both
  await category.selectOption({ label: 'Boodschappen' });
  await amount.fill('5');
  await add.click();
  await expect(page.locator('.group-name')).toHaveText(['Boodschappen', 'Uitjes']);
  await expect(total).toHaveText('€ 25,00');
  await expect(page.locator('.line')).toHaveCount(3);

  // Edit: one line at a time, the amount as typed, refused amount, then 30
  const first = uitjes.locator('.line').nth(0);
  const second = uitjes.locator('.line').nth(1);
  const edit = page.locator('.line-edit');
  await first.getByRole('button', { name: 'Edit' }).click();
  await expect(edit.getByLabel('Amount')).toHaveValue('12,50');
  await expect(edit.getByLabel('Amount')).toBeFocused();
  await expect(edit.getByLabel('Note')).toHaveValue('bioscoop');
  await second.getByRole('button', { name: 'Edit' }).click();
  await expect(edit).toHaveCount(1);
  await expect(edit.getByLabel('Amount')).toHaveValue('7,50');
  await edit.getByRole('button', { name: 'Cancel' }).click();
  await expect(edit).toHaveCount(0);
  await first.getByRole('button', { name: 'Edit' }).click();
  await edit.getByLabel('Amount').fill('abc');
  await edit.getByRole('button', { name: 'Save' }).click();
  await expect(edit.locator('.field-amount .validation-message')).toHaveText('Enter an amount like 12,50');
  await edit.getByLabel('Amount').fill('30');
  await page.screenshot({ path: `${screens}/ticket-8-edit.png`, fullPage: true });
  await edit.getByRole('button', { name: 'Save' }).dblclick(); // a double click saves once
  await expect(edit).toHaveCount(0);
  await expect(uitjes.locator('.line-amount')).toHaveText(['€ 30,00', '€ 7,50']);
  await expect(uitjes.locator('.group-subtotal')).toHaveText('€ 37,50');
  await expect(total).toHaveText('€ 42,50');
  await expect(page.locator('.validation-message')).toHaveCount(0);

  // Delete: Delete? Yes / No, one line at a time; No keeps it, Yes removes it
  await second.getByRole('button', { name: 'Delete' }).click();
  await expect(second.locator('.confirm-text')).toHaveText('Delete?');
  await expect(second.getByRole('button', { name: 'Yes' })).toHaveCSS('background-color', 'rgb(180, 35, 24)');
  await page.screenshot({ path: `${screens}/ticket-8-delete.png`, fullPage: true });
  await second.getByRole('button', { name: 'No' }).click();
  await expect(uitjes.locator('.line')).toHaveCount(2);
  await expect(second.getByRole('button', { name: 'Edit' })).toBeVisible();
  await second.getByRole('button', { name: 'Delete' }).click();
  await first.getByRole('button', { name: 'Delete' }).click();
  await expect(page.locator('.confirm-text')).toHaveCount(1);
  await expect(first.locator('.confirm-text')).toHaveCount(1);
  await first.getByRole('button', { name: 'No' }).click();
  await second.getByRole('button', { name: 'Delete' }).click();
  await second.getByRole('button', { name: 'Yes' }).dblclick(); // a double click deletes once
  await expect(uitjes.locator('.line-amount')).toHaveText(['€ 30,00']);
  await expect(uitjes.locator('.group-subtotal')).toHaveText('€ 30,00');
  await expect(total).toHaveText('€ 35,00');
  await expect(page.locator('.validation-message')).toHaveCount(0);

  // Phone width: nothing scrolls sideways
  await page.setViewportSize({ width: 390, height: 844 });
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(overflow).toBeLessThanOrEqual(0);
  await page.screenshot({ path: `${screens}/ticket-8-phone.png`, fullPage: true });
  await page.setViewportSize({ width: 1280, height: 720 });

  // ‹ and › change the heading and the address; December › is January of the next year
  await page.getByRole('link', { name: 'Previous month' }).click();
  await expect(page).toHaveURL(/\/spending\/2026\/9$/);
  await expect(page.locator('.month-name')).toHaveText('September 2026');
  await expect(page.locator('.spending-lines')).toHaveText('No spending in September 2026');
  await page.getByRole('link', { name: 'Next month' }).click();
  await expect(page).toHaveURL(/\/spending\/2026\/10$/);
  await expect(page.locator('.month-name')).toHaveText('October 2026');
  await expect(total).toHaveText('€ 35,00');
  await page.goto('/spending/2026/12');
  await expect(add).toBeEnabled();
  await page.getByRole('link', { name: 'Next month' }).click();
  await expect(page).toHaveURL(/\/spending\/2027\/1$/);
  await expect(page.locator('.month-name')).toHaveText('January 2027');
  await page.getByRole('link', { name: 'Previous month' }).click();
  await expect(page).toHaveURL(/\/spending\/2026\/12$/);
  await expect(page.locator('.month-name')).toHaveText('December 2026');

  // /spending is this month, and the menu link goes back to it from another month
  const now = new Date();
  const thisMonth = `${now.toLocaleString('en-US', { month: 'long' })} ${now.getFullYear()}`;
  await page.locator('.sidebar .nav-link', { hasText: 'Spending' }).click();
  await expect(page).toHaveURL(/\/spending$/);
  await expect(page.locator('.month-name')).toHaveText(thisMonth);
  await expect(add).toBeEnabled();
  const before = now.getMonth() === 0 ? [now.getFullYear() - 1, 12] : [now.getFullYear(), now.getMonth()];
  await page.getByRole('link', { name: 'Previous month' }).click();
  await expect(page).toHaveURL(new RegExp(`/spending/${before[0]}/${before[1]}$`));

  // Categories: Uitjes has 1 line, € 30,00 and a disabled grey Delete; a rename shows on Spending
  await page.goto('/categories');
  await expect(addCategory).toBeEnabled();
  const uitjesRow = categoryRow(page, 'Uitjes');
  await expect(uitjesRow.locator('td')).toHaveText(['', 'Uitjes', '1', '€ 30,00', /^Rename\s+Delete$/]);
  const uitjesDelete = uitjesRow.getByRole('button', { name: 'Delete' });
  await expect(uitjesDelete).toBeDisabled();
  await expect(uitjesDelete).toHaveCSS('background-color', 'rgb(228, 227, 223)');
  await expect(categoryRow(page, 'Boodschappen').locator('td')).toHaveText(['', 'Boodschappen', '1', '€ 5,00', /^Rename\s+Delete$/]);
  await page.screenshot({ path: `${screens}/ticket-8-categories.png`, fullPage: true });
  await uitjesRow.getByRole('button', { name: 'Rename' }).click();
  await page.locator('.rename-input').fill('Uitgaan');
  await page.locator('.rename-input').press('Enter');
  await expect(categoryRow(page, 'Uitgaan')).toHaveCount(1);
  await page.goto(october);
  await expect(add).toBeEnabled();
  await expect(page.locator('.group-name')).toHaveText(['Boodschappen', 'Uitgaan']);
  await expect(category.locator('option')).toHaveText(['Boodschappen', 'Uitgaan']);
  await page.screenshot({ path: `${screens}/ticket-8-renamed.png`, fullPage: true });

  expect(problems).toEqual([]);
});

test('a month or year outside the range shows Page not found', async ({ page }) => {
  for (const path of ['/spending/2026/13', '/spending/2026/0', '/spending/1999/5', '/spending/2101/1']) {
    await page.goto(path);
    await expect(page.locator('main h1')).toHaveText('Page not found');
    await expect(page.locator('.month-switch')).toHaveCount(0);
  }
  await page.screenshot({ path: `${screens}/ticket-8-not-found.png`, fullPage: true });
});
