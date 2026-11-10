LLMFS JS - test tooling

harness.js runs llmfs.js outside CyborgShell under nodejs, with a stand-in for the shell's api
object and canned LLM replies, and prints what each command showed and what was sent to the LLM.

  node tools/harness.js llmfs.js            chatgpt flavour
  node tools/harness.js llmfs.js claude     claude flavour
  node tools/harness.js llmfs.js gemini     gemini flavour
