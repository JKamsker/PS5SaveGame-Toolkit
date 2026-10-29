function Assert-ReleaseVersionConfiguration {
    $apiPath = Join-Path $releaseRoot 'src/SaveToolkit.Api/SaveToolkit.Api.csproj'
    $api = [xml](Get-Content -LiteralPath $apiPath -Raw)
    $prefix = @($api.SelectNodes('/Project/PropertyGroup/MinVerTagPrefix'))
    $reference = @($api.SelectNodes('/Project/ItemGroup/PackageReference[@Include="MinVer"]'))
    $packages = [xml](Get-Content -LiteralPath (Join-Path $releaseRoot 'Directory.Packages.props') -Raw)
    $central = @($packages.SelectNodes('/Project/ItemGroup/PackageVersion[@Include="MinVer"]'))
    if ($prefix.Count -ne 1 -or $prefix[0].InnerText -cne 'v' -or $reference.Count -ne 1 -or $central.Count -ne 1 -or
        [string]::IsNullOrWhiteSpace($central[0].GetAttribute('Version'))) {
        throw 'Application version configuration must use the centrally pinned MinVer package with MinVerTagPrefix v.'
    }
    # There is deliberately no application version file: the prospective v<Version> tag supplies
    # the exact version. npm metadata and plugin PluginInfo versions are independent.
    $overrides = @('Version', 'VersionPrefix', 'VersionSuffix', 'AssemblyVersion', 'FileVersion',
        'InformationalVersion', 'MinVerVersionOverride', 'MinVerSkip')
    foreach ($path in @($apiPath, (Join-Path $releaseRoot 'Directory.Build.props'), (Join-Path $releaseRoot 'Directory.Build.targets'))) {
        if (!(Test-Path -LiteralPath $path)) { continue }
        $project = [xml](Get-Content -LiteralPath $path -Raw)
        foreach ($property in $project.SelectNodes('/Project/PropertyGroup/*')) {
            if ($property.LocalName -in $overrides -or
                ($property.LocalName -eq 'MinVerTagPrefix' -and $property.InnerText -cne 'v')) {
                throw "Application version override $($property.LocalName) in $path conflicts with tag-derived MinVer releases."
            }
        }
    }
}

function Assert-CuratedReleaseNotes([string]$Version) {
    $path = Get-ReleaseNotesPath $Version
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Curated release notes are missing: $path" }
    $notes = Get-Content -LiteralPath $path -Raw
    if ([string]::IsNullOrWhiteSpace($notes) -or $notes -cnotmatch "(?m)^# v$([regex]::Escape($Version)) highlights\r?$") {
        throw "Curated release notes must be nonempty and titled '# v$Version highlights': $path"
    }
    if ($notes -match '(?i)\b(DRAFT|TODO|TBD|FIXME|PLACEHOLDER|XXX)\b|<!--|<[^>\r\n]*placeholder[^>\r\n]*>') {
        throw "Curated release notes still contain placeholder markers: $path"
    }
}

function Assert-SdkRequestCounts {
    $path = Join-Path $releaseRoot 'docs/SDK_REQUESTS.md'
    $text = Get-Content -LiteralPath $path -Raw
    $summaries = [regex]::Matches($text, '(\d+) deduplicated items: (\d+) open, (\d+) partially resolved, (\d+) resolved, (\d+) obsolete')
    $inventory = [regex]::Match($text, '(?ms)^## Prioritized inventory\r?\n(?<rows>.*?)(?=^## |\z)')
    if ($summaries.Count -ne 1 -or !$inventory.Success) { throw 'SDK_REQUESTS.md must contain one counts line and a prioritized inventory.' }
    $counts = @{ open = 0; 'partially resolved' = 0; resolved = 0; obsolete = 0 }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($line in $inventory.Groups['rows'].Value -split '\r?\n') {
        if ($line -notmatch '^\|\s*SDK-') { continue }
        $cells = $line.Split('|')
        if ($cells.Count -lt 6) { throw 'SDK_REQUESTS.md contains a malformed inventory row.' }
        $id = $cells[1].Trim()
        $status = $cells[4].Trim()
        if ($id -cnotmatch '^SDK-[0-9]+$' -or !$ids.Add($id) -or $status -cnotin $counts.Keys) {
            throw "SDK_REQUESTS.md has a duplicate/invalid ID or unknown status: $id / $status"
        }
        $counts[$status]++
    }
    $actual = @($ids.Count, $counts.open, $counts['partially resolved'], $counts.resolved, $counts.obsolete)
    $expected = @($summaries[0].Groups | Select-Object -Skip 1 | ForEach-Object { [int]$_.Value })
    if (!$ids.Count -or ($actual -join ',') -ne ($expected -join ',')) {
        throw "SDK_REQUESTS.md counts line is inconsistent. Actual total/open/partial/resolved/obsolete: $($actual -join '/')."
    }
    $appendix = [regex]::Matches($text, 'The (\d+) resolved rows')
    if ($appendix.Count -ne 1 -or [int]$appendix[0].Groups[1].Value -ne $counts.resolved) {
        throw 'SDK_REQUESTS.md resolved appendix count is inconsistent.'
    }
}

function Assert-ReleaseCi([string]$Head) {
    if (!(Get-Command gh -ErrorAction SilentlyContinue)) {
        Write-Warning 'gh is unavailable; skipping the HEAD CI check. Verify main CI manually before tagging.'
        return
    }
    Push-Location -LiteralPath $releaseRoot
    try {
        $json = & gh run list --workflow ci.yml --commit $Head --branch main --event push --limit 100 --json databaseId,status,conclusion,headSha
    }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw 'gh could not query CI for HEAD; authenticate/fix the query and retry.' }
    try { $runs = @((($json -join "`n") | ConvertFrom-Json -ErrorAction Stop)) }
    catch { throw 'gh returned invalid CI JSON for HEAD.' }
    $latest = $runs | Where-Object { $_.headSha -ceq $Head } | Sort-Object { [long]$_.databaseId } -Descending | Select-Object -First 1
    if (!$latest -or $latest.status -cne 'completed' -or $latest.conclusion -cne 'success') {
        throw 'CI is not green for HEAD: the latest main push run of ci.yml must have completed successfully.'
    }
    Write-Host "CI run $($latest.databaseId) is green for HEAD $Head."
}
