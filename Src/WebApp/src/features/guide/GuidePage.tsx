import { Fragment, useEffect, useRef, type ReactNode } from "react";
import Keycap from "../../components/Keycap";
import Notice from "../../components/Notice";
import { guideTopics, type GuideBlock, type GuideTopic } from "../../content/guideContent";
import { useOnlineGuide } from "../../hooks/useOnlineGuide";
import { strings } from "../../strings";
import "../../components/components.css";
import "./GuidePage.css";

interface GuidePageProps {
  /** The id of the topic to show. An unknown id shows the first topic. */
  topic: string;
  onTopicChange: (topic: string) => void;
}

const INLINE = /(\*\*[^*]+\*\*|`[^`]+`|\{[^}]+\})/g;

/** Text with **bold**, `mono` and {Ctrl+Enter} keycaps turned into elements. */
function Inline({ text }: { text: string }) {
  return (
    <>
      {text.split(INLINE).map((part, index) => {
        if (part.startsWith("**")) return <b key={index}>{part.slice(2, -2)}</b>;
        if (part.startsWith("`")) return <span key={index} className="mono">{part.slice(1, -1)}</span>;
        if (part.startsWith("{")) return <Keys key={index} keys={part.slice(1, -1).split("+")} />;
        return <Fragment key={index}>{part}</Fragment>;
      })}
    </>
  );
}

/** Keycaps joined with "+". */
function Keys({ keys }: { keys: string[] }) {
  return (
    <span className="keyrow">
      {keys.map((key, index) => (
        <Fragment key={index}>
          {index > 0 && "+"}
          <Keycap>{key}</Keycap>
        </Fragment>
      ))}
    </span>
  );
}

function Block({ block }: { block: GuideBlock }): ReactNode {
  switch (block.type) {
    case "p":
      return (
        <p>
          <Inline text={block.text} />
        </p>
      );
    case "h3":
      return <h3>{block.text}</h3>;
    case "ul":
      return (
        <ul>
          {block.items.map((item) => (
            <li key={item}>
              <Inline text={item} />
            </li>
          ))}
        </ul>
      );
    case "note":
      return (
        <Notice tone="warn" title={block.title}>
          <Inline text={block.text} />
        </Notice>
      );
    case "shortcuts":
      return (
        <table className="keys">
          <tbody>
            {block.items.map((item) => (
              <tr key={item.label}>
                <td>
                  <Keys keys={item.keys} />
                </td>
                <td>
                  {item.label}
                  {item.note && <div className="keynote">{item.note}</div>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      );
  }
}

/**
 * Maps in-app guide topic ids to the host's closed set of online section ids (`GuideLinks.cs`).
 * `"start"` omits the topic (`undefined`) so the host opens the guide at its start.
 */
const onlineTopicIds: Record<string, string | undefined> = {
  start: undefined,
  keys: "shortcuts",
  "getting-started": "getting-started",
  "files-and-output": "files-and-output",
  "levels-and-settings": "levels-and-settings",
  techniques: "techniques",
  "what-can-break": "what-can-break",
  results: "results",
  "stack-traces": "stack-traces",
  shortcuts: "shortcuts",
  "command-line": "command-line",
  "ide-and-build": "ide-and-build",
  troubleshooting: "troubleshooting",
};

/** The Guide: a short quick-start, a topic list on the left and the chosen topic's article on the right, with a way to the full guide online. */
export default function GuidePage({ topic, onTopicChange }: GuidePageProps) {
  const current: GuideTopic = guideTopics.find((candidate) => candidate.id === topic) ?? guideTopics[0];
  const article = useRef<HTMLElement>(null);
  const [openOnline, onlineError] = useOnlineGuide();

  // A new topic starts at its top.
  useEffect(() => {
    article.current?.closest("main")?.scrollTo?.(0, 0);
  }, [current.id]);

  return (
    <div className="guide" data-testid="guide-page">
      <nav className="topics" aria-label={strings.guide.topics}>
        <h1 className="label topics-title">{strings.guide.title}</h1>
        {guideTopics.map((candidate) => (
          <button
            key={candidate.id}
            type="button"
            className="topic"
            aria-current={candidate.id === current.id ? "true" : undefined}
            onClick={() => onTopicChange(candidate.id)}
          >
            {candidate.title}
          </button>
        ))}
      </nav>
      <section ref={article} className="article" aria-labelledby="guide-article-title">
        <h2 id="guide-article-title">{current.title}</h2>
        {current.blocks.map((block, index) => (
          <Block key={index} block={block} />
        ))}
        <div className="online">
          <p>{strings.guide.onlineHint}</p>
          <button type="button" className="btn primary" onClick={() => void openOnline(onlineTopicIds[current.id])}>
            {strings.guide.online}
          </button>
          {onlineError && <Notice tone="error">{onlineError}</Notice>}
        </div>
      </section>
    </div>
  );
}
