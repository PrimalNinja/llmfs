<?php
$b = json_decode(file_get_contents('php://input'), true);
$f = isset($b['contents']) ? 'gemini' : (isset($b['system']) ? 'claude' : 'chatgpt');
if (strpos($_SERVER['REQUEST_URI'], 'claude') !== false) { $f = 'claude'; }
$p = $f === 'gemini' ? $b['contents'][0]['parts'][0]['text'] : $b['messages'][count($b['messages'])-1]['content'];
file_put_contents(__DIR__ . '/last.json', json_encode(array('uri'=>$_SERVER['REQUEST_URI'],'hdr'=>array_change_key_case(getallheaders()),'body'=>$b)));
if (strpos($p, 'fail') !== false) { http_response_code(401); echo json_encode(array('error'=>array('message'=>'bad key'))); exit; }
if (strpos($p, 'List ') !== false) { $t = "```json\n{\"dirs\":[\"Bears\",\"birds\"],\"files\":[\"diet\",\"bears\"]}\n```"; }
elseif (strpos($p, 'Load "') !== false) { $t = strpos($p, '"nothing"') ? '{"name":"nothing","content":""}' : 'Here: {"name":"diet","content":"Fish and <b>berries</b>."}'; }
else { $t = 'Bears eat fish and berries.'; }
if ($f === 'claude') echo json_encode(array('content'=>array(array('text'=>$t))));
elseif ($f === 'gemini') echo json_encode(array('candidates'=>array(array('content'=>array('parts'=>array(array('text'=>$t)))))));
else echo json_encode(array('choices'=>array(array('message'=>array('content'=>$t)))));
