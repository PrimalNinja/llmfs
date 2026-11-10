LLMFS PHP - test tooling

mock-llm/index.php is a stand-in LLM that answers in the chatgpt, claude and gemini formats,
so the engine can be run without a key or a network.

  php -S 127.0.0.1:8901 -t tools/mock-llm

Then point a service's endpoint in llmfs-config.php at it:
  http://127.0.0.1:8901/openai    (flavour chatgpt)
  http://127.0.0.1:8901/claude    (flavour claude)
  http://127.0.0.1:8901/gemini    (flavour gemini)

and run  php llmfs.php
The last request it received is written to mock-llm/last.json.
