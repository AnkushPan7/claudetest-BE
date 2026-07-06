const fs = require('fs');
const path = require('path');

async function main() {
  const pdfParse = require('pdf-parse');
  const files = process.argv.slice(2);
  for (const file of files) {
    const buf = fs.readFileSync(file);
    const data = await pdfParse(buf);
    const out = path.join(__dirname, path.basename(file, path.extname(file)) + '.txt');
    fs.writeFileSync(out, data.text, 'utf8');
    console.log(`Wrote ${out} (${data.numpages} pages, ${data.text.length} chars)`);
  }
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
