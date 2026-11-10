# LLMFS - LLM FileSystem

LLMFS puts an LLM behind the commands of a filesystem. You do not write prompts.
You type `dir`, `cd` and `load`, or you save a text file into a folder, and the LLM does the rest.

There are three versions here. They share the idea, not the code.

| Folder | Version | What it is |
|---|---|---|
| `js/` | JavaScript | A CyborgShell transformer. A conversation browsed as folders and files. |
| `php/` | PHP 5.4+ | The same thing standalone: an engine plus a commandline, in a browser (no JavaScript) or a terminal. |
| `cs/` | C# / .NET 8 | A folder watcher. Save a text file into a watched folder and a folder of results appears beside it. |

The design, the rules and the reasons for them are in [LLMFS.md](LLMFS.md).

## Status

Proof of concept. All three versions have been run against a stand-in LLM that returns canned replies.
None of them has been proven against every provider listed in the configs, and the model names in the
sample configs are examples: check your provider's current list.

---

## JS version - CyborgShell

`js/llmfs.js` is built on CyborgShell's `chatgpt.js` and is used the same way. Put it beside `chatgpt.js`.
It uses the same provider settings (`ai-provider`) and the same sessions, so a session built in `chatgpt`
can be browsed with `llmfs`.

```
\> what do bears eat?
Bears eat fish, berries and ...

\> dir
 Directory of \

<DIR>  animals
<FILE> diet

1 File(s), 1 Dir(s)

\> cd animals
\animals

\animals> load diet
Fish and berries ...
```

### Commands

| Command | What it does |
|---|---|
| `dir` | Lists what has been discussed under the current path, as folders and files. |
| `cd <folder>` | Changes the current path. `cd ..`, `cd \` and `cd a\b` work. No LLM call. |
| `load <name>` | Shows what was discussed about `<name>` at the current path. |
| anything else | A normal question. The current path is sent with it, and it is added to the session. |
| `<sessionid>: ...` | Runs any of the above against a named session, e.g. `math: dir`. |
| `sessions` | Lists the sessions in memory. |
| `clear [<sessionid>]` | Clears one session, or all of them. |
| `save [<sessionid>]` | Saves one session, or all of them, to a file. |
| `loadsession [<sessionid>]` | Loads one session, or all of them, from a file. |
| `train [<sessionid>:] <files-or-sessions>` | Adds text files or other sessions to a session. |
| `services` / `service <name>` | Lists the configured LLM services / switches to one. |
| `x` | Leaves interactive mode. |

`train` is what turns a document into a filesystem: `train manual: manual.txt`, then `manual: dir`.

---

## PHP version - standalone

Three files, kept in one folder:

| File | What it is |
|---|---|
| `php/llmfs-config.php` | The LLM services: endpoint, model, API key and flavour for each. |
| `php/llmfs-engine.php` | The engine. One line in, the lines to show back. No user interface. |
| `php/llmfs.php` | The commandline. A plain HTML form in a browser, or `php llmfs.php` in a terminal. |

Needs PHP 5.4 or later. Uses curl when it is there, PHP streams when it is not.

1. Put your API keys in `llmfs-config.php` and set `provider` to the service to start on.
2. Open `llmfs.php` in a browser, or run `php llmfs.php`.

The commands are the same as the JS version. Saved sessions and `train` files live in a `data` folder
beside the engine, and the session files are the same format the JS version writes.

**There is no login on the page.** Anyone who can reach `llmfs.php` can spend your API keys.
Put it somewhere private or put your own login in front of it.

### Using the engine from your own code

```php
require 'llmfs-engine.php';

$objEngine = new LLMFSEngine(require 'llmfs-config.php');
$objEngine->setState($arrState);                    // optional: state kept from the last call
$arrLines = $objEngine->invokeChatGPT('dir');       // the lines to show the user
$arrState = $objEngine->getState();                 // keep this for the next call
```

The state holds the sessions, the current path and the current service.

---

## C# version - folder watcher

A program that watches the folders you nominate, and their subfolders.

- **Ask** by saving a short text file. Its content is the request.
- **The answer** is a folder named after the file, holding one file or many.
- **Each file shows its state in its name:**

```
C:\LLMFS\
  cats.txt             "10 short stories about cats"
  cats\
    story1.txt         finished
    story2.txt.busy    being made
    story3.txt.error   failed - the reason is inside
```

Any program that can save a file can use it. Notepad is enough.

### Build

Run `build.bat` in the `cs` folder. It runs:

```
dotnet publish llmfs.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o bin/llmfs
```

The result is `cs\bin\llmfs\llmfs.exe`. No NuGet packages. `llmfs-config.json` is copied beside `llmfs.exe`.

### Run

```
llmfs                    uses llmfs-config.json beside the exe
llmfs <config file>      uses the config file given
```

It is a console program that shows a log. Ctrl+C stops it.

### The rules

| Rule | What happens |
|---|---|
| What is watched | The folders under `folders` in the config, and all their subfolders. |
| A request | A new plain-text file no bigger than `maxrequestsize`. The subfolder path is sent with it. |
| The result | A folder named after the file: `peter.txt` gives `peter\`. |
| Asking again | Save the request with different text and it runs again into `peter 2\`. |
| Left alone | Files that were already there, overwrites of them, folders you make, binary files, big files. |
| Its own output | Never treated as a request. Copy a result file elsewhere in a watched folder to run it as one. |
| Cancelling | Delete a `.busy` file and that file is not written. |
| Big jobs | Asked about first. See confirming, below. |

### Config

```json
{
	"confirm": true,
	"planner": "ollama",
	"services": {
		"ollama": { "endpoint": "http://localhost:11434/v1/chat/completions", "model": "llama3.2:latest", "flavour": "chatgpt", "confirmabove": 0 },
		"claude": { "endpoint": "https://api.anthropic.com/v1/messages", "model": "...", "flavour": "claude", "apikey": "", "confirmabove": 5 }
	},
	"filetypes": { ".js": "claude", "*": "ollama" },
	"folders": [ { "path": "C:\\LLMFS" } ]
}
```

| Setting | Meaning |
|---|---|
| `planner` | The service that turns a request into the list of files to make. |
| `services` | One entry per LLM service. `flavour` is `chatgpt` (any OpenAI-style endpoint), `claude` or `gemini`. |
| `filetypes` | Which service makes which kind of file. `*` is everything not listed. A folder can carry its own. |
| `folders` | The folders to watch. |
| `confirm` | `false` turns all asking off. |
| `confirmabove` | Per service: ask before a job makes more than this many files on it. `0` never asks. |
| `burst` | More new request files than this at once gets one "run them all?" question. |
| `maxfiles` | The most files one request may make. |
| `maxrequestsize` | Bytes. A new file bigger than this is not a request. |
| `parallel` | Files being made at the same time. |
| `timeout`, `rescan` | Seconds per LLM call / seconds between full rescans. |

### Image services

Three flavours, chosen per service:

| `flavour` | Speaks to |
|---|---|
| `openai-image` | OpenAI's images API, and anything that copies it. |
| `gemini-image` | Google's `generateContent` image models. |
| `stability-image` | Stability AI's `stable-image/generate` endpoints. |

`options` on an image service is passed straight through to the provider (size, quality, aspect ratio and so on).
A picture is saved under the extension of what actually came back: ask for `cat.jpg`, get PNG data, and the file is `cat.png`.

### Known gaps

- A console window, not a tray icon.
- Confirming is a Yes/No box on Windows. There is no way yet to confirm with nobody at the screen.
- Confirming counts files per job, not across a batch of jobs.
- A file over `maxrequestsize` is ignored without a message.

---

## Test tooling

Each version has a `tools` folder with the stand-in LLM it was tested against and a `readme.txt` on running it.

## Licence

MIT. See [LICENSE.md](LICENSE.md).

(c) 2026 Cyborg Unicorn Pty Ltd
