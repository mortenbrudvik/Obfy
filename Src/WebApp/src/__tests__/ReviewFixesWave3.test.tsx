import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import Shell from "../features/shell/Shell";
import { apiClient } from "../interop/WebViewApiClient";
import { mockBridge } from "../test/bridge";
import { emptyFiles, idleRun, notRunResults, readyRun, standardSettings } from "../test/snapshots";
import type { RunSnapshot } from "../types/domain";

function calls(service: string, method: string) {
  return vi.mocked(apiClient.call).mock.calls.filter(([s, m]) => s === service && m === method);
}

describe("Wave 3 review fixes", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  describe("B-35: Confirm before replacing dirty settings on native .json drop", () => {
    it("rendersConfirmDialog_whenPendingConfigDropIsTrue_andInvokesConfirmConfigDrop", async () => {
      const host = mockBridge({
        "app.info": () => ({ version: "1.0.0" }),
        "run.get": () => idleRun,
        "files.get": () => emptyFiles,
        "settings.get": () => ({ ...standardSettings, revision: 1, dirty: true, pendingConfigDrop: false }),
        "settings.confirmConfigDrop": () => ({
          ...standardSettings,
          revision: 3,
          level: "Aggressive",
          dirty: false,
          pendingConfigDrop: false,
        }),
        "results.summary": () => notRunResults,
      });

      render(
        <Shell screen="files" onNavigate={vi.fn()}>
          content
        </Shell>,
      );
      await screen.findByText("Add at least one included file.");
      await screen.findByText("Standard", { selector: ".meta" });
      expect(screen.queryByRole("alertdialog")).toBeNull();

      host.push("settings.changed", {
        ...standardSettings,
        revision: 2,
        dirty: true,
        pendingConfigDrop: true,
      });

      const dialog = await screen.findByRole("alertdialog");
      expect(dialog.getAttribute("aria-modal")).toBe("true");
      expect(within(dialog).getByText("Replace unsaved changes?")).toBeTruthy();
      expect(within(dialog).getByText(/dropped configuration will replace them/i)).toBeTruthy();

      fireEvent.click(within(dialog).getByRole("button", { name: "Replace changes" }));

      await waitFor(() => expect(calls("settings", "confirmConfigDrop")).toHaveLength(1));
      await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
      expect(calls("settings", "cancelConfigDrop")).toHaveLength(0);
    });

    it("invokesCancelConfigDrop_onCancelClickAndEscape_andClosesDialog", async () => {
      let settingsRevision = 1;
      const host = mockBridge({
        "app.info": () => ({ version: "1.0.0" }),
        "run.get": () => idleRun,
        "files.get": () => emptyFiles,
        "settings.get": () => ({ ...standardSettings, revision: settingsRevision, dirty: true, pendingConfigDrop: true }),
        "settings.cancelConfigDrop": () => ({
          ...standardSettings,
          revision: ++settingsRevision,
          dirty: true,
          pendingConfigDrop: false,
        }),
        "results.summary": () => notRunResults,
      });

      render(
        <Shell screen="protection" onNavigate={vi.fn()}>
          content
        </Shell>,
      );

      const dialog = await screen.findByRole("alertdialog");
      fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));

      await waitFor(() => expect(calls("settings", "cancelConfigDrop")).toHaveLength(1));
      await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
      expect(calls("settings", "confirmConfigDrop")).toHaveLength(0);

      // Re-open via settings.changed and dismiss with Escape.
      settingsRevision = 3;
      host.push("settings.changed", {
        ...standardSettings,
        revision: settingsRevision,
        dirty: true,
        pendingConfigDrop: true,
      });
      const reopened = await screen.findByRole("alertdialog");
      fireEvent.keyDown(reopened, { key: "Escape" });

      await waitFor(() => expect(calls("settings", "cancelConfigDrop")).toHaveLength(2));
      await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    });

    it("clearsConfirmOpenConfig_whenPendingConfigDropResolves_andIgnoresOpenRequestsWhilePending", async () => {
      let settingsRevision = 1;
      const host = mockBridge({
        "app.info": () => ({ version: "1.0.0" }),
        "run.get": () => idleRun,
        "files.get": () => emptyFiles,
        "settings.get": () => ({ ...standardSettings, revision: settingsRevision, dirty: true, pendingConfigDrop: false }),
        "settings.openConfig": () => ({ ...standardSettings, revision: ++settingsRevision, dirty: false }),
        "settings.cancelConfigDrop": () => ({
          ...standardSettings,
          revision: ++settingsRevision,
          dirty: true,
          pendingConfigDrop: false,
        }),
        "settings.confirmConfigDrop": () => ({
          ...standardSettings,
          revision: ++settingsRevision,
          dirty: false,
          pendingConfigDrop: false,
        }),
        "results.summary": () => notRunResults,
      });

      render(
        <Shell screen="files" onNavigate={vi.fn()}>
          {({ onOpenConfig }) => (
            <button type="button" onClick={onOpenConfig}>
              Trigger open config
            </button>
          )}
        </Shell>,
      );

      await screen.findByText("Add at least one included file.");

      // 1. Open confirmOpenConfig while dirty, then receive a pendingConfigDrop and cancel it.
      fireEvent.click(screen.getByRole("button", { name: "Trigger open config" }));
      const openPrompt = await screen.findByRole("alertdialog");
      expect(within(openPrompt).getByText(/Opening a configuration will replace them/i)).toBeTruthy();

      settingsRevision = 2;
      host.push("settings.changed", {
        ...standardSettings,
        revision: settingsRevision,
        dirty: true,
        pendingConfigDrop: true,
      });

      const dropPrompt = await screen.findByRole("alertdialog");
      expect(within(dropPrompt).getByText(/Loading the dropped configuration will replace them/i)).toBeTruthy();

      // requestOpenConfig must be ignored while pendingConfigDrop is true.
      fireEvent.click(screen.getByRole("button", { name: "Trigger open config" }));
      expect(calls("settings", "openConfig")).toHaveLength(0);

      fireEvent.click(within(dropPrompt).getByRole("button", { name: "Cancel" }));
      await waitFor(() => expect(calls("settings", "cancelConfigDrop")).toHaveLength(1));
      await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());

      // 2. Open confirmOpenConfig again, receive pendingConfigDrop, and confirm it: underlying dialog stays closed.
      fireEvent.click(screen.getByRole("button", { name: "Trigger open config" }));
      expect(within(await screen.findByRole("alertdialog")).getByText(/Opening a configuration/i)).toBeTruthy();

      settingsRevision = 4;
      host.push("settings.changed", {
        ...standardSettings,
        revision: settingsRevision,
        dirty: true,
        pendingConfigDrop: true,
      });

      const secondDropPrompt = await screen.findByRole("alertdialog");
      fireEvent.click(within(secondDropPrompt).getByRole("button", { name: "Replace changes" }));

      await waitFor(() => expect(calls("settings", "confirmConfigDrop")).toHaveLength(1));
      await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
      expect(calls("settings", "openConfig")).toHaveLength(0);
    });

    it("disablesConfirmWhileRunning_andShowsInlineErrorInsideDialogOnFailure", async () => {
      const runningRun: RunSnapshot = {
        ...readyRun,
        canStart: false,
        status: "Running",
        currentFile: "App.dll",
        total: 1,
      };
      let failCancel = true;

      const host = mockBridge({
        "app.info": () => ({ version: "1.0.0" }),
        "run.get": () => runningRun,
        "files.get": () => emptyFiles,
        "settings.get": () => ({ ...standardSettings, revision: 1, dirty: true, pendingConfigDrop: true }),
        "settings.confirmConfigDrop": () => {
          throw new Error("Run in progress.");
        },
        "settings.cancelConfigDrop": () => {
          if (failCancel) throw new Error("Bridge busy.");
          return { ...standardSettings, revision: 2, dirty: true, pendingConfigDrop: false };
        },
        "results.summary": () => notRunResults,
      });

      render(
        <Shell screen="protection" onNavigate={vi.fn()}>
          content
        </Shell>,
      );

      const dialog = await screen.findByRole("alertdialog");
      const confirmBtn = within(dialog).getByRole("button", { name: "Replace changes" }) as HTMLButtonElement;
      expect(confirmBtn.disabled).toBe(true);

      // Clicking disabled confirm does not call confirmConfigDrop.
      fireEvent.click(confirmBtn);
      expect(calls("settings", "confirmConfigDrop")).toHaveLength(0);

      // Failing cancelConfigDrop renders an inline error banner inside the dialog using cancelConfigDropFailed.
      fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
      const inlineAlert = await within(dialog).findByRole("alert");
      expect(inlineAlert.textContent).toContain("Could not dismiss the dropped configuration.");
      expect(inlineAlert.textContent).toContain("Bridge busy");

      // When the run finishes, confirm becomes enabled; if confirm rejects, its error shows inside the dialog.
      host.push("run.changed", { ...idleRun, revision: 3 });
      await waitFor(() => expect(confirmBtn.disabled).toBe(false));

      fireEvent.click(confirmBtn);
      await waitFor(() => expect(calls("settings", "confirmConfigDrop")).toHaveLength(1));
      const confirmAlert = await within(dialog).findByRole("alert");
      expect(confirmAlert.textContent).toContain("Could not open the configuration");
      expect(confirmAlert.textContent).toContain("Run in progress");

      // Retrying cancel succeeds, clears the error, and closes the dialog.
      failCancel = false;
      fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
      await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    });
  });
});
