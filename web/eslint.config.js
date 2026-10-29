import js from "@eslint/js";
import { defineConfig } from "eslint/config";
import jsxA11y from "eslint-plugin-jsx-a11y";
import reactHooks from "eslint-plugin-react-hooks";
import globals from "globals";
import tseslint from "typescript-eslint";

export default defineConfig([
  {
    files: ["src/**/*.{ts,tsx}", "e2e/**/*.ts"],
    extends: [js.configs.recommended, tseslint.configs.recommended],
    languageOptions: {
      globals: globals.browser,
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
    linterOptions: { reportUnusedDisableDirectives: "error" },
    rules: {
      "@typescript-eslint/consistent-type-imports": "error",
      "@typescript-eslint/no-floating-promises": "error",
      "@typescript-eslint/no-misused-promises": "error",
      "no-duplicate-imports": ["error", { allowSeparateTypeImports: true }],
      "no-console": "error",
      "no-debugger": "error",
      "max-lines": ["warn", 400],
      "max-lines-per-function": ["warn", 50],
    },
  },
  {
    files: ["src/**/*.{ts,tsx}"],
    extends: [reactHooks.configs.flat.recommended, jsxA11y.flatConfigs.recommended],
  },
  {
    files: ["e2e/**/*.ts"],
    languageOptions: { globals: globals.node },
  },
  {
    files: [
      "src/components/document/DocumentEditor.tsx",
      "src/components/document/SectionView.tsx",
      "src/components/document/table/ServerTableView.tsx",
    ],
    // Preserve render-time store/page snapshots and stale-row guards until their controllers are refactored.
    rules: { "react-hooks/refs": "off" },
  },
  {
    files: [
      "src/components/document/DocumentEditor.tsx",
      "src/components/document/table/ServerTableView.tsx",
      "src/components/document/table/useTableValues.ts",
      "src/components/ui/useAsyncTask.ts",
      "src/components/ui/useLatestRequest.ts",
      "src/components/workspace/useOpenDocument.ts",
    ],
    // These identity-scoped mutable controllers serialize requests/subscriptions; changing them needs concurrency review.
    rules: { "react-hooks/immutability": "off" },
  },
  {
    files: [
      "src/components/document/LazyExpansion.tsx",
      "src/components/document/SectionView.tsx",
      "src/components/document/TabsView.tsx",
      "src/components/document/table/useCollectionQuery.ts",
      "src/components/fields/reference/ReferenceControl.tsx",
      "src/components/fields/reference/ReferencePicker.tsx",
    ],
    // Existing effects synchronize navigation, lazy expansion and rejected drafts; retain their established timing.
    rules: { "react-hooks/set-state-in-effect": "off" },
  },
]);
