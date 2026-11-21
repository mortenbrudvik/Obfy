import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { content } from "../../../content/content";
import type { SettingsSnapshot } from "../../../types/domain";
import TechniqueDetail from "../TechniqueDetail";
import { flushPendingEdits } from "../../../hooks/pendingEdits";
import { hostTechniqueIds, protectionSnapshot } from "./fixtures";

interface Handlers {
  onUpdate?: (patch: Record<string, unknown>) => void;
  onOpenGuide?: (topic: string) => void;
  onPickKey?: () => void;
}

function renderDetail(id: string, snapshot: SettingsSnapshot = protectionSnapshot(), handlers: Handlers = {}) {
  const technique = snapshot.techniques.find((t) => t.id === id)!;
  return render(
    <TechniqueDetail
      technique={technique}
      snapshot={snapshot}
      onUpdate={handlers.onUpdate ?? vi.fn()}
      onPickKey={handlers.onPickKey ?? vi.fn()}
      onOpenGuide={handlers.onOpenGuide ?? vi.fn()}
    />,
  );
}

describe("TechniqueDetail", () => {
  it("showsWhatItDoesAndWhatToKnow", () => {
    renderDetail("controlFlow");

    expect(screen.getByRole("heading", { name: "Control flow" })).toBeTruthy();
    expect(screen.getByText("What it does")).toBeTruthy();
    expect(screen.getByText(/a decompiler shows a loop around a large switch/)).toBeTruthy();
    expect(screen.getByText("Good to know")).toBeTruthy();
    expect(screen.getByText("The output file is larger.")).toBeTruthy();
  });

  it("showsEveryOptionsHelpBeneathItsControl_withoutAnyInteraction", () => {
    renderDetail("symbolRenaming");

    expect(screen.getByText("Turn off a kind to leave those names as they are.")).toBeTruthy();
    expect(screen.getByText(/Leave public names alone/)).toBeTruthy();
    expect(screen.getByText(/Keeps the names of public properties on types that look bindable/)).toBeTruthy();
    expect(screen.getByText(/It does not keep type names/)).toBeTruthy();
    // A select explains the value it has now.
    expect(screen.getByText(/Look-alike or non-printing characters/)).toBeTruthy();
    // The help belongs to its control, for a screen reader.
    const publicApi = screen.getByRole("checkbox", { name: "Preserve public API" });
    expect(document.getElementById(publicApi.getAttribute("aria-describedby")!)?.textContent).toMatch(/Leave public names alone/);
    const naming = screen.getByLabelText("Naming style");
    expect(document.getElementById(naming.getAttribute("aria-describedby")!)?.textContent).toMatch(/Look-alike/);
  });

  it("explainsTheChosenValueOfASelect_andFollowsIt", () => {
    const { unmount } = renderDetail("stringEncryption");
    expect(screen.getByText("More work to undo, and slower at run time.")).toBeTruthy();
    unmount();

    renderDetail("stringEncryption", protectionSnapshot({ optionValues: { stringEncryption: { algorithm: "Xor" } } }));
    expect(screen.getByText("Fastest. Easy to undo for someone who looks.")).toBeTruthy();
  });

  it("offersOnlyTheValuesTheHostOffers", () => {
    renderDetail("controlFlow");

    const select = screen.getByLabelText("Mode") as HTMLSelectElement;
    expect(Array.from(select.options).map((o) => o.value)).toEqual(["Switch", "OpaquePredicate", "Combined"]);
    expect(Array.from(select.options).map((o) => o.text)).toEqual(["Switch flattening", "Opaque conditions", "Combined"]);
  });

  it("sendsTheDottedPatchKey_forEachKindOfControl", () => {
    const onUpdate = vi.fn();
    renderDetail("symbolRenaming", protectionSnapshot(), { onUpdate });

    fireEvent.click(screen.getByRole("checkbox", { name: "Fields" }));
    expect(onUpdate).toHaveBeenLastCalledWith({ "symbolRenaming.renameFields": false });

    fireEvent.click(screen.getByRole("checkbox", { name: "Preserve public API" }));
    expect(onUpdate).toHaveBeenLastCalledWith({ "symbolRenaming.preservePublicApi": true });

    fireEvent.change(screen.getByLabelText("Naming style"), { target: { value: "Random" } });
    expect(onUpdate).toHaveBeenLastCalledWith({ "symbolRenaming.mode": "Random" });
  });

  it("sendsNumbersAndRanges_asNumbers", () => {
    const onUpdate = vi.fn();
    const { unmount } = renderDetail("stringEncryption", protectionSnapshot(), { onUpdate });
    fireEvent.change(screen.getByLabelText("Minimum length"), { target: { value: "8" } });
    expect(onUpdate).toHaveBeenLastCalledWith({ "stringEncryption.minStringLength": 8 });
    unmount();

    renderDetail("controlFlow", protectionSnapshot(), { onUpdate });
    fireEvent.change(screen.getByLabelText("Intensity"), { target: { value: "70" } });
    expect(onUpdate).toHaveBeenLastCalledWith({ "controlFlow.intensity": 70 });
  });

  it("doesNotSendANumber_whileTheFieldIsEmpty", () => {
    const onUpdate = vi.fn();
    renderDetail("stringEncryption", protectionSnapshot(), { onUpdate });

    fireEvent.change(screen.getByLabelText("Minimum length"), { target: { value: "" } });

    expect(onUpdate).not.toHaveBeenCalled();
  });

  it("setsApartTheSecurityNote_onlyOnTheEncryptionTechniques", () => {
    const { unmount } = renderDetail("stringEncryption");
    expect(screen.getByText(/not confidentiality/)).toBeTruthy();
    expect(screen.getByText(/Do not ship real secrets/)).toBeTruthy();
    unmount();

    renderDetail("controlFlow");
    expect(screen.queryByText(/not confidentiality/)).toBeNull();
  });

  it("showsBeforeAndAfter_whereTheContentHasOne", () => {
    const { unmount } = renderDetail("stringEncryption");
    expect(screen.getByText("Before and after")).toBeTruthy();
    expect(screen.getByText('"Order not found"')).toBeTruthy();
    expect(screen.getByText("c.a(1482)")).toBeTruthy();
    unmount();

    renderDetail("watermark");
    expect(screen.queryByText("Before and after")).toBeNull();
  });

  it("offersReadMoreOnline_atTheTechniquesSectionOfTheOnlineGuide", () => {
    const onOpenGuide = vi.fn();
    renderDetail("antiDebug", protectionSnapshot(), { onOpenGuide });

    const link = screen.getByRole("button", { name: "Read more online" });
    expect(link.getAttribute("title")).toBe("Opens the online guide in your browser");
    fireEvent.click(link);

    expect(onOpenGuide).toHaveBeenCalledWith("techniques");
  });

  it("linksMerge_toTheFilesAndOutputSection", () => {
    const onOpenGuide = vi.fn();
    renderDetail("assemblyMerge", protectionSnapshot(), { onOpenGuide });

    fireEvent.click(screen.getByRole("button", { name: "Read more online" }));

    expect(onOpenGuide).toHaveBeenCalledWith("files-and-output");
  });

  it("showsTheUnavailableReason_andDisablesTheSwitch", () => {
    renderDetail("methodEncryption", protectionSnapshot({ level: "Aggressive", runtimeProfile: "NativeAot" }));

    expect(screen.getByText(/Not available for NativeAOT\./)).toBeTruthy();
    expect((screen.getByRole("switch", { name: "Enable Method body encryption" }) as HTMLInputElement).disabled).toBe(true);
  });

  it("labelsTheSwitchOnOrOff", () => {
    renderDetail("controlFlow", protectionSnapshot({ on: ["controlFlow"] }));
    expect(screen.getByText("On")).toBeTruthy();
  });

  it("turnsTheMetadataProtectionOnThroughItsOptions_becauseItHasNoSwitchOfItsOwn", () => {
    const onUpdate = vi.fn();
    renderDetail("metadataRemoval", protectionSnapshot({ off: ["metadataRemoval"] }), { onUpdate });

    fireEvent.click(screen.getByRole("switch", { name: "Enable Metadata removal" }));

    expect(onUpdate).toHaveBeenLastCalledWith({ "metadataRemoval.removeDebugInfo": true, "metadataRemoval.removeAttributes": true });
  });

  it("turnsEveryMetadataOptionOff_whenTheProtectionIsSwitchedOff", () => {
    const onUpdate = vi.fn();
    renderDetail("metadataRemoval", protectionSnapshot(), { onUpdate });

    fireEvent.click(screen.getByRole("switch", { name: "Enable Metadata removal" }));

    expect(onUpdate).toHaveBeenLastCalledWith({
      "metadataRemoval.removeDebugInfo": false,
      "metadataRemoval.removeAttributes": false,
      "metadataRemoval.stripDocumentation": false,
    });
  });

  it("picksTheSigningKeyThroughTheHost", () => {
    const onPickKey = vi.fn();
    renderDetail("signing", protectionSnapshot(), { onPickKey });

    fireEvent.click(screen.getByRole("button", { name: "Browse…" }));

    expect(onPickKey).toHaveBeenCalled();
  });

  it("showsTheKeyFileError_nextToTheKeyFileField", () => {
    const snapshot = protectionSnapshot({
      on: ["signing"],
      errors: [{ field: "signing.keyFile", message: "Signing is enabled but no key file was specified." }],
    });
    renderDetail("signing", snapshot);

    const field = screen.getByLabelText(/^Key file/);
    expect(field.getAttribute("aria-invalid")).toBe("true");
    expect(within(field.closest(".opt") as HTMLElement).getByText(/no key file was specified/)).toBeTruthy();
  });

  it("commitsAPatternListOnBlur_oneWholePatternPerLine", () => {
    const onUpdate = vi.fn();
    renderDetail("dependencyEmbedding", protectionSnapshot(), { onUpdate });

    const include = screen.getByLabelText("Include");
    fireEvent.change(include, { target: { value: "*.dll\nPlugin.*" } });
    expect(onUpdate).not.toHaveBeenCalled();
    fireEvent.blur(include);

    expect(onUpdate).toHaveBeenCalledWith({ "dependencyEmbedding.includePatterns": ["*.dll", "Plugin.*"] });
  });
});

describe("TechniqueDetail text options", () => {
  it("commitsTheWatermarkId_whenTheBoxIsLeft_andNotBefore", () => {
    const onUpdate = vi.fn();
    renderDetail("watermark", protectionSnapshot(), { onUpdate });
    const id = screen.getByLabelText(/^Id/);

    fireEvent.change(id, { target: { value: "acme-1" } });
    expect(onUpdate).not.toHaveBeenCalled();
    fireEvent.blur(id);

    expect(onUpdate).toHaveBeenCalledTimes(1);
    expect(onUpdate).toHaveBeenCalledWith({ "watermark.id": "acme-1" });
  });

  it("commitsTheWatermarkId_onEnter", () => {
    const onUpdate = vi.fn();
    renderDetail("watermark", protectionSnapshot(), { onUpdate });
    const id = screen.getByLabelText(/^Id/);

    fireEvent.change(id, { target: { value: "acme-2" } });
    fireEvent.keyDown(id, { key: "Enter" });

    expect(onUpdate).toHaveBeenCalledWith({ "watermark.id": "acme-2" });
  });

  it("sendsNothing_whenTheBoxIsLeftWithTheValueTheHostHas", () => {
    const onUpdate = vi.fn();
    renderDetail("watermark", protectionSnapshot({ optionValues: { watermark: { id: "acme-1" } } }), { onUpdate });
    const id = screen.getByLabelText(/^Id/);

    fireEvent.focus(id);
    fireEvent.blur(id);
    fireEvent.keyDown(id, { key: "Enter" });

    expect(onUpdate).not.toHaveBeenCalled();
  });

  it("commitsTheKeyFileAndThePasswordVariable_onBlurAndOnEnter", () => {
    const onUpdate = vi.fn();
    renderDetail("signing", protectionSnapshot(), { onUpdate });

    const keyFile = screen.getByLabelText(/^Key file/);
    fireEvent.change(keyFile, { target: { value: "C:\\keys\\company.pfx" } });
    fireEvent.blur(keyFile);
    expect(onUpdate).toHaveBeenLastCalledWith({ "signing.keyFile": "C:\\keys\\company.pfx" });

    const password = screen.getByLabelText("Password variable");
    fireEvent.change(password, { target: { value: "OBFY_KEY_PASSWORD" } });
    fireEvent.keyDown(password, { key: "Enter" });
    expect(onUpdate).toHaveBeenLastCalledWith({ "signing.passwordEnvironmentVariable": "OBFY_KEY_PASSWORD" });
  });

  it("followsTheHostsValue_whenItChanges", () => {
    const { rerender } = renderDetail("watermark", protectionSnapshot({ optionValues: { watermark: { id: "a" } } }));
    expect((screen.getByLabelText(/^Id/) as HTMLInputElement).value).toBe("a");

    const next = protectionSnapshot({ revision: 2, optionValues: { watermark: { id: "b" } } });
    rerender(
      <TechniqueDetail
        technique={next.techniques.find((t) => t.id === "watermark")!}
        snapshot={next}
        onUpdate={vi.fn()}
        onPickKey={vi.fn()}
        onOpenGuide={vi.fn()}
      />,
    );

    expect((screen.getByLabelText(/^Id/) as HTMLInputElement).value).toBe("b");
  });

  it("flushesATypedValue_throughFlushPendingEdits_withoutTheBoxBeingLeft", async () => {
    const onUpdate = vi.fn().mockResolvedValue(true);
    renderDetail("watermark", protectionSnapshot(), { onUpdate });
    fireEvent.change(screen.getByLabelText(/^Id/), { target: { value: "acme-3" } });

    await expect(flushPendingEdits()).resolves.toBe("sent");

    expect(onUpdate).toHaveBeenCalledWith({ "watermark.id": "acme-3" });
  });

  it("flushesAPatternList_andReportsARefusal", async () => {
    const onUpdate = vi.fn().mockResolvedValue(false);
    renderDetail("dependencyEmbedding", protectionSnapshot(), { onUpdate });
    fireEvent.change(screen.getByLabelText("Exclude"), { target: { value: "*.resources.dll\nTest.*" } });

    await expect(flushPendingEdits()).resolves.toBe("failed");

    expect(onUpdate).toHaveBeenCalledWith({ "dependencyEmbedding.excludePatterns": ["*.resources.dll", "Test.*"] });
  });

  it("reportsIdle_whenNoBoxHoldsNewText", async () => {
    renderDetail("watermark");

    await expect(flushPendingEdits()).resolves.toBe("idle");
  });
});

describe("techniques.json", () => {
  it("hasEntriesForExactlyTheTechniquesTheHostLists", () => {
    expect(Object.keys(content.techniques).sort()).toEqual([...hostTechniqueIds].sort());
  });

  it("putsEveryTechniqueInAKnownGroup_andGivesItItsWords", () => {
    for (const [id, technique] of Object.entries(content.techniques)) {
      expect(content.groups, id).toContain(technique.group);
      expect(technique.name.length, id).toBeGreaterThan(0);
      expect(technique.summary.length, id).toBeGreaterThan(0);
      expect(technique.what.length, id).toBeGreaterThan(0);
      expect(technique.good.length, id).toBeGreaterThan(0);
    }
  });

  it("explainsEveryOptionOfEveryTechnique", () => {
    for (const [id, technique] of Object.entries(content.techniques)) {
      for (const option of technique.options) {
        const choices = option.choiceSet ? Object.values(content.choiceSets[option.choiceSet].values) : [];
        const hasHelp = Boolean(option.help) || (choices.length > 0 && choices.every((v) => v.help));
        expect(hasHelp, `${id}.${option.key}`).toBe(true);
      }
    }
  });

  it("carriesTheSecurityNote_onTheEncryptionTechniques", () => {
    for (const id of ["stringEncryption", "constantEncryption", "resourceEncryption", "methodEncryption"]) {
      expect(content.techniques[id].securityNote, id).toBeTruthy();
    }
  });
});
