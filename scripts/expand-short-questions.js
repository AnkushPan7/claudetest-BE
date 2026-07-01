const fs = require('fs');
const path = require('path');

/** Full scenario stems for questions that were stored with abbreviated text. */
const expandedTextById = {
  3: 'Your music recommendation assistant is in a multi-turn conversation. Claude asks what genres a user enjoys even though they said "I love jazz" two messages earlier. What is the most likely cause?',
  11: 'Your event planning assistant asks an average of 4.2 clarifying questions per session. Analytics show 35% of users abandon before completing their request. Reducing questions causes poor recommendations, but keeping them causes abandonment. What is the best trade-off?',
  12: 'A user\'s first message to your focus music assistant is: "Set up my focus music." This could mean configure preferences, create a playlist, or play music immediately. What is the best approach?',
  14: 'Your conversational assistant shows increasing API latency and costs after 50+ conversation turns. What is the PRIMARY cause?',
  15: 'Users report your assistant\'s responses feel repetitive across turns, with the same openers like "Certainly!" and "I\'d be happy to help!" appearing frequently. What is the most effective approach?',
  16: 'Your document extraction pipeline requires `extract_metadata` to run before enrichment tools like `lookup_citations`. However, Claude sometimes calls `lookup_citations` before `extract_metadata`. What is the most effective fix?',
  17: 'Your real estate extraction schema uses a `property_type` enum. 8% of extractions fail schema validation because new property types keep appearing that are not in the enum. What is the best long-term solution?',
  18: 'Your document processing system handles standard monthly reports (archived after processing) and urgent exception reports (require action within 30 minutes). Both use the same JSON schema. How should you architect the pipeline to minimize costs while meeting latency requirements?',
  19: 'Documents arrive continuously at your processing center. You need a batch processing strategy that satisfies 99.9% reliability within a 30-hour SLA. Which batching frequency should you use?',
  23: 'Your invoice extraction pipeline extracts line items and totals, but downstream systems reject records when line items do not sum to the stated total. What is the most effective approach?',
  24: 'You submitted 10,000 documents via the Batch API. 300 failed with `context_length_exceeded`. What is the most cost-effective approach to process only the failures?',
  25: 'Your extraction system implements automatic retries when validation fails, appending specific validation errors to the prompt. For which failure pattern would additional retries be LEAST effective?',
  26: 'Your invoice extraction uses tool use with strict JSON schemas. JSON syntax errors never occur, but 12% of extractions fail semantic validation (e.g., line items do not sum to total). These currently route to manual review. What is the most effective approach to reduce manual review?',
  27: 'Your contract extraction system processes documents with amendments. The original contract states "30-day payment terms" but Amendment 1 specifies "45 days." What is the most effective improvement?',
  28: 'Your product catalog extraction has inconsistent "materials" field output: sometimes "cotton blend", sometimes "Cotton/Polyester mix", and sometimes the field is omitted when materials are clearly present. What is the most effective fix?',
  30: 'Your extraction system has operated with 100% human review for 3 months, achieving 97% accuracy on high-confidence extractions. Before deploying automation that routes high-confidence extractions directly to downstream systems, what validation is most critical?',
  31: 'A customer sends: "I want to talk to a real person NOW." Your support agent has not called any tools yet. What should the agent do?',
  32: 'You are implementing escalation logic for your customer support agent to determine when to call `escalate_to_human`. Which approach most reliably identifies cases that genuinely require human intervention?',
  33: 'Your billing dispute agent uses `process_refund`, which returns both technical errors (transient) and business errors (permanent). The agent wastes 3-4 turns retrying permanent business errors because both return plain text messages. What is the most effective fix?',
  37: 'Compliance policy requires that refunds exceeding $500 must always escalate to a human agent. Despite clear system prompt instructions, logs show a 3% failure rate where high-value refunds are processed directly. How do you achieve guaranteed compliance?',
  38: 'Your agent performs multi-step identity verification before resetting passwords. After the customer answers the third verification question, the agent asks for their name again as if the earlier exchange never happened. What is the most likely cause?',
  39: 'You are implementing an MCP `lookup_order` tool. The backend sometimes returns "Order not found" or temporary database failures. What is the correct error communication pattern?',
  42: 'A customer raises three separate issues in a single support session. At turn 48, they ask about their refund discussed in turns 1–15. What strategy best maintains context for all issues throughout the session?',
  46: 'Your team proposes bypassing user confirmation for MCP tools annotated with `readOnlyHint: true`. What should guide this decision?',
  47: 'Your organization needs to integrate weather API data into multiple AI applications. What is the primary advantage of using an MCP server approach instead of custom tools?',
  49: 'You are building a `search_documents` tool with three document databases. Users naturally express their intent (e.g., "search the research database"). How should you design the database selection parameter?',
  51: 'Your financial agent uses a `get_portfolio_value` tool. What is the primary advantage of using structured JSON output with defined fields instead of free-form text?',
  52: 'Your team management agent has a `remove_team_member` tool with a required preview+confirm workflow using `dry_run=true` before `dry_run=false`. The agent sometimes calls with `dry_run=false` directly, skipping preview. What is the most reliable enforcement approach?',
  53: 'Your CRM agent\'s `delete_contact` tool sometimes deletes misidentified records when multiple contacts share similar names. The current multi-step confirmation adds friction. How do you reduce error rate while maintaining efficiency?',
  54: 'Your agent uses a `send_notification` tool. When the underlying service times out, the tool returns a generic `is_error: true`, causing agents to retry and send duplicate notifications. How should you modify the error response?',
  56: 'Your travel agent\'s `search_flights` tool calls an external API that sometimes returns 503 Service Unavailable. What is the most effective way to handle this error in your tool implementation?',
  57: 'Your expense agent has a `process_reimbursement` tool with a $500 approval threshold. Which design ensures the threshold cannot be bypassed regardless of how the agent is prompted?',
  58: 'You are building a fitness assistant with a single `log_workout` tool that accepts exercise type and a measurement field (minutes, miles, reps, or sets). During testing, cardio exercises are logged with "reps" and strength exercises with "miles" — clearly invalid combinations. What is the most effective fix?',
  59: 'Your scheduling agent uses `get_available_slots` followed by `book_appointment`. Another user books the selected slot in between the two calls. How should you redesign the workflow?',
  68: 'In your multi-agent research system, a web search subagent times out during a coordinated query. Which error propagation approach best enables intelligent coordinator recovery?',
};

const questionsPath = path.join(
  __dirname,
  '..',
  'ClaudeCertPractice.Api',
  'Data',
  'questions.json',
);
const bank = JSON.parse(fs.readFileSync(questionsPath, 'utf8'));

let updated = 0;
for (const q of bank.questions) {
  const expanded = expandedTextById[q.id];
  if (!expanded) continue;
  if (q.text === expanded) continue;
  q.text = expanded;
  updated++;
}

fs.writeFileSync(questionsPath, JSON.stringify(bank, null, 2) + '\n');
console.log(`Expanded ${updated} question stems.`);
