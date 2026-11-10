<?php
$b = json_decode(file_get_contents('php://input'), true);
$claude = strpos($_SERVER['REQUEST_URI'], 'claude') !== false;
$p = $b['messages'][count($b['messages'])-1]['content'];
file_put_contents(__DIR__ . '/prompts.log', "=== " . $_SERVER['REQUEST_URI'] . " temp=" . $b['temperature'] . " max=" . $b['max_tokens'] . "\n" . $p . "\n", FILE_APPEND);
$trunc = false;
if (strpos($p, 'Plan the files') !== false) {
  preg_match('/Request:\n(.*?)\n\nPlan the files/s', $p, $m); $r = $m[1];
  if (strpos($r, 'planfail') !== false) { http_response_code(401); echo json_encode(array('error'=>array('message'=>'bad key'))); exit; }
  if (strpos($r, 'cat images') !== false) { $f = array(); for ($i=1;$i<=6;$i++) $f[] = array('name'=>"cat$i.jpg",'description'=>"cat number $i"); }
  elseif (strpos($r, 'pictures') !== false) { $f = array(array('name'=>'a.png','description'=>'a red fox'),array('name'=>'b.jpg','description'=>'a blue whale'),array('name'=>'c.webp','description'=>'a green frog'),array('name'=>'d.gif','description'=>'a yellow bird'),array('name'=>'e.bmp','description'=>'a bad key'),array('name'=>'notes.txt','description'=>'notes')); }
  elseif (strpos($r, 'game') !== false) { $f = array(array('name'=>'index.html','description'=>'page'),array('name'=>'Game.JS','description'=>'the game code'),array('name'=>'../readme.txt','description'=>'how to play')); }
  elseif (strpos($r, 'many') !== false) { $f = array(); for ($i=1;$i<=4;$i++) $f[] = array('name'=>"part$i.txt",'description'=>"part $i"); }
  elseif (strpos($r, 'long') !== false) { $f = array(array('name'=>'story.txt','description'=>'LONGFILE a very long story')); }
  else { $f = array(array('name'=>'letter.txt','description'=>'the letter')); }
  $t = "Here is the plan:\n```json\n" . json_encode(array('files'=>$f)) . "\n```";
} else {
  preg_match('/Write this file: (.*)\n/', $p, $m);
  usleep(400000);
  if (strpos($p, 'LONGFILE') !== false) { $trunc = true; }
  $t = substr($m[1], -3) === '.js' ? "```javascript\nvar x = 1; // " . $m[1] . "\n```" : "CONTENT of " . $m[1] . "\n";
}
if ($claude) echo json_encode(array('content'=>array(array('text'=>$t)), 'stop_reason'=>$trunc ? 'max_tokens' : 'end_turn'));
else echo json_encode(array('choices'=>array(array('message'=>array('content'=>$t), 'finish_reason'=>$trunc ? 'length' : 'stop'))));
