/**
 * Repair Ankush Q55–Q59 where screenshot OCR stems/options were applied
 * one slot ahead of titles, correctAnswers, and explanations.
 *
 * Source of truth for answers/explanations: bank metadata (answer key).
 * Source of truth for full stems/options: the OCR'd content currently sitting
 * one id ahead (and Q54 for the Q55 duplicate).
 *
 * Run: node scripts/repair-ankush-q55-59-alignment.js
 */
const fs = require('fs');
const path = require('path');

const BANK_PATH = path.join(
  __dirname,
  '..',
  'ClaudeCertPractice.Api',
  'Data',
  'questions-ankush.json',
);

function cloneStem(q) {
  return {
    text: q.text,
    options: { ...q.options },
  };
}

function main() {
  const bank = JSON.parse(fs.readFileSync(BANK_PATH, 'utf8'));
  const byId = new Map(bank.questions.map((q) => [q.id, q]));

  const q54 = byId.get(54);
  const q55 = byId.get(55);
  const q56 = byId.get(56);
  const q57 = byId.get(57);
  const q58 = byId.get(58);
  const q59 = byId.get(59);

  if (!q54 || !q55 || !q56 || !q57 || !q58 || !q59) {
    throw new Error('Expected questions 54–59 in ankush bank');
  }

  // Snapshot the misaligned stems before overwrite.
  const stemSearchFlights = cloneStem(q55); // currently on 55, belongs on 56
  const stemReimbursement = cloneStem(q56); // currently on 56, belongs on 57
  const stemWorkout = cloneStem(q57); // currently on 57, belongs on 58
  const stemScheduling = cloneStem(q58); // currently on 58, belongs on 59
  const stemSendNotif = cloneStem(q54); // full OCR duplicate for Q55

  // Sanity: scheduling stem must mention race-condition tools.
  if (!/get_available_slots|book_appointment|hold_slot|find_and_book/i.test(stemScheduling.text)) {
    throw new Error('Expected scheduling stem on Q58 before repair; aborting');
  }
  if (!/search_flights|503/i.test(stemSearchFlights.text)) {
    throw new Error('Expected search_flights stem on Q55 before repair; aborting');
  }

  Object.assign(q55, stemSendNotif);
  Object.assign(q56, stemSearchFlights);
  Object.assign(q57, stemReimbursement);
  Object.assign(q58, stemWorkout);
  Object.assign(q59, stemScheduling);

  // Ensure top-level explanation letter matches correctAnswer.
  for (const q of [q55, q56, q57, q58, q59]) {
    const letter = String(q.correctAnswer || '').toUpperCase();
    const body = (q.optionExplanations && q.optionExplanations[letter]) || q.explanation || '';
    const cleaned = String(body)
      .replace(/^(Correct|Incorrect)\.\s*/i, '')
      .replace(new RegExp(`^Why\\s+${letter}\\s*:\\s*`, 'i'), '')
      .trim();
    if (cleaned) {
      q.explanation = `Why ${letter}: ${cleaned}`;
    }
  }

  fs.writeFileSync(BANK_PATH, JSON.stringify(bank, null, 2) + '\n');
  console.log('Repaired Ankush Q55–Q59 stem/option alignment with titles & explanations.');
  for (const id of [55, 56, 57, 58, 59]) {
    const q = byId.get(id);
    console.log(
      `Q${id} ans=${q.correctAnswer} | ${(q.title || '').slice(0, 50)} | text: ${(q.text || '').slice(0, 70)}`,
    );
  }
}

main();
