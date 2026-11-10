<?php
// LLMFS (LLM FileSystem) - the commandline
//
// Presents a commandline to the user and hands each line to the engine (llmfs-engine.php).
// The LLM services come from llmfs-config.php.
//
//   In a browser:   open llmfs.php - a plain HTML form, no JavaScript.
//                   The sessions, path and screen are kept in the PHP session.
//   From a shell:   php llmfs.php - the same commandline in the terminal.
//
// PHP 5.4+

require_once dirname(__FILE__) . '/llmfs-engine.php';

$g_MAXSCREENLINES = 1000;	// browser only: lines of output kept on screen

$g_arrConfig = require dirname(__FILE__) . '/llmfs-config.php';
$g_objEngine = new LLMFSEngine($g_arrConfig);

if (php_sapi_name() === 'cli')
{
	// ---- terminal ----
	$g_arrLines = $g_objEngine->invokeChatGPT('');	// shows the usage
	$g_blnAgain = true;

	echo implode("\n", $g_arrLines) . "\n";

	while ($g_blnAgain)
	{
		echo "\n" . $g_objEngine->getPathText() . '> ';

		$g_strInput = fgets(STDIN);

		if ($g_strInput === false || strtolower(trim($g_strInput)) === 'x')
		{
			$g_blnAgain = false;
		}
		else
		{
			$g_arrLines = $g_objEngine->invokeChatGPT(trim($g_strInput));
			echo implode("\n", $g_arrLines) . "\n";
		}
	}
}
else
{
	// ---- browser ----
	session_start();

	if (isset($_SESSION['llmfs_state']))
	{
		$g_objEngine->setState($_SESSION['llmfs_state']);
	}

	if (!isset($_SESSION['llmfs_screen']) || !is_array($_SESSION['llmfs_screen']))
	{
		$_SESSION['llmfs_screen'] = $g_objEngine->invokeChatGPT('');	// shows the usage
	}

	if ($_SERVER['REQUEST_METHOD'] === 'POST')
	{
		$g_strInput = isset($_POST['commandline']) ? trim((string)$_POST['commandline']) : '';
		$g_arrScreen = $_SESSION['llmfs_screen'];

		$g_arrScreen[] = ' ';
		$g_arrScreen[] = $g_objEngine->getPathText() . '> ' . $g_strInput;

		if (strtolower($g_strInput) === 'x')
		{
			// exit: forget the sessions, the path and the screen
			$g_objEngine->setState(array());
			$g_arrScreen = $g_objEngine->invokeChatGPT('');
		}
		else if (strlen($g_strInput) > 0)
		{
			@set_time_limit(isset($g_arrConfig['timeout']) ? intval($g_arrConfig['timeout']) + 30 : 150);

			$g_arrLines = $g_objEngine->invokeChatGPT($g_strInput);
			for ($g_intI = 0; $g_intI < count($g_arrLines); $g_intI++)
			{
				$g_arrScreen[] = $g_arrLines[$g_intI];
			}
		}

		if (count($g_arrScreen) > $g_MAXSCREENLINES)
		{
			$g_arrScreen = array_slice($g_arrScreen, count($g_arrScreen) - $g_MAXSCREENLINES);
		}

		$_SESSION['llmfs_screen'] = $g_arrScreen;
		$_SESSION['llmfs_state'] = $g_objEngine->getState();

		// redirect so a page refresh does not send the command again
		$g_strSelf = strtok($_SERVER['REQUEST_URI'], '?#');
		header('Location: ' . $g_strSelf . '#commandline', true, 303);
	}
	else
	{
		header('Content-Type: text/html; charset=utf-8');

		$g_strScreen = htmlspecialchars(implode("\n", $_SESSION['llmfs_screen']), ENT_QUOTES, 'UTF-8');
		$g_strPath = htmlspecialchars($g_objEngine->getPathText(), ENT_QUOTES, 'UTF-8');
?>
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>LLMFS</title>
<style>
body { background: #000; color: #0f0; font-family: monospace; font-size: 14px; margin: 8px; }
pre { margin: 0; white-space: pre-wrap; word-wrap: break-word; font-family: inherit; font-size: inherit; }
form { display: flex; margin-top: 14px; }
label { white-space: pre; }
input { flex: 1; background: #000; color: #0f0; border: 0; outline: 0; font-family: inherit; font-size: inherit; padding: 0; }
</style>
</head>
<body>
<pre><?php echo $g_strScreen; ?></pre>
<form method="post" action="" autocomplete="off">
<label for="commandline"><?php echo $g_strPath; ?>&gt; </label><input type="text" id="commandline" name="commandline" autofocus>
</form>
</body>
</html>
<?php
	}
}
