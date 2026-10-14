import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, type TestInfo } from '@playwright/test';

// Scan the rendered page, including open disclosures and active training controls.
// No rules or elements are excluded. Manual keyboard and assistive checks remain separate.
export async function auditAccessibility(page: Page, info: TestInfo, state: string) {
  const scan = await new AxeBuilder({ page }).analyze();
  await info.attach(`axe-${state}`, { body: JSON.stringify(scan, null, 2), contentType: 'application/json' });
  expect.soft(scan.violations.map(({ id, impact, nodes }) => ({ id, impact, nodes: nodes.map(n => ({ target: n.target, summary: n.failureSummary })) })), state).toEqual([]);
  expect.soft(scan.incomplete.filter(rule => rule.id === 'aria-prohibited-attr').map(rule => rule.nodes.map(node => node.target)), `${state}: unsupported labels`).toEqual([]);
  const width = await page.evaluate(() => ({ content: document.documentElement.scrollWidth, viewport: document.documentElement.clientWidth }));
  if (width.content > width.viewport + 1) {
    const boxes = await page.evaluate(() => [...document.querySelectorAll('body *')].map(element => {
      const bounds = element.getBoundingClientRect();
      return { tag: element.tagName, class: element.className.toString(), role: element.getAttribute('role'), text: element.textContent?.slice(0, 60), left: bounds.left, right: bounds.right, width: bounds.width };
    }).filter(bounds => bounds.right > document.documentElement.clientWidth + 1));
    await info.attach(`layout-${state}`, { body: JSON.stringify(boxes, null, 2), contentType: 'application/json' });
  }
  expect.soft(width.content, `${state}: document width`).toBeLessThanOrEqual(width.viewport + 1);
}

// Axe leaves single-character board coordinates for manual review. Check their
// rendered foreground/background numerically as well as retaining the full axe report.
export async function checkCoordinateContrast(page: Page) {
  const colours = await page.locator('.board-wrap [data-square] > span > span').evaluateAll(elements => elements.map(element => ({
    foreground: getComputedStyle(element).color,
    background: getComputedStyle(element.closest('[data-square]')!).backgroundColor,
  })));
  expect(colours.length).toBeGreaterThanOrEqual(16);
  const luminance = (colour: string) => {
    const channels = colour.match(/[\d.]+/g)!.slice(0, 3).map(Number).map(channel => channel / 255).map(channel => channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4);
    return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
  };
  for (const { foreground, background } of colours) {
    const values = [luminance(foreground), luminance(background)].sort((a, b) => a - b);
    expect((values[1] + 0.05) / (values[0] + 0.05)).toBeGreaterThanOrEqual(4.5);
  }
}
