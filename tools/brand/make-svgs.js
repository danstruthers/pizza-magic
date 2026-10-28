// Writes the Pizza Magic! SVG mark and logo (same geometry as make-icons.js). Usage: node make-svgs.js <outDir>
const fs = require('fs');
const path = require('path');
const outDir = process.argv[2];

const r2 = (n) => Math.round(n * 100) / 100;
function star(cx, cy, outer, inner) {
  const pts = [];
  for (let i = 0; i < 8; i++) {
    const a = -Math.PI / 2 + (i * Math.PI) / 4;
    const r = i % 2 === 0 ? outer : inner;
    pts.push(`${r2(cx + r * Math.cos(a))},${r2(cy + r * Math.sin(a))}`);
  }
  return pts.join(' ');
}

const mark = `
    <circle cx="29" cy="35" r="27" fill="#B5651D"/>
    <circle cx="29" cy="35" r="25" fill="#E8A23B"/>
    <circle cx="29" cy="35" r="20.5" fill="#FFD15C"/>
    <g fill="#C0392B">
      <circle cx="20" cy="29" r="5"/>
      <circle cx="35" cy="26" r="5"/>
      <circle cx="27" cy="44" r="5"/>
      <circle cx="40" cy="39" r="4.5"/>
    </g>
    <polygon points="${star(52, 12, 12, 3.2)}" fill="#7B3FBF"/>
    <polygon points="${star(59, 28, 5.5, 1.6)}" fill="#7B3FBF"/>
    <polygon points="${star(43, 4.5, 4, 1.2)}" fill="#A66BE0"/>`;

const markSvg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="64" height="64" role="img" aria-label="Pizza Magic!">
  <title>Pizza Magic!</title>${mark}
</svg>
`;

const logoSvg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 280 64" width="280" height="64" role="img" aria-label="Pizza Magic!">
  <title>Pizza Magic!</title>
  <g>${mark}
  </g>
  <text x="72" y="44" font-family="'Trebuchet MS', 'Segoe UI', Arial, sans-serif" font-size="34" font-weight="700" letter-spacing="0.5">
    <tspan fill="#C0392B">Pizza</tspan>
    <tspan fill="#7B3FBF">Magic!</tspan>
  </text>
</svg>
`;

fs.writeFileSync(path.join(outDir, 'logo-mark.svg'), markSvg);
fs.writeFileSync(path.join(outDir, 'logo.svg'), logoSvg);
fs.writeFileSync(path.join(outDir, 'favicon.svg'), markSvg);
console.log('ok');
