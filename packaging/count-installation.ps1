$ErrorActionPreference = 'Stop'

# Only the setup requests this image. GitHub Pages reads the JSON statistics API instead.
$uri = 'https://hits.sh/leandrocannizzaro.github.io/liveclaude/installations.svg'
try {
    $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -TimeoutSec 5 -MaximumRedirection 0 `
        -UserAgent 'LiveClaude-Setup' -Headers @{ 'Cache-Control' = 'no-cache' }
    if ($response.Headers['Content-Type'] -notlike 'image/svg+xml*') {
        throw [IO.InvalidDataException]::new('Install counter did not return an SVG image.')
    }
}
catch [Net.WebException], [InvalidOperationException], [IO.InvalidDataException] {
    Write-Warning "Install counter unavailable; setup still succeeded: $($_.Exception.Message)"
}
