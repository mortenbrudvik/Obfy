package com.obfy.rider.services

/**
 * Reads what the Obfy CLI prints, so the IDE can show real statistics and the real failure reason.
 *
 * The CLI prints a Metric/Value table (box characters, or `|` borders when stdout is redirected to a
 * non-Unicode code page) and writes failures to stdout as `Error: ...` lines, not to stderr.
 */
object CliOutput {

    private val lastInteger = Regex("-?\\d+")
    private val ansiEscape = Regex("\u001B\\[[0-9;?]*[ -/]*[@-~]")
    private val markup = Regex("\\[[^\\[\\]]*]")

    /**
     * Accepts colon lines (`Strings encrypted: 4`) and table rows (`│ Strings Encrypted │ 42 │`).
     * The CLI reports renames as separate "Types Renamed" and "Methods Renamed" rows and never prints a
     * "Symbols renamed" row, so [ObfuscationStatistics.symbolsRenamed] is their sum.
     */
    fun parseStatistics(output: String): ObfuscationStatistics {
        var strings = 0
        var symbols = 0
        var total = 0
        var sawExplicitTotal = false

        for (raw in output.lines()) {
            val line = markup.replace(ansiEscape.replace(raw, ""), "")
            val lower = line.lowercase()
            when {
                "strings encrypted" in lower -> lastInt(line)?.let { strings = it }
                "symbols renamed" in lower || "types renamed" in lower || "methods renamed" in lower ->
                    lastInt(line)?.let { symbols += it }
                "transformations" in lower -> lastInt(line)?.let {
                    total = it
                    sawExplicitTotal = true
                }
            }
        }

        return ObfuscationStatistics(
            totalTransformations = if (sawExplicitTotal) total else strings + symbols,
            stringsEncrypted = strings,
            symbolsRenamed = symbols
        )
    }

    /**
     * Picks the text to show for a failed run: stderr when it has anything, then the CLI's own `Error:`
     * lines from stdout, then the last line it printed, then the exit code.
     */
    fun extractErrorMessage(stdout: String, stderr: String, exitCode: Int): String {
        val fromStderr = ansiEscape.replace(stderr, "").trim()
        if (fromStderr.isNotEmpty()) return fromStderr

        val lines = stdout.lines().map { ansiEscape.replace(it, "").trim() }.filter { it.isNotEmpty() }
        val errors = lines
            .filter { it.startsWith("Error:", ignoreCase = true) }
            .map { it.substring("Error:".length).trim() }
            .filter { it.isNotEmpty() }
        if (errors.isNotEmpty()) return errors.joinToString("\n")

        return lines.lastOrNull() ?: "Process exited with code $exitCode"
    }

    private fun lastInt(line: String): Int? =
        lastInteger.findAll(line).lastOrNull()?.value?.toIntOrNull()?.coerceAtLeast(0)
}
