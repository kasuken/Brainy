[CmdletBinding()]
param(
    [string] $BaseUrl = 'https://brainy-prod-002.azurewebsites.net',
    [string] $ResourceGroup = 'Brainy.Prod',
    [string] $WebAppName = 'brainy-prod-002',
    [string] $SqlServerName = 'brainy-prod-001-server'
)

$ErrorActionPreference = 'Stop'
$baseUri = [Uri]$BaseUrl
if ($baseUri.Scheme -ne 'https') {
    throw 'BaseUrl must use HTTPS.'
}

function Invoke-Head([string] $Uri) {
    Invoke-WebRequest -Uri $Uri -Method Head -MaximumRedirection 0 -SkipHttpErrorCheck
}

$httpLogin = "http://$($baseUri.Authority)/Account/Login"
$redirect = Invoke-Head $httpLogin
if ($redirect.StatusCode -notin 301, 308 -or $redirect.Headers.Location -notlike 'https://*') {
    throw "HTTP login is not forced to HTTPS. Status=$($redirect.StatusCode), Location=$($redirect.Headers.Location)"
}

$login = Invoke-Head "$BaseUrl/Account/Login"
if ($login.StatusCode -ne 200) {
    throw "HTTPS login returned $($login.StatusCode)."
}

foreach ($header in 'Strict-Transport-Security', 'Content-Security-Policy', 'X-Content-Type-Options', 'Referrer-Policy') {
    if (-not $login.Headers.ContainsKey($header)) {
        throw "HTTPS login is missing $header."
    }
}

foreach ($path in '/health/live', '/health/ready') {
    $health = Invoke-WebRequest -Uri "$BaseUrl$path" -SkipHttpErrorCheck
    if ($health.StatusCode -ne 200) {
        throw "$path returned $($health.StatusCode)."
    }
}

if (Get-Command az -ErrorAction SilentlyContinue) {
    $webApp = az webapp show --name $WebAppName --resource-group $ResourceGroup -o json | ConvertFrom-Json
    if (-not $webApp.httpsOnly) { throw 'Azure App Service HTTPS Only is disabled.' }
    if ($webApp.identity.type -notmatch 'SystemAssigned') { throw 'The App Service managed identity is missing.' }

    $webConfig = az webapp config show --name $WebAppName --resource-group $ResourceGroup -o json | ConvertFrom-Json
    if (-not $webConfig.webSocketsEnabled) { throw 'WebSockets are disabled.' }
    if (-not $webConfig.http20Enabled) { throw 'HTTP/2 is disabled.' }

    $sql = az sql server show --name $SqlServerName --resource-group $ResourceGroup -o json | ConvertFrom-Json
    if ($sql.publicNetworkAccess -ne 'Disabled') { throw 'SQL public network access is enabled.' }
}

Write-Host 'Brainy production readiness checks passed.' -ForegroundColor Green
