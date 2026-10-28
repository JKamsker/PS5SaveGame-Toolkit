import { act, renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { useQueryableSelection } from "./selection";

describe("queryable selection", () => {
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

    act(() => result.current.clear());
    expect(result.current.queryScope).toBeNull();
  });
});
