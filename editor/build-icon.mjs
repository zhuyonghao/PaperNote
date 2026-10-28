import { Resvg } from '@resvg/resvg-js';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
const svg = await readFile('../packaging/icon.svg', 'utf8');
await mkdir('../packaging/PaperNote.iconset', { recursive: true });
for (const size of [16,32,64,128,256,512,1024]) {
  const png = new Resvg(svg, { fitTo: { mode: 'width', value: size } }).render().asPng();
  if (size <= 512) await writeFile(`../packaging/PaperNote.iconset/icon_${size}x${size}.png`, png);
  if (size >= 32) await writeFile(`../packaging/PaperNote.iconset/icon_${size/2}x${size/2}@2x.png`, png);
  if (size === 256) {
    await writeFile('../packaging/icon.png', png);
    const header = Buffer.alloc(22); header.writeUInt16LE(1,2); header.writeUInt16LE(1,4); header.writeUInt16LE(1,10); header.writeUInt16LE(32,12); header.writeUInt32LE(png.length,14); header.writeUInt32LE(22,18);
    await writeFile('../packaging/icon.ico', Buffer.concat([header, png]));
  }
}
