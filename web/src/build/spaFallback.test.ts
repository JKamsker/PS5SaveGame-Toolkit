import { expect, it, vi } from "vitest";
import { spaFallback } from "./spaFallback";

/** Runs the plugin's generateBundle hook against a bundle holding `index.html`. */
function generate(base: string, bundle: Record<string, unknown> = { "index.html": { type: "asset", source: "<html>app</html>" } }) {
  const context = { emitFile: vi.fn(), warn: vi.fn(), error: vi.fn((message: string) => { throw new Error(message); }) };
  const hook = spaFallback(base).generateBundle as unknown as (this: typeof context, options: unknown, bundle: unknown) => void;
  hook.call(context, {}, bundle);
  return context;
}

it.each(["/", "/PS5SaveGame-Toolkit/app/"])("emits 404.html as a copy of index.html for the absolute base %s", base => {
  const plugin = spaFallback(base);
  expect(plugin).toMatchObject({ apply: "build", enforce: "post" });
  expect(generate(base).emitFile).toHaveBeenCalledWith({ type: "asset", fileName: "404.html", source: "<html>app</html>" });
});

it.each(["./", ""])("skips the fallback with a warning for the relative base %j, whose assets would break below a deep link", base => {
  const context = generate(base);
  expect(context.emitFile).not.toHaveBeenCalled();
  expect(context.warn).toHaveBeenCalledWith(expect.stringContaining("relative"));
});

it("fails the build when index.html is missing", () => {
  expect(() => generate("/", {})).toThrow(/index\.html is missing/);
});
