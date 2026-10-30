/** Run every selected suite, retaining failure even when another suite succeeds. */
export function runSelectedSuites(suites, launch, reportError) {
  let exitCode = 0;
  for (const [suite, selected] of suites) {
    if (!selected.length) continue; // File/grep filters can select only one complementary suite.
    try {
      const result = launch(suite);
      if (result.error) throw result.error;
      if (result.status !== 0) exitCode = result.status || 1;
    } catch (error) {
      reportError(`E2E suite '${suite}' could not launch: ${error.message}`);
      exitCode = 1;
    }
  }
  return exitCode;
}
