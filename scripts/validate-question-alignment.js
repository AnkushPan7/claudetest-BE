/**
 * Validate that each question's stem/options, correctAnswer, and explanations
 * refer to the same topic (catches off-by-one OCR merges).
 *
 * Usage: node scripts/validate-question-alignment.js [bank...]
 *   bank = ankush | yagnesh | nilesh | all (default: ankush)
 * Exit code 1 if mismatches found.
 */
const fs = require('fs');
const path = require('path');

const DATA = path.join(__dirname, '..', 'ClaudeCertPractice.Api', 'Data');
const BANKS = {
  ankush: 'questions-ankush.json',
  yagnesh: 'questions-yagnesh.json',
  nilesh: 'questions-nilesh.json',
};

const TOPIC_DEFS = [
  {
    id: 'send_notification',
    keys: ['send_notification', 'retry_safe', 'duplicate notifications'],
  },
  { id: 'search_flights', keys: ['search_flights', 'airline api', '503'] },
  {
    id: 'reimbursement',
    keys: ['process_reimbursement', 'reimbursement', 'approved_by_manager'],
  },
  {
    id: 'log_workout',
    keys: ['log_workout', 'exercise_type', 'cardio', 'measurement combinations', 'exercise type'],
  },
  {
    id: 'scheduling',
    keys: [
      'get_available_slots',
      'hold_slot',
      'book_appointment',
      'find_and_book',
      'slot no longer',
    ],
  },
  {
    id: 'track_shipment',
    keys: ['track_shipment', 'tracking_id', 'tracking number'],
  },
  {
    id: 'doc_extract',
    keys: ['document extraction', 'requires_review', 'confidence scores', 'extraction_quality'],
  },
];

function topics(s) {
  const lower = String(s || '').toLowerCase();
  return TOPIC_DEFS.filter((t) => t.keys.some((k) => lower.includes(k))).map((t) => t.id);
}

function validateBank(name, fileName) {
  const filePath = path.join(DATA, fileName);
  const bank = JSON.parse(fs.readFileSync(filePath, 'utf8'));
  const issues = [];

  for (const q of bank.questions || []) {
    const titleTopics = topics(q.title);
    const textTopics = topics(q.text);
    const explTopics = topics(
      `${q.explanation || ''} ${Object.values(q.optionExplanations || {}).join(' ')}`,
    );
    const why = (q.explanation || '').match(/Why\s+([A-D])/i);
    const whyLetter = why ? why[1].toUpperCase() : null;
    const correct = String(q.correctAnswer || '').toUpperCase();
    const local = [];

    if (titleTopics[0] && textTopics[0] && titleTopics[0] !== textTopics[0]) {
      local.push(`title topic (${titleTopics[0]}) != text topic (${textTopics[0]})`);
    }
    if (textTopics[0] && explTopics[0] && textTopics[0] !== explTopics[0]) {
      local.push(`text topic (${textTopics[0]}) != explanation topic (${explTopics[0]})`);
    }
    if (titleTopics[0] && explTopics[0] && titleTopics[0] !== explTopics[0]) {
      local.push(`title topic (${titleTopics[0]}) != explanation topic (${explTopics[0]})`);
    }
    if (whyLetter && correct && whyLetter !== correct) {
      local.push(`explanation says Why ${whyLetter} but correctAnswer is ${correct}`);
    }
    if (correct && q.optionExplanations && !q.optionExplanations[correct]?.trim()) {
      local.push(`missing optionExplanations.${correct}`);
    }

    if (local.length) {
      issues.push({ bank: name, id: q.id, title: q.title, problems: local });
    }
  }

  return issues;
}

function main() {
  const args = process.argv.slice(2);
  const names =
    !args.length || args.includes('all')
      ? Object.keys(BANKS)
      : args.filter((a) => BANKS[a]);

  if (!names.length) {
    console.error('No valid banks. Use: ankush | yagnesh | nilesh | all');
    process.exit(2);
  }

  // Default CLI with no args → ankush only (most common regression).
  const targets = args.length === 0 ? ['ankush'] : names;
  const allIssues = targets.flatMap((n) => validateBank(n, BANKS[n]));

  if (!allIssues.length) {
    console.log(`OK: ${targets.join(', ')} — no title/text/explanation mismatches detected.`);
    return;
  }

  console.error(`Found ${allIssues.length} misaligned question(s):\n`);
  for (const issue of allIssues) {
    console.error(`[${issue.bank}] Q${issue.id} — ${issue.title}`);
    for (const p of issue.problems) console.error(`  - ${p}`);
  }
  process.exit(1);
}

main();
