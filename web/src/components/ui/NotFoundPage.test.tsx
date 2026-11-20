import { cleanup, render, screen } from "@testing-library/react";
import { matchRoutes } from "react-router";
import { afterEach, expect, it } from "vitest";
import { isOutsideApp, siteHome } from "../../lib/sitePaths";
import { SiteNotFoundPage } from "./NotFoundPage";

afterEach(cleanup);

it("tells the app's own routes from the rest of the site it is deployed in", () => {
  const app = "/PS5SaveGame-Toolkit/app";
  expect(isOutsideApp("/PS5SaveGame-Toolkit/app/", app)).toBe(false);
  expect(isOutsideApp("/PS5SaveGame-Toolkit/app", app)).toBe(false);
  expect(isOutsideApp("/PS5SaveGame-Toolkit/app/w/abc", app)).toBe(false);
  expect(isOutsideApp("/PS5SaveGame-Toolkit/guides/missing", app)).toBe(true);
  expect(isOutsideApp("/PS5SaveGame-Toolkit/apple/x", app)).toBe(true);
  // An app served from the root has no rest of the site.
  expect(isOutsideApp("/anything", "/")).toBe(false);
  expect(siteHome("/PS5SaveGame-Toolkit/app/")).toBe("/PS5SaveGame-Toolkit/");
  expect(siteHome("/app/")).toBe("/");
});

it("ignores case like the router, so every address the router routes counts as the app's", () => {
  const app = "/PS5SaveGame-Toolkit/app";
  for (const pathname of ["/ps5savegame-toolkit/APP/w/abc", "/PS5SAVEGAME-TOOLKIT/App", "/PS5SaveGame-Toolkit/apple/x", "/elsewhere"]) {
    const routed = matchRoutes([{ path: "*" }], pathname, app) !== null;
    expect(isOutsideApp(pathname, app), pathname).toBe(!routed);
  }
  expect(isOutsideApp("/ps5savegame-toolkit/APP/w/abc", app)).toBe(false);
});

it("points a missing page outside the app to the documentation and the editor, without editor wording", () => {
  render(<SiteNotFoundPage documentation="/PS5SaveGame-Toolkit/" editor="/PS5SaveGame-Toolkit/app/" />);
  expect(screen.getByRole("heading", { name: "Page not found" })).toBeTruthy();
  expect(screen.getByRole("link", { name: "Documentation home" }).getAttribute("href")).toBe("/PS5SaveGame-Toolkit/");
  expect(screen.getByRole("link", { name: "Open the editor" }).getAttribute("href")).toBe("/PS5SaveGame-Toolkit/app/");
  expect(screen.getByRole("main").textContent).not.toMatch(/workspace|imported saves/i);
});
