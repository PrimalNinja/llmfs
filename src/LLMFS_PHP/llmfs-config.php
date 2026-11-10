<?php
// LLMFS (LLM FileSystem) - configuration
//
// 'provider'  the service used on startup (the JS version reads this from ai-provider)
// 'datadir'   where saved sessions and training files live (must be writable by PHP)
// 'services'  one entry per LLM service, same settings as the CyborgShell csconfig:
//               endpoint, model   required - a service without both is 'not configured'
//               apikey            the key for the service ('' for a local service such as ollama)
//               flavour           chatgpt (default, any OpenAI-compatible endpoint), claude or gemini
//               maxtokens         default 2000
//               temperature       default 0.7
//               proxy             optional URL prefix put in front of the endpoint
//               jsonbearer        Y to also send the key inside the JSON payload as 'bearer'
//
// The endpoints and models below are copied from the CyborgShell csconfig.json - change them to suit.

return array(
	'provider' => 'openai',
	'datadir' => dirname(__FILE__) . '/data',
	'maxsessionsize' => 256,	// megabytes, all sessions together (PHP's memory_limit has to allow it)
	'maxpromptsize' => 100000,	// characters of session sent to the LLM as context
	'timeout' => 120,			// seconds

	'services' => array(
		'openai' => array(
			'apikey' => '',
			'endpoint' => 'https://api.openai.com/v1/chat/completions',
			'flavour' => 'chatgpt',
			'maxtokens' => '2000',
			'model' => 'gpt-4o-mini',
			'temperature' => '0.7',
			'proxy' => '',
			'jsonbearer' => 'N'
		),
		'claude' => array(
			'apikey' => '',
			'endpoint' => 'https://api.anthropic.com/v1/messages',
			'flavour' => 'claude',
			'maxtokens' => '2000',
			'model' => 'claude-3-5-sonnet-20241022',
			'temperature' => '0.7',
			'proxy' => '',
			'jsonbearer' => 'N'
		),
		'gemini' => array(
			'apikey' => '',
			'endpoint' => 'https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent',
			'flavour' => 'gemini',
			'maxtokens' => '1000',
			'model' => 'gemini-1.5-flash',
			'temperature' => '0.9',
			'proxy' => '',
			'jsonbearer' => 'N'
		),
		'ollama' => array(
			'apikey' => '',
			'endpoint' => 'http://localhost:11434/v1/chat/completions',
			'flavour' => 'chatgpt',
			'maxtokens' => '2000',
			'model' => 'llama3.2:latest',
			'temperature' => '0.7',
			'proxy' => '',
			'jsonbearer' => 'N'
		)
	)
);
