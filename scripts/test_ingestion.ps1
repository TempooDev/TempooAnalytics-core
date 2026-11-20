# Script to test event ingestion in Tempoo Analytics
# Simulates different kinds of traffic: human, AI bot and crawlers.

$baseUrl = "http://localhost:8081"
$siteKey = "tempoo-local"
$writeKey = "wk_dev_local_123456"

function Send-Event {
    param (
        [string]$Name,
        [string]$UserAgent,
        [string]$Type = "pageview",
        [Object]$Data = $null
    )
    
    Write-Host "---"
    Write-Host "Enviando evento: $Name" -ForegroundColor Cyan
    Write-Host "User-Agent: $UserAgent" -ForegroundColor Gray
    
    $body = @{
        siteId = $siteKey
        type = $Type
        url = "https://example.com/demo-page"
        title = "Página de Prueba Ingesta"
        referrer = "https://github.com"
        visitorId = [guid]::NewGuid().ToString()
        sessionId = [guid]::NewGuid().ToString()
        userAgent = $UserAgent
    }
    
    if ($Data -ne $null) {
        $body["data"] = $Data
        $body["eventName"] = "test_interaction"
    }
    
    $jsonBody = $body | ConvertTo-Json
    
    $headers = @{
        "Authorization" = "Bearer $writeKey"
        "Content-Type" = "application/json"
    }
    
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/analytics/events" -Method Post -Body $jsonBody -Headers $headers
        Write-Host "✅ Éxito: Evento procesado" -ForegroundColor Green
    } catch {
        Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.Exception.Response) {
            $stream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            Write-Host "Respuesta del servidor: $($reader.ReadToEnd())" -ForegroundColor Red
        }
    }
}

Write-Host "🚀 Iniciando test de ingesta..." -ForegroundColor Yellow

# 1. Human traffic (Chrome on Windows)
Send-Event -Name "Humano (Chrome)" -UserAgent "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36"

# 2. AI Bot (ChatGPT)
Send-Event -Name "IA (ChatGPT)" -UserAgent "Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko); compatible; ChatGPT-User/1.0; +https://openai.com/bot"

# 3. AI Bot (Claude)
Send-Event -Name "IA (Claude)" -UserAgent "Mozilla/5.0 (compatible; ClaudeBot/1.0; +http://www.anthropic.com/claudebot)"

# 4. Crawler (Googlebot)
Send-Event -Name "Crawler (Googlebot)" -UserAgent "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)"

# 5. Crawler (Bingbot)
Send-Event -Name "Crawler (Bingbot)" -UserAgent "Mozilla/5.0 (compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm)"

# 6. Custom event (human)
Send-Event -Name "Evento Custom" -UserAgent "Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Mobile/15E148 Safari/604.1" -Type "custom" -Data @{ "action" = "click_buy"; "tier" = "pro" }

Write-Host "`n✨ Test finalizado. Revisa el dashboard para ver los resultados." -ForegroundColor Yellow
