import type { SettingsSnapshot, TechniqueState } from "../../../types/domain";

/** The ids ProtectionSession lists, in its order (Obfy.App/Sessions/ProtectionSession.cs). */
export const hostTechniqueIds = [
  "dependencyEmbedding", "stringEncryption", "constantEncryption", "resourceEncryption", "antiDebug", "antiDump",
  "antiDecompiler", "watermark", "antiTamper", "virtualization", "methodEncryption", "controlFlow", "referenceProxy",
  "symbolRenaming", "metadataRemoval", "assemblyMerge", "signing", "packing", "incremental",
];

/** The techniques that the runtime profile rules out (GatedByProfile in ProtectionSession). */
export const gatedIds = ["dependencyEmbedding", "antiDump", "antiTamper", "virtualization", "methodEncryption", "packing"];

const levelOn: Record<string, string[]> = {
  Minimal: ["symbolRenaming", "metadataRemoval"],
  Standard: ["symbolRenaming", "metadataRemoval", "stringEncryption"],
  Aggressive: [
    "symbolRenaming", "metadataRemoval", "stringEncryption", "constantEncryption", "resourceEncryption", "controlFlow",
    "referenceProxy", "methodEncryption", "antiDebug", "antiTamper", "antiDump", "antiDecompiler",
  ],
  Custom: ["symbolRenaming", "metadataRemoval", "stringEncryption"],
};

/** The options each technique carries, with the values the host's defaults give. */
const options: Record<string, Record<string, unknown>> = {
  dependencyEmbedding: { includePatterns: ["*.dll"], excludePatterns: ["*.resources.dll"] },
  stringEncryption: { algorithm: "Aes256", minStringLength: 3 },
  constantEncryption: {
    algorithm: "Xor", encryptIntegers: true, encryptLongs: true, encryptFloats: true, encryptDoubles: true,
    integerThreshold: 2, skipCommonFloats: true, skipCommonDoubles: true,
  },
  resourceEncryption: { algorithm: "Aes256" },
  antiDecompiler: { injectJunkTypes: true, addSuppressIldasmAttribute: true, addDecoyAttributes: true, junkTypeCount: 5, junkMethodsPerType: 3 },
  watermark: { id: "" },
  antiTamper: { checkEntryPoint: true, checkModuleInitializer: true },
  controlFlow: { mode: "Switch", intensity: 50 },
  referenceProxy: { proxyExternalCalls: false },
  symbolRenaming: {
    mode: "Unreadable", renameTypes: true, renameMethods: true, renameFields: true, renameProperties: true,
    renameParameters: true, renameEvents: true, renameNamespaces: true, preservePublicApi: false, preserveXaml: false,
  },
  metadataRemoval: { removeDebugInfo: true, removeAttributes: true, stripDocumentation: true },
  assemblyMerge: { internalize: true },
  signing: { keyFile: "", passwordEnvironmentVariable: "" },
  packing: { rid: "win-x64" },
};

export interface SnapshotOptions {
  level?: string;
  revision?: number;
  runtimeProfile?: string;
  dirty?: boolean;
  pendingConfigDrop?: boolean;
  configPath?: string | null;
  /** Ids switched on in addition to what the level gives. */
  on?: string[];
  /** Ids switched off even if the level gives them. */
  off?: string[];
  changed?: string[];
  optionValues?: Record<string, Record<string, unknown>>;
  errors?: { field: string; message: string }[];
}

/** A snapshot as ProtectionSession builds it, for tests. */
export function protectionSnapshot(opts: SnapshotOptions = {}): SettingsSnapshot {
  const level = opts.level ?? "Standard";
  const profile = opts.runtimeProfile ?? "Default";
  const gated = profile !== "Default";
  const reason = profile === "NativeAot" ? "Not available for NativeAOT" : "Not available for the chosen runtime";
  const on = new Set([...(levelOn[level] ?? []), ...(opts.on ?? [])]);
  (opts.off ?? []).forEach((id) => on.delete(id));

  const techniques: TechniqueState[] = hostTechniqueIds.map((id) => {
    const unavailable = gated && gatedIds.includes(id);
    return {
      id,
      enabled: on.has(id),
      available: !unavailable,
      unavailableReason: unavailable ? reason : null,
      differsFromLevel: (opts.changed ?? []).includes(id),
      options: { ...(options[id] ?? {}), ...(opts.optionValues?.[id] ?? {}) },
    };
  });

  return {
    revision: opts.revision ?? 1,
    level,
    levelSummary: "",
    techniques,
    errors: opts.errors ?? [],
    configPath: opts.configPath ?? null,
    dirty: opts.dirty ?? false,
    runtimeProfile: profile,
    exclusions: { namespaces: [], types: [], methods: [] },
    inclusions: { namespaces: [], types: [], methods: [] },
    choices: {
      levels: ["Minimal", "Standard", "Aggressive", "Custom"],
      algorithms: ["Xor", "Aes256"],
      controlFlowModes: ["Switch", "OpaquePredicate", "Combined"],
      namingModes: ["Unreadable", "Sequential", "Hash", "Random"],
      runtimeProfiles: ["Default", "NativeAot", "UnityIl2Cpp", "BlazorWasm"],
      packingRids: ["win-x64", "portable"],
    },
    pendingConfigDrop: opts.pendingConfigDrop ?? false,
  };
}
