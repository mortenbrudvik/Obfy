package com.obfy.rider.services

import kotlin.test.Test
import kotlin.test.assertEquals

/**
 * What the real CLI prints: a Metric/Value table (rounded box characters, or ASCII borders when stdout is
 * redirected to a non-Unicode code page) with "Types Renamed" and "Methods Renamed" rows, and failures
 * written to stdout as "Error: ..." lines.
 */
class CliOutputTest {
    private val unicodeTable = """
        ╭───────────────────────┬──────────────────────────────╮
        │ Metric                │ Value                        │
        ├───────────────────────┼──────────────────────────────┤
        │ Status                │ Success                      │
        │ File                  │ App.dll                      │
        │ Output                │ C:\out\App2.obfuscated.dll   │
        │ Duration              │ 812ms                        │
        │ Strings Encrypted     │ 12                           │
        │ Resources Encrypted   │ 1                            │
        │ Types Renamed         │ 30                           │
        │ Methods Renamed       │ 120                          │
        │ Control Flow          │ 4                            │
        │ Protections           │ 2                            │
        │ Metadata Removed      │ 5                            │
        │ Total Transformations │ 174                          │
        ╰───────────────────────┴──────────────────────────────╯
    """.trimIndent()

    private val asciiTable = """
        +-----------------------------------------------+
        | Metric                | Value                 |
        |-----------------------+-----------------------|
        | Status                | Success               |
        | Strings Encrypted     | 12                    |
        | Types Renamed         | 30                    |
        | Methods Renamed       | 120                   |
        | Total Transformations | 174                   |
        +-----------------------------------------------+
    """.trimIndent()

    @Test
    fun parseStatistics_readsTheUnicodeTableTheCliPrints() {
        val stats = CliOutput.parseStatistics(unicodeTable)
        assertEquals(12, stats.stringsEncrypted)
        assertEquals(150, stats.symbolsRenamed) // 30 types + 120 methods
        assertEquals(174, stats.totalTransformations)
    }

    @Test
    fun parseStatistics_readsTheAsciiTableTheCliPrintsWhenRedirected() {
        val stats = CliOutput.parseStatistics(asciiTable)
        assertEquals(12, stats.stringsEncrypted)
        assertEquals(150, stats.symbolsRenamed)
        assertEquals(174, stats.totalTransformations)
    }

    @Test
    fun parseStatistics_stillReadsColonLines() {
        val stats = CliOutput.parseStatistics("Strings encrypted: 4\nSymbols renamed: 9\n")
        assertEquals(4, stats.stringsEncrypted)
        assertEquals(9, stats.symbolsRenamed)
        assertEquals(13, stats.totalTransformations)
    }

    @Test
    fun parseStatistics_ignoresAnsiColourCodes() {
        val stats = CliOutput.parseStatistics("\u001B[1m│ Total Transformations │ 42 │\u001B[0m")
        assertEquals(42, stats.totalTransformations)
    }

    @Test
    fun parseStatistics_withoutASummary_isAllZero() {
        val stats = CliOutput.parseStatistics("nothing useful here\n")
        assertEquals(ObfuscationStatistics(), stats)
    }

    @Test
    fun extractErrorMessage_prefersStderr() {
        assertEquals("boom on stderr", CliOutput.extractErrorMessage("Error: from stdout", "boom on stderr\n", 1))
    }

    @Test
    fun extractErrorMessage_fallsBackToTheErrorLinesTheCliWritesToStdout() {
        val stdout = """
            Failed to obfuscate App.dll
            Error: Assembly has no metadata
            Obfuscation finished with errors.
        """.trimIndent()
        assertEquals("Assembly has no metadata", CliOutput.extractErrorMessage(stdout, "", 1))
    }

    @Test
    fun extractErrorMessage_withoutErrorLines_usesTheLastStdoutLine() {
        assertEquals("No assemblies found.", CliOutput.extractErrorMessage("No assemblies found.\n\n", "", 1))
    }

    @Test
    fun extractErrorMessage_withNoOutputAtAll_reportsTheExitCode() {
        assertEquals("Process exited with code 7", CliOutput.extractErrorMessage("", "  ", 7))
    }
}
