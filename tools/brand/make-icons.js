// Rasterizes the Pizza Magic! mark (same geometry as favicon.svg, viewBox 0 0 64 64) into PNGs and a multi-size ICO.
// Usage: node make-icons.js <outDir>
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const outDir = process.argv[2];

const hex = (h) => [parseInt(h.slice(1, 3), 16), parseInt(h.slice(3, 5), 16), parseInt(h.slice(5, 7), 16)];

function starPolygon(cx, cy, outer, inner) {
  const pts = [];
  for (let i = 0; i < 8; i++) {
    const a = -Math.PI / 2 + (i * Math.PI) / 4;
    const r = i % 2 === 0 ? outer : inner;
    pts.push([cx + r * Math.cos(a), cy + r * Math.sin(a)]);
  }
  return pts;
}

function inPolygon(x, y, pts) {
  let inside = false;
  for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
    const [xi, yi] = pts[i];
    const [xj, yj] = pts[j];
    if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) inside = !inside;
  }
  return inside;
}

const circle = (cx, cy, r, color) => ({ color: hex(color), hit: (x, y) => (x - cx) ** 2 + (y - cy) ** 2 <= r * r });
const star = (cx, cy, outer, inner, color) => {
  const pts = starPolygon(cx, cy, outer, inner);
  return { color: hex(color), hit: (x, y) => inPolygon(x, y, pts) };
};

// Painter's order: later shapes on top. Keep in sync with favicon.svg / logo-mark.svg.
const shapes = [
  circle(29, 35, 27, '#B5651D'),   // crust edge
  circle(29, 35, 25, '#E8A23B'),   // crust
  circle(29, 35, 20.5, '#FFD15C'), // cheese
  circle(20, 29, 5, '#C0392B'),    // pepperoni
  circle(35, 26, 5, '#C0392B'),
  circle(27, 44, 5, '#C0392B'),
  circle(40, 39, 4.5, '#C0392B'),
  star(52, 12, 12, 3.2, '#7B3FBF'), // magic sparkle
  star(59, 28, 5.5, 1.6, '#7B3FBF'),
  star(43, 4.5, 4, 1.2, '#A66BE0'),
];

function render(size, samples = 4) {
  const px = Buffer.alloc(size * size * 4);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      let r = 0, g = 0, b = 0, a = 0;
      for (let sy = 0; sy < samples; sy++) {
        for (let sx = 0; sx < samples; sx++) {
          const ux = ((x + (sx + 0.5) / samples) / size) * 64;
          const uy = ((y + (sy + 0.5) / samples) / size) * 64;
          let hitColor = null;
          for (const s of shapes) if (s.hit(ux, uy)) hitColor = s.color;
          if (hitColor) { r += hitColor[0]; g += hitColor[1]; b += hitColor[2]; a += 1; }
        }
      }
      const n = samples * samples;
      const i = (y * size + x) * 4;
      px[i] = a ? Math.round(r / a) : 0;
      px[i + 1] = a ? Math.round(g / a) : 0;
      px[i + 2] = a ? Math.round(b / a) : 0;
      px[i + 3] = Math.round((a / n) * 255);
    }
  }
  return px;
}

const crcTable = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});
const crc32 = (buf) => {
  let c = 0xffffffff;
  for (const byte of buf) c = crcTable[(c ^ byte) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
};
function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(body));
  return Buffer.concat([len, body, crc]);
}
function png(size) {
  const px = render(size);
  const raw = Buffer.alloc((size * 4 + 1) * size);
  for (let y = 0; y < size; y++) {
    raw[y * (size * 4 + 1)] = 0;
    px.copy(raw, y * (size * 4 + 1) + 1, y * size * 4, (y + 1) * size * 4);
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0); ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0)),
  ]);
}
function ico(sizes) {
  const images = sizes.map(png);
  const header = Buffer.alloc(6); header.writeUInt16LE(0, 0); header.writeUInt16LE(1, 2); header.writeUInt16LE(sizes.length, 4);
  let offset = 6 + 16 * sizes.length;
  const entries = sizes.map((s, i) => {
    const e = Buffer.alloc(16);
    e[0] = s >= 256 ? 0 : s; e[1] = s >= 256 ? 0 : s; e[2] = 0; e[3] = 0;
    e.writeUInt16LE(1, 4); e.writeUInt16LE(32, 6); e.writeUInt32LE(images[i].length, 8); e.writeUInt32LE(offset, 12);
    offset += images[i].length;
    return e;
  });
  return Buffer.concat([header, ...entries, ...images]);
}

fs.writeFileSync(path.join(outDir, 'favicon.ico'), ico([16, 32, 48]));
fs.writeFileSync(path.join(outDir, 'apple-touch-icon.png'), png(180));
fs.writeFileSync(path.join(outDir, 'preview-256.png'), png(256));
fs.writeFileSync(path.join(outDir, 'preview-32.png'), png(32));
console.log('ok');
