import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { SymbolsPage } from "../../../types/domain";
import SymbolsTab from "../SymbolsTab";
import { node, page } from "./fixtures";

type Query = (text: string | null, parentId: string | null, offset: number, limit: number) => Promise<SymbolsPage>;

function setup(query: Query) {
  const querySymbols = vi.fn(query);
  render(<SymbolsTab querySymbols={querySymbols} generation={1} />);
  return querySymbols;
}

const searchBox = () => screen.getByRole("searchbox", { name: "Search symbols" });

describe("SymbolsTab", () => {
  beforeEach(() => vi.clearAllMocks());

  it("rendersEachRenameAsOriginalToObfuscated_andAContainerAsAGroupingWithoutAnArrow", async () => {
    setup(async () =>
      page([
        node({ id: "t1", originalName: "MyApp.Customer", obfuscatedName: "a", kind: "Type", childCount: 1 }),
        node({ id: "ns1", originalName: "MyApp.Models", obfuscatedName: null, kind: "Namespace", isContainer: true, childCount: 2 }),
      ]),
    );

    const renamed = (await screen.findByText("MyApp.Customer")).closest(".tnode") as HTMLElement;
    expect(within(renamed).getByText("a")).toBeTruthy();
    expect(within(renamed).getByLabelText("becomes")).toBeTruthy();

    const container = screen.getByText("MyApp.Models").closest(".tnode") as HTMLElement;
    expect(within(container).queryByLabelText("becomes")).toBeNull();
    expect(container.textContent).not.toContain("→");
    expect(container.textContent).toContain("MyApp.Models");
  });

  it("searchesTheStoreWithTheTypedText", async () => {
    const querySymbols = setup(async () => page([node({ id: "t1", originalName: "Foo", obfuscatedName: "a" })]));
    await screen.findByText("Foo");

    fireEvent.change(searchBox(), { target: { value: "cust" } });

    await waitFor(() => expect(querySymbols).toHaveBeenCalledWith("cust", null, 0, expect.any(Number)));
  });

  it("loadsChildrenOnExpand_nestingMembersWithTheirSignatures", async () => {
    const querySymbols = setup(async (_text, parentId) =>
      parentId === "t1"
        ? page([node({ id: "m1", originalName: "Method:Ns.Customer.Save(System.Void ())", obfuscatedName: "b", kind: "Method" })], { parentId: "t1" })
        : page([node({ id: "t1", originalName: "Ns.Customer", obfuscatedName: "a", childCount: 1 })]),
    );

    fireEvent.click(await screen.findByRole("button", { name: /Ns\.Customer/ }));

    expect(await screen.findByText("Method:Ns.Customer.Save(System.Void ())")).toBeTruthy();
    expect(querySymbols).toHaveBeenCalledWith(null, "t1", 0, expect.any(Number));
  });

  it("opensNodesTheSearchExpanded", async () => {
    setup(async (text, parentId) =>
      parentId === "t1"
        ? page([node({ id: "m1", originalName: "Save", obfuscatedName: "b", kind: "Method" })], { parentId: "t1" })
        : page([node({ id: "t1", originalName: "Customer", obfuscatedName: "a", childCount: 1, expanded: text === "sav" })]),
    );
    await screen.findByText("Customer");
    expect(screen.queryByText("Save")).toBeNull();

    fireEvent.change(searchBox(), { target: { value: "sav" } });

    expect(await screen.findByText("Save")).toBeTruthy();
  });

  it("showsMoreOfALongLevel_fromTheOffsetOnward", async () => {
    const querySymbols = setup(async (_text, _parent, offset) =>
      offset === 0
        ? page([node({ id: "a", originalName: "First", obfuscatedName: "x" })], { total: 2 })
        : page([node({ id: "b", originalName: "Second", obfuscatedName: "y" })], { total: 2, offset }),
    );

    fireEvent.click(await screen.findByRole("button", { name: "Show more" }));

    expect(await screen.findByText("Second")).toBeTruthy();
    expect(screen.getByText("First")).toBeTruthy();
    expect(querySymbols).toHaveBeenCalledWith(null, null, 1, expect.any(Number));
    expect(screen.queryByRole("button", { name: "Show more" })).toBeNull();
  });

  it("saysSoWhenNothingMatchesOrNothingWasRenamed", async () => {
    setup(async (text) => page([], text ? { hasNoSearchMatches: true } : { hasNoSymbols: true, totalSymbols: 0 }));
    expect(await screen.findByText("This run renamed nothing.")).toBeTruthy();

    fireEvent.change(searchBox(), { target: { value: "zzz" } });

    expect(await screen.findByText("No symbol matches “zzz”.")).toBeTruthy();
  });

  it("showsTheError_whenTheQueryFails", async () => {
    setup(async () => {
      throw new Error("Boom");
    });

    expect((await screen.findByRole("alert")).textContent).toContain("Boom");
  });
});
