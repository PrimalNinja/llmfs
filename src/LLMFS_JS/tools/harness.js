var fs = require('fs');
var code = fs.readFileSync(process.argv[2], 'utf8');
var globals = {};
var store = { 'cyborgshell-config-ai-provider': '"p"', 'cyborgshell-config-p-endpoint': '"http://x"', 'cyborgshell-config-p-model': '"m"', 'cyborgshell-config-p-flavour': '"' + (process.argv[3] || 'chatgpt') + '"' };
var localStorage = { length: 0, key: function(){return null;}, getItem: function(k){return store[k];} };
var replies = [];
var sent = [];
var $ = {
  isFunction: function(f){ return typeof f === 'function'; },
  isArray: Array.isArray,
  ajax: function(o){ sent.push(JSON.parse(o.data)); var r = replies.shift(); var h = { done: function(f){ setTimeout(function(){ f(wrap(r)); },0); return h; }, fail: function(){ return h; } }; return h; }
};
function wrap(t){ var f = process.argv[3] || 'chatgpt'; if (f==='claude') return {content:[{text:t}]}; if (f==='gemini') return {candidates:[{content:{parts:[{text:t}]}}]}; return {choices:[{message:{content:t}}]}; }
var out = [];
var api = {
  print: function(s){ out.push(s); }, errorOutput: function(s){ out.push('ERR ' + s); },
  loadLocalData: function(k, cb){ var v = store['cyborgshell-config-' + k]; cb(v === undefined ? null : JSON.parse(v)); },
  saveFile: function(n,d,cb){ cb({error:''}); }, loadFile: function(n,cb){ cb({error:'x'}); }, stop: function(){}, input: function(){}
};
global.console.log = function(){};
function run(cmd, reply, next){ if (reply !== null) replies.push(reply); api.commandline = ['run','llmfs'].concat(cmd.length ? cmd.split(' ') : []); out = []; var n = sent.length;
  new Function('api','globals','$','localStorage', code)(api, globals, $, localStorage);
  setTimeout(function(){ process.stdout.write('### ' + cmd + '\n' + out.join('\n') + '\n'); if (sent.length > n) { process.stdout.write('--- sent: ' + JSON.stringify(sent[sent.length-1]).substring(0, 700) + '\n'); } else { process.stdout.write('--- (no LLM call)\n'); } next(); }, 20); }
var steps = [
 ['dir', null], ['cd animals', null], ['what do bears eat?', 'Bears eat fish and berries.'],
 ['dir', '```json\n{"dirs":["Bears","birds"],"files":["diet","bears"]}\n```'],
 ['cd bears\\food', null], ['cd ..', null], ['cd /animals/birds/', null], ['cd', null], ['cd \\', null],
 ['load diet', 'Sure! {"name":"diet","content":"Fish and berries."}'],
 ['load nothing', '{"name":"nothing","content":""}'],
 ['dir', 'I cannot do that'], ['dir', '["a","b"]'],
 ['math: dir', null], ['load', null], ['sessions', null]
];
(function go(i){ if (i < steps.length) { run(steps[i][0], steps[i][1], function(){ go(i+1); }); } else { process.stdout.write('SESSION: ' + JSON.stringify(globals.chatgpt.sessions) + '\nPATH: ' + JSON.stringify(globals.llmfs.path) + '\n'); } })(0);
