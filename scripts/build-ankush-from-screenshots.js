/**
 * Rebuild Ankush question bank stems/options from OCR'd exam screenshots.
 * Uses existing bank options as anchors so selected (marker-less) choices still split correctly.
 * Keeps correctAnswer, explanations, titles, sectionId.
 *
 * Run: node build-ankush-from-screenshots.js
 */
const fs = require('fs');
const path = require('path');

const OCR_DIR = path.join(__dirname, 'exam-extract-ocr');
const BANK_PATH = path.join(__dirname, '..', 'ClaudeCertPractice.Api', 'Data', 'questions-ankush.json');
const REPORT_PATH = path.join(__dirname, 'ankush-screenshot-build-report.json');

const SKIP_LINE = /^(flag|previous|next|review)$/i;
const Q_HEADER = /^Question\s+(\d+)\s+of\s+60$/i;
const SCENARIO = /^Scenario:/i;
const LETTERS = ['A', 'B', 'C', 'D'];

function normalize(s) {
  return String(s || '')
    .toLowerCase()
    .replace(/[""]/g, '"')
    .replace(/['']/g, "'")
    .replace(/—/g, '-')
    .replace(/[^a-z0-9\s]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

function tokens(s) {
  return normalize(s).split(' ').filter((t) => t.length > 2);
}

function jaccard(a, b) {
  const A = new Set(tokens(a));
  const B = new Set(tokens(b));
  if (!A.size || !B.size) return 0;
  let inter = 0;
  for (const t of A) if (B.has(t)) inter++;
  return inter / (A.size + B.size - inter);
}

function cleanText(s) {
  return String(s || '')
    .replace(/\r/g, '')
    .replace(/[ \t]+/g, ' ')
    .replace(/\s+([?.!,;:])/g, '$1')
    .replace(/\s+'/g, "'")
    .replace(/'\s+/g, "'")
    .replace(/\bl have\b/gi, 'I have')
    .replace(/\bl want\b/gi, 'I want')
    .replace(/\bI ve\b/g, "I've")
    .replace(/\bdon t\b/gi, "don't")
    .replace(/\bisn t\b/gi, "isn't")
    .replace(/\bcan t\b/gi, "can't")
    .replace(/\bwont\b/gi, "won't")
    .replace(/\s{2,}/g, ' ')
    .trim();
}

function readOcrBody(filePath) {
  const raw = fs.readFileSync(filePath, 'utf8').replace(/^\uFEFF/, '');
  const lines = raw
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter(Boolean);

  let qNum = null;
  const kept = [];
  for (const line of lines) {
    const hm = line.match(Q_HEADER);
    if (hm) {
      qNum = Number(hm[1]);
      continue;
    }
    if (SKIP_LINE.test(line) || SCENARIO.test(line)) continue;
    if (/^O$/i.test(line) || /^o$/i.test(line)) continue;
    if (/^[A-D]$/i.test(line)) continue;
    kept.push(line);
  }
  return { qNum, text: cleanText(kept.join(' ')), file: path.basename(filePath) };
}

/** Find best starting index of `needle` tokens inside `hay` tokens. */
function findTokenSpan(hayTokens, needleTokens) {
  if (!needleTokens.length || needleTokens.length > hayTokens.length) return null;
  const minKeep = Math.max(3, Math.ceil(needleTokens.length * 0.45));
  // Use first N distinctive tokens as search key
  const keyLen = Math.min(6, needleTokens.length);
  const key = needleTokens.slice(0, keyLen);
  let best = null;

  for (let i = 0; i <= hayTokens.length - keyLen; i++) {
    let matched = 0;
    for (let k = 0; k < keyLen; k++) {
      if (hayTokens[i + k] === key[k]) matched++;
    }
    if (matched < Math.ceil(keyLen * 0.6)) continue;

    // Expand forward to cover needle length
    const end = Math.min(hayTokens.length, i + needleTokens.length + 4);
    const window = hayTokens.slice(i, end);
    let inter = 0;
    const windowSet = new Set(window);
    for (const t of needleTokens) if (windowSet.has(t)) inter++;
    if (inter < minKeep) continue;
    const score = inter / needleTokens.length;
    if (!best || score > best.score || (score === best.score && i < best.start)) {
      best = { start: i, end: Math.min(hayTokens.length, i + Math.max(needleTokens.length, keyLen)), score };
    }
  }
  return best;
}

function extractFromOcr(ocrText, bankQuestion) {
  const hay = tokens(ocrText);
  if (hay.length < 10) return null;

  const spans = [];
  for (const L of LETTERS) {
    const needle = tokens(bankQuestion.options[L]);
    const span = findTokenSpan(hay, needle);
    if (!span || span.score < 0.4) {
      return { ok: false, reason: `option ${L} not found`, score: span?.score };
    }
    spans.push({ L, ...span });
  }

  spans.sort((a, b) => a.start - b.start);
  // Must appear in A-B-C-D order mostly
  const order = spans.map((s) => s.L).join('');
  if (order !== 'ABCD') {
    // Allow if unique letters and increasing positions already sorted
    const unique = new Set(spans.map((s) => s.L));
    if (unique.size !== 4) return { ok: false, reason: `bad order ${order}` };
  }

  // Prevent overlapping starts
  for (let i = 1; i < spans.length; i++) {
    if (spans[i].start <= spans[i - 1].start) {
      return { ok: false, reason: 'overlapping option spans' };
    }
  }

  // Refine ends: each option ends where next starts
  for (let i = 0; i < spans.length; i++) {
    const nextStart = i + 1 < spans.length ? spans[i + 1].start : hay.length;
    spans[i].end = nextStart;
  }

  const stemTokens = hay.slice(0, spans[0].start);
  const stem = cleanText(stemTokens.join(' '));
  if (stem.length < 30) return { ok: false, reason: 'stem too short', stem };

  const options = {};
  for (const s of spans) {
    options[s.L] = cleanText(hay.slice(s.start, s.end).join(' '));
  }

  const stemScore = jaccard(stem, bankQuestion.text);
  return { ok: true, stem, options, stemScore, order };
}

function assignFilesToQuestions(bodies, bank) {
  // Prefer explicit Q numbers; otherwise match by stem similarity to bank questions in order.
  const files = bodies.slice().sort((a, b) => a.file.localeCompare(b.file));
  const assigned = new Map(); // id -> body
  const usedFiles = new Set();

  // Pass 1: explicit numbers
  for (const body of files) {
    if (body.qNum == null) continue;
    if (assigned.has(body.qNum)) {
      // keep longer OCR
      if (body.text.length > assigned.get(body.qNum).text.length) assigned.set(body.qNum, body);
      usedFiles.add(body.file);
      continue;
    }
    assigned.set(body.qNum, body);
    usedFiles.add(body.file);
  }

  // Pass 2: remaining files → remaining questions by best jaccard to bank stem
  const remainingQs = bank.questions.filter((q) => !assigned.has(q.id));
  const remainingFiles = files.filter((f) => !usedFiles.has(f.file));

  for (const body of remainingFiles) {
    let best = null;
    for (const q of remainingQs) {
      if (assigned.has(q.id)) continue;
      const score = jaccard(body.text, q.text);
      // also compare against expanded first 20 tokens of OCR vs bank
      if (!best || score > best.score) best = { q, score };
    }
    if (best && best.score >= 0.2) {
      assigned.set(best.q.id, body);
    }
  }

  // Pass 3: chronological fill for any still missing
  const unused = files.filter((f) => ![...assigned.values()].includes(f));
  for (const q of bank.questions) {
    if (assigned.has(q.id)) continue;
    const next = unused.shift();
    if (next) assigned.set(q.id, next);
  }

  return assigned;
}

function polishOption(ocrOpt, bankOpt) {
  const o = cleanText(ocrOpt);
  const b = cleanText(bankOpt);
  if (!o) return b;
  // OCR token join loses punctuation — restore useful punctuation from bank when nearly same
  if (jaccard(o, b) >= 0.75) {
    // Prefer OCR if longer (more complete wording), else bank
    if (o.length >= b.length * 0.95) {
      // Fix common OCR artifacts while keeping longer wording
      return o
        .replace(/\bprocess _ refund\b/gi, 'process_refund')
        .replace(/\bget customer\b/gi, 'get_customer')
        .replace(/\blookup order\b/gi, 'lookup_order')
        .replace(/\bextract metadata\b/gi, 'extract_metadata');
    }
    return b;
  }
  if (o.length > b.length && jaccard(o, b) >= 0.4) return o;
  return b;
}

function polishStem(ocrStem, bankStem) {
  const o = cleanText(ocrStem);
  const b = cleanText(bankStem);
  if (!o || o.length < 40) return b;
  if (jaccard(o, b) < 0.25 && b.length > 60) return b;
  // Prefer full OCR stem when it expands the bank
  if (o.length >= b.length * 0.9) {
    return o
      .replace(/\bprocess _ refund\b/gi, 'process_refund')
      .replace(/\bget customer\b/gi, 'get_customer')
      .replace(/\bl\b(?= have| want| ve)/gi, 'I');
  }
  return b;
}

function main() {
  const bank = JSON.parse(fs.readFileSync(BANK_PATH, 'utf8'));
  const bodies = fs
    .readdirSync(OCR_DIR)
    .filter((f) => f.endsWith('.txt'))
    .map((f) => readOcrBody(path.join(OCR_DIR, f)));

  const assigned = assignFilesToQuestions(bodies, bank);
  const report = {
    updatedText: [],
    updatedOptions: [],
    failedExtract: [],
    fileMap: {},
  };

  for (const q of bank.questions) {
    const body = assigned.get(q.id);
    if (!body) {
      report.failedExtract.push({ id: q.id, reason: 'no file' });
      continue;
    }
    report.fileMap[q.id] = body.file;

    const extracted = extractFromOcr(body.text, q);
    if (!extracted?.ok) {
      // Fallback: if OCR starts with a much longer stem-like prefix before first bank option words
      report.failedExtract.push({ id: q.id, reason: extracted?.reason || 'extract failed', file: body.file });
      // Still try to update stem if OCR text begins with expansion of bank stem
      const approx = cleanText(body.text);
      const bankOptStarts = LETTERS.map((L) => tokens(q.options[L])[0]).filter(Boolean);
      let cut = approx.length;
      for (const L of LETTERS) {
        const firstWords = tokens(q.options[L]).slice(0, 5).join(' ');
        const idx = normalize(approx).indexOf(normalize(firstWords).slice(0, 40));
        if (idx > 20 && idx < cut) cut = idx;
      }
      if (cut < approx.length && cut > 40) {
        // cut is on normalized string — approximate using token approach
        const hay = tokens(approx);
        let earliest = hay.length;
        for (const L of LETTERS) {
          const span = findTokenSpan(hay, tokens(q.options[L]));
          if (span && span.start < earliest) earliest = span.start;
        }
        if (earliest > 5 && earliest < hay.length) {
          const stem = cleanText(hay.slice(0, earliest).join(' '));
          const polished = polishStem(stem, q.text);
          if (polished !== q.text) {
            q.text = polished;
            report.updatedText.push(q.id);
          }
        }
      }
      continue;
    }

    const newStem = polishStem(extracted.stem, q.text);
    if (newStem !== q.text) {
      q.text = newStem;
      report.updatedText.push(q.id);
    }

    let optChanged = false;
    for (const L of LETTERS) {
      const next = polishOption(extracted.options[L], q.options[L]);
      if (next !== q.options[L]) {
        q.options[L] = next;
        optChanged = true;
      }
    }
    if (optChanged) report.updatedOptions.push(q.id);
  }

  fs.writeFileSync(BANK_PATH, JSON.stringify(bank, null, 2) + '\n');
  fs.writeFileSync(REPORT_PATH, JSON.stringify(report, null, 2) + '\n');

  console.log(`Files assigned: ${assigned.size}`);
  console.log(`Updated text: ${report.updatedText.length} → ${report.updatedText.join(',')}`);
  console.log(`Updated options: ${report.updatedOptions.length} → ${report.updatedOptions.join(',')}`);
  console.log(`Failed extract: ${report.failedExtract.length}`);
  if (report.failedExtract.length) {
    console.log(report.failedExtract.slice(0, 15));
  }
}

main();
