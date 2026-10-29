Invoke-Case 'CI cancels only superseded PRs and checks scripts on all platforms' {
    $ci = Get-Content -LiteralPath (Join-Path $sourceRoot '.github/workflows/ci.yml') -Raw
    # Check the actual workflow expression, then evaluate its boolean subset for each trigger.
    $expected = "github.workflow == 'CI' && (github.event_name == 'pull_request' || (github.event_name == 'push' && github.ref_type == 'branch' && github.ref != 'refs/heads/main'))"
    $cancellation = [regex]::Match($ci, '(?m)^  cancel-in-progress: \$\{\{ (.+) \}\}\r?$').Groups[1].Value
    Assert-True ($cancellation -ceq $expected) 'Only standalone PR and non-main branch-push CI runs may cancel.'
    $group = 'ci-${{ github.workflow }}-${{ github.event_name }}-${{ github.ref }}-${{ (' + $expected + ") && 'latest' || github.run_id }}"
    Assert-True ($ci.Contains("group: $group")) 'Newer cancellable runs must share workflow/event/ref; protected runs must have unique run IDs.'
    # The exact expression above contains only strings, equality and boolean operators.
    $expression = $cancellation.Replace('github.workflow', '$workflow').Replace('github.event_name', '$eventName').
        Replace('github.ref_type', '$refType').Replace('github.ref', '$ref').
        Replace('==', '-ceq').Replace('!=', '-cne').Replace('&&', '-and').Replace('||', '-or')
    $evaluate = [scriptblock]::Create('param($workflow, $eventName, $refType, $ref) ' + $expression)
    foreach ($case in @(
        @{ Workflow = 'CI'; Event = 'pull_request'; Type = 'branch'; Ref = 'refs/pull/40/merge'; Cancel = $true },
        @{ Workflow = 'CI'; Event = 'push'; Type = 'branch'; Ref = 'refs/heads/ci/hardening'; Cancel = $true },
        @{ Workflow = 'CI'; Event = 'push'; Type = 'branch'; Ref = 'refs/heads/main'; Cancel = $false },
        @{ Workflow = 'CI'; Event = 'push'; Type = 'tag'; Ref = 'refs/tags/v1.2.3'; Cancel = $false },
        @{ Workflow = 'CI'; Event = 'push'; Type = 'tag'; Ref = 'refs/tags/v1.2.3-rc.1'; Cancel = $false },
        @{ Workflow = 'CI'; Event = 'workflow_dispatch'; Type = 'branch'; Ref = 'refs/heads/ci/hardening'; Cancel = $false },
        @{ Workflow = 'CI'; Event = 'schedule'; Type = 'branch'; Ref = 'refs/heads/main'; Cancel = $false },
        @{ Workflow = 'Release'; Event = 'push'; Type = 'tag'; Ref = 'refs/tags/v1.2.3'; Cancel = $false },
        @{ Workflow = 'Release'; Event = 'workflow_dispatch'; Type = 'branch'; Ref = 'refs/heads/ci/hardening'; Cancel = $false },
        @{ Workflow = 'Release'; Event = 'push'; Type = 'branch'; Ref = 'refs/heads/ci/hardening'; Cancel = $false }
    )) {
        $actual = & $evaluate $case.Workflow $case.Event $case.Type $case.Ref
        Assert-True ($actual -eq $case.Cancel) "Cancellation must be $($case.Cancel): $($case.Workflow)/$($case.Event)/$($case.Ref)."
    }
    foreach ($workflowFile in @('release.yml', 'nightly-fuzz.yml', 'dependency-drift.yml')) {
        $workflow = Get-Content -LiteralPath (Join-Path $sourceRoot ".github/workflows/$workflowFile") -Raw
        $policies = @([regex]::Matches($workflow, '(?m)^\s+cancel-in-progress: ([^\r\n]+)'))
        Assert-True ($policies.Count -eq 1 -and $policies[0].Groups[1].Value -ceq 'false') "Release, manual and scheduled/nightly runs must never cancel: $workflowFile."
    }
    Assert-True ($ci.Contains('./scripts/self-test.ps1')) 'Script regressions must run in CI.'
    Assert-True ($ci.Contains('os: [ubuntu-latest, windows-latest, macos-latest]')) 'Script checks must cover all three OS families.'
}
