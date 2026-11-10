<?php
// image mock: routed by path
$u = $_SERVER['REQUEST_URI'];
$png = base64_decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==');
$h = array_change_key_case(getallheaders());
$raw = file_get_contents('php://input');
file_put_contents(__DIR__ . '/img.log', "=== $u auth=" . (isset($h['authorization'])?$h['authorization']:'-') . " goog=" . (isset($h['x-goog-api-key'])?$h['x-goog-api-key']:'-') . " accept=" . (isset($h['accept'])?$h['accept']:'-') . " ct=" . (isset($h['content-type'])?$h['content-type']:'-') . "\nPOST=" . json_encode($_POST) . "\nBODY=" . substr($raw,0,300) . "\n", FILE_APPEND);
if (strpos($u, '/file.png') !== false) { header('Content-Type: image/png'); echo $png; exit; }
if (strpos($u, '/oai-url') !== false) { echo json_encode(array('data'=>array(array('url'=>'http://127.0.0.1:8911/img.php/file.png')))); exit; }
if (strpos($u, '/oai-bad') !== false) { http_response_code(401); echo json_encode(array('error'=>array('message'=>'Incorrect API key provided'))); exit; }
if (strpos($u, '/oai') !== false) { echo json_encode(array('created'=>1,'data'=>array(array('b64_json'=>base64_encode($png))))); exit; }
if (strpos($u, '/gem') !== false) { echo json_encode(array('candidates'=>array(array('content'=>array('role'=>'model','parts'=>array(array('text'=>'here you go'), array('thought'=>true,'inlineData'=>array('mimeType'=>'image/png','data'=>'AAAA')), array('inlineData'=>array('mimeType'=>'image/png','data'=>base64_encode($png))))))))); exit; }
if (strpos($u, '/stab') !== false) { if (!isset($_POST['prompt'])) { http_response_code(400); echo json_encode(array('errors'=>array('prompt: required'),'name'=>'bad_request')); exit; } header('Content-Type: image/webp'); echo "RIFF\x10\x00\x00\x00WEBPVP8 xxxx"; exit; }
