Invoke-Case 'manual and tag releases share a version group without cancelling publication' {
    $release = Get-Content -LiteralPath (Join-Path $sourceRoot '.github/workflows/release.yml') -Raw
    Assert-True ($release.Contains("format('v{0}', inputs.version)")) 'Manual and tag release concurrency must normalize to v-prefixed versions.'
    Assert-True ($release.Contains('cancel-in-progress: false')) 'Release work must never cancel running publication.'
}
