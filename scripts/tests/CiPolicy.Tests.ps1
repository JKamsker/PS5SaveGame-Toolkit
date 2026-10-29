Invoke-Case 'CI cancels only superseded PRs and checks scripts on all platforms' {
    $ci = Get-Content -LiteralPath (Join-Path $sourceRoot '.github/workflows/ci.yml') -Raw
    Assert-True ($ci.Contains("cancel-in-progress: `${{ github.event_name == 'pull_request' }}")) 'Only superseded PR runs should cancel.'
    Assert-True ($ci.Contains('./scripts/self-test.ps1')) 'Script regressions must run in CI.'
    Assert-True ($ci.Contains('os: [ubuntu-latest, windows-latest, macos-latest]')) 'Script checks must cover all three OS families.'
}
