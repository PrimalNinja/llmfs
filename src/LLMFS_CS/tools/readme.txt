LLMFS folder watcher - test tooling

mock-llm is a stand-in for the LLM and image services, so the watcher can be run without keys or a network.

  php -S 127.0.0.1:8911 -t tools/mock-llm

  llmfs tools/test-config.json           text jobs, confirming, errors   (watches .\testroot)
  llmfs tools/test-config-images.json    the three image flavours        (watches .\testroot-images)

What the mock plans depends on words in the request file:
  "cat images"  6 jpg files          "game"      3 files (html, js, txt)
  "many"        4 txt files          "long"      a file that is cut off at maxtokens
  "pictures"    png, jpg, webp, gif  "planfail"  the plan call fails
  anything else 1 file, letter.txt

The prompts it received are written to mock-llm/prompts.log and mock-llm/img.log.
