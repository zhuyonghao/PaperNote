import { test, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';
const html = await readFile(new URL('../../src/PaperNote/Editor/editor.html', import.meta.url), 'utf8');
const seed = '# 我的便签\n\n**粗体** 和 *斜体*\n\n- [ ] 喝水\n- [x] 散步\n\n> 留下想法\n\n```js\nconst idea = 1;\n```';
async function load(page, markdown = seed, id = 'note-a', token = 1) {
  await page.evaluate(data => window.paper.setNote(data), { id, token, markdown, paper: '#fff6cf', deleted: false });
}
async function paste(page, text, html = '') {
  await page.evaluate(({ text, html }) => {
    const clipboardData = new DataTransfer();
    clipboardData.setData('text/plain', text);
    if (html) clipboardData.setData('text/html', html);
    document.activeElement.dispatchEvent(new ClipboardEvent('paste', { clipboardData, bubbles: true, cancelable: true }));
  }, { text, html });
}
test.beforeEach(async ({ page }) => {
  await page.route('https://**', route => route.abort());
  await page.setContent(html);
  await page.evaluate(() => { window.messages = []; window.addEventListener('paper-message', e => window.messages.push(e.detail)); });
  await load(page);
});
test('Markdown renders as editable rich text with task states', async ({ page }) => {
  await expect(page.locator('.tiptap h1')).toHaveText('我的便签');
  await expect(page.locator('.tiptap strong')).toHaveText('粗体');
  await expect(page.locator('.tiptap em')).toHaveText('斜体');
  await expect(page.locator('.tiptap blockquote')).toContainText('留下想法');
  await expect(page.locator('.tiptap pre')).toContainText('const idea = 1;');
  await expect(page.locator('input[type=checkbox]').nth(0)).not.toBeChecked();
  await expect(page.locator('input[type=checkbox]').nth(1)).toBeChecked();
});
test('checking a task emits Markdown with correct note identity', async ({ page }) => {
  await page.locator('input[type=checkbox]').first().check();
  const message = await page.evaluate(() => window.messages.filter(m => m.type === 'change').at(-1));
  expect(message.id).toBe('note-a'); expect(message.token).toBe(1);
  expect(message.markdown).toContain('- [x] 喝水');
  expect(message.markdown).toContain('**粗体**');
});

test('character statistics preserve Unicode code points in rich and source views', async ({ page }) => {
  const text = '中文📝👨‍👩‍👧‍👦e\u0301';
  await load(page, text);
  expect(await page.evaluate(() => window.messages.filter(m => m.type === 'stats').at(-1).count)).toBe([...text].length);
  await page.evaluate(() => window.paper.setMode('source'));
  await page.locator('#source').fill(text + '🙂');
  expect(await page.evaluate(() => window.messages.filter(m => m.type === 'change').at(-1).count)).toBe([...text + '🙂'].length);
});

test('toolbar follows selection changes and recovers after being hidden', async ({ page }) => {
  await load(page, '**bold**\n\nplain');
  const bold = page.getByRole('button', { name: '粗体 · ⌘/Ctrl B', exact: true });
  await page.locator('.tiptap strong').selectText();
  await expect(bold).toHaveAttribute('aria-pressed', 'true');
  await page.locator('.tiptap p').nth(1).selectText();
  await expect(bold).toHaveAttribute('aria-pressed', 'false');
  await page.evaluate(() => window.paper.configure({ compact: true, toolbar: false }));
  await page.locator('.tiptap strong').selectText();
  await page.evaluate(() => window.paper.configure({ compact: true, toolbar: true }));
  await expect(bold).toHaveAttribute('aria-pressed', 'true');
  expect(await page.evaluate(() => window.paper.getMarkdown())).toBe('**bold**\n\nplain');
});
test('source edits round trip and preserve exact text before rich edits', async ({ page }) => {
  const markdown = '# 新标题\n\n你好，世界 📝\n\n1. 一\n2. 二\n\n| A | B |\n| --- | --- |\n| 1 | 2 |';
  await page.evaluate(() => window.paper.toggleSource());
  await page.locator('#source').fill(markdown);
  expect(await page.evaluate(() => window.paper.getMarkdown())).toBe(markdown);
  await page.evaluate(() => window.paper.toggleSource());
  await expect(page.locator('.tiptap h1')).toHaveText('新标题');
  await expect(page.locator('.tiptap table')).toBeVisible();
  expect(await page.evaluate(() => window.paper.getMarkdown())).toBe(markdown);
});
test('typing Markdown heading shortcut creates a heading', async ({ page }) => {
  await load(page, ''); await page.locator('.tiptap').click();
  await page.keyboard.type('## '); await page.keyboard.insertText('新的灵感');
  await expect(page.locator('.tiptap h2')).toHaveText('新的灵感');
  expect(await page.evaluate(() => window.paper.getMarkdown())).toContain('## 新的灵感');
});
test('Markdown copied with an HTML clipboard flavor renders and emits the parsed document', async ({ page }) => {
  await load(page, '');
  await page.locator('.tiptap').click();
  await paste(page, seed, `<pre>${seed}</pre>`);
  await expect(page.locator('.tiptap h1')).toHaveText('我的便签');
  await expect(page.locator('.tiptap strong')).toHaveText('粗体');
  await expect(page.locator('.tiptap blockquote')).toContainText('留下想法');
  await expect(page.locator('input[type=checkbox]').nth(1)).toBeChecked();
  await expect(page.locator('.tiptap pre')).toContainText('const idea = 1;');
  const message = await page.evaluate(() => window.messages.filter(m => m.type === 'change').at(-1));
  expect(message.id).toBe('note-a'); expect(message.token).toBe(1);
  expect(message.markdown).toContain('# 我的便签');
  await page.evaluate(() => window.paper.setMode('source'));
  await expect(page.locator('#source')).toHaveValue(message.markdown);
  await page.evaluate(() => window.paper.setMode('rich'));
  await expect(page.locator('.tiptap h1')).toHaveText('我的便签');
});
for (const [name, markdown, selector, content] of [
  ['ordered lists', '1. 第一项\n2. 第二项', 'ol > li', ['第一项', '第二项']],
  ['tables', '| 项目 | 状态 |\n| --- | --- |\n| 粘贴 | 正常 |', 'table th', ['项目', '状态']],
  ['inline marks', '*斜体*、~~删除~~、`代码`、[链接](https://example.com)', 'em, s, code, a', ['斜体', '删除', '代码', '链接']],
  ['setext headings', '粘贴标题\n===', 'h1', '粘贴标题'],
  ['plus lists', '+ 第一项\n+ 第二项', 'ul > li', ['第一项', '第二项']],
  ['tilde code fences', '~~~js\nconst pasted = true;\n~~~', 'pre code', 'const pasted = true;'],
  ['images', '![图片](https://example.com/image.png)', 'img', null],
  ['horizontal rules', '---', 'hr', null],
]) {
  test(`pasting standalone Markdown parses ${name}`, async ({ page }) => {
    await load(page, '');
    await page.locator('.tiptap').click();
    await paste(page, markdown);
    if (content) await expect(page.locator('.tiptap').locator(selector)).toHaveText(content);
    else await expect(page.locator(`.tiptap ${selector}`)).toHaveCount(1);
  });
}
test('Markdown paste replaces the selection and undoes in one step', async ({ page }) => {
  await load(page, '原始内容');
  await page.locator('.tiptap').click();
  await page.keyboard.press('ControlOrMeta+a');
  await paste(page, '**替换内容**');
  await expect(page.locator('.tiptap strong')).toHaveText('替换内容');
  await page.keyboard.press('ControlOrMeta+z');
  await expect(page.locator('.tiptap')).toHaveText('原始内容');
});
test('rich HTML without Markdown syntax retains its formatting', async ({ page }) => {
  await load(page, '');
  await page.locator('.tiptap').click();
  await paste(page, '富文本', '<p><strong>富文本</strong></p>');
  await expect(page.locator('.tiptap strong')).toHaveText('富文本');
});
test('pasting Markdown inside a code block keeps literal syntax', async ({ page }) => {
  await load(page, '```text\nexisting\n```');
  await page.locator('.tiptap pre').click();
  await page.keyboard.press('End');
  await paste(page, '# literal\n\n**not bold**');
  await expect(page.locator('.tiptap pre')).toContainText('# literal');
  await expect(page.locator('.tiptap pre')).toContainText('**not bold**');
  await expect(page.locator('.tiptap h1, .tiptap strong')).toHaveCount(0);
});
test('switching notes cannot leak undo history across notes', async ({ page }) => {
  await page.locator('.tiptap').click(); await page.keyboard.press('End'); await page.keyboard.insertText('FIRST NOTE SECRET');
  await load(page, '# 第二张便签', 'note-b', 2);
  await page.getByRole('button', { name: '撤销 · ⌘/Ctrl Z', exact: true }).click();
  await expect(page.locator('.tiptap')).not.toContainText('FIRST NOTE SECRET');
  expect(await page.evaluate(() => window.paper.getMarkdown())).toBe('# 第二张便签');
});
test('deleted notes are read only in rich and source modes', async ({ page }) => {
  await page.evaluate(() => window.paper.setNote({ id: 'trash', token: 3, markdown: '# 已删除', paper: '#fff6cf', deleted: true }));
  await expect(page.locator('.tiptap')).toHaveAttribute('contenteditable', 'false');
  await page.evaluate(() => window.paper.toggleSource());
  await expect(page.locator('#source')).toHaveAttribute('readonly', '');
});
test('bold toolbar transforms selected text and undo works', async ({ page }) => {
  await load(page, 'hello');
  await page.locator('.tiptap p').selectText();
  await expect.poll(() => page.evaluate(() => window.getSelection()?.toString())).toBe('hello');
  await page.getByRole('button', { name: '粗体 · ⌘/Ctrl B', exact: true }).click();
  await expect(page.locator('.tiptap strong')).toHaveText('hello');
  expect(await page.evaluate(() => window.paper.getMarkdown())).toBe('**hello**');
  await page.getByRole('button', { name: '撤销 · ⌘/Ctrl Z', exact: true }).click();
  expect(await page.evaluate(() => window.paper.getMarkdown())).toBe('hello');
});
test('imported HTML cannot execute and Markdown images preserve their URL', async ({ page }) => {
  await load(page, '# safe\n\n<script>window.evil=true</script>\n\n![image](https://example.com/a.png)');
  expect(await page.evaluate(() => window.evil)).toBeUndefined();
  await expect(page.locator('.tiptap script')).toHaveCount(0);
  await expect(page.locator('.tiptap img')).toHaveAttribute('src', 'https://example.com/a.png');
});

test('task checkboxes sit on the same row as their text', async ({ page }) => {
  const checkbox = await page.locator('input[type=checkbox]').first().boundingBox();
  const text = await page.locator('ul[data-type="taskList"] > li > div').first().boundingBox();
  expect(Math.abs(checkbox.y - text.y)).toBeLessThan(12);
});
test('switching modes refreshes the visible character count', async ({ page }) => {
  await load(page, '**hello**');
  await page.evaluate(() => { window.messages = []; window.paper.toggleSource(); window.paper.toggleSource(); });
  const stats = await page.evaluate(() => window.messages.filter(m => m.type === 'stats').at(-1));
  expect(stats.count).toBe(5);
});

test('documents open in one directly editable WYSIWYG surface', async ({ page }) => {
  await expect(page.locator('.tiptap')).toHaveAttribute('contenteditable', 'true');
  await expect(page.locator('#source')).toBeHidden();
  await expect(page.getByRole('button', { name: '所见即所得', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('button', { name: '对照预览', exact: true })).toHaveCount(0);
  await page.locator('.tiptap h1').click();
  await page.keyboard.press('End'); await page.keyboard.insertText('，直接修改');
  await expect(page.locator('.tiptap h1')).toContainText('直接修改');
  expect(await page.evaluate(() => window.paper.getMarkdown())).toContain('直接修改');
});

test('clicking the current mode preserves WYSIWYG undo history', async ({ page }) => {
  await load(page, 'hello');
  await page.locator('.tiptap').click(); await page.keyboard.press('ControlOrMeta+a');
  await page.getByRole('button', { name: '粗体 · ⌘/Ctrl B', exact: true }).click();
  await page.getByRole('button', { name: '所见即所得', exact: true }).click();
  await page.getByRole('button', { name: '撤销 · ⌘/Ctrl Z', exact: true }).click();
  expect(await page.evaluate(() => window.paper.getMarkdown())).toBe('hello');
});

test('mode changes and flush preserve CRLF until editing and keep CRLF on input', async ({ page }) => {
  const markdown = '# 标题\r\n\r\n原始文本  \r\n';
  await load(page, markdown);
  for (const mode of ['source', 'rich', 'source']) {
    await page.evaluate(mode => { window.paper.setMode(mode); window.paper.flush(); }, mode);
    expect(await page.evaluate(() => window.paper.getMarkdown())).toBe(markdown);
  }
  await page.locator('#source').fill('# 新标题\n\n新文本\n');
  expect(await page.evaluate(() => window.paper.getMarkdown())).toBe('# 新标题\r\n\r\n新文本\r\n');
});

test('source toolbar wraps selection and native undo restores it', async ({ page }) => {
  await load(page, 'hello');
  await page.getByRole('button', { name: '源码', exact: true }).click();
  await page.locator('#source').selectText();
  await page.getByRole('button', { name: '粗体 · ⌘/Ctrl B', exact: true }).click();
  await expect(page.locator('#source')).toHaveValue('**hello**');
  expect(await page.evaluate(() => window.paper.getMarkdown())).toBe('**hello**');
  await page.getByRole('button', { name: '撤销 · ⌘/Ctrl Z', exact: true }).click();
  await expect(page.locator('#source')).toHaveValue('hello');
  expect(await page.evaluate(() => window.paper.getMarkdown())).toBe('hello');
});

test('source shortcuts insert Markdown and links preserve the selected label', async ({ page }) => {
  await load(page, 'hello');
  await page.evaluate(() => window.paper.setMode('source'));
  await page.locator('#source').selectText();
  await page.keyboard.press('ControlOrMeta+b');
  await expect(page.locator('#source')).toHaveValue('**hello**');
  await page.getByRole('button', { name: '插入链接', exact: true }).click();
  await page.getByRole('textbox', { name: '链接地址' }).fill('https://example.com');
  await page.locator('#link-apply').click();
  await expect(page.locator('#source')).toHaveValue('**[hello](https://example.com)**');
  await page.getByRole('button', { name: '所见即所得', exact: true }).click();
  await expect(page.locator('.tiptap a')).toHaveText('hello');
});

for (const [line, expected] of [['- [x] 完成', '- [x] 完成\n- [ ] '], ['9. 九', '9. 九\n10. '], ['> 引用', '> 引用\n> '], ['- ', '\n']]) {
  test(`source Enter continues or exits the Markdown prefix: ${line}`, async ({ page }) => {
    await load(page, line);
    await page.evaluate(() => window.paper.setMode('source'));
    await page.locator('#source').focus();
    await page.evaluate(() => { const source = document.querySelector('#source'); source.setSelectionRange(source.value.length, source.value.length); });
    await page.keyboard.press('Enter');
    await expect(page.locator('#source')).toHaveValue(expected);
    expect(await page.evaluate(() => window.paper.getMarkdown())).toBe(expected);
  });
}

test('switching files clears source undo history', async ({ page }) => {
  await page.evaluate(() => window.paper.setMode('source'));
  await page.locator('#source').fill('# previous secret');
  await load(page, '# 新文件', 'new-file', 9);
  await page.locator('#source').focus();
  await page.keyboard.press('ControlOrMeta+z');
  await expect(page.locator('#source')).toHaveValue('# 新文件');
  await expect(page.locator('.tiptap h1')).toHaveText('新文件');
  expect(await page.evaluate(() => window.paper.getMarkdown())).toBe('# 新文件');
});

test('file shortcuts forward open and Save As actions to the host', async ({ page }) => {
  await page.keyboard.press('ControlOrMeta+o');
  await page.keyboard.press('ControlOrMeta+Shift+s');
  expect(await page.evaluate(() => window.messages.filter(m => m.type === 'action').map(m => m.action))).toEqual(['open', 'save-as']);
});

for (const key of ['q', 'w']) for (const mode of ['rich', 'source']) {
  test(`Command+${key.toUpperCase()} flushes the ${mode} editor before requesting current window closure`, async ({ page }) => {
    await page.evaluate(mode => { window.paper.setMode(mode); window.messages = []; window.paper.focus(); }, mode);
    await page.keyboard.press(`Meta+${key}`);
    const messages = await page.evaluate(() => window.messages);
    expect(messages.at(-1)).toEqual({ type: 'action', action: 'close' });
    expect(messages.at(-2)).toMatchObject({ type: 'change', id: 'note-a', token: 1, markdown: seed });
    expect(await page.evaluate(() => window.paper.getMarkdown())).toBe(seed);
  });
}

test('Control+Q still forwards the Windows quit shortcut', async ({ page }) => {
  await page.locator('.tiptap').click();
  await page.keyboard.press('Control+q');
  expect(await page.evaluate(() => window.messages.filter(m => m.type === 'action').map(m => m.action))).toEqual(['quit']);
});

test('Control+W still forwards the Windows close-window shortcut', async ({ page }) => {
  await page.locator('.tiptap').click();
  await page.keyboard.press('Control+w');
  expect(await page.evaluate(() => window.messages.filter(m => m.type === 'action').map(m => m.action))).toEqual(['close']);
});

test('WYSIWYG editor fills the document area on narrow windows', async ({ page }) => {
  await page.setViewportSize({ width: 580, height: 600 });
  const bounds = await page.locator('#editor').boundingBox();
  expect(bounds.width).toBe(580);
  expect(bounds.height).toBeGreaterThan(400);
  await expect(page.locator('#source')).toBeHidden();
});

test('compact sticky editor keeps mode controls hidden and the source usable', async ({ page }) => {
  await page.evaluate(() => { window.paper.configure({ compact: true, toolbar: false, fontSize: 15 }); window.paper.toggleSource(); });
  await expect(page.locator('#view-modes')).toBeHidden();
  await expect(page.locator('#toolbar')).toBeHidden();
  const bounds = await page.locator('#source').boundingBox();
  expect(bounds.height).toBeGreaterThan(200);
});

test('immersive reading hides editing tools and blocks changes in rich and source views', async ({ page }) => {
  await page.evaluate(() => window.paper.configure({ compact: false, toolbar: true, readOnly: true }));
  await expect(page.locator('#editor-tools')).toBeHidden();
  await expect(page.locator('.tiptap')).toHaveAttribute('contenteditable', 'false');
  await expect(page.locator('input[type=checkbox]').first()).toBeDisabled();
  await page.locator('.tiptap').click();
  await page.keyboard.insertText('不能写入');
  await page.keyboard.press('ControlOrMeta+z');
  expect(await page.evaluate(() => window.paper.getMarkdown())).toBe(seed);
  await page.evaluate(() => window.paper.setMode('source'));
  await expect(page.locator('#source')).toHaveAttribute('readonly', '');
  await page.keyboard.press('Escape');
  expect(await page.evaluate(() => window.messages.filter(m => m.type === 'action').at(-1))).toEqual({ type: 'action', action: 'reading' });
  await page.evaluate(() => { window.paper.configure({ compact: false, toolbar: true, readOnly: false }); window.paper.setMode('rich'); });
  await expect(page.locator('#editor-tools')).toBeVisible();
  await expect(page.locator('.tiptap')).toHaveAttribute('contenteditable', 'true');
  await expect(page.locator('input[type=checkbox]').first()).toBeEnabled();
  expect(await page.evaluate(() => window.paper.getMarkdown())).toBe(seed);
});

test('theme changes preserve source edits, selection and undo history', async ({ page }) => {
  await page.evaluate(() => window.paper.setMode('source'));
  await page.locator('#source').fill('# 夜间内容');
  await page.evaluate(() => window.paper.configure({ compact: false, toolbar: true, dark: true, paper: '#272e26', canvas: '#191a21' }));
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(25, 26, 33)');
  await expect(page.locator('#source')).toHaveValue('# 夜间内容');
  await page.evaluate(() => window.paper.configure({ compact: true, toolbar: false, dark: true, paper: '#363325' }));
  await expect(page.locator('html')).toHaveCSS('background-color', 'rgb(54, 51, 37)');
  expect(await page.evaluate(() => window.paper.getMarkdown())).toBe('# 夜间内容');
  await page.evaluate(() => window.paper.configure({ compact: false, toolbar: true, dark: false, canvas: '#f8f8fa' }));
  await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(248, 248, 250)');
  expect(await page.evaluate(() => window.paper.getMarkdown())).toBe('# 夜间内容');
});

 test('reading and theme toggles preserve exact Markdown and rich undo history', async ({ page }) => {
  const exact = '# 标题\r\n\r\n| A | B |\r\n| --- | --- |\r\n| 1 | 2 |\r\n';
  await load(page, exact);
  for (const readOnly of [true, false, true, false]) {
    await page.evaluate(readOnly => window.paper.configure({ toolbar: true, readOnly, dark: readOnly }), readOnly);
    expect(await page.evaluate(() => window.paper.getMarkdown())).toBe(exact);
  }
  await load(page, 'hello');
  await page.locator('.tiptap').click(); await page.keyboard.press('ControlOrMeta+a');
  await page.getByRole('button', { name: '粗体 · ⌘/Ctrl B', exact: true }).click();
  await page.evaluate(() => { window.paper.configure({ toolbar: true, readOnly: true }); window.paper.configure({ toolbar: true, readOnly: false }); });
  await page.getByRole('button', { name: '撤销 · ⌘/Ctrl Z', exact: true }).click();
  expect(await page.evaluate(() => window.paper.getMarkdown())).toBe('hello');
});
