Invoke-Case 'workflow cache and artifact actions use verified Node 24 majors' {
    $ci = Get-Content -LiteralPath (Join-Path $sourceRoot '.github/workflows/ci.yml') -Raw
    $release = Get-Content -LiteralPath (Join-Path $sourceRoot '.github/workflows/release.yml') -Raw
    Assert-True (($ci + $release) -notmatch 'actions/(?:cache|upload-artifact)@v4\b') 'Deprecated Node 20 actions must be replaced.'
    Assert-True ($release.Contains('actions/download-artifact@v8')) 'Artifact download must match the verified current release.'
}
