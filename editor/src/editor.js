import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { Markdown } from '@tiptap/markdown';
import TaskList from '@tiptap/extension-task-list';
import TaskItem from '@tiptap/extension-task-item';
import Placeholder from '@tiptap/extension-placeholder';
import { TableKit } from '@tiptap/extension-table';
import Image from '@tiptap/extension-image';

let readOnly = false, deleted = false, toolbarVisible = true;
let editor, noteId = '', token = 0, sourceMode = false, currentMarkdown = '', editable = true;
const editorEl = document.querySelector('#editor');
let source = document.querySelector('#source');
const send = message => {
  const payload = JSON.stringify(message);
  if (typeof window.invokeCSharpAction === 'function') window.invokeCSharpAction(payload);
  else window.dispatchEvent(new CustomEvent('paper-message', { detail: message }));
};
function applyReadingState() {
  editable = !readOnly && !deleted;
  document.body.classList.toggle('reading', !editable);
  document.querySelector('#toolbar').hidden = !toolbarVisible || !editable;
  source.readOnly = !editable;
  editor?.setEditable(editable && !sourceMode, false);
  for (const checkbox of document.querySelectorAll('.tiptap input[type="checkbox"]')) checkbox.disabled = !editable;
  if (!editable) document.querySelector('#link-panel').hidden = true;
  updateToolbar();
}
function flush() {
  if (!noteId) return;
  send({ type: 'change', id: noteId, token, markdown: currentMarkdown, count: [...(sourceMode ? source.value : editor.getText())].length });
}
function hasMarkdownFormatting(node) {
  return (node.type !== 'doc' && node.type !== 'paragraph' && node.type !== 'text' && node.type !== 'hardBreak')
    || !!node.marks?.length || !!node.content?.some(hasMarkdownFormatting);
}
function makeEditor(markdown) {
  editor?.destroy();
  editor = new Editor({
    element: editorEl,
    extensions: [StarterKit.configure({ underline: false, trailingNode: false, link: { openOnClick: false, autolink: false } }), Markdown,
      TaskList, TaskItem.configure({ nested: true }), TableKit, Image.configure({ allowBase64: true }),
      Placeholder.configure({ placeholder: ({ node }) => node.type.name === 'heading' ? '新的灵感' : '写下此刻的想法…' })],
    content: markdown, contentType: 'markdown', editable: editable && !sourceMode,
    editorProps: {
      attributes: { 'aria-label': '便签内容，Markdown 所见即所得编辑器', spellcheck: 'false' },
      handlePaste(view, event) {
        const text = event.clipboardData?.getData('text/plain');
        if (!text || editor.isActive('codeBlock') || editor.isActive('code')) return false;
        // Browsers and code editors often copy Markdown with an HTML flavor too.
        // Inspect the parsed text so all supported syntax is recognized; ordinary
        // rich text still follows ProseMirror's HTML clipboard handling.
        const document = editor.markdown.parse(text);
        if (!hasMarkdownFormatting(document)) return false;
        return editor.commands.insertContent(document);
      }
    },
    onUpdate: () => { if (!sourceMode) { currentMarkdown = editor.getMarkdown(); flush(); } updateToolbar(); },
    onSelectionUpdate: updateToolbar,
    onTransaction: updateToolbar
  });
}
const icons = {
  bold: '<path d="M6 4h6a4 4 0 010 8H6zm0 8h7a4 4 0 010 8H6z"/>',
  italic: '<path d="M10 4h9M5 20h9M15 4L9 20"/>',
  strike: '<path d="M17 5c-1-2-8-3-10 1-1 2 1 4 4 5M5 12h14M7 18c2 3 9 3 10-1"/>',
  bullet: '<path d="M9 6h12M9 12h12M9 18h12M3 6h.01M3 12h.01M3 18h.01"/>',
  task: '<rect x="3" y="4" width="16" height="16" rx="3"/><path d="m7 12 3 3 6-7"/>',
  quote: '<path d="M4 11h6v7H3v-7c0-4 2-6 6-6M15 11h6v7h-7v-7c0-4 2-6 6-6"/>',
  code: '<path d="m8 6-6 6 6 6m8-12 6 6-6 6m-3-15-2 18"/>',
  link: '<path d="m10 13 4-4m-6 7-1 1a4 4 0 01-6-6l5-5a4 4 0 016 0m0 12a4 4 0 006 0l5-5a4 4 0 00-6-6l-1 1" transform="translate(1 0) scale(.9)"/>',
  undo: '<path d="M3 9h11a6 6 0 010 12M3 9l5-5M3 9l5 5"/>',
  redo: '<path d="M21 9H10a6 6 0 000 12M21 9l-5-5m5 5-5 5"/>'
};
const tools = [
  ['heading', '标题', 'H₁', () => editor.chain().focus().toggleHeading({ level: 1 }).run(), () => editor.isActive('heading')],
  ['bold', '粗体 · ⌘/Ctrl B', null, () => editor.chain().focus().toggleBold().run(), () => editor.isActive('bold')],
  ['italic', '斜体 · ⌘/Ctrl I', null, () => editor.chain().focus().toggleItalic().run(), () => editor.isActive('italic')],
  ['strike', '删除线', null, () => editor.chain().focus().toggleStrike().run(), () => editor.isActive('strike')],
  ['separator'],
  ['bullet', '无序列表', null, () => editor.chain().focus().toggleBulletList().run(), () => editor.isActive('bulletList')],
  ['task', '待办清单', null, () => editor.chain().focus().toggleTaskList().run(), () => editor.isActive('taskList')],
  ['quote', '引用', null, () => editor.chain().focus().toggleBlockquote().run(), () => editor.isActive('blockquote')],
  ['code', '代码块', null, () => editor.chain().focus().toggleCodeBlock().run(), () => editor.isActive('codeBlock')],
  ['link', '插入链接', null, () => { document.querySelector('#link-panel').hidden = false; document.querySelector('#link-url').focus(); }, () => editor.isActive('link')],
  ['separator'],
  ['undo', '撤销 · ⌘/Ctrl Z', null, () => editor.chain().focus().undo().run()],
  ['redo', '重做 · ⌘/Ctrl Shift Z', null, () => editor.chain().focus().redo().run()]
];
const toolbar = document.querySelector('#toolbar');
function insertSource(text, selectionStart = 0, selectionEnd = selectionStart) {
  if (!editable) return;
  source.focus();
  const start = source.selectionStart;
  // Native insertText preserves the textarea's undo stack on WebKit and Chromium.
  document.execCommand('insertText', false, text);
  source.setSelectionRange(start + selectionStart, start + selectionEnd);
  captureSource();
}
function sourceCommand(key) {
  if (key === 'undo' || key === 'redo') { source.focus(); document.execCommand(key); return; }
  if (key === 'link') { document.querySelector('#link-panel').hidden = false; document.querySelector('#link-url').focus(); return; }
  const text = source.value.slice(source.selectionStart, source.selectionEnd);
  const wrappers = { bold: ['**', '**', '粗体'], italic: ['*', '*', '斜体'], strike: ['~~', '~~', '删除线'], code: ['```\n', '\n```', '代码'] };
  if (wrappers[key]) {
    const [before, after, placeholder] = wrappers[key], value = text || placeholder;
    insertSource(before + value + after, before.length, before.length + value.length);
    return;
  }
  const prefixes = { heading: '# ', bullet: '- ', task: '- [ ] ', quote: '> ' };
  const prefix = prefixes[key];
  if (prefix) {
    const start = source.value.lastIndexOf('\n', source.selectionStart - 1) + 1;
    const end = source.selectionEnd;
    const value = source.value.slice(start, end).split('\n').map(line => prefix + line).join('\n');
    source.setSelectionRange(start, end);
    insertSource(value, value.length);
  }
}
for (const [key, label, text, action] of tools) {
  if (key === 'separator') { const span = document.createElement('span'); span.className = 'separator'; toolbar.append(span); continue; }
  const button = document.createElement('button');
  button.dataset.command = key; button.title = label; button.setAttribute('aria-label', label);
  button.innerHTML = text || `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">${icons[key]}</svg>`;
  button.addEventListener('mousedown', e => e.preventDefault());
  button.addEventListener('click', () => { if (editable) sourceMode ? sourceCommand(key) : action(); });
  toolbar.append(button);
}
function updateToolbar() {
  if (!editor) return;
  for (const [key, , , , isActive] of tools) {
    const button = toolbar.querySelector(`[data-command="${key}"]`);
    if (!button) continue;
    button.disabled = !editable;
    button.classList.toggle('active', !sourceMode && !!isActive?.());
    button.setAttribute('aria-pressed', String(!sourceMode && !!isActive?.()));
  }
}
document.querySelector('#link-cancel').onclick = () => { document.querySelector('#link-panel').hidden = true; sourceMode ? source.focus() : editor.commands.focus(); };
document.querySelector('#link-apply').onclick = () => {
  const url = document.querySelector('#link-url').value.trim();
  if (!/^https?:\/\/\S+$/i.test(url)) { document.querySelector('#link-url').setCustomValidity('请输入 http 或 https 链接'); document.querySelector('#link-url').reportValidity(); return; }
  document.querySelector('#link-url').setCustomValidity('');
  if (!editable) return;
  if (sourceMode) {
    const label = source.value.slice(source.selectionStart, source.selectionEnd) || '链接文字';
    insertSource(`[${label}](${url})`, 1, 1 + label.length);
  }
  else if (editor.state.selection.empty) editor.chain().focus().insertContent({ type: 'text', text: url, marks: [{ type: 'link', attrs: { href: url } }] }).run();
  else editor.chain().focus().setLink({ href: url }).run();
  document.querySelector('#link-panel').hidden = true;
};
function captureSource() {
  // Textareas normalize line endings. Preserve the file's CRLF convention and
  // keep the original bytes' text untouched until the user actually edits.
  currentMarkdown = currentMarkdown.includes('\r\n') ? source.value.replace(/\n/g, '\r\n') : source.value;
  flush();
}
function handleSourceKeydown(e) {
  if (!editable || e.isComposing) return;
  if ((e.metaKey || e.ctrlKey) && ['b', 'i'].includes(e.key.toLowerCase())) {
    e.preventDefault(); sourceCommand(e.key.toLowerCase() === 'b' ? 'bold' : 'italic'); return;
  }
  if (e.key === 'Tab' && !e.shiftKey) {
    e.preventDefault(); insertSource('  ', 2); return;
  }
  if (e.key !== 'Enter' || e.shiftKey || e.metaKey || e.ctrlKey || e.altKey || source.selectionStart !== source.selectionEnd) return;
  const start = source.value.lastIndexOf('\n', source.selectionStart - 1) + 1;
  const line = source.value.slice(start, source.selectionStart);
  const match = /^(\s*)(?:([-*+] )((?:\[[ xX]\] )?)|(\d+)([.)] )|(> ))(.*)$/.exec(line);
  if (!match) return;
  e.preventDefault();
  if (!match[7].trim()) { source.setSelectionRange(start, source.selectionStart); insertSource('\n', 1); return; }
  const prefix = match[2] ? match[2] + (match[3] ? '[ ] ' : '') : match[4] ? `${Number(match[4]) + 1}${match[5]}` : '> ';
  const next = '\n' + match[1] + prefix;
  insertSource(next, next.length);
}
function resetSource() {
  // Replacing the control prevents a browser-native undo from restoring a
  // previous file, including when two files contain the same text.
  const fresh = source.cloneNode(false);
  source.replaceWith(fresh); source = fresh;
  source.addEventListener('input', captureSource);
  source.addEventListener('keydown', handleSourceKeydown);
}
resetSource();
function setMode(mode) {
  if (!['rich', 'source'].includes(mode) || !editor) return;
  if (sourceMode === (mode === 'source')) return;
  flush();
  const wasSource = sourceMode;
  sourceMode = mode === 'source';
  if (!wasSource) source.value = currentMarkdown;
  makeEditor(currentMarkdown);
  editorEl.hidden = mode === 'source'; source.hidden = mode === 'rich';
  document.body.classList.toggle('source-mode', sourceMode);
  for (const button of document.querySelectorAll('[data-mode]')) button.setAttribute('aria-pressed', String(button.dataset.mode === mode));
  document.querySelector('#link-panel').hidden = true;
  applyReadingState(); (sourceMode ? source : editor.view.dom).focus();
  send({ type: 'mode', source: sourceMode });
  send({ type: 'stats', id: noteId, token, count: [...(sourceMode ? source.value : editor.getText())].length });
}
for (const button of document.querySelectorAll('[data-mode]')) button.onclick = () => setMode(button.dataset.mode);
document.addEventListener('keydown', e => {
  if (e.key === 'Escape' && readOnly) { e.preventDefault(); send({ type: 'action', action: 'reading' }); return; }
  if (!(e.metaKey || e.ctrlKey)) return;
  let action;
  if (e.key.toLowerCase() === 'n') action = 'new';
  if (e.key.toLowerCase() === 'f') action = 'search';
  if (e.key.toLowerCase() === 's') action = 'save';
  if (e.shiftKey && e.key.toLowerCase() === 's') action = 'save-as';
  if (!e.shiftKey && e.key.toLowerCase() === 'o') action = 'open';
  if (e.key.toLowerCase() === 'q') action = e.metaKey ? 'close' : 'quit';
  if (e.key === ',') action = 'settings';
  if (e.key.toLowerCase() === 'w') action = 'close';
  if (e.shiftKey && e.key.toLowerCase() === 'f') action = 'format';
  if (e.shiftKey && e.key.toLowerCase() === 'o') action = 'library';
  if (e.shiftKey && e.key.toLowerCase() === 'l') action = 'collapse';
  if (e.shiftKey && e.key.toLowerCase() === 'r') action = 'reading';
  if (e.shiftKey && e.key.toLowerCase() === 'm') action = 'source';
  if (action) { e.preventDefault(); flush(); send({ type: 'action', action }); }
});
window.paper = {
  configure(options) {
    document.body.classList.toggle('compact', !!options.compact);
    toolbarVisible = !!options.toolbar;
    readOnly = !!options.readOnly;
    document.documentElement.dataset.theme = options.dark ? 'dark' : 'light';
    if (options.paper) document.documentElement.style.setProperty('--paper', options.paper);
    applyReadingState();
    document.documentElement.style.setProperty('--font-size', `${options.fontSize || 15}px`);
    if (options.mode) setMode(options.mode);
  },
  setNote(note) {
    resetSource();
    noteId = note.id; token = note.token; deleted = !!note.deleted; editable = !deleted && !readOnly;
    currentMarkdown = note.markdown;
    document.documentElement.style.setProperty('--paper', note.paper);
    source.value = currentMarkdown; source.readOnly = !editable;
    document.querySelector('#link-panel').hidden = true;
    makeEditor(currentMarkdown); applyReadingState();
    send({ type: 'stats', id: noteId, token, count: [...(sourceMode ? source.value : editor.getText())].length });
  },
  setColor(paper) { document.documentElement.style.setProperty('--paper', paper); },
  toggleSource() {
    setMode(sourceMode ? 'rich' : 'source');
  },
  setMode,
  flush, focus() { if (sourceMode) source.focus(); else editor.commands.focus(); },
  getMarkdown() { return currentMarkdown; }
};
send({ type: 'ready' });
