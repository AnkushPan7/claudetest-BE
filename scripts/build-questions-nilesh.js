/**
 * Build questions-nilesh.json from CCA-F_Practice_With_Answers.pdf text.
 * Run: npm run build-nilesh-questions
 * Or:  node build-questions-nilesh.js [optional-path-to-pdf]
 */
const fs = require('fs');
const path = require('path');

const PDF_NAME = 'CCA-F_Practice_With_Answers.pdf';
const INPUT = path.join(__dirname, 'CCA-F_Practice_With_Answers.txt');
const OUTPUT = path.join(__dirname, '..', 'ClaudeCertPractice.Api', 'Data', 'questions-nilesh.json');

function pdfCandidates() {
  const home = process.env.USERPROFILE || process.env.HOME || '';
  return [
    process.argv[2],
    home ? path.join(home, 'Downloads', PDF_NAME) : null,
    path.join(__dirname, '..', '..', '..', '..', 'Downloads', PDF_NAME),
    path.join(__dirname, '..', '..', '..', 'Downloads', PDF_NAME),
  ].filter(Boolean);
}

function findPdfPath() {
  for (const candidate of pdfCandidates()) {
    if (fs.existsSync(candidate)) return candidate;
  }
  return null;
}

async function ensureExtracted() {
  if (fs.existsSync(INPUT)) {
    const pdfPath = findPdfPath();
    if (!pdfPath) return;
    const pdfNewer = fs.statSync(pdfPath).mtimeMs > fs.statSync(INPUT).mtimeMs;
    if (!pdfNewer) return;
    console.log('PDF is newer than extracted text — re-extracting…');
  }

  const pdfPath = findPdfPath();
  if (!pdfPath) {
    if (fs.existsSync(INPUT)) return;
    console.error(`Missing ${INPUT} and could not find ${PDF_NAME}.`);
    console.error('Place the PDF in your Downloads folder or run:');
    console.error(`  node extract-pdf-text.js "C:\\path\\to\\${PDF_NAME}"`);
    console.error(`  node build-questions-nilesh.js "C:\\path\\to\\${PDF_NAME}"`);
    process.exit(1);
  }

  const pdfParse = require('pdf-parse');
  const buf = fs.readFileSync(pdfPath);
  const data = await pdfParse(buf);
  fs.writeFileSync(INPUT, data.text, 'utf8');
  console.log(`Extracted ${pdfPath} → ${INPUT} (${data.numpages} pages)`);
}

function sectionIdForQuestion(num) {
  if (num <= 15) return 1;
  if (num <= 30) return 2;
  if (num <= 45) return 3;
  return 4;
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

function parsePracticePdf(text) {
  const start = text.indexOf('Question 1\n');
  if (start < 0) throw new Error('Could not find Question 1 in extracted text');

  const body = text.slice(start);
  const blocks = body.split(/\n(?=Question \d+\n)/);
  const questions = [];

  for (const block of blocks) {
    const numMatch = block.match(/^Question (\d+)/);
    if (!numMatch) continue;

    const num = Number(numMatch[1]);
    const content = block.replace(/^Question \d+\n+/, '');

    const domainLine = content.match(/^Domain \d+:[^\n]+\n/);
    if (!domainLine) {
      console.warn(`Q${num}: missing domain line`);
      continue;
    }

    const domainFull = domainLine[0].trim();
    const titleMatch = domainFull.match(/ n (.+)$/);
    const title = titleMatch ? titleMatch[1].trim() : `Question ${num}`;

    const afterDomain = content.slice(domainLine[0].length);
    const answerMatch = afterDomain.match(/\nCorrect Answer:\s*([A-D])\s*\n/i);
    if (!answerMatch) {
      console.warn(`Q${num}: missing correct answer`);
      continue;
    }

    const beforeAnswer = afterDomain.slice(0, answerMatch.index);
    const afterAnswer = afterDomain.slice(answerMatch.index + answerMatch[0].length);

    const nextQ = afterAnswer.search(/\nQuestion \d+\n/);
    const explanationRaw = nextQ >= 0 ? afterAnswer.slice(0, nextQ) : afterAnswer;

    const lines = beforeAnswer.split('\n');
    const questionLines = [];
    const optionLines = { A: [], B: [], C: [], D: [] };
    let currentOpt = null;

    for (const line of lines) {
      const optStart = line.match(/^([A-D])\.\s+(.*)$/);
      if (optStart) {
        currentOpt = optStart[1];
        optionLines[currentOpt].push(optStart[2].trim());
        continue;
      }
      if (currentOpt) {
        if (line.trim()) optionLines[currentOpt].push(line.trim());
      } else if (line.trim()) {
        questionLines.push(line.trim());
      }
    }

    const options = {};
    for (const letter of ['A', 'B', 'C', 'D']) {
      options[letter] = normalizeQuotes(cleanText(optionLines[letter].join(' ')));
      if (!options[letter]) {
        console.warn(`Q${num}: missing option ${letter}`);
      }
    }

    questions.push({
      id: num,
      sectionId: sectionIdForQuestion(num),
      title,
      text: normalizeQuotes(cleanText(questionLines.join(' '))),
      options,
      correctAnswer: answerMatch[1].toUpperCase(),
      explanation: normalizeQuotes(cleanText(explanationRaw)),
    });
  }

  return questions.sort((a, b) => a.id - b.id);
}

async function main() {
  await ensureExtracted();

  if (!fs.existsSync(INPUT)) {
    console.error(`Missing ${INPUT}.`);
    process.exit(1);
  }

  const text = fs.readFileSync(INPUT, 'utf8');
  const questions = parsePracticePdf(text);

  console.log(`Parsed Nilesh bank: ${questions.length} questions`);

  if (questions.length !== 60) {
    console.warn(`Expected 60 questions, got ${questions.length}`);
  }

  const bank = {
    examTitle: 'Claude Certified Architect – Foundations (CCA-F)',
    totalQuestions: questions.length,
    sections: [
      { id: 1, name: 'Multi-Agent Orchestration', range: 'Domain 1' },
      { id: 2, name: 'Structured Data Extraction & Tool Use', range: 'Domain 2' },
      { id: 3, name: 'Agentic Tooling & MCP', range: 'Domain 3' },
      { id: 4, name: 'CI/CD Integration & Prompt Engineering', range: 'Domain 4' },
    ],
    questions,
  };

  fs.writeFileSync(OUTPUT, JSON.stringify(bank, null, 2) + '\n');
  console.log(`Wrote ${OUTPUT}`);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
