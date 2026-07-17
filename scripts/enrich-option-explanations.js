/**
 * Backfill unique per-option explanations for question banks.
 * Uses embedded distractor notes when present; otherwise calls Anthropic.
 *
 * Usage: node scripts/enrich-option-explanations.js [bank...]
 *   bank = ankush | yagnesh | nilesh | all (default: all)
 */
const fs = require('fs');
const path = require('path');

const ROOT = path.join(__dirname, '..');
const DATA = path.join(ROOT, 'ClaudeCertPractice.Api', 'Data');
const ENV_PATH = path.join(ROOT, '.env');

const BANKS = {
  ankush: 'questions-ankush.json',
  yagnesh: 'questions-yagnesh.json',
  nilesh: 'questions-nilesh.json',
};

const OPTION_START =
  /(?:^Why\s+([A-D])\s*:\s*)|(?:(?:^|[.!?•]\s+|;\s+|\n\s*)(?:Option\s+)?([A-D])(?=\s*[:.]|\s+(?:only|is|may|means|forces|requires|penalises|penalizes|adds|meets|with|still|alone|would|can|does|fixes|treats|works|requests|keeps|applies|relies|leaves|causes|measures|flags|over-engineers|can't|cannot|doesn't|don't|won't|isn't|aren't|not\b)))|(?:\s([A-D])(?=\s+(?:only|is|may|means|meets|works|treats|requires|penalises|penalizes|adds|forces|still|alone|would|can|does|fixes|measures|flags|can't|cannot|doesn't|don't|won't|isn't|aren't|not\b)))/gi;

const BATCH_SIZE = 4;
const MODEL = process.env.ANTHROPIC_MODEL || 'claude-haiku-4-5';

function loadApiKey() {
  if (process.env.ANTHROPIC_API_KEY) return process.env.ANTHROPIC_API_KEY.trim();
  if (process.env.AnthropicApiKey) return process.env.AnthropicApiKey.trim();
  if (!fs.existsSync(ENV_PATH)) return null;
  const env = fs.readFileSync(ENV_PATH, 'utf8');
  for (const line of env.split(/\r?\n/)) {
    const m = line.match(/^\s*(?:ANTHROPIC_API_KEY|AnthropicApiKey)\s*=\s*(.+?)\s*$/);
    if (m) return m[1].replace(/^["']|["']$/g, '').trim();
  }
  return null;
}

function extractOptionNotes(explanation) {
  const notes = {};
  if (!explanation?.trim()) return notes;
  const starts = [];
  let match;
  const re = new RegExp(OPTION_START.source, OPTION_START.flags);
  while ((match = re.exec(explanation)) !== null) {
    const letter = (match[1] || match[2] || match[3] || '').toUpperCase();
    if (!/^[A-D]$/.test(letter)) continue;
    const last = starts[starts.length - 1];
    if (last && last.letter === letter && match.index - last.index < 10) continue;
    starts.push({ letter, index: match.index });
  }
  for (let i = 0; i < starts.length; i++) {
    const cur = starts[i];
    const end = i + 1 < starts.length ? starts[i + 1].index : explanation.length;
    let chunk = explanation.slice(cur.index, end).trim().replace(/^[.!?•;]\s+/, '');
    if (chunk && !notes[cur.letter]) notes[cur.letter] = chunk;
  }
  return notes;
}

function cleanBody(letter, raw) {
  let body = (raw || '').trim().replace(/^[.!?•;:\s]+/, '');
  body = body.replace(/^(Correct|Incorrect)\.\s*/i, '');
  body = body.replace(new RegExp(`^Why\\s+${letter}\\s*(?:is\\s+wrong)?\\s*:\\s*`, 'i'), '');
  body = body.replace(new RegExp(`^Option\\s+${letter}\\s*[:.]?\\s*`, 'i'), '');
  body = body.replace(new RegExp(`^${letter}\\s*[:.]\\s*`, 'i'), '');
  body = body.replace(/\s+/g, ' ').trim();
  if (!body) return '';
  return body.charAt(0).toUpperCase() + body.slice(1);
}

function missingIncorrectLetters(q, map) {
  return ['A', 'B', 'C', 'D'].filter(
    (l) => l !== String(q.correctAnswer || '').toUpperCase() && !map[l]?.trim(),
  );
}

function seedFromExplanation(q) {
  const map = { ...(q.optionExplanations || {}) };
  const notes = extractOptionNotes(q.explanation || '');
  const correct = String(q.correctAnswer || '').toUpperCase();
  for (const [letter, text] of Object.entries(notes)) {
    const cleaned = cleanBody(letter, text);
    if (!cleaned) continue;
    if (letter === correct) {
      if (!map[letter]) map[letter] = cleaned;
      continue;
    }
    if (!map[letter]) map[letter] = cleaned;
  }
  if (!map[correct] && q.explanation?.trim()) {
    map[correct] = cleanBody(correct, q.explanation);
  }
  return map;
}

async function callAnthropic(apiKey, prompt) {
  const res = await fetch('https://api.anthropic.com/v1/messages', {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
      'x-api-key': apiKey,
      'anthropic-version': '2023-06-01',
    },
    body: JSON.stringify({
      model: MODEL,
      max_tokens: 4096,
      messages: [{ role: 'user', content: prompt }],
    }),
  });
  const text = await res.text();
  if (!res.ok) {
    throw new Error(`Anthropic ${res.status}: ${text.slice(0, 400)}`);
  }
  const doc = JSON.parse(text);
  let out = doc.content?.[0]?.text ?? '[]';
  out = out.trim();
  if (out.startsWith('```')) {
    const start = out.indexOf('\n') + 1;
    const end = out.lastIndexOf('```');
    if (end > start) out = out.slice(start, end).trim();
  }
  return JSON.parse(out);
}

function buildBatchPrompt(items) {
  return `You write unique, meaningful quiz feedback for incorrect multiple-choice options.

For each question below, write a brief explanation (1-2 sentences) for EACH missing letter explaining specifically WHY that option is wrong in this scenario. Do NOT restate the correct answer's rationale for every option. Each wrong option must have a DISTINCT reason tied to that option's specific flaw.

Return ONLY a JSON array (no markdown) like:
[
  {
    "id": 1,
    "optionExplanations": {
      "A": "specific reason A is wrong...",
      "B": "specific reason B is wrong..."
    }
  }
]

Only include the missing letters listed for each question.

Questions:
${JSON.stringify(items, null, 2)}`;
}

async function enrichBank(fileName, apiKey) {
  const filePath = path.join(DATA, fileName);
  const bank = JSON.parse(fs.readFileSync(filePath, 'utf8'));
  let seeded = 0;
  let aiFilled = 0;

  const needAi = [];

  for (const q of bank.questions) {
    const map = seedFromExplanation(q);
    const missing = missingIncorrectLetters(q, map);
    q.optionExplanations = map;
    if (Object.keys(map).length) seeded++;
    if (missing.length) {
      needAi.push({
        id: q.id,
        missing,
        title: q.title,
        text: q.text,
        options: q.options,
        correctAnswer: q.correctAnswer,
        explanation: q.explanation,
      });
    }
  }

  for (let i = 0; i < needAi.length; i += BATCH_SIZE) {
    const batch = needAi.slice(i, i + BATCH_SIZE);
    const promptItems = batch.map((q) => ({
      id: q.id,
      missingLetters: q.missing,
      stem: q.text,
      options: Object.fromEntries(
        Object.entries(q.options || {}).map(([k, v]) => [k, String(v).slice(0, 220)]),
      ),
      correctAnswer: q.correctAnswer,
      correctExplanation: String(q.explanation || '').slice(0, 500),
    }));

    process.stdout.write(
      `  AI batch ${Math.floor(i / BATCH_SIZE) + 1}/${Math.ceil(needAi.length / BATCH_SIZE)} (${batch.map((b) => b.id).join(',')})... `,
    );

    let results;
    try {
      results = await callAnthropic(apiKey, buildBatchPrompt(promptItems));
    } catch (err) {
      console.log('FAILED');
      throw err;
    }

    if (!Array.isArray(results)) {
      console.log('BAD RESPONSE');
      throw new Error('Expected JSON array from model');
    }

    const byId = new Map(results.map((r) => [r.id, r]));
    for (const item of batch) {
      const got = byId.get(item.id);
      const ex = got?.optionExplanations || {};
      const q = bank.questions.find((x) => x.id === item.id);
      if (!q) continue;
      q.optionExplanations = q.optionExplanations || {};
      for (const letter of item.missing) {
        const raw = ex[letter] || ex[letter?.toLowerCase?.()] || '';
        const cleaned = cleanBody(letter, raw);
        if (cleaned) {
          q.optionExplanations[letter] = cleaned;
          aiFilled++;
        }
      }
    }
    console.log('ok');
  }

  // Normalize keys A-D only, drop empties
  for (const q of bank.questions) {
    const next = {};
    for (const letter of ['A', 'B', 'C', 'D']) {
      const v = q.optionExplanations?.[letter]?.trim();
      if (v) next[letter] = v;
    }
    q.optionExplanations = Object.keys(next).length ? next : undefined;
  }

  fs.writeFileSync(filePath, JSON.stringify(bank, null, 2) + '\n');
  return { total: bank.questions.length, seeded, aiFilled, needAi: needAi.length };
}

async function main() {
  const arg = (process.argv[2] || 'all').toLowerCase();
  const names =
    arg === 'all' ? Object.keys(BANKS) : arg.split(',').map((s) => s.trim()).filter(Boolean);

  const apiKey = loadApiKey();
  if (!apiKey) {
    console.error('Missing Anthropic API key (ANTHROPIC_API_KEY / AnthropicApiKey / .env).');
    process.exit(1);
  }

  for (const name of names) {
    const file = BANKS[name];
    if (!file) {
      console.error(`Unknown bank: ${name}`);
      process.exit(1);
    }
    console.log(`\nEnriching ${name} (${file})...`);
    const stats = await enrichBank(file, apiKey);
    console.log(
      `Done ${name}: ${stats.total} questions, ${stats.needAi} needed AI, wrote ${stats.aiFilled} option notes.`,
    );
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
