import { chromium } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

// Run against either the current editor or a saved pre-change bundle.
const path = resolve(process.argv[2] || '../src/PaperNote/Editor/editor.html');
const browser = await chromium.launch(process.env.PAPERNOTE_EDGE ? { channel: 'msedge' } : {});
try {
  const page = await browser.newPage();
  await page.setContent(await readFile(path, 'utf8'));
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Performance.enable');
  const heap = async () => {
    await cdp.send('HeapProfiler.collectGarbage');
    const { metrics } = await cdp.send('Performance.getMetrics');
    const { nodes } = await cdp.send('Memory.getDOMCounters');
    return { heapBytes: metrics.find(m => m.name === 'JSHeapUsedSize').value, nodes };
  };
  const markdown = '# 性能测试\n\n' + '- [ ] 一段正文 **bold** 和 `code` 📝\n'.repeat(2000);
  await page.evaluate(markdown => {
    window.paper.setNote({ id: 'benchmark', token: 1, markdown, paper: '#fff6cf' });
    window.changeCount = 0; window.changeBytes = 0;
    window.addEventListener('paper-message', ({ detail }) => {
      if (detail.type === 'change') {
        window.changeCount++;
        window.changeBytes += new TextEncoder().encode(JSON.stringify(detail)).length;
      }
    });
  }, markdown);
  const rich = await heap();
  await page.evaluate(() => window.paper.setMode('source'));
  const source = await heap();
  await page.evaluate(() => window.paper.setMode('rich'));
  const burstMs = await page.evaluate(async () => {
    window.changeCount = 0; window.changeBytes = 0;
    const el = document.querySelector('.tiptap');
    el.focus();
    const selection = window.getSelection();
    selection.selectAllChildren(el); selection.collapseToEnd();
    const start = performance.now();
    for (let i = 0; i < 50; i++) {
      document.execCommand('insertText', false, 'x');
      // Let ProseMirror observe each individual input, as with real key events.
      await new Promise(resolve => setTimeout(resolve, 0));
    }
    return performance.now() - start;
  });
  await page.waitForFunction(() => window.changeCount > 0);
  // Include both the debounce and the maximum-latency timer.
  await new Promise(resolve => setTimeout(resolve, 500));
  const changes = await page.evaluate(() => ({ messages: window.changeCount, bytes: window.changeBytes,
    correct: window.paper.getMarkdown().endsWith('x'.repeat(50)) }));
  if (!changes.correct) throw new Error('The input burst did not preserve its last edit');
  console.log(JSON.stringify({ path, rich, source, burstMs, changes }, null, 2));
} finally { await browser.close(); }
