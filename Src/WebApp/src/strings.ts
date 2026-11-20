/** User-visible text the interop layer and the shell show or throw. */
export const strings = {
  error: {
    timeout: "The app did not answer in time.",
    unknown: "Something went wrong.",
    bridgeUnavailable: "The WebView2 API bridge is not available.",
  },
  shell: {
    connecting: "Connecting…",
    unreachable: "Could not reach the app.",
    sections: "Sections",
    startFailed: "Could not start the run",
    cancelFailed: "Could not cancel the run",
    addFilesFailed: "Could not add files",
    saveConfigFailed: "Could not save the configuration",
    openConfigFailed: "Could not open the configuration",
  },
  nav: {
    files: "Files",
    protection: "Protection",
    results: "Results",
    guide: "Guide",
    resultsRunning: "running",
    resultsFinished: "finished",
    resultsNone: "no results yet",
  },
  run: {
    obfuscate: "Obfuscate",
    cancel: "Cancel",
    progress: "Progress",
  },
} as const;
