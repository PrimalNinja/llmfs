<?php
// LLMFS (LLM FileSystem) - engine
//
// A PHP port of llmfs.js: same commands, same session management, same prompts.
// The engine has no user interface. A front end (llmfs.php) gives it one line at a time:
//
//   $objEngine = new LLMFSEngine($arrConfig);
//   $objEngine->setState($arrState);                  // optional: state kept from the last call
//   $arrLines = $objEngine->invokeChatGPT($strLine);  // returns the lines to show the user
//   $arrState = $objEngine->getState();               // keep this for the next call
//
// The state holds what the JS version keeps in globals: the sessions, the current path and
// the current service.
//
// PHP 5.4+

class LLMFSEngine
{
	private $m_FSTEMPERATURE = '0';	// dir and load: keep the listing as stable as the LLM allows
	private $m_PATHSEPARATOR = '\\';

	private $m_intDefaultTimeout = 120;		// in seconds
	private $m_intMaxSessionSize = 256;		// megabytes
	private $m_intMaxPromptSize = 100000;	// characters

	private $m_arrConfig = array();
	private $m_arrOutput = array();
	private $m_objGlobals = array();

	private $m_strSessionID = '';	// for a forced sessionid

	private $m_blnConfigured = false;
	private $m_strService = '';
	private $m_strProvider = '';
	private $m_strAPIKey = '';
	private $m_strEndpoint = '';
	private $m_strFlavour = '';
	private $m_strModel = '';
	private $m_strMaxTokens = '';
	private $m_strTemperature = '';
	private $m_strProxy = '';
	private $m_strJSONBearer = '';

	public function __construct($arrConfig_a)
	{
		$this->m_arrConfig = $arrConfig_a;

		if (isset($arrConfig_a['timeout'])) { $this->m_intDefaultTimeout = intval($arrConfig_a['timeout']); }
		if (isset($arrConfig_a['maxsessionsize'])) { $this->m_intMaxSessionSize = intval($arrConfig_a['maxsessionsize']); }
		if (isset($arrConfig_a['maxpromptsize'])) { $this->m_intMaxPromptSize = intval($arrConfig_a['maxpromptsize']); }

		$this->setState(array());
	}

	// ---- state ----

	public function getState()
	{
		return $this->m_objGlobals;
	}

	public function setState($arrState_a)
	{
		$this->m_objGlobals = is_array($arrState_a) ? $arrState_a : array();

		// Initialize session storage
		if (!isset($this->m_objGlobals['chatgpt']) || !is_array($this->m_objGlobals['chatgpt']))
		{
			$this->m_objGlobals['chatgpt'] = array();
		}

		if (!isset($this->m_objGlobals['chatgpt']['sessions']) || !is_array($this->m_objGlobals['chatgpt']['sessions']))
		{
			$this->m_objGlobals['chatgpt']['sessionsize'] = 0;
			$this->m_objGlobals['chatgpt']['sessions'] = array();
		}

		if (!isset($this->m_objGlobals['llmfs']) || !is_array($this->m_objGlobals['llmfs']))
		{
			$this->m_objGlobals['llmfs'] = array();
		}

		if (!isset($this->m_objGlobals['llmfs']['path']) || !is_array($this->m_objGlobals['llmfs']['path']))
		{
			$this->m_objGlobals['llmfs']['path'] = array();
		}

		// the service in use: the one kept in the state, else the configured default
		$strService = '';
		if (isset($this->m_objGlobals['llmfs']['service']))
		{
			$strService = $this->m_objGlobals['llmfs']['service'];
		}
		if ($strService === '' && isset($this->m_arrConfig['provider']))
		{
			$strService = $this->m_arrConfig['provider'];
		}

		$this->loadProviderSettings($strService);
	}

	public function setSessionID($strSessionID_a)
	{
		$this->m_strSessionID = $strSessionID_a;
	}

	public function getPathText()
	{
		return $this->pathToText($this->getPath());
	}

	// ---- helpers ----

	private function endsWith($str_a, $strSuffix_a)
	{
		$intLength = strlen($strSuffix_a);

		return ($intLength === 0) || (substr($str_a, -$intLength) === $strSuffix_a);
	}

	private function startsWith($str_a, $strPrefix_a)
	{
		return strpos($str_a, $strPrefix_a) === 0;
	}

	private function toBoolean($str_a)
	{
		$strResult = $str_a;
		if ($strResult === null) { $strResult = 'N'; }

		$strResult = strtoupper($strResult);

		return ($strResult === 'Y' || $strResult === 'YES' || $strResult === 'T' || $strResult === 'TRUE' ||
		$strResult === '1' || $strResult === 'ON' || $strResult === 'ENABLE' || $strResult === 'ENABLED');
	}

	private function apiPrint($str_a)
	{
		$this->m_arrOutput[] = $str_a;
	}

	private function getSetting($arrSettings_a, $strKey_a)
	{
		$strResult = '';

		if (isset($arrSettings_a[$strKey_a]))
		{
			$strResult = (string)$arrSettings_a[$strKey_a];
		}

		return $strResult;
	}

	// returns true when the service exists and has an endpoint and a model
	private function loadProviderSettings($strProvider_a)
	{
		$blnResult = false;
		$arrSettings = array();

		if (isset($this->m_arrConfig['services']) && isset($this->m_arrConfig['services'][$strProvider_a]))
		{
			$arrSettings = $this->m_arrConfig['services'][$strProvider_a];
		}

		$strEndpoint = $this->getSetting($arrSettings, 'endpoint');
		$strModel = $this->getSetting($arrSettings, 'model');

		if (strlen($strProvider_a) > 0 && strlen($strEndpoint) > 0 && strlen($strModel) > 0)
		{
			$this->m_strService = $strProvider_a;
			$this->m_strProvider = $strProvider_a;
			$this->m_strAPIKey = $this->getSetting($arrSettings, 'apikey');
			$this->m_strEndpoint = $strEndpoint;
			$this->m_strFlavour = $this->getSetting($arrSettings, 'flavour');
			$this->m_strModel = $strModel;
			$this->m_strMaxTokens = $this->getSetting($arrSettings, 'maxtokens');
			$this->m_strTemperature = $this->getSetting($arrSettings, 'temperature');
			$this->m_strProxy = $this->getSetting($arrSettings, 'proxy');
			$this->m_strJSONBearer = $this->getSetting($arrSettings, 'jsonbearer');

			$this->m_blnConfigured = true;
			$this->m_objGlobals['llmfs']['service'] = $strProvider_a;
			$blnResult = true;
		}

		return $blnResult;
	}

	// ---- files (saved sessions and training files live in the data directory) ----

	private function getDataDir()
	{
		$strResult = dirname(__FILE__) . '/data';

		if (isset($this->m_arrConfig['datadir']) && strlen($this->m_arrConfig['datadir']) > 0)
		{
			$strResult = $this->m_arrConfig['datadir'];
		}

		return rtrim($strResult, '/\\');
	}

	// a filename only: no folders, nothing that can leave the data directory
	private function isSafeFilename($strFilename_a)
	{
		return (preg_match('/^[A-Za-z0-9_][A-Za-z0-9_.\-]*$/', $strFilename_a) === 1) && (strpos($strFilename_a, '..') === false);
	}

	private function saveFile($strFilename_a, $strContent_a)
	{
		$objResponse = array('error' => '', 'content' => '');
		$strDir = $this->getDataDir();

		if (!$this->isSafeFilename($strFilename_a))
		{
			$objResponse['error'] = 'Invalid filename';
		}
		else
		{
			if (!is_dir($strDir))
			{
				@mkdir($strDir, 0755, true);
			}

			if (@file_put_contents($strDir . '/' . $strFilename_a, $strContent_a) === false)
			{
				$objResponse['error'] = 'Could not write to the data directory';
			}
		}

		return $objResponse;
	}

	private function loadFile($strFilename_a)
	{
		$objResponse = array('error' => '', 'content' => '');
		$strPath = $this->getDataDir() . '/' . $strFilename_a;
		$strContent = false;

		if (!$this->isSafeFilename($strFilename_a))
		{
			$objResponse['error'] = 'Invalid filename';
		}
		else if (!is_file($strPath))
		{
			$objResponse['error'] = 'File not found';
		}
		else
		{
			$strContent = @file_get_contents($strPath);
			if ($strContent === false)
			{
				$objResponse['error'] = 'File not found';
			}
			else
			{
				$objResponse['content'] = $strContent;
			}
		}

		return $objResponse;
	}

	// ---- llmfs: path ----

	private function getPath()
	{
		return $this->m_objGlobals['llmfs']['path'];
	}

	private function pathToText($arrPath_a)
	{
		return $this->m_PATHSEPARATOR . implode($this->m_PATHSEPARATOR, $arrPath_a);
	}

	// cd <folder>, cd .., cd \, cd a\b, cd \a\b (/ works as well as \)
	private function changePath($strArgument_a)
	{
		$arrPath = $this->getPath();
		$strArgument = str_replace('/', $this->m_PATHSEPARATOR, trim($strArgument_a));
		$arrParts = explode($this->m_PATHSEPARATOR, $strArgument);
		$strPart = '';

		if ($this->startsWith($strArgument, $this->m_PATHSEPARATOR))
		{
			$arrPath = array();
		}

		for ($intI = 0; $intI < count($arrParts); $intI++)
		{
			$strPart = strtolower(trim($arrParts[$intI]));

			if ($strPart === '..')
			{
				if (count($arrPath) > 0)
				{
					array_pop($arrPath);
				}
			}
			else if (strlen($strPart) > 0 && $strPart !== '.')
			{
				$arrPath[] = $strPart;
			}
		}

		$this->m_objGlobals['llmfs']['path'] = $arrPath;

		return $arrPath;
	}

	// ---- llmfs: reading what the LLM sends back ----

	// LLMs wrap JSON in ``` fences or add chatter even when told not to: take the outermost {...} or [...]
	private function extractJSON($strText_a)
	{
		$objResult = null;
		$strText = trim((string)$strText_a);
		$intObject = strpos($strText, '{');
		$intArray = strpos($strText, '[');
		$intStart = -1;
		$intEnd = -1;

		if ($intObject !== false && ($intArray === false || $intObject < $intArray))
		{
			$intStart = $intObject;
			$intEnd = strrpos($strText, '}');
		}
		else if ($intArray !== false)
		{
			$intStart = $intArray;
			$intEnd = strrpos($strText, ']');
		}

		if ($intStart !== -1 && $intEnd !== false && $intEnd > $intStart)
		{
			$objResult = json_decode(substr($strText, $intStart, $intEnd - $intStart + 1), true);
			if (!is_array($objResult))
			{
				$objResult = null;
			}
		}

		return $objResult;
	}

	// true for a JSON array ([...]), false for a JSON object ({...})
	private function isList($arr_a)
	{
		return is_array($arr_a) && (count($arr_a) === 0 || array_keys($arr_a) === range(0, count($arr_a) - 1));
	}

	// clean a list of names from the LLM: strings only, lowercase, no duplicates, sorted
	private function cleanNames($arrNames_a, &$objSeen_a)
	{
		$arrResult = array();
		$strName = '';

		if (is_array($arrNames_a))
		{
			$arrNames = array_values($arrNames_a);

			for ($intI = 0; $intI < count($arrNames); $intI++)
			{
				if (is_string($arrNames[$intI]))
				{
					$strName = strtolower(trim($arrNames[$intI]));
					if (strlen($strName) > 0 && !isset($objSeen_a[$strName]))
					{
						$objSeen_a[$strName] = true;
						$arrResult[] = $strName;
					}
				}
			}
		}

		sort($arrResult, SORT_STRING);

		return $arrResult;
	}

	private function buildDirPrompt($arrPath_a)
	{
		$strPrompt = 'You are acting as a filesystem over our conversation (the conversation is in the context).' . "\n";

		if (count($arrPath_a) === 0)
		{
			$strPrompt .= 'Current path: ' . $this->m_PATHSEPARATOR . ' (the root)' . "\n";
			$strPrompt .= 'List the top-level topics we have discussed.' . "\n";
		}
		else
		{
			$strPrompt .= 'Current path: ' . $this->pathToText($arrPath_a) . "\n";
			$strPrompt .= 'Each part of the path is a topic drilled down from the part before it.' . "\n";
			$strPrompt .= 'List what we have discussed that sits directly under this path.' . "\n";
		}

		$strPrompt .= '- A "dir" is a category: we discussed it enough that it breaks down into further sub-topics.' . "\n";
		$strPrompt .= '- A "file" is an information dump: a single piece of information with nothing under it.' . "\n";
		$strPrompt .= 'One lowercase word per name. Direct children only, no deeper levels.' . "\n";
		$strPrompt .= 'Only things we actually discussed. Each name appears once, as a dir or a file, not both.' . "\n";
		$strPrompt .= 'If there is nothing, both lists are empty.' . "\n";
		$strPrompt .= 'Reply with JSON only, no other text: {"dirs":[],"files":[]}';

		return $strPrompt;
	}

	private function buildLoadPrompt($arrPath_a, $strName_a)
	{
		$strPrompt = 'You are acting as a filesystem over our conversation (the conversation is in the context).' . "\n";

		$strPrompt .= 'Current path: ' . $this->pathToText($arrPath_a) . "\n";
		$strPrompt .= 'Each part of the path is a topic drilled down from the part before it.' . "\n";
		$strPrompt .= 'Load "' . $strName_a . '" from this path: give all the information we discussed about it, as plain text.' . "\n";
		$strPrompt .= 'Only what we actually discussed. If we discussed nothing about it, content is an empty string.' . "\n";
		$strPrompt .= 'Reply with JSON only, no other text: {"name":"","content":""}';

		return $strPrompt;
	}

	private function printDir($arrPath_a, $arrDirs_a, $arrFiles_a)
	{
		$this->apiPrint(' Directory of ' . $this->pathToText($arrPath_a));
		$this->apiPrint(' ');

		for ($intI = 0; $intI < count($arrDirs_a); $intI++)
		{
			$this->apiPrint('<DIR>  ' . $arrDirs_a[$intI]);
		}

		for ($intI = 0; $intI < count($arrFiles_a); $intI++)
		{
			$this->apiPrint('<FILE> ' . $arrFiles_a[$intI]);
		}

		if (count($arrDirs_a) + count($arrFiles_a) > 0)
		{
			$this->apiPrint(' ');
		}

		$this->apiPrint(count($arrFiles_a) . ' File(s), ' . count($arrDirs_a) . ' Dir(s)');
	}

	// ---- sessions ----

	private function entrySize($strText_a)
	{
		return strlen($strText_a) * 2;	// same sum as the JS (2 bytes per character)
	}

	private function sessionSize($strSessionID_a)
	{
		$intTotal = 0;
		$arrSession = array_values($this->m_objGlobals['chatgpt']['sessions'][$strSessionID_a]);

		for ($intJ = 0; $intJ < count($arrSession); $intJ++)
		{
			$intTotal += $this->entrySize($arrSession[$intJ]);
		}

		return $intTotal;
	}

	private function formatSize($intSize_a)
	{
		$strSize = '';

		if ($intSize_a < 1024)
		{
			$strSize = $intSize_a . ' bytes';
		}
		else if ($intSize_a < 1024 * 1024)
		{
			$strSize = round($intSize_a / 1024) . ' KB';
		}
		else
		{
			$strSize = round($intSize_a / (1024 * 1024)) . ' MB';
		}

		return $strSize;
	}

	private function addToSession($strSessionID_a, $strText_a)
	{
		if (!isset($this->m_objGlobals['chatgpt']['sessions'][$strSessionID_a]))
		{
			$this->m_objGlobals['chatgpt']['sessions'][$strSessionID_a] = array();
		}

		$intTextSize = $this->entrySize($strText_a);
		$intMaxSizeBytes = $this->m_intMaxSessionSize * 1024 * 1024;	// Convert MB to bytes

		// Add new text
		$this->m_objGlobals['chatgpt']['sessions'][$strSessionID_a][] = $strText_a;
		$this->m_objGlobals['chatgpt']['sessionsize'] += $intTextSize;

		// Remove oldest entries if we exceed the total size limit
		$blnRemovedAny = true;
		while ($blnRemovedAny && $this->m_objGlobals['chatgpt']['sessionsize'] > $intMaxSizeBytes)
		{
			$blnRemovedAny = false;

			// Find the first session with an entry and remove its oldest
			$strOldestSessionID = null;

			foreach ($this->m_objGlobals['chatgpt']['sessions'] as $strSessionKey => $arrCurrentSession)
			{
				if ($strOldestSessionID === null && count($arrCurrentSession) > 0)
				{
					$strOldestSessionID = $strSessionKey;
				}
			}

			// Remove the oldest entry
			if ($strOldestSessionID !== null)
			{
				$strRemovedText = array_shift($this->m_objGlobals['chatgpt']['sessions'][$strOldestSessionID]);
				$this->m_objGlobals['chatgpt']['sessionsize'] -= $this->entrySize($strRemovedText);
				$blnRemovedAny = true;

				// Clean up empty sessions
				if (count($this->m_objGlobals['chatgpt']['sessions'][$strOldestSessionID]) === 0)
				{
					unset($this->m_objGlobals['chatgpt']['sessions'][$strOldestSessionID]);
				}
			}
		}
	}

	// the most recent entries of the session that fit in intMaxSize_a characters
	private function getSessionContext($strSessionID_a, $intMaxSize_a)
	{
		$strResult = '';

		if (isset($this->m_objGlobals['chatgpt']['sessions'][$strSessionID_a]))
		{
			$arrSession = array_values($this->m_objGlobals['chatgpt']['sessions'][$strSessionID_a]);
			$strContext = '';
			$blnFits = true;

			for ($intI = count($arrSession) - 1; $intI >= 0 && $blnFits; $intI--)
			{
				$strEntry = $arrSession[$intI];
				if (strlen($strContext) + strlen($strEntry) > $intMaxSize_a)
				{
					$blnFits = false;
				}
				else
				{
					$strContext = $strEntry . "\n" . $strContext;
				}
			}

			$strResult = trim($strContext);
		}

		return $strResult;
	}

	// ---- the LLM call ----

	// returns array('status' => http status, 'body' => text, 'error' => '' or what went wrong, 'timeout' => bool)
	private function httpPost($strURL_a, $arrHeaders_a, $strData_a)
	{
		$arrResult = array('status' => 0, 'body' => '', 'error' => '', 'timeout' => false);
		$arrHeaderLines = array();

		foreach ($arrHeaders_a as $strName => $strValue)
		{
			$arrHeaderLines[] = $strName . ': ' . $strValue;
		}

		if (function_exists('curl_init'))
		{
			$objCurl = curl_init($strURL_a);
			curl_setopt($objCurl, CURLOPT_POST, true);
			curl_setopt($objCurl, CURLOPT_POSTFIELDS, $strData_a);
			curl_setopt($objCurl, CURLOPT_HTTPHEADER, $arrHeaderLines);
			curl_setopt($objCurl, CURLOPT_RETURNTRANSFER, true);
			curl_setopt($objCurl, CURLOPT_TIMEOUT, $this->m_intDefaultTimeout);

			$strBody = curl_exec($objCurl);

			if ($strBody === false)
			{
				$arrResult['error'] = curl_error($objCurl);
				$arrResult['timeout'] = (curl_errno($objCurl) === 28);
			}
			else
			{
				$arrResult['body'] = $strBody;
				$arrResult['status'] = intval(curl_getinfo($objCurl, CURLINFO_HTTP_CODE));
			}

			curl_close($objCurl);
		}
		else
		{
			// no curl: PHP streams (needs allow_url_fopen, and openssl for https)
			$objContext = stream_context_create(array('http' => array(
				'method' => 'POST',
				'header' => implode("\r\n", $arrHeaderLines),
				'content' => $strData_a,
				'timeout' => $this->m_intDefaultTimeout,
				'ignore_errors' => true
			)));

			$strBody = @file_get_contents($strURL_a, false, $objContext);

			if ($strBody === false)
			{
				$arrResult['error'] = 'Request failed';
			}
			else
			{
				$arrResult['body'] = $strBody;
				$arrResult['status'] = 200;
				if (isset($http_response_header) && count($http_response_header) > 0 &&
				preg_match('/^HTTP\/\S+\s+(\d+)/', $http_response_header[0], $arrMatches) === 1)
				{
					$arrResult['status'] = intval($arrMatches[1]);
				}
			}
		}

		return $arrResult;
	}

	// returns array('error' => null or message, 'response' => null or the LLM's text)
	private function callChatGPT($strPrompt_a, $strSessionContext_a, $strTemperature_a)
	{
		$arrResult = array('error' => null, 'response' => null);

		// defaults
		$strFlavour = $this->m_strFlavour;
		if (strlen($strFlavour) === 0) { $strFlavour = 'chatgpt'; }
		$strFlavour = strtolower($strFlavour);

		$strMaxTokens = $this->m_strMaxTokens;
		if (strlen($strMaxTokens) === 0) { $strMaxTokens = '2000'; }

		$strTemperature = $strTemperature_a;
		if (strlen($strTemperature) === 0) { $strTemperature = '0.7'; }

		$strEndpoint = $this->m_strEndpoint;
		if (strlen($this->m_strProxy) > 0)
		{
			$strEndpoint = $this->m_strProxy . $strEndpoint;
		}

		$arrMessages = array();

		if (strlen($strSessionContext_a) > 0)
		{
			$arrMessages[] = array(
				'role' => 'system',
				'content' => 'Context:' . "\n" . $strSessionContext_a
			);
		}

		$arrMessages[] = array(
			'role' => 'user',
			'content' => $strPrompt_a
		);

		$objHeaders = array();
		$objPayload = array();

		if ($strFlavour === 'claude')
		{
			$strSystemMessage = '';
			$arrUserMessages = array();

			for ($intI = 0; $intI < count($arrMessages); $intI++)
			{
				if ($arrMessages[$intI]['role'] === 'system')
				{
					$strSystemMessage = $arrMessages[$intI]['content'];
				}
				else
				{
					$arrUserMessages[] = $arrMessages[$intI];
				}
			}

			$objPayload = array(
				'model' => $this->m_strModel,
				'max_tokens' => intval($strMaxTokens),
				'temperature' => floatval($strTemperature),
				'system' => $strSystemMessage,
				'messages' => $arrUserMessages
			);

			if ($this->toBoolean($this->m_strJSONBearer))
			{
				$objPayload['bearer'] = $this->m_strAPIKey;
			}

			$objHeaders = array(
				'x-api-key' => $this->m_strAPIKey,
				'Content-Type' => 'application/json',
				'anthropic-version' => '2023-06-01'
			);
		}
		else if ($strFlavour === 'gemini')
		{
			$strFullPrompt = $strPrompt_a;
			if (strlen($strSessionContext_a) > 0)
			{
				$strFullPrompt = 'Context:' . "\n" . $strSessionContext_a . "\n\n" . 'User: ' . $strPrompt_a;
			}

			$objPayload = array(
				'contents' => array(
					array(
						'role' => 'user',
						'parts' => array(
							array('text' => $strFullPrompt)
						)
					)
				),
				'generationConfig' => array(
					'maxOutputTokens' => intval($strMaxTokens),
					'temperature' => floatval($strTemperature)
				)
			);

			$objHeaders = array(
				'Content-Type' => 'application/json'
			);

			$strEndpoint = $strEndpoint . '?key=' . $this->m_strAPIKey;
		}
		else
		{
			$objPayload = array(
				'model' => $this->m_strModel,
				'max_tokens' => intval($strMaxTokens),
				'temperature' => floatval($strTemperature),
				'messages' => $arrMessages
			);

			if ($this->toBoolean($this->m_strJSONBearer))
			{
				$objPayload['bearer'] = $this->m_strAPIKey;
			}

			$objHeaders = array(
				'Content-Type' => 'application/json'
			);

			if (strlen($this->m_strAPIKey) > 0)
			{
				$objHeaders['Authorization'] = 'Bearer ' . $this->m_strAPIKey;
			}
		}

		$arrHTTP = $this->httpPost($strEndpoint, $objHeaders, json_encode($objPayload));
		$objResponse = json_decode($arrHTTP['body'], true);

		if (strlen($arrHTTP['error']) > 0 || $arrHTTP['status'] < 200 || $arrHTTP['status'] >= 300)
		{
			$strErrorMessage = $this->m_strProvider . ' API error: ';

			if (is_array($objResponse) && isset($objResponse['error']) && is_array($objResponse['error']) && isset($objResponse['error']['message']))
			{
				$strErrorMessage .= $objResponse['error']['message'];
			}
			else if ($arrHTTP['timeout'])
			{
				$strErrorMessage .= 'Request timed out';
			}
			else
			{
				$strErrorMessage .= 'error - ' . ($arrHTTP['status'] > 0 ? 'HTTP ' . $arrHTTP['status'] : $arrHTTP['error']);
			}

			$arrResult['error'] = $strErrorMessage;
		}
		else if (!is_array($objResponse))
		{
			$arrResult['error'] = 'No response from ' . $this->m_strProvider;
		}
		else if ($strFlavour === 'claude')
		{
			if (isset($objResponse['content']) && is_array($objResponse['content']) && count($objResponse['content']) > 0 &&
			isset($objResponse['content'][0]['text']))
			{
				$arrResult['response'] = $objResponse['content'][0]['text'];
			}
			else
			{
				$arrResult['error'] = 'No response from ' . $this->m_strProvider;
			}
		}
		else if ($strFlavour === 'gemini')
		{
			if (isset($objResponse['candidates']) && is_array($objResponse['candidates']) && count($objResponse['candidates']) > 0)
			{
				$objCandidate = $objResponse['candidates'][0];

				if (isset($objCandidate['content']['parts'][0]['text']))
				{
					$arrResult['response'] = $objCandidate['content']['parts'][0]['text'];
				}
				else if (isset($objCandidate['text']))
				{
					$arrResult['response'] = $objCandidate['text'];
				}
				else if (isset($objCandidate['message']['content']))
				{
					$arrResult['response'] = $objCandidate['message']['content'];
				}
				else
				{
					$arrResult['error'] = 'No response from ' . $this->m_strProvider . ' - unexpected format';
				}
			}
			else
			{
				$arrResult['error'] = 'No response from ' . $this->m_strProvider;
			}
		}
		else
		{
			if (isset($objResponse['choices']) && is_array($objResponse['choices']) && count($objResponse['choices']) > 0 &&
			isset($objResponse['choices'][0]['message']['content']))
			{
				$arrResult['response'] = $objResponse['choices'][0]['message']['content'];
			}
			else
			{
				$arrResult['error'] = 'No response from ' . $this->m_strProvider;
			}
		}

		return $arrResult;
	}

	// ---- commands ----

	private function commandClear($arrWords_a)
	{
		if (count($arrWords_a) > 1)
		{
			// Clear specific session
			$strSessionID = $arrWords_a[1];
			if (isset($this->m_objGlobals['chatgpt']['sessions'][$strSessionID]))
			{
				$this->m_objGlobals['chatgpt']['sessionsize'] -= $this->sessionSize($strSessionID);
				$this->m_objGlobals['chatgpt']['sessions'][$strSessionID] = array();
				$this->apiPrint('Session "' . $strSessionID . '" cleared.');
			}
			else
			{
				$this->apiPrint('Session "' . $strSessionID . '" not found.');
			}
		}
		else
		{
			// Clear all sessions
			foreach ($this->m_objGlobals['chatgpt']['sessions'] as $strSessionKey => $arrCurrentSession)
			{
				$this->m_objGlobals['chatgpt']['sessions'][$strSessionKey] = array();
			}
			$this->m_objGlobals['chatgpt']['sessionsize'] = 0;
			$this->apiPrint('All sessions cleared.');
		}
	}

	private function commandSave($arrWords_a)
	{
		$objResponse = null;

		if (count($arrWords_a) > 1)
		{
			// Save specific session
			$strSessionID = $arrWords_a[1];
			if (isset($this->m_objGlobals['chatgpt']['sessions'][$strSessionID]))
			{
				$objSessionData = array(
					'session' => array_values($this->m_objGlobals['chatgpt']['sessions'][$strSessionID]),
					'size' => $this->sessionSize($strSessionID)
				);

				$objResponse = $this->saveFile('chatgpt-session-' . $strSessionID . '.json', json_encode($objSessionData));

				if (strlen($objResponse['error']) === 0)
				{
					$this->apiPrint('Session "' . $strSessionID . '" saved.');
				}
				else
				{
					$this->apiPrint('Failed to save session "' . $strSessionID . '": ' . $objResponse['error']);
				}
			}
			else
			{
				$this->apiPrint('Session "' . $strSessionID . '" not found.');
			}
		}
		else
		{
			// Save all sessions
			$objAllSessions = array(
				'sessions' => (object)$this->m_objGlobals['chatgpt']['sessions'],
				'totalSize' => $this->m_objGlobals['chatgpt']['sessionsize']
			);

			$objResponse = $this->saveFile('chatgpt-all-sessions.json', json_encode($objAllSessions));

			if (strlen($objResponse['error']) === 0)
			{
				$this->apiPrint('All sessions saved.');
			}
			else
			{
				$this->apiPrint('Failed to save sessions: ' . $objResponse['error']);
			}
		}
	}

	private function commandLoadSession($arrWords_a)
	{
		$objData = null;
		$objResponse = null;

		if (count($arrWords_a) > 1)
		{
			// Load specific session
			$strSessionID = $arrWords_a[1];
			$objResponse = $this->loadFile('chatgpt-session-' . $strSessionID . '.json');

			if (strlen($objResponse['error']) === 0 && strlen($objResponse['content']) > 0)
			{
				$objData = json_decode($objResponse['content'], true);

				if (is_array($objData) && isset($objData['session']) && is_array($objData['session']))
				{
					// Remove current session size if it exists
					if (isset($this->m_objGlobals['chatgpt']['sessions'][$strSessionID]))
					{
						$this->m_objGlobals['chatgpt']['sessionsize'] -= $this->sessionSize($strSessionID);
					}

					// Load new session
					$this->m_objGlobals['chatgpt']['sessions'][$strSessionID] = array_values($objData['session']);
					$this->m_objGlobals['chatgpt']['sessionsize'] += $this->sessionSize($strSessionID);
					$this->apiPrint('Session "' . $strSessionID . '" loaded.');
				}
				else
				{
					$this->apiPrint('Session "' . $strSessionID . '" file is corrupted.');
				}
			}
			else
			{
				$this->apiPrint('Session "' . $strSessionID . '" not found.');
			}
		}
		else
		{
			// Load all sessions (replace current sessions)
			$objResponse = $this->loadFile('chatgpt-all-sessions.json');

			if (strlen($objResponse['error']) === 0 && strlen($objResponse['content']) > 0)
			{
				$objData = json_decode($objResponse['content'], true);

				if (is_array($objData) && isset($objData['sessions']) && is_array($objData['sessions']))
				{
					$this->m_objGlobals['chatgpt']['sessions'] = array();
					$this->m_objGlobals['chatgpt']['sessionsize'] = 0;

					foreach ($objData['sessions'] as $strSessionKey => $arrCurrentSession)
					{
						if (is_array($arrCurrentSession))
						{
							$this->m_objGlobals['chatgpt']['sessions'][$strSessionKey] = array_values($arrCurrentSession);
							$this->m_objGlobals['chatgpt']['sessionsize'] += $this->sessionSize($strSessionKey);
						}
					}

					$this->apiPrint('All sessions loaded.');
				}
				else
				{
					$this->apiPrint('Session file is corrupted.');
				}
			}
			else
			{
				$this->apiPrint('No saved sessions found.');
			}
		}
	}

	private function commandTrain($arrWords_a)
	{
		$strSessionID = '';
		$strTrainingSources = '';

		if (count($arrWords_a) >= 2)
		{
			// Train specific session with file content or other sessions
			// Handle case where session ID is omitted (use 'default')
			if (count($arrWords_a) === 2)
			{
				$strSessionID = 'default';
				$strTrainingSources = $arrWords_a[1];
			}
			else
			{
				// Session ID provided - strip colon if present
				$strSessionID = $arrWords_a[1];
				if ($this->endsWith($strSessionID, ':'))
				{
					$strSessionID = substr($strSessionID, 0, strlen($strSessionID) - 1);
				}
				$strTrainingSources = $arrWords_a[2];
			}

			// Parse training sources: files (.ext) and sessions (:)
			$arrSources = explode(',', $strTrainingSources);
			$intTodo = 0;

			for ($intI = 0; $intI < count($arrSources); $intI++)
			{
				$strSource = trim($arrSources[$intI]);

				if (strlen($strSource) > 0)
				{
					$intTodo++;

					if ($this->endsWith($strSource, ':'))
					{
						// Session reference - copy from another session
						$strSourceSession = substr($strSource, 0, strlen($strSource) - 1);

						if (isset($this->m_objGlobals['chatgpt']['sessions'][$strSourceSession]))
						{
							// Append all entries from source session
							$arrSourceEntries = array_values($this->m_objGlobals['chatgpt']['sessions'][$strSourceSession]);
							for ($intJ = 0; $intJ < count($arrSourceEntries); $intJ++)
							{
								$this->addToSession($strSessionID, $arrSourceEntries[$intJ]);
							}
							$this->apiPrint('Session "' . $strSessionID . '" trained with session "' . $strSourceSession . '"');
						}
						else
						{
							$this->apiPrint('Source session "' . $strSourceSession . '" not found');
						}
					}
					else
					{
						// File reference - load and train
						$objResponse = $this->loadFile($strSource);

						if (strlen($objResponse['error']) === 0 && strlen($objResponse['content']) > 0)
						{
							// Add file content to session
							$this->addToSession($strSessionID, $objResponse['content']);
							$this->apiPrint('Session "' . $strSessionID . '" trained with ' . $strSource);
						}
						else
						{
							$this->apiPrint('Failed to load training file "' . $strSource . '": ' .
								(strlen($objResponse['error']) > 0 ? $objResponse['error'] : 'File not found'));
						}
					}
				}
			}

			if ($intTodo === 0)
			{
				$this->apiPrint('No valid training sources specified');
			}
		}
		else
		{
			$this->apiPrint('Usage: llmfs train <sessionid> <filename>');
		}
	}

	private function commandService($arrWords_a)
	{
		if (count($arrWords_a) > 1)
		{
			// Switch to specified service
			$strNewService = $arrWords_a[1];

			$this->apiPrint('Switching to service: ' . $strNewService);

			if ($this->loadProviderSettings($strNewService))
			{
				$this->apiPrint('Service switched to: ' . $strNewService);
				$this->apiPrint('Provider: ' . $this->m_strProvider);
				$this->apiPrint('Endpoint: ' . $this->m_strEndpoint);
				$this->apiPrint('Model: ' . $this->m_strModel);
				$this->apiPrint('Flavour: ' . (strlen($this->m_strFlavour) > 0 ? $this->m_strFlavour : 'chatgpt'));
			}
			else
			{
				$this->apiPrint("The provider '" . $strNewService . "' is not configured.");
			}
		}
		else
		{
			// Show current service
			$this->apiPrint('Current service: ' . (strlen($this->m_strService) > 0 ? $this->m_strService : 'unknown'));
			$this->apiPrint('Provider: ' . $this->m_strProvider);
			$this->apiPrint('Endpoint: ' . $this->m_strEndpoint);
			$this->apiPrint('Model: ' . $this->m_strModel);
			$this->apiPrint('Flavour: ' . (strlen($this->m_strFlavour) > 0 ? $this->m_strFlavour : 'chatgpt'));
			$this->apiPrint('Max Tokens: ' . (strlen($this->m_strMaxTokens) > 0 ? $this->m_strMaxTokens : '2000'));
			$this->apiPrint('Temperature: ' . (strlen($this->m_strTemperature) > 0 ? $this->m_strTemperature : '0.7'));
			$this->apiPrint('Proxy: ' . (strlen($this->m_strProxy) > 0 ? $this->m_strProxy : 'none'));
			$this->apiPrint('JSON Bearer: ' . (strlen($this->m_strJSONBearer) > 0 ? $this->m_strJSONBearer : 'N'));
		}
	}

	private function commandServices()
	{
		$this->apiPrint('Loading configured services...');

		$arrServices = array();

		// every service in the config file that has an endpoint
		if (isset($this->m_arrConfig['services']) && is_array($this->m_arrConfig['services']))
		{
			foreach ($this->m_arrConfig['services'] as $strServiceName => $arrSettings)
			{
				if (strlen($this->getSetting($arrSettings, 'endpoint')) > 0)
				{
					$arrServices[] = $strServiceName;
				}
			}
		}

		if (count($arrServices) > 0)
		{
			sort($arrServices, SORT_STRING);
			$this->apiPrint('Configured services: ' . implode(', ', $arrServices));
		}
		else
		{
			$this->apiPrint('No services configured. Add services to llmfs-config.php.');
		}
	}

	private function commandSessions()
	{
		$arrSessionList = array();

		foreach ($this->m_objGlobals['chatgpt']['sessions'] as $strSessionKey => $arrCurrentSession)
		{
			if (count($arrCurrentSession) > 0)
			{
				$arrSessionList[] = $strSessionKey . ' (' . count($arrCurrentSession) . ' entries, ' . $this->formatSize($this->sessionSize($strSessionKey)) . ')';
			}
		}

		if (count($arrSessionList) > 0)
		{
			$this->apiPrint('Active sessions: ' . implode(', ', $arrSessionList));
			$this->apiPrint('Total memory usage: ' . $this->formatSize($this->m_objGlobals['chatgpt']['sessionsize']) . ' of ' . $this->m_intMaxSessionSize . ' MB');
		}
		else
		{
			$this->apiPrint('No active sessions.');
		}
	}

	private function commandDir($strSessionID_a)
	{
		// asks the LLM for the listing as JSON; nothing is added to the session
		$arrFSPath = $this->getPath();
		$strFSPrompt = $this->buildDirPrompt($arrFSPath);
		$strFSContext = $this->getSessionContext($strSessionID_a, $this->m_intMaxPromptSize - strlen($strFSPrompt));

		if (strlen($strFSContext) === 0)
		{
			// nothing discussed yet: no need to ask
			$this->printDir($arrFSPath, array(), array());
		}
		else if (!$this->m_blnConfigured)
		{
			$this->apiPrint("The provider '" . $this->m_strService . "' is not configured.");
		}
		else
		{
			$arrCall = $this->callChatGPT($strFSPrompt, $strFSContext, $this->m_FSTEMPERATURE);

			if ($arrCall['error'] !== null)
			{
				$this->apiPrint('Error: ' . $arrCall['error']);
			}
			else
			{
				$objData = $this->extractJSON($arrCall['response']);
				$objSeen = array();

				if ($objData !== null && !isset($objData['dirs']) && !isset($objData['files']) && $this->isList($objData))
				{
					// a bare array: treat as all dirs
					$objData = array('dirs' => $objData, 'files' => array());
				}

				if ($objData !== null && ((isset($objData['dirs']) && is_array($objData['dirs'])) || (isset($objData['files']) && is_array($objData['files']))))
				{
					$arrDirs = $this->cleanNames(isset($objData['dirs']) ? $objData['dirs'] : array(), $objSeen);
					$arrFiles = $this->cleanNames(isset($objData['files']) ? $objData['files'] : array(), $objSeen);
					$this->printDir($arrFSPath, $arrDirs, $arrFiles);
				}
				else
				{
					$this->apiPrint('Error: the directory listing could not be read.');
				}
			}
		}
	}

	private function commandCd($strFSArgument_a)
	{
		// no LLM call: the path is kept for the next dir, load or question
		$arrFSPath = $this->getPath();

		if (strlen($strFSArgument_a) > 0)
		{
			$arrFSPath = $this->changePath($strFSArgument_a);
		}

		$this->apiPrint($this->pathToText($arrFSPath));
	}

	private function commandLoad($strSessionID_a, $strFSArgument_a)
	{
		$arrFSPath = $this->getPath();
		$strFSArgument = strtolower($strFSArgument_a);

		if (strlen($strFSArgument) === 0)
		{
			$this->apiPrint('Usage: llmfs load <name>');
		}
		else
		{
			// asks the LLM for the content as JSON; nothing is added to the session
			$strFSPrompt = $this->buildLoadPrompt($arrFSPath, $strFSArgument);
			$strFSContext = $this->getSessionContext($strSessionID_a, $this->m_intMaxPromptSize - strlen($strFSPrompt));

			if (strlen($strFSContext) === 0)
			{
				$this->apiPrint('File not found: ' . $strFSArgument);
			}
			else if (!$this->m_blnConfigured)
			{
				$this->apiPrint("The provider '" . $this->m_strService . "' is not configured.");
			}
			else
			{
				$arrCall = $this->callChatGPT($strFSPrompt, $strFSContext, $this->m_FSTEMPERATURE);

				if ($arrCall['error'] !== null)
				{
					$this->apiPrint('Error: ' . $arrCall['error']);
				}
				else
				{
					$objData = $this->extractJSON($arrCall['response']);

					if ($objData !== null && isset($objData['content']) && is_string($objData['content']))
					{
						if (strlen(trim($objData['content'])) > 0)
						{
							$this->apiPrint($objData['content']);
						}
						else
						{
							$this->apiPrint('File not found: ' . $strFSArgument);
						}
					}
					else
					{
						$this->apiPrint('Error: the file could not be read.');
					}
				}
			}
		}
	}

	private function commandPrompt($strSessionID_a, $strActualPrompt_a)
	{
		// a normal question: the current path goes with it, so the LLM knows what it is about
		// and so the session records where in the tree it was asked
		$arrFSPath = $this->getPath();
		$strPathPrompt = $strActualPrompt_a;
		$strPathEntry = $strActualPrompt_a;

		if (count($arrFSPath) > 0)
		{
			$strPathPrompt = 'Current topic path: ' . $this->pathToText($arrFSPath) . "\n\n" . $strActualPrompt_a;
			$strPathEntry = '[' . $this->pathToText($arrFSPath) . '] ' . $strActualPrompt_a;
		}

		if (!$this->m_blnConfigured)
		{
			$this->apiPrint("The provider '" . $this->m_strService . "' is not configured.");
		}
		else
		{
			// Calculate available space for session context
			$intAvailableSpace = $this->m_intMaxPromptSize - strlen($strPathPrompt);

			// Get session context if available
			$strSessionContext = '';
			if (strlen($strSessionID_a) > 0 && $strSessionID_a !== 'null' && $intAvailableSpace > 0)
			{
				$strSessionContext = $this->getSessionContext($strSessionID_a, $intAvailableSpace);
			}

			$arrCall = $this->callChatGPT($strPathPrompt, $strSessionContext, $this->m_strTemperature);

			if ($arrCall['error'] !== null)
			{
				$this->apiPrint('Error: ' . $arrCall['error']);
			}
			else
			{
				$this->apiPrint($arrCall['response']);

				// Add the prompt and response to the session for future context
				if (strlen($strSessionID_a) > 0 && $strSessionID_a !== 'null')
				{
					$this->addToSession($strSessionID_a, 'USER: ' . $strPathEntry);
					$this->addToSession($strSessionID_a, 'ASSISTANT: ' . $arrCall['response']);
				}
			}
		}
	}

	private function printUsage()
	{
		$this->apiPrint('Usage: [<sessionid>:] <prompt>');
		$this->apiPrint('       [<sessionid>:] dir');
		$this->apiPrint('       cd <folder>');
		$this->apiPrint('       [<sessionid>:] load <name>');
		$this->apiPrint('       clear <sessionid>');
		$this->apiPrint('       sessions');
		$this->apiPrint('       save <sessionid>');
		$this->apiPrint('       loadsession <sessionid>');
		$this->apiPrint('       services');
		$this->apiPrint('       service <services>');
		$this->apiPrint('       train [<sessionid>:] <filenames-sessions>');
		$this->apiPrint(' ');
		$this->apiPrint('Examples:');
		$this->apiPrint('  what do bears eat?');
		$this->apiPrint('  dir');
		$this->apiPrint('  cd animals');
		$this->apiPrint('  cd bears\\food');
		$this->apiPrint('  cd ..');
		$this->apiPrint('  cd \\');
		$this->apiPrint('  load diet');
		$this->apiPrint('  math: dir');
		$this->apiPrint('  clear math');
		$this->apiPrint('  sessions');
		$this->apiPrint('  save');
		$this->apiPrint('  save math');
		$this->apiPrint('  loadsession');
		$this->apiPrint('  loadsession math');
		$this->apiPrint('  train animals: animals.txt');
		$this->apiPrint(' ');
		$this->apiPrint("  Type 'x' to exit");
	}

	// ---- the one entry point: a line in, the lines to show back ----

	public function invokeChatGPT($strPrompt_a)
	{
		$this->m_arrOutput = array();

		// Parse command for session management
		$arrWords = explode(' ', trim($strPrompt_a));
		$strCommand = strtolower($arrWords[0]);

		// Handle session commands
		if ($strCommand === 'clear')
		{
			$this->commandClear($arrWords);
		}
		else if ($strCommand === 'save')
		{
			$this->commandSave($arrWords);
		}
		else if ($strCommand === 'loadsession')	// 'load' is the llmfs file load
		{
			$this->commandLoadSession($arrWords);
		}
		else if ($strCommand === 'train')
		{
			$this->commandTrain($arrWords);
		}
		else if ($strCommand === 'service')
		{
			$this->commandService($arrWords);
		}
		else if ($strCommand === 'services')
		{
			$this->commandServices();
		}
		else if ($strCommand === 'sessions')
		{
			$this->commandSessions();
		}
		else
		{
			// Parse session ID from command
			$strSessionID = 'default';
			if (strlen($this->m_strSessionID) > 0)
			{
				$strSessionID = $this->m_strSessionID;
			}
			$strActualPrompt = $strPrompt_a;

			if (strlen($strCommand) > 1 && $this->endsWith($strCommand, ':'))
			{
				$strSessionID = substr($strCommand, 0, strlen($strCommand) - 1);
				$strActualPrompt = implode(' ', array_slice($arrWords, 1));
			}

			// llmfs: dir, cd and load are intercepted here (after the session id, so 'math: dir' works)
			$arrFSWords = explode(' ', trim($strActualPrompt));
			$strFSCommand = strtolower($arrFSWords[0]);
			$strFSArgument = trim(implode(' ', array_slice($arrFSWords, 1)));

			if ($strFSCommand === 'dir')
			{
				$this->commandDir($strSessionID);
			}
			else if ($strFSCommand === 'cd')
			{
				$this->commandCd($strFSArgument);
			}
			else if ($strFSCommand === 'load')
			{
				$this->commandLoad($strSessionID, $strFSArgument);
			}
			else if (strlen(trim($strActualPrompt)) > 0)
			{
				$this->commandPrompt($strSessionID, $strActualPrompt);
			}
			else
			{
				$this->printUsage();
			}
		}

		return $this->m_arrOutput;
	}
}
