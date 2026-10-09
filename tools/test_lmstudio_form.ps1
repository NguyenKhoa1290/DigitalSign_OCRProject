$ErrorActionPreference = 'Stop'
$imagePath = Resolve-Path "$PSScriptRoot\..\tests\artifacts\ke-hoach-thuc-te.jpg"
$imageBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($imagePath))
$payload = @{
  model = 'gemma4-12b-qat-uncensored-hauhaucs-balanced'
  temperature = 0
  max_tokens = 700
  reasoning_effort = 'none'
  messages = @(
    @{ role = 'system'; content = 'You extract photographed Vietnamese document forms. Return compact valid JSON only. Preserve all visible Vietnamese text. Never invent content in blank cells. Include: form_title, term, student_name, class_name, major, project_title, advisor, timeframe, table_headers, blank_body_rows, date_place, student_signer, advisor_signer, notes.' },
    @{ role = 'user'; content = @(
      @{ type = 'text'; text = 'Export this photographed form into the requested JSON.' },
      @{ type = 'image_url'; image_url = @{ url = "data:image/jpeg;base64,$imageBase64" } }
    ) }
  )
} | ConvertTo-Json -Depth 10
$response = Invoke-RestMethod -Uri 'http://127.0.0.1:1234/v1/chat/completions' -Method Post -ContentType 'application/json' -Body $payload -TimeoutSec 180
$response | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath "$PSScriptRoot\lmstudio_form_result.json" -Encoding UTF8
