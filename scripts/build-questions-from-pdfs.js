/**
 * Build separate question banks from extracted PDF text:
 *   - Ankush  ← ClaudeCertificateExam-ANSWERS
 *   - Yagnesh ← Claude_Architect_Exam_Study_Guide
 *
 * Run: node extract-pdf-text.js <study.pdf> <answers.pdf>
 *      node build-questions-from-pdfs.js
 */
const fs = require('fs');
const path = require('path');

const STUDY_GUIDE_TXT = path.join(__dirname, 'Claude_Architect_Exam_Study_Guide_260531_232603 (2).txt');
const STUDY_GUIDE_TXT_FALLBACK = path.join(__dirname, 'Claude_Architect_Exam_Study_Guide_260531_232603 (1).txt');
const ANSWERS_TXT = path.join(__dirname, 'ClaudeCertificateExam-ANSWERS (2).txt');
const ANSWERS_TXT_FALLBACK = path.join(__dirname, 'ClaudeCertificateExam-ANSWERS (1).txt');

const OUTPUT_ANKUSH = path.join(__dirname, '..', 'ClaudeCertPractice.Api', 'Data', 'questions-ankush.json');
const OUTPUT_YAGNESH = path.join(__dirname, '..', 'ClaudeCertPractice.Api', 'Data', 'questions-yagnesh.json');

const SECTIONS = [
  { id: 1, name: 'Agentic Architecture & Orchestration', range: 'Domain 1' },
  { id: 2, name: 'Tool Design & MCP Integration', range: 'Domain 2' },
  { id: 3, name: 'Claude Code Configuration & Workflows', range: 'Domain 3' },
  { id: 4, name: 'Prompt Engineering & Structured Output', range: 'Domain 4' },
  { id: 5, name: 'Context Management & Reliability', range: 'Domain 5' },
];

/** Official CCA-F domain id (1–5) per Study Guide question number. */
const STUDY_GUIDE_DOMAIN = {
  1: 3, 2: 3, 3: 3,
  4: 4, 5: 4, 6: 4, 7: 4, 8: 4, 9: 4, 10: 4, 11: 4, 12: 4, 13: 4, 14: 4, 15: 4,
  16: 3, 17: 3, 18: 3, 19: 3, 20: 3, 21: 3, 22: 3, 23: 3, 24: 3, 25: 3, 26: 4, 27: 3, 28: 3, 29: 3, 30: 3,
  31: 1, 32: 1, 33: 2, 34: 1, 35: 2, 36: 2, 37: 5, 38: 1, 39: 1, 40: 5, 41: 1, 42: 1, 43: 1, 44: 1, 45: 1,
  46: 3, 47: 3, 48: 2, 49: 3, 50: 2, 51: 3, 52: 3, 53: 3, 54: 3, 55: 3, 56: 3, 57: 3, 58: 3, 59: 3, 60: 3,
};

/** Domain per scenario-exam question (Answers PDF). Section 3 split: MCP/tool → 2, agentic → 1. */
const SCENARIO_EXAM_DOMAIN = {
  ...Object.fromEntries(Array.from({ length: 15 }, (_, i) => [i + 1, 5])),
  ...Object.fromEntries(Array.from({ length: 15 }, (_, i) => [i + 16, 4])),
  31: 1, 32: 1, 33: 2, 34: 1, 35: 5, 36: 1, 37: 1, 38: 1, 39: 2, 40: 1,
  41: 1, 42: 5, 43: 5, 44: 2, 45: 1, 46: 2, 47: 2, 48: 2, 49: 2, 50: 2,
  51: 2, 52: 2, 53: 2, 54: 2, 55: 2, 56: 2, 57: 2, 58: 2, 59: 2, 60: 2,
};

function resolveInput(preferred, fallback) {
  if (fs.existsSync(preferred)) return preferred;
  if (fs.existsSync(fallback)) return fallback;
  return null;
}

function cleanText(s) {
  return s
    .replace(/\r/g, '')
    .replace(/[ \t]+\n/g, '\n')
    .replace(/\n{3,}/g, '\n\n')
    .replace(/[ \t]{2,}/g, ' ')
    .replace(/\s+([?.!,;:])/g, '$1')
    .trim();
}

function normalizeQuotes(s) {
  return s.replace(/['']/g, "'").replace(/[""]/g, '"');
}

function parseStudyGuide(text) {
  const lines = text.split('\n');
  const questions = [];
  let current = null;
  let phase = null; // 'question' | 'option' | 'explanation'
  let currentOption = null;

  function flushQuestion() {
    if (!current) return;
    if (currentOption) {
      current.options[currentOption] = cleanText(current.options[currentOption]);
      currentOption = null;
    }
    if (current.correctAnswer && Object.values(current.options).every(Boolean)) {
      questions.push(current);
    } else {
      console.warn(
        `Study Q${current.id}: incomplete (answer=${current.correctAnswer}, opts=${Object.keys(current.options).filter((k) => current.options[k]).join('')})`,
      );
    }
    current = null;
    phase = null;
  }

  for (const rawLine of lines) {
    const line = rawLine.trimEnd();
    const trimmed = line.trim();

    const header = trimmed.match(/^Q(\d+) of 60 · (.+)$/);
    if (header) {
      flushQuestion();
      const num = Number(header[1]);
      current = {
        id: num,
        sectionId: STUDY_GUIDE_DOMAIN[num],
        source: 'study-guide',
        title: header[2].trim(),
        text: '',
        options: { A: '', B: '', C: '', D: '' },
        correctAnswer: '',
        explanation: '',
      };
      phase = null;
      continue;
    }

    if (!current) continue;

    if (/^Question \d+:\s*$/.test(trimmed)) {
      phase = 'question';
      continue;
    }

    const answerLine = trimmed.match(/^(?:[n3]\s*)?CORRECT ANSWER:\s*([A-D])\s*$/i);
    if (answerLine) {
      if (currentOption) {
        current.options[currentOption] = cleanText(current.options[currentOption]);
        currentOption = null;
      }
      current.correctAnswer = answerLine[1].toUpperCase();
      phase = 'explanation';
      continue;
    }

    const optLine = trimmed.match(/^(?:[n3]\s+)?([A-D])\.\s+(.*)$/);
    if (optLine) {
      if (currentOption) {
        current.options[currentOption] = cleanText(current.options[currentOption]);
      }
      currentOption = optLine[1];
      current.options[currentOption] = optLine[2];
      phase = 'option';
      continue;
    }

    if (!trimmed) continue;

    if (phase === 'question') {
      current.text += (current.text ? ' ' : '') + trimmed;
    } else if (phase === 'option' && currentOption) {
      current.options[currentOption] += ' ' + trimmed;
    } else if (phase === 'explanation') {
      current.explanation += (current.explanation ? ' ' : '') + trimmed;
    }
  }

  flushQuestion();
  return questions
    .map((q) => ({
      ...q,
      text: cleanText(q.text),
      options: {
        A: normalizeQuotes(cleanText(q.options.A)),
        B: normalizeQuotes(cleanText(q.options.B)),
        C: normalizeQuotes(cleanText(q.options.C)),
        D: normalizeQuotes(cleanText(q.options.D)),
      },
      explanation: q.explanation
        ? `Why ${q.correctAnswer}: ${cleanText(q.explanation)}`
        : '',
    }))
    .sort((a, b) => a.id - b.id);
}

function parseAnswersPdf(text) {
  const blocks = text.split(/\n(?=Q\d+ — )/);
  const questions = [];

  for (const block of blocks) {
    const header = block.match(/^Q(\d+) — (.+?)(?:\n|$)/);
    if (!header) continue;

    const num = Number(header[1]);
    const title = header[2].trim();

    const body = block.slice(header[0].length).trim();
    const whyMatch = body.match(/\nWhy ([A-D]):\s*([\s\S]*?)(?=\nQ\d+ — |\n {2}Section |\n {2}Summary Answer Key|$)/i);
    const beforeWhy = whyMatch ? body.slice(0, whyMatch.index) : body;
    const correctLetter = whyMatch ? whyMatch[1].toUpperCase() : null;
    const explanationTail = whyMatch ? whyMatch[2].trim() : '';

    const lines = beforeWhy.split('\n');
    const questionLines = [];
    const optionLines = { A: [], B: [], C: [], D: [] };
    let currentOpt = null;

    for (const line of lines) {
      const optStart = line.match(/^([A-D]):\s*(.*)$/);
      if (optStart) {
        currentOpt = optStart[1];
        optionLines[currentOpt].push(optStart[2].replace(/\s*[n37]\s*(\(was[^)]*\))?\s*$/i, '').trim());
        continue;
      }
      if (currentOpt) {
        optionLines[currentOpt].push(line.trim());
      } else if (line.trim()) {
        questionLines.push(line.trim());
      }
    }

    let detectedCorrect = correctLetter;
    if (!detectedCorrect) {
      for (const letter of ['A', 'B', 'C', 'D']) {
        const joined = optionLines[letter].join(' ');
        if (/\s[n]\s*(\(was|$)/i.test(joined) || joined.endsWith(' n')) {
          detectedCorrect = letter;
        }
      }
    }

    const options = {};
    for (const letter of ['A', 'B', 'C', 'D']) {
      options[letter] = normalizeQuotes(
        cleanText(optionLines[letter].join(' ').replace(/\s*[n37]\s*(\(was[^)]*\))?\s*$/i, '')),
      );
    }

    if (!detectedCorrect && num === 55) {
      detectedCorrect = 'D';
    }

    if (!detectedCorrect) {
      console.warn(`Scenario Q${num}: could not detect correct answer`);
      continue;
    }

    questions.push({
      id: num,
      sectionId: SCENARIO_EXAM_DOMAIN[num],
      source: 'scenario-exam',
      title: num === 55 ? 'send_notification Timeouts: Cannot Determine If Message Was Sent' : title,
      text:
        num === 55
          ? 'Currently returns generic is_error: true, agents retry, causing duplicate notifications. How to modify error response?'
          : cleanText(questionLines.join(' ')),
      options:
        num === 55
          ? {
              A: 'Return is_error: true with a structured field retry_safe: true for timeouts',
              B: 'Return is_error: false with the original message content echoed back',
              C: 'Return is_error: true with a message encouraging retry',
              D: 'Return is_error: true with a message communicating uncertainty: "Timeout—status unknown. Message may have been sent. Avoid retry."',
            }
          : options,
      correctAnswer: detectedCorrect,
      explanation:
        num === 55
          ? 'Why D: The critical information is the delivery uncertainty and the instruction NOT to retry. Option A with retry_safe: true would cause the exact duplicate problem you\'re trying to prevent.'
          : explanationTail
            ? `Why ${detectedCorrect}: ${cleanText(explanationTail)}`
            : '',
    });
  }

  return questions.sort((a, b) => a.id - b.id);
}

function writeBank(outputPath, questions) {
  const domainCounts = {};
  for (const q of questions) {
    domainCounts[q.sectionId] = (domainCounts[q.sectionId] || 0) + 1;
  }
  console.log(`  Domain counts:`, domainCounts);

  const bank = {
    examTitle: 'Claude Certified Architect – Foundations (CCA-F)',
    totalQuestions: questions.length,
    sections: SECTIONS,
    questions: questions.map(({ source, ...q }) => q),
  };

  fs.writeFileSync(outputPath, JSON.stringify(bank, null, 2) + '\n');
  console.log(`  Wrote ${outputPath} (${questions.length} questions)`);
}

function main() {
  const studyPath = resolveInput(STUDY_GUIDE_TXT, STUDY_GUIDE_TXT_FALLBACK);
  const answersPath = resolveInput(ANSWERS_TXT, ANSWERS_TXT_FALLBACK);

  if (!studyPath || !answersPath) {
    console.error('Missing extracted PDF text. Run extract-pdf-text.js first.');
    console.error(`  Study guide: ${studyPath || 'NOT FOUND'}`);
    console.error(`  Answers: ${answersPath || 'NOT FOUND'}`);
    process.exit(1);
  }

  console.log(`Study guide text: ${studyPath}`);
  console.log(`Answers text: ${answersPath}`);

  const studyText = fs.readFileSync(studyPath, 'utf8');
  const answersText = fs.readFileSync(answersPath, 'utf8');

  const studyQuestions = parseStudyGuide(studyText);
  const scenarioQuestions = parseAnswersPdf(answersText);

  console.log(`\nYagnesh (Study Guide): ${studyQuestions.length} questions`);
  if (studyQuestions.length !== 60) {
    console.warn(`Expected 60 study guide questions, got ${studyQuestions.length}`);
  }
  writeBank(OUTPUT_YAGNESH, studyQuestions);

  console.log(`\nAnkush (Scenario Answers): ${scenarioQuestions.length} questions`);
  if (scenarioQuestions.length < 59) {
    console.warn(`Expected ~60 scenario questions, got ${scenarioQuestions.length}`);
  }
  writeBank(OUTPUT_ANKUSH, scenarioQuestions);
}

main();
