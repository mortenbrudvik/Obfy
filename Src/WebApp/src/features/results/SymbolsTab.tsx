import { useEffect, useRef, useState } from "react";
import { failure } from "../../interop/errorMessage";
import { strings } from "../../strings";
import type { SymbolNode, SymbolsPage } from "../../types/domain";

/** How many nodes of a level are asked for at a time; "Show more" asks for the next window. */
const PAGE_SIZE = 200;
/** How long typing pauses before the search is sent. */
const SEARCH_DELAY_MS = 250;

export type QuerySymbols = (text: string | null, parentId: string | null, offset: number, limit: number) => Promise<SymbolsPage>;

interface SymbolsTabProps {
  querySymbols: QuerySymbols;
  /**
   * Which load of the symbols this tree belongs to. A tree built for one generation is kept until the generation
   * changes, whatever happens to the results revision meanwhile.
   */
  generation: number;
}

/** A search box and the symbol tree under it: a level is fetched when it is first opened, a window at a time. */
export default function SymbolsTab({ querySymbols, generation }: SymbolsTabProps) {
  const text = strings.results.symbols;
  const [typed, setTyped] = useState("");
  const [applied, setApplied] = useState("");

  useEffect(() => {
    const timer = setTimeout(() => setApplied(typed.trim()), SEARCH_DELAY_MS);
    return () => clearTimeout(timer);
  }, [typed]);

  return (
    <>
      <div className="fl symsearch">
        <label className="vh" htmlFor="symbol-search">{text.search}</label>
        <input
          className="field"
          id="symbol-search"
          type="search"
          placeholder={text.placeholder}
          autoComplete="off"
          value={typed}
          onChange={(e) => setTyped(e.target.value)}
        />
      </div>
      <div className="card tree">
        {/* A new search or a reload starts a new tree: each node opens the way the search says. */}
        <ul>
          <TreeLevel key={`${generation}:${applied}`} querySymbols={querySymbols} search={applied || null} parentId={null} />
        </ul>
      </div>
    </>
  );
}

interface TreeLevelProps {
  querySymbols: QuerySymbols;
  search: string | null;
  parentId: string | null;
}

/** The nodes of one level, fetched when it appears. */
function TreeLevel({ querySymbols, search, parentId }: TreeLevelProps) {
  const text = strings.results.symbols;
  const [items, setItems] = useState<SymbolNode[]>([]);
  const [first, setFirst] = useState<SymbolsPage | null>(null);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    querySymbols(search, parentId, 0, PAGE_SIZE).then(
      (page) => {
        if (!alive.current) return;
        setItems(page.items);
        setTotal(page.total);
        setFirst(page);
      },
      (err) => {
        if (alive.current) setError(failure(text.failed, err));
      },
    );
    return () => {
      alive.current = false;
    };
  }, [querySymbols, search, parentId, text.failed]);

  const showMore = async () => {
    setLoadingMore(true);
    try {
      const page = await querySymbols(search, parentId, items.length, PAGE_SIZE);
      if (!alive.current) return;
      setItems((held) => [...held, ...page.items]);
      setTotal(page.total);
      setError(null);
    } catch (err) {
      if (alive.current) setError(failure(text.failed, err));
    } finally {
      if (alive.current) setLoadingMore(false);
    }
  };

  const root = parentId === null;
  return (
    <>
      {items.map((node) => (
        <TreeNode key={node.id} node={node} querySymbols={querySymbols} search={search} />
      ))}
      {error && (
        <li className="empty-find" role="alert">{error}</li>
      )}
      {!error && !first && <li className="empty-find">{text.loading}</li>}
      {root && first?.hasNoSymbols && <li className="empty-find">{text.noSymbols}</li>}
      {root && first?.hasNoSearchMatches && <li className="empty-find">{text.noMatches(search ?? "")}</li>}
      {items.length < total && (
        <li>
          <button type="button" className="btn sm more-symbols" disabled={loadingMore} onClick={() => void showMore()}>
            {text.showMore}
          </button>
        </li>
      )}
    </>
  );
}

interface TreeNodeProps {
  node: SymbolNode;
  querySymbols: QuerySymbols;
  search: string | null;
}

/** One row. A container only groups what it holds, so it shows its name once; anything else shows the rename. */
function TreeNode({ node, querySymbols, search }: TreeNodeProps) {
  const text = strings.results.symbols;
  const expandable = node.childCount > 0;
  const [open, setOpen] = useState(expandable && node.expanded);

  const label = (
    <>
      <span className="chev" aria-hidden="true">{expandable ? (open ? "▾" : "▸") : ""}</span>
      <span className="kind">{text.kind[node.kind]}</span>
      <span className="o">{node.originalName}</span>
      {!node.isContainer && node.obfuscatedName !== null && (
        <>
          <span className="arrow" role="img" aria-label={text.becomes}>→</span>
          <span className="nn">{node.obfuscatedName}</span>
        </>
      )}
    </>
  );

  return (
    <li>
      {expandable ? (
        <button type="button" className="tnode" aria-expanded={open} onClick={() => setOpen(!open)}>
          {label}
        </button>
      ) : (
        <div className="tnode">{label}</div>
      )}
      {open && (
        <ul>
          <TreeLevel querySymbols={querySymbols} search={search} parentId={node.id} />
        </ul>
      )}
    </li>
  );
}
