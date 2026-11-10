# LLMFS - the idea and the rules

## The idea

Using an LLM normally means learning how to ask. LLMFS removes that by putting the LLM behind
something people already know: a filesystem.

- A **folder** is a topic.
- A **file** is a piece of information, or a thing that was made.
- **Listing** a folder asks the LLM what is in that topic.
- **Opening** a file asks the LLM for its content.
- **Saving** a file is asking for something.

The person types `dir`, `cd` and `load`, or saves a text file. The prompts are written by LLMFS.
The person never sees a prompt and never sees the JSON that comes back.

If an LLM were part of an operating system, a filesystem that behaved like this is one way it would show up.

## Two forms

LLMFS exists in two forms. They are the same idea pointed in two directions.

| | Commandline form | Folder form |
|---|---|---|
| Versions | JS (CyborgShell), PHP | C# folder watcher |
| What the filesystem is | A view of a conversation | A real folder on disk |
| Where files live | Nowhere. Each listing is worked out by the LLM when asked. | On disk, as ordinary files. |
| How you ask | `dir`, `cd`, `load`, or a plain question | Save a text file |
| Who can use it | Someone at that commandline | Any program that can save a file |

## Commandline form (JS, PHP)

### How it works

The only data is the session: the list of questions and answers so far. Nothing is stored as a tree.

- **`dir`** sends the session with a fixed prompt: list what we discussed directly under this path,
  split into categories (dirs) and single pieces of information (files), one lowercase word each,
  as JSON `{"dirs":[],"files":[]}`. The reply is parsed and printed as a directory listing.
- **`cd`** only changes the current path. It makes no LLM call.
- **`load <name>`** sends the session with a fixed prompt: give what we discussed about this name at
  this path, as JSON `{"name":"","content":""}`. The content is printed.
- **Anything else** is a normal question and goes to the LLM with the session as context.

The filesystem builds itself as you talk. Ask more about a topic and the next `dir` shows more under it.
A file can turn into a folder once enough has been said about it.

### The rules

| Rule | Reason |
|---|---|
| `dir` and `load` are not added to the session. | Otherwise the next `dir` lists "topics" and "JSON" as things you discussed. |
| A question asked inside a path is stored with the path, e.g. `[\animals\bears] what do they eat?` | It tells the LLM where in the tree that discussion belongs. |
| The path is sent with every question. | So "what do they eat in winter?" is understood as being about bears. |
| `dir` and `load` run at temperature 0. | To cut down on entries changing between two listings. |
| `cd` accepts any name. | The LLM decides the listing afresh each time, so refusing unlisted names would fight it. |
| An empty session makes no LLM call. | There is nothing to list, and it stops the LLM making things up. |
| `load` is limited to what was discussed. | Same limit as `dir`. |
| JSON is dug out of the reply. | LLMs wrap JSON in code fences or add chatter even when told not to. |

### Sessions

Sessions are named. `math: what is a prime?` talks to the session `math`; with no name it is `default`.
`math: dir` browses it. Sessions can be saved, loaded, cleared and trained.

`train` adds a text file, or another session, to a session. That makes any document browsable:

```
train manual: manual.txt
manual: dir
```

### Limits

- **Listings can change.** Ask `dir` twice and a name may differ, or a file may become a folder.
- **Every `dir` and `load` sends the whole session**, up to 100,000 characters. On a paid service that is a full-price call each time.
- **Old material drops out.** Once a session passes that size, the earliest discussion is no longer sent.
- **`load` can still invent.** The prompt limits it to what was discussed. Nothing enforces that.

Keeping each path's last listing until the session changes would fix the first two. It is not built.

## Folder form (C#)

### How it works

A program watches nominated folders and their subfolders. It is a file creator and dispatcher: it sees
a request arrive, plans the files that answer it, and has the right service make each one.

1. You save `peter.txt` containing "congratulations letter for Peter for getting a job promotion".
2. The watcher waits for the save to finish, then makes the folder `peter\`.
3. **Plan.** One call to the planner service turns the request into a list of files, each with a name
   and a one-line description of what it must contain.
4. Every planned file appears at once as `name.busy`, holding its description.
5. **Fill.** Each file is made by the service its extension maps to. `name.busy` becomes `name`,
   or `name.error` holding the reason.

The plan is one cheap call, so the size of a job is known before anything costly runs.

### The rules

| Rule | Reason |
|---|---|
| The request is always a file. A folder is always a result. | A folder you make yourself (`work`, `invoices 2026`) is never mistaken for a request. |
| The request file is left as written. | You can see what made the folder, change it, and run it again. |
| Results go into a new folder. | Nothing of yours is ever replaced. A wrong guess costs one LLM call and a folder to delete. |
| A second save with different text goes to `peter 2\`. | The first results are kept. |
| Only a new, short, plain-text file is a request. | A photo or a real document copied in is not text or is too big, and is left alone. |
| Files already there are never requests. | The watcher records what exists at startup; saving over an existing file is a plain save. |
| State is in the file name: `.busy`, finished, `.error`. | It shows in Explorer, in `dir` and in every program, with no special viewer. |
| A finished file does not exist until it is complete. | There is never a half-written file to open. |
| The watcher never treats its own output as a request. | Nothing chains on its own, so a job cannot run away. |
| Jobs over a limit are confirmed first. | Paid services cost money. The limit is per service, and `0` never asks, which suits a local model. |
| File type picks the service. | Text can go to a local model, code to a stronger one, pictures to an image service. |

### Chaining

The output of one request is a text file, and a text file dropped in a watched folder is a request.
So results can be fed back in by hand:

```
ideas.txt      "make 10 text files with a video game idea in each and a prompt to make them"
ideas\         idea1.txt ... idea10.txt
```

Copy those ten files up into a watched folder and each one becomes a request, giving ten game folders.
Copying is the "run" step. Nothing chains until you do it.

### Pictures

A planned file with a picture extension goes to an image service. The prompt for the picture is that
file's one-line description from the plan. Three provider APIs are handled, and `options` in the config
is passed straight through to the provider.

The file is named for what comes back. The watcher has no image converter, so if `cat.jpg` was asked
for and PNG data came back, the file is saved as `cat.png`.

### Limits

- A request that is too big is ignored without a message.
- Confirming counts each job on its own. Ten small jobs started together are not added up.
- Programs save files in different ways. Telling a new file from an overwrite depends on the watcher's
  own record of what it has seen.

## Not built

These came out of the design and are not in any version:

- **A real drive.** The same rules behind a drive letter, so that files which do not exist yet can be
  listed and opened. Windows offers ways to do it (its Projected File System, or a user-mode filesystem
  driver). It would have to be one drive or folder, not the whole machine: programs create temp and
  settings files all the time, and each would become a request.
- **A merged listing.** One listing showing stored files and LLM-derived topics together, with the
  source kept as hidden metadata.
- **A command table.** `dir`, `load` and the rest as rows of keyword, prompt template and what to do
  with the reply, in a file, so that new commands need no code.
- **Confirming with nobody at the screen**, by renaming a file the watcher writes.

## Licence

MIT. (c) 2026 Cyborg Unicorn Pty Ltd
