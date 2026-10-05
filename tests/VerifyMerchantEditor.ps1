param([int]$Port = 5079)
$ErrorActionPreference = 'Stop'
$root = Join-Path 'artifacts/merchant-editor-checks' ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root -Force | Out-Null
Copy-Item BaldursGateStyleOblivion/*.json -Destination $root
$config = Join-Path $root 'merchants.json'
$original = [IO.File]::ReadAllText([IO.Path]::GetFullPath($config))
$actorConfig = [IO.Path]::GetFullPath((Join-Path $root 'actor-classification.json'))
$process = Start-Process -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @('tools/ActorResearch/bin/Debug/net10.0/ActorResearch.dll', 'edit', '--no-open', '--port', $Port, '--config', $actorConfig) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'editor.log') -RedirectStandardError (Join-Path $root 'error.log')
try {
    $url = "http://127.0.0.1:$Port"
    for ($i=0; $i -lt 40; $i++) {
        try { $page = Invoke-WebRequest "$url/merchants"; break } catch { Start-Sleep -Milliseconds 250 }
    }
    if (-not $page -or $page.Content -notmatch 'const token=("[A-F0-9]+")') { throw 'Merchant page/token missing.' }
    $token = $Matches[1] | ConvertFrom-Json
    if ($page.Content -notmatch 'aria-current="page">Merchants') { throw 'Shared navigation does not select merchant page.' }
    $headers = @{ Origin = $url; 'X-Actor-Editor-Token' = $token }
    function Post($route, $body) { Invoke-RestMethod "$url/api/merchants/$route" -Method Post -ContentType 'application/json' -Headers $headers -Body ($body | ConvertTo-Json -Depth 10) }
    $data = Invoke-RestMethod "$url/api/merchants"
    if ($data.Records.Count -ne 266) { throw 'Merchant report loading failed.' }
    $actor = $data.Records | Where-Object Name -eq 'Seed-Neeus' | Select-Object -First 1
    $saved = Post record @{ FormKey = $actor.FormKey; Profile = 'Fence'; PreferredMaterial = 'Orcish'; Preserve = $false }
    if ($saved.Settings.Overrides.($actor.FormKey).Profile -ne 'Fence' -or $saved.Settings.Overrides.($actor.FormKey).PreferredMaterial -ne 'Orcish') { throw 'Direct override persistence failed.' }
    $deleted = Post record @{ FormKey = $actor.FormKey; Delete = $true; Preserve = $false }
    if ($deleted.Settings.Overrides.PSObject.Properties[$actor.FormKey]) { throw 'Override deletion failed.' }
    $archer = $data.Records | Where-Object Name -eq 'Daenlin' | Select-Object -First 1
    $savedOffer = Post record @{ FormKey = $archer.FormKey; Profile = 'Expert Specialist'; Preserve = $false }
    if ($savedOffer.Settings.Overrides.($archer.FormKey).GlassItems -notcontains '035E7A:Oblivion.esm') { throw 'Saving an assignment lost its curated Glass offer.' }
    $before = Get-FileHash $config
    try { Post record @{ FormKey = $actor.FormKey; Profile = 'Missing profile'; Preserve = $false } | Out-Null; throw 'Invalid input accepted.' }
    catch { if ($_.Exception.Response.StatusCode.value__ -ne 400) { throw } }
    if ((Get-FileHash $config).Hash -ne $before.Hash) { throw 'Invalid input modified config.' }
    try { Post config @{ Json = $original; Revision = $data.Revision } | Out-Null; throw 'Stale revision accepted.' }
    catch { if ($_.Exception.Response.StatusCode.value__ -ne 409) { throw } }
    $current = Invoke-RestMethod "$url/api/merchants"
    $restored = Post config @{ Json = $original; Revision = $current.Revision }
    if ($restored.Settings.Overrides.($actor.FormKey).Profile -ne 'General Store') { throw 'Full configuration save failed.' }
    Write-Output 'Merchant editor persistence, deletion, invalid input, navigation, and stale revision checks passed.'
}
finally { if (-not $process.HasExited) { Stop-Process -Id $process.Id } }
