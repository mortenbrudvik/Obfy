import { act, fireEvent, render, renderHook, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import App from "../App";
import ErrorBoundary from "../components/ErrorBoundary";
import Field from "../components/Field";
import FileTable from "../features/files/FileTable";
import { useInput } from "../features/files/useInput";
import GuidePage from "../features/guide/GuidePage";
import LevelCards from "../features/protection/LevelCards";
import ProtectionPage from "../features/protection/ProtectionPage";
import TechniqueDetail from "../features/protection/TechniqueDetail";
import { protectionSnapshot } from "../features/protection/__tests__/fixtures";
import { visibleLines } from "../features/results/logFormat";
import { appendBatch, emptyLog, MAX_LOG_LINES, replaceLog } from "../features/results/logModel";
import { useResults } from "../features/results/useResults";
import { finishedResults, line } from "../features/results/__tests__/fixtures";
import Shell from "../features/shell/Shell";
import { useAppAction } from "../hooks/useAppAction";
import { apiClient } from "../interop/WebViewApiClient";
import { mockBridge } from "../test/bridge";
import { emptyFiles, idleRun, notRunResults, readyRun, standardSettings } from "../test/snapshots";
import type { FileRow, LogLine, RunSnapshot } from "../types/domain";

function calls(service: string, method: string) {
  return vi.mocked(apiClient.call).mock.calls.filter(([s, m]) => s === service && m === method);
}

describe("Wave 2 review fixes", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  describe("B-54: logModel keeps warnings and errors when trimming >5,000 lines", () => {
    it("dropsOldestNonProblemLinesFirst_whenReplaceLogExceedsMaxLines", () => {
      const lines: LogLine[] = [
        line(1, "Warning", "early warning"),
        line(2, "Debug", "debug 2"),
        ...Array.from({ length: MAX_LOG_LINES + 5 }, (_, i) => line(i + 3, "Info", `info ${i + 3}`)),
        line(MAX_LOG_LINES + 8, "Error", "late error"),
      ];

      const held = replaceLog(emptyLog, { revision: 10, lines });

      expect(held.lines).toHaveLength(MAX_LOG_LINES);
      expect(held.trimmed).toBe(8);
      // Chronological order (id ascending) is preserved and both problems remain.
      expect(held.lines[0]).toEqual(line(1, "Warning", "early warning"));
      expect(held.lines[1].id).toBe(10);
      expect(held.lines[held.lines.length - 1]).toEqual(line(MAX_LOG_LINES + 8, "Error", "late error"));
      for (let i = 1; i < held.lines.length; i++) {
        expect(held.lines[i].id).toBeGreaterThan(held.lines[i - 1].id);
      }
    });

    it("keepsEarlyWarningsAndErrorsAcrossBatches_evenAfter5000InfoLinesArrive", () => {
      const start = replaceLog(emptyLog, {
        revision: 1,
        lines: [line(1, "Warning", "startup warning"), line(2, "Error", "startup error")],
      });
      const flooded = appendBatch(start, {
        revision: 2,
        lines: Array.from({ length: MAX_LOG_LINES + 20 }, (_, i) => line(i + 3, "Info", `verbose ${i + 3}`)),
      });

      expect(flooded.lines).toHaveLength(MAX_LOG_LINES);
      expect(flooded.trimmed).toBe(22);
      const problems = visibleLines(flooded.lines, true);
      expect(problems.map((l) => l.message)).toEqual(["startup warning", "startup error"]);
    });

    it("capsAtMaxLogLines_whenProblemsAloneExceedMaxLogLines", () => {
      const lines: LogLine[] = [
        line(1, "Info", "info 1"),
        ...Array.from({ length: MAX_LOG_LINES + 3 }, (_, i) => line(i + 2, "Error", `err ${i + 2}`)),
      ];

      const held = replaceLog(emptyLog, { revision: 1, lines });

      expect(held.lines).toHaveLength(MAX_LOG_LINES);
      expect(held.trimmed).toBe(4);
      // All info lines were dropped before dropping the oldest 3 error lines.
      expect(held.lines[0].id).toBe(5);
      expect(held.lines.every((l) => l.level === "Error")).toBe(true);
    });
  });

  describe("B-15: GuidePage passes matching GuideLinks topic ID", () => {
    it("passesShortcutsTopic_whenViewingShortcutsSection", async () => {
      mockBridge({ "app.openGuide": () => ({}) });
      const onTopicChange = vi.fn();
      render(<GuidePage topic="keys" onTopicChange={onTopicChange} />);

      fireEvent.click(screen.getByRole("button", { name: "Open the full guide online" }));

      await waitFor(() =>
        expect(vi.mocked(apiClient.call)).toHaveBeenCalledWith("app", "openGuide", { topic: "shortcuts" }, undefined),
      );
    });

    it("passesNullTopic_whenViewingStartSection", async () => {
      mockBridge({ "app.openGuide": () => ({}) });
      render(<GuidePage topic="start" onTopicChange={vi.fn()} />);

      fireEvent.click(screen.getByRole("button", { name: "Open the full guide online" }));

      await waitFor(() =>
        expect(vi.mocked(apiClient.call)).toHaveBeenCalledWith("app", "openGuide", { topic: null }, undefined),
      );
    });
  });

  describe("B-16 & B-35: Save As… and ConfirmDialog before replacing dirty settings", () => {
    it("rendersSaveAsButton_thatCallsSaveConfigTrue_evenWhileRunning", async () => {
      const running: RunSnapshot = { ...readyRun, canStart: false, status: "Running", currentFile: "App.dll", total: 1 };
      mockBridge({
        "settings.get": () => protectionSnapshot({ level: "Standard" }),
        "settings.saveConfig": () => protectionSnapshot({ level: "Standard" }),
        "run.get": () => running,
      });

      render(<ProtectionPage />);
      const saveAs = (await screen.findByRole("button", { name: "Save As…" })) as HTMLButtonElement;
      expect(saveAs.disabled).toBe(false);

      fireEvent.click(saveAs);

      await waitFor(() => expect(calls("settings", "saveConfig")).toHaveLength(1));
      expect(calls("settings", "saveConfig")[0][2]).toEqual({ saveAs: true });
    });

    it("wiresCtrlShiftS_inShell_toSaveConfigTrue", async () => {
      mockBridge({
        "app.info": () => ({ version: "1.0.0" }),
        "run.get": () => readyRun,
        "files.get": () => emptyFiles,
        "settings.get": () => standardSettings,
        "results.summary": () => notRunResults,
        "settings.saveConfig": () => standardSettings,
      });

      render(
        <Shell screen="protection" onNavigate={vi.fn()}>
          content
        </Shell>,
      );
      await screen.findByText(/0 of 0 files · Standard/);

      fireEvent.keyDown(window, { key: "S", ctrlKey: true, shiftKey: true });

      await waitFor(() => expect(calls("settings", "saveConfig")).toHaveLength(1));
      expect(calls("settings", "saveConfig")[0][2]).toEqual({ saveAs: true });
    });

    it("showsConfirmDialog_whenOpeningConfigWithDirtySettings_inProtectionPage", async () => {
      mockBridge({
        "app.info": () => ({ version: "1.0.0" }),
        "run.get": () => idleRun,
        "files.get": () => emptyFiles,
        "settings.get": () => protectionSnapshot({ dirty: true }),
        "settings.openConfig": () => protectionSnapshot({ dirty: false }),
        "results.summary": () => notRunResults,
      });

      render(<App />);
      fireEvent.click(screen.getByRole("button", { name: /^Protection/ }));
      const openBtn = await screen.findByRole("button", { name: "Open…" });
      openBtn.focus();

      fireEvent.click(openBtn);

      const dialog = screen.getByRole("alertdialog");
      expect(dialog.getAttribute("aria-modal")).toBe("true");
      expect(calls("settings", "openConfig")).toHaveLength(0);
      const cancelBtn = within(dialog).getByRole("button", { name: "Cancel" });
      expect(document.activeElement).toBe(cancelBtn);

      // Escape cancels without calling openConfig and returns focus to Open….
      fireEvent.keyDown(dialog, { key: "Escape" });
      expect(screen.queryByRole("alertdialog")).toBeNull();
      expect(calls("settings", "openConfig")).toHaveLength(0);
      expect(document.activeElement).toBe(openBtn);

      // Confirming proceeds with openConfig.
      fireEvent.click(openBtn);
      const confirmDialog = screen.getByRole("alertdialog");
      fireEvent.click(within(confirmDialog).getByRole("button", { name: "Replace changes" }));

      expect(screen.queryByRole("alertdialog")).toBeNull();
      await waitFor(() => expect(calls("settings", "openConfig")).toHaveLength(1));
    });

    it("skipsConfirmDialog_whenOpeningConfigWithCleanSettings_inProtectionPage", async () => {
      mockBridge({
        "app.info": () => ({ version: "1.0.0" }),
        "run.get": () => idleRun,
        "files.get": () => emptyFiles,
        "settings.get": () => protectionSnapshot({ dirty: false }),
        "settings.openConfig": () => protectionSnapshot({ dirty: false }),
        "results.summary": () => notRunResults,
      });

      render(<App />);
      fireEvent.click(screen.getByRole("button", { name: /^Protection/ }));
      fireEvent.click(await screen.findByRole("button", { name: "Open…" }));

      expect(screen.queryByRole("alertdialog")).toBeNull();
      await waitFor(() => expect(calls("settings", "openConfig")).toHaveLength(1));
    });

    it("showsConfirmDialog_whenLoadingConfigWithDirtySettings_fromFilesEmptyState", async () => {
      mockBridge({
        "app.info": () => ({ version: "1.0.0" }),
        "run.get": () => idleRun,
        "files.get": () => emptyFiles,
        "settings.get": () => ({ ...standardSettings, dirty: true }),
        "settings.openConfig": () => standardSettings,
        "results.summary": () => notRunResults,
      });

      render(<App />);
      const loadBtn = await screen.findByRole("button", { name: "Load a configuration (obfy.json)" });
      fireEvent.click(loadBtn);

      const dialog = screen.getByRole("alertdialog");
      expect(calls("settings", "openConfig")).toHaveLength(0);
      fireEvent.click(within(dialog).getByRole("button", { name: "Replace changes" }));
      await waitFor(() => expect(calls("settings", "openConfig")).toHaveLength(1));
    });

    it("showsUnknownKeysWarningNotice_onProtectionPage_belowHeader", async () => {
      const host = mockBridge({
        "app.info": () => ({ version: "1.0.0" }),
        "run.get": () => idleRun,
        "files.get": () => emptyFiles,
        "settings.get": () => protectionSnapshot({ dirty: false }),
        "results.summary": () => notRunResults,
      });

      render(<App />);
      fireEvent.click(screen.getByRole("button", { name: /^Protection/ }));
      await screen.findByRole("button", { name: "Open…" });

      host.push("files.changed", {
        ...emptyFiles,
        revision: 2,
        notices: [
          {
            level: "Warning",
            title: "Unknown configuration keys",
            message: "Unknown configuration keys ignored: bogusKey.",
          },
        ],
      });

      expect(await screen.findByText("Unknown configuration keys")).toBeTruthy();
      expect(screen.getByText("Unknown configuration keys ignored: bogusKey.")).toBeTruthy();
    });

    it("blocksGlobalShortcuts_whileConfirmDialogIsOpen_andTrapsTabAfterClickingModalText", async () => {
      const onNavigate = vi.fn();
      mockBridge({
        "app.info": () => ({ version: "1.0.0" }),
        "run.get": () => readyRun,
        "files.get": () => emptyFiles,
        "files.pick": () => emptyFiles,
        "settings.get": () => ({ ...standardSettings, dirty: true }),
        "settings.openConfig": () => standardSettings,
        "results.summary": () => notRunResults,
      });

      render(
        <Shell screen="files" onNavigate={onNavigate}>
          content
        </Shell>,
      );
      await screen.findByText(/0 of 0 files · Standard/);

      fireEvent.keyDown(window, { key: "l", ctrlKey: true });
      const dialog = screen.getByRole("alertdialog");

      // While alertdialog is open, global shortcuts must not fire behind it.
      fireEvent.keyDown(window, { key: "o", ctrlKey: true });
      fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });
      fireEvent.keyDown(window, { key: "F1" });
      expect(calls("files", "pick")).toHaveLength(0);
      expect(calls("run", "start")).toHaveLength(0);
      expect(onNavigate).not.toHaveBeenCalled();

      // Clicking non-focusable modal text moves focus outside buttons; Tab must stay inside modal.
      const msg = within(dialog).getByText(/You have unsaved protection changes/);
      fireEvent.mouseDown(msg);
      (document.activeElement as HTMLElement | null)?.blur();

      const cancelBtn = within(dialog).getByRole("button", { name: "Cancel" });
      const confirmBtn = within(dialog).getByRole("button", { name: "Replace changes" });
      fireEvent.keyDown(document.body, { key: "Tab" });
      expect(document.activeElement).toBe(cancelBtn);

      confirmBtn.focus();
      fireEvent.keyDown(confirmBtn, { key: "Tab" });
      expect(document.activeElement).toBe(cancelBtn);

      fireEvent.keyDown(cancelBtn, { key: "Tab", shiftKey: true });
      expect(document.activeElement).toBe(confirmBtn);
    });

    it("clearsPreviousShortcutError_whenAnotherShortcutActionRunsInShell", async () => {
      mockBridge({
        "app.info": () => ({ version: "1.0.0" }),
        "run.get": () => readyRun,
        "files.get": () => emptyFiles,
        "files.pick": () => {
          throw new Error("Picker failed.");
        },
        "settings.get": () => standardSettings,
        "settings.saveConfig": () => standardSettings,
        "results.summary": () => notRunResults,
      });

      render(
        <Shell screen="files" onNavigate={vi.fn()}>
          content
        </Shell>,
      );
      await screen.findByText(/0 of 0 files · Standard/);

      fireEvent.keyDown(window, { key: "o", ctrlKey: true });
      expect((await screen.findByRole("alert")).textContent).toContain("Could not add files");

      fireEvent.keyDown(window, { key: "s", ctrlKey: true });
      await waitFor(() => expect(calls("settings", "saveConfig")).toHaveLength(1));
      expect(screen.queryByRole("alert")).toBeNull();
    });

    it("showsConfirmDialog_onCtrlL_whenDirtyInShell_andSkipsWhenClean", async () => {
      const host = mockBridge({
        "app.info": () => ({ version: "1.0.0" }),
        "run.get": () => readyRun,
        "files.get": () => emptyFiles,
        "settings.get": () => ({ ...standardSettings, dirty: true }),
        "settings.openConfig": () => standardSettings,
        "results.summary": () => notRunResults,
      });

      render(
        <Shell screen="files" onNavigate={vi.fn()}>
          content
        </Shell>,
      );
      await screen.findByText(/0 of 0 files · Standard/);

      fireEvent.keyDown(window, { key: "l", ctrlKey: true });
      const dialog = screen.getByRole("alertdialog");
      expect(calls("settings", "openConfig")).toHaveLength(0);

      fireEvent.click(within(dialog).getByRole("button", { name: "Replace changes" }));
      await waitFor(() => expect(calls("settings", "openConfig")).toHaveLength(1));

      // When clean, Ctrl+L opens directly without dialog.
      host.push("settings.changed", { ...standardSettings, revision: 2, dirty: false });
      fireEvent.keyDown(window, { key: "l", ctrlKey: true });
      expect(screen.queryByRole("alertdialog")).toBeNull();
      await waitFor(() => expect(calls("settings", "openConfig")).toHaveLength(2));
    });
  });

  describe("B-75: ErrorBoundary reports screen and error type without message or stack", () => {
    it("reportsErrorTypeAndScreen_withoutErrorMessageOrStack", async () => {
      vi.spyOn(console, "error").mockImplementation(() => {});
      mockBridge({
        "app.ready": () => ({}),
        "app.reportError": () => ({}),
      });

      function BadComponent(): never {
        throw new TypeError("C:\\secret\\user\\path failed");
      }

      render(
        <ErrorBoundary screen="protection" fallback={() => <div role="alert">broken</div>}>
          <BadComponent />
        </ErrorBoundary>,
      );

      await waitFor(() => expect(calls("app", "reportError")).toHaveLength(1));
      expect(calls("app", "reportError")[0][2]).toEqual({ screen: "protection", errorType: "TypeError" });
      const serialized = JSON.stringify(calls("app", "reportError")[0]);
      expect(serialized).not.toContain("secret");
      expect(serialized).not.toContain("componentStack");
    });

    it("passesScreenRoot_forTopLevelAppBoundary", async () => {
      vi.spyOn(console, "error").mockImplementation(() => {});
      mockBridge({
        "app.info": () => ({ version: "1.0.0" }),
        "run.get": () => idleRun,
        "files.get": () => ({ ...emptyFiles, files: null }),
        "settings.get": () => standardSettings,
        "results.summary": () => notRunResults,
        "app.ready": () => ({}),
        "app.reportError": () => ({}),
      });

      render(<App />);

      await waitFor(() => expect(calls("app", "reportError")).toHaveLength(1));
      expect(calls("app", "reportError")[0][2]).toMatchObject({ screen: "root" });
    });
  });

  describe("B-113: Accessibility and state polish", () => {
    it("LevelCards_supportsArrowNavigationRovingTabIndex_andUsesNoDivInsideButton", () => {
      const onSelect = vi.fn();
      const { container, rerender } = render(<LevelCards level="Standard" onSelect={onSelect} />);

      expect(container.querySelectorAll("button div")).toHaveLength(0);

      const minimal = screen.getByRole("radio", { name: /^Minimal/ });
      const standard = screen.getByRole("radio", { name: /^Standard/ });
      const aggressive = screen.getByRole("radio", { name: /^Aggressive/ });
      const custom = screen.getByRole("radio", { name: /^Custom/ });

      expect(minimal.tabIndex).toBe(-1);
      expect(standard.tabIndex).toBe(0);
      expect(aggressive.tabIndex).toBe(-1);
      expect(custom.tabIndex).toBe(-1);

      standard.focus();
      fireEvent.keyDown(standard, { key: "ArrowRight" });
      expect(onSelect).toHaveBeenLastCalledWith("Aggressive");
      expect(document.activeElement).toBe(aggressive);

      rerender(<LevelCards level="Custom" onSelect={onSelect} />);
      expect(custom.tabIndex).toBe(0);
      expect(standard.tabIndex).toBe(-1);

      custom.focus();
      fireEvent.keyDown(custom, { key: "ArrowDown" });
      expect(onSelect).toHaveBeenLastCalledWith("Minimal");
      expect(document.activeElement).toBe(minimal);

      fireEvent.keyDown(minimal, { key: "ArrowLeft" });
      expect(onSelect).toHaveBeenLastCalledWith("Custom");
      expect(document.activeElement).toBe(custom);

      fireEvent.keyDown(custom, { key: "ArrowUp" });
      expect(onSelect).toHaveBeenLastCalledWith("Aggressive");
      expect(document.activeElement).toBe(aggressive);
    });

    it("Field_omitsAriaDescribedBy_whenHelpAndErrorAreAbsent", () => {
      render(
        <Field label="Plain field">
          {(props) => <input {...props} />}
        </Field>,
      );

      const input = screen.getByLabelText("Plain field");
      expect(input.hasAttribute("aria-describedby")).toBe(false);
    });

    it("FileTable_managesRowMenuFocus_andRemovesInvalidAriaSelectedOnTableRows", () => {
      const files: FileRow[] = [
        {
          id: "f1",
          name: "A.dll",
          path: "C:/A.dll",
          origin: "Added",
          kind: "Assembly",
          size: 100,
          sizeText: "100 B",
          included: true,
          status: "Pending",
          skipReason: null,
          outputPath: null,
          error: null,
        },
      ];
      const onRemove = vi.fn();
      render(
        <FileTable
          files={files}
          locked={false}
          onSetIncluded={vi.fn()}
          onRemove={onRemove}
          onReveal={vi.fn()}
        />,
      );

      const row = screen.getByText("A.dll").closest("tr")!;
      expect(row.hasAttribute("aria-selected")).toBe(false);

      fireEvent.keyDown(row, { key: " " });
      expect(row.className).toContain("selected");
      fireEvent.keyDown(row, { key: "Delete" });
      expect(onRemove).toHaveBeenCalledWith(["f1"]);

      const trigger = screen.getByRole("button", { name: "More actions for A.dll" });
      trigger.focus();
      fireEvent.click(trigger);

      const menu = screen.getByRole("menu");
      const showItem = within(menu).getByRole("menuitem", { name: "Show in folder" });
      const removeItem = within(menu).getByRole("menuitem", { name: "Remove" });
      expect(document.activeElement).toBe(showItem);

      fireEvent.keyDown(menu, { key: "ArrowDown" });
      expect(document.activeElement).toBe(removeItem);

      fireEvent.keyDown(menu, { key: "ArrowUp" });
      expect(document.activeElement).toBe(showItem);

      fireEvent.keyDown(showItem, { key: "Escape" });
      expect(screen.queryByRole("menu")).toBeNull();
      expect(document.activeElement).toBe(trigger);
    });

    it("useAppAction_ignoresStaleRejection_whenResetOnChangedWhileInFlight", async () => {
      let rejectCall!: (err: Error) => void;
      const action = vi.fn(
        () =>
          new Promise<void>((_, reject) => {
            rejectCall = reject;
          }),
      );

      const { result, rerender } = renderHook(({ resetOn }) => useAppAction(action, "Action failed", resetOn), {
        initialProps: { resetOn: "v1" },
      });

      let promise!: Promise<boolean>;
      act(() => {
        promise = result.current[0]();
      });

      rerender({ resetOn: "v2" });

      await act(async () => {
        rejectCall(new Error("late failure"));
        await promise;
      });

      expect(result.current[1]).toBeNull();
    });

    it("useInput_clearsPriorError_whenAnotherActionRuns", async () => {
      mockBridge({
        "files.get": () => emptyFiles,
        "files.pick": () => {
          throw new Error("picker failed");
        },
        "files.clear": () => emptyFiles,
      });

      const { result } = renderHook(() => useInput());
      await waitFor(() => expect(result.current.input).not.toBeNull());

      await act(async () => {
        await result.current.pick();
      });
      expect(result.current.error).toBe("Could not add files (picker failed).");

      await act(async () => {
        await result.current.clear();
      });
      expect(result.current.error).toBeNull();
    });

    it("useResults_clearsPriorError_whenAnotherExportRuns", async () => {
      mockBridge({
        "results.summary": () => finishedResults,
        "results.exportMap": () => {
          throw new Error("disk full");
        },
        "results.exportReport": () => ({ exported: true, path: "C:/report.html" }),
      });

      const { result } = renderHook(() => useResults());
      await waitFor(() => expect(result.current.results).not.toBeNull());

      await act(async () => {
        await result.current.exportMap();
      });
      expect(result.current.error).toBe("Could not export (disk full).");

      await act(async () => {
        await result.current.exportReport();
      });
      expect(result.current.error).toBeNull();
      expect(result.current.exportedTo).toBe("C:/report.html");
    });

    it("OptionControl_syncsNumberAndRangeDrafts_onExternalValueChange", () => {
      const initialStr = protectionSnapshot({ optionValues: { stringEncryption: { minStringLength: 3 } } });
      const { rerender } = render(
        <TechniqueDetail
          technique={initialStr.techniques.find((t) => t.id === "stringEncryption")!}
          snapshot={initialStr}
          onUpdate={vi.fn()}
          onPickKey={vi.fn()}
          onOpenGuide={vi.fn()}
        />,
      );

      const numInput = screen.getByLabelText("Minimum length") as HTMLInputElement;
      numInput.focus();
      fireEvent.change(numInput, { target: { value: "" } });
      expect(numInput.value).toBe("");

      const loadedStr = protectionSnapshot({ revision: 2, optionValues: { stringEncryption: { minStringLength: 9 } } });
      rerender(
        <TechniqueDetail
          technique={loadedStr.techniques.find((t) => t.id === "stringEncryption")!}
          snapshot={loadedStr}
          onUpdate={vi.fn()}
          onPickKey={vi.fn()}
          onOpenGuide={vi.fn()}
        />,
      );
      expect(numInput.value).toBe("9");

      const initialCf = protectionSnapshot({ optionValues: { controlFlow: { intensity: 50 } } });
      rerender(
        <TechniqueDetail
          technique={initialCf.techniques.find((t) => t.id === "controlFlow")!}
          snapshot={initialCf}
          onUpdate={vi.fn()}
          onPickKey={vi.fn()}
          onOpenGuide={vi.fn()}
        />,
      );

      const rangeInput = screen.getByLabelText("Intensity") as HTMLInputElement;
      rangeInput.focus();
      fireEvent.change(rangeInput, { target: { value: "60" } });
      fireEvent.change(rangeInput, { target: { value: "80" } });
      expect(rangeInput.value).toBe("80");

      // An echo of an earlier step (60) does not pull the slider back while 80 is still pending.
      const echoEarlier = protectionSnapshot({ revision: 2, optionValues: { controlFlow: { intensity: 60 } } });
      rerender(
        <TechniqueDetail
          technique={echoEarlier.techniques.find((t) => t.id === "controlFlow")!}
          snapshot={echoEarlier}
          onUpdate={vi.fn()}
          onPickKey={vi.fn()}
          onOpenGuide={vi.fn()}
        />,
      );
      expect(rangeInput.value).toBe("80");

      // An external change (e.g., Ctrl+L loading 25) updates the slider immediately.
      const loadedCf = protectionSnapshot({ revision: 3, optionValues: { controlFlow: { intensity: 25 } } });
      rerender(
        <TechniqueDetail
          technique={loadedCf.techniques.find((t) => t.id === "controlFlow")!}
          snapshot={loadedCf}
          onUpdate={vi.fn()}
          onPickKey={vi.fn()}
          onOpenGuide={vi.fn()}
        />,
      );
      expect(rangeInput.value).toBe("25");
    });
  });
});
