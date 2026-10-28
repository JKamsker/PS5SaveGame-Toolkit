import { act, renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { useQueryableSelection } from "./selection";

describe("queryable selection", () => {
  it("selects and deselects a page while retaining selections on other pages", () => {
    const rows = [{ key: "a", cells: {} }, { key: "b", cells: {} }];
    const { result } = renderHook(() => useQueryableSelection(rows, "items", {}, 7, 100));

    act(() => result.current.toggle("other-page"));
    act(() => result.current.setMany(["a", "b"], true));
    expect(result.current.count).toBe(3);
    expect([...result.current.selectedKeys]).toEqual(["a", "b"]);

    act(() => result.current.setMany(["a", "b"], false));
    expect(result.current.count).toBe(1);
    expect(result.current.selectedRows).toEqual([]);
  });

  it("serializes select-all scope and explicit exclusions", () => {
    const rows = [{ key: "a", cells: {} }, { key: "b", cells: {} }];
    const query = { q: "potion", filters: { category: "usable" }, sort: "name", offset: 0, limit: 50 };
    const { result } = renderHook(() => useQueryableSelection(rows, "items", query, 7, 100));

    act(() => result.current.selectFiltered?.());
    expect(result.current.count).toBe(100);
    expect(result.current.queryScope).toMatchObject({ collectionId: "items", revision: 7, totalCount: 100 });
    expect(result.current.queryScope?.query).toEqual(query);

    act(() => result.current.toggle("a"));
    expect(result.current.count).toBe(99);
    expect(result.current.queryScope?.excludedRowKeys).toEqual(["a"]);

    act(() => result.current.setMany(["a", "b"], false));
    expect(result.current.queryScope?.excludedRowKeys).toEqual(["a", "b"]);
    expect(result.current.count).toBe(98);
    act(() => result.current.setMany(["a", "b"], true));
    expect(result.current.queryScope?.excludedRowKeys).toEqual([]);
    expect(result.current.count).toBe(100);

    act(() => result.current.clear());
    expect(result.current.queryScope).toBeNull();
  });
});
