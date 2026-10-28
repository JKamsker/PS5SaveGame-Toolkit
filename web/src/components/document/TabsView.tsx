import { useEffect, useId, useRef, useState } from "react";
import type { TabsNode } from "../../api/protocol";
import { ReviewPath, useNextReviewPath } from "./review/navigation";
import { ViewList } from "./ViewRenderer";

/** Tabs activate on arrow/Home/End keys; only the selected tab enters the page's tab order. */
export function TabsView({ node }: { node: TabsNode }) {
  const [selected, setSelected] = useState(node.tabs[0]?.title);
  const active = Math.max(0, node.tabs.findIndex(tab => tab.title === selected));
  const id = useId();
  const buttons = useRef<(HTMLButtonElement | null)[]>([]);
  const next = useNextReviewPath();
  useEffect(() => {
    const tab = node.tabs.find(tab => next === `tab:${tab.title}`);
    if (tab) setSelected(tab.title);
  }, [next, node.tabs]);

  return <div className="tabs">
    <div className="tabs__list" role="tablist" aria-label="Document views">
      {node.tabs.map((tab, i) => <button key={tab.title} type="button" role="tab" className="tabs__tab"
        ref={element => { buttons.current[i] = element; }}
        id={`${id}-tab-${i}`} aria-controls={`${id}-panel-${i}`} aria-selected={i === active}
        tabIndex={i === active ? 0 : -1} onClick={() => setSelected(tab.title)}
        onKeyDown={event => {
          let index: number;
          switch (event.key) {
            case "ArrowRight": index = (i + 1) % node.tabs.length; break;
            case "ArrowLeft": index = (i + node.tabs.length - 1) % node.tabs.length; break;
            case "Home": index = 0; break;
            case "End": index = node.tabs.length - 1; break;
            default: return;
          }
          event.preventDefault();
          setSelected(node.tabs[index]!.title);
          buttons.current[index]?.focus();
        }}>{tab.title}</button>)}
    </div>
    {node.tabs.map((tab, i) => <div key={tab.title} role="tabpanel" id={`${id}-panel-${i}`}
      aria-labelledby={`${id}-tab-${i}`} hidden={i !== active} tabIndex={0}>
      {i === active && <ReviewPath part={`tab:${tab.title}`}><ViewList nodes={tab.children} /></ReviewPath>}
    </div>)}
  </div>;
}
